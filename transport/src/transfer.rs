//! File transfers between nodes.
//!
//! The node that wants a file connects and names it. The node that has the file asks Nodearr
//! whether this peer may have it and where it is on disk, then sends a header (size and BLAKE3
//! hash, fixed before any data moves) followed by the bytes.
//!
//! File data only ever moves while the connection's selected path is a direct IP path. The
//! sender holds every chunk back until that is so, and the receiver refuses data that arrives
//! otherwise. A relay may carry the handshake and the request. It never carries the file
//! (ADR 0011).

use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    sync::{
        atomic::{AtomicU64, Ordering},
        Arc, Mutex,
    },
    time::{Duration, Instant, SystemTime},
};

use anyhow::{anyhow, bail, Result};
use iroh::{
    endpoint::{Connection, ConnectionError, PathEvent, ReadError, RecvStream, SendStream, VarInt},
    Endpoint, EndpointAddr, EndpointId,
};
use n0_future::StreamExt;
use serde::{Deserialize, Serialize};
use tokio::{
    io::{AsyncReadExt, AsyncSeekExt, AsyncWriteExt},
    sync::{mpsc, oneshot, watch},
    task::AbortHandle,
};

use crate::{Incoming, IncomingKind, Reply, CODE_BLOCKED, CODE_DONE, CODE_NOT_AUTHORIZED};

pub const TRANSFER_ALPN: &[u8] = b"nodearr/transfer/1";

const CHUNK: usize = 256 * 1024;
const MAX_FRAME: usize = 64 * 1024;

const CODE_NO_DIRECT_ROUTE: u32 = 3;
const CODE_DIRECT_PATH_LOST: u32 = 4;
const CODE_RELAYED_MEDIA: u32 = 6;
const CODE_REVOKED: u32 = 7;
const CODE_NOT_FOUND: u32 = 8;

/// How long the receiver waits for the header. The sender may have to hash a large file first.
const HEADER_TIMEOUT: Duration = Duration::from_secs(30 * 60);

#[derive(Serialize, Deserialize)]
struct FileRequest {
    file: String,
    /// How much of the file the receiver already holds.
    offset: u64,
    /// The hash of the file those bytes came from. The sender resumes only if it still matches.
    if_hash: Option<String>,
}

#[derive(Serialize, Deserialize)]
struct FileHeader {
    size: u64,
    blake3: String,
    /// Where the sender starts. Zero when it would not resume.
    offset: u64,
}

#[derive(Serialize, Clone)]
pub struct Failure {
    pub kind: &'static str,
    pub message: String,
}

#[derive(Serialize, Clone)]
pub struct DownloadState {
    pub id: String,
    /// connecting, receiving, completed or failed.
    pub state: &'static str,
    pub bytes_done: u64,
    pub size: u64,
    pub blake3: Option<String>,
    pub failure: Option<Failure>,
}

/// One file this node is sending, or has just sent, to another node.
#[derive(Serialize, Clone)]
pub struct UploadState {
    pub id: String,
    pub remote_node_id: String,
    /// The name the other node asked for the file by.
    pub file: String,
    /// Where the file is on this machine.
    pub path: String,
    /// preparing (working out the hash), sending, completed or failed.
    pub state: &'static str,
    /// How much of the file the other node has, counting what it already held when it asked.
    pub bytes_sent: u64,
    pub size: u64,
    pub failure: Option<Failure>,
    #[serde(skip)]
    finished: Option<Instant>,
}

/// How long a finished upload stays in the list, so that whoever is watching sees how it ended.
const UPLOAD_HISTORY: Duration = Duration::from_secs(300);

struct Download {
    state: Arc<Mutex<DownloadState>>,
    abort: AbortHandle,
}

/// A file's BLAKE3 hash, and the size and modified time it was worked out for. Nodearr keeps these
/// and hands them back with a grant, so a file is not read twice for the same version of it.
#[derive(Serialize, Deserialize, Clone)]
pub struct FileHash {
    pub size: u64,
    /// Nanoseconds since 1970, as this machine's filesystem reports it.
    pub modified_ns: u64,
    pub blake3: String,
}

fn modified_ns(modified: SystemTime) -> u64 {
    modified.duration_since(SystemTime::UNIX_EPOCH).map(|d| d.as_nanos() as u64).unwrap_or(0)
}

pub struct Transfers {
    direct_timeout: Duration,
    /// Bytes per second each upload may send. Zero means no limit.
    upload_limit: u64,
    downloads: Mutex<HashMap<String, Download>>,
    uploads: Mutex<HashMap<u64, (EndpointId, Connection)>>,
    upload_states: Mutex<HashMap<u64, UploadState>>,
    next_upload: AtomicU64,
    hashes: Mutex<HashMap<PathBuf, FileHash>>,
    /// One lock for each file being hashed, so that a request and the background pass never read
    /// the same file at once. The second waits and takes the first one's answer.
    hashing: Mutex<HashMap<PathBuf, Arc<tokio::sync::Mutex<()>>>>,
}

impl Transfers {
    pub fn new(direct_timeout: Duration, upload_limit: u64) -> Self {
        Self {
            direct_timeout,
            upload_limit,
            downloads: Mutex::new(HashMap::new()),
            uploads: Mutex::new(HashMap::new()),
            upload_states: Mutex::new(HashMap::new()),
            // Starts from the clock, so that an upload after a restart is not mistaken for one before it.
            next_upload: AtomicU64::new(SystemTime::now().duration_since(SystemTime::UNIX_EPOCH).map(|d| d.as_millis() as u64).unwrap_or(1)),
            hashes: Mutex::new(HashMap::new()),
            hashing: Mutex::new(HashMap::new()),
        }
    }

    pub fn list(&self) -> Vec<DownloadState> {
        self.downloads.lock().unwrap().values().map(|d| d.state.lock().unwrap().clone()).collect()
    }

    /// Stops a download and forgets it. What it wrote stays on disk for Nodearr to resume or remove.
    pub fn cancel(&self, id: &str) -> bool {
        match self.downloads.lock().unwrap().remove(id) {
            Some(download) => {
                download.abort.abort();
                true
            }
            None => false,
        }
    }

    /// What this node is sending, and what it finished sending in the last few minutes.
    pub fn list_uploads(&self) -> Vec<UploadState> {
        let mut states = self.upload_states.lock().unwrap();
        states.retain(|_, upload| upload.finished.map(|at| at.elapsed() < UPLOAD_HISTORY).unwrap_or(true));

        let mut list: Vec<UploadState> = states.values().cloned().collect();
        list.sort_by(|a, b| a.id.cmp(&b.id));
        list
    }

    fn upload_update(&self, id: u64, change: impl FnOnce(&mut UploadState)) {
        if let Some(upload) = self.upload_states.lock().unwrap().get_mut(&id) {
            change(upload);
            if matches!(upload.state, "completed" | "failed") && upload.finished.is_none() {
                upload.finished = Some(Instant::now());
            }
        }
    }

    fn upload_failed(&self, id: u64, kind: &'static str, message: impl Into<String>) {
        let message = message.into();
        self.upload_update(id, |upload| {
            // The first reason given is the real one. What follows is the connection coming apart.
            if upload.failure.is_none() {
                upload.state = "failed";
                upload.failure = Some(Failure { kind, message });
            }
        });
    }

    /// Cuts every upload to a node. Used when Nodearr stops trusting it part way through.
    pub fn disconnect(&self, node: EndpointId) -> usize {
        let uploads = self.uploads.lock().unwrap();
        let mut closed = 0;
        for (id, (remote, conn)) in uploads.iter() {
            if *remote == node {
                self.upload_failed(*id, "Revoked", "trust in this node ended");
                conn.close(VarInt::from_u32(CODE_REVOKED), b"Revoked");
                closed += 1;
            }
        }
        closed
    }

    /// Cuts one upload. Used when Nodearr stops sharing the file with that node part way through.
    pub fn stop_upload(&self, id: u64, reason: Option<&str>) -> bool {
        let uploads = self.uploads.lock().unwrap();
        let Some((_, conn)) = uploads.get(&id) else {
            return false;
        };
        let (code, name) = refusal(reason);
        let message = match name {
            "Revoked" => "trust in this node ended",
            "NotFound" => "the file is no longer in the library",
            _ => "this file is no longer shared with that node",
        };
        self.upload_failed(id, name, message);
        conn.close(VarInt::from_u32(code), name.as_bytes());
        true
    }

    /// Takes a hash Nodearr kept from before. It is used only while the file's size and modified
    /// time are still the ones it was worked out for.
    pub fn remember(&self, path: &Path, known: FileHash) {
        self.hashes.lock().unwrap().insert(path.to_path_buf(), known);
    }

    /// The size and BLAKE3 hash of a file, worked out once for each version of it.
    pub async fn hash(&self, path: &Path) -> Result<FileHash> {
        let lock = self.hashing.lock().unwrap().entry(path.to_path_buf()).or_default().clone();
        let result = {
            let _held = lock.lock().await;
            self.hash_now(path).await
        };

        // Nobody else is waiting on this file.
        let mut hashing = self.hashing.lock().unwrap();
        if Arc::strong_count(&lock) == 2 {
            hashing.remove(path);
        }
        result
    }

    async fn hash_now(&self, path: &Path) -> Result<FileHash> {
        let metadata = tokio::fs::metadata(path).await?;
        if !metadata.is_file() {
            bail!("not a file");
        }
        let size = metadata.len();
        let modified = modified_ns(metadata.modified()?);

        if let Some(cached) = self.hashes.lock().unwrap().get(path) {
            if cached.size == size && cached.modified_ns == modified {
                return Ok(cached.clone());
            }
        }

        let owned = path.to_path_buf();
        let blake3 = tokio::task::spawn_blocking(move || hash_start(&owned, u64::MAX)).await??.finalize().to_hex().to_string();
        let hashed = FileHash { size, modified_ns: modified, blake3 };
        self.hashes.lock().unwrap().insert(path.to_path_buf(), hashed.clone());
        Ok(hashed)
    }
}

/// A hasher that has read the first `length` bytes of a file.
fn hash_start(path: &Path, length: u64) -> std::io::Result<blake3::Hasher> {
    use std::io::Read;

    let mut hasher = blake3::Hasher::new();
    let mut file = std::fs::File::open(path)?.take(length);
    std::io::copy(&mut file, &mut hasher)?;
    Ok(hasher)
}

async fn write_json<T: Serialize>(send: &mut SendStream, value: &T) -> Result<()> {
    let bytes = serde_json::to_vec(value)?;
    send.write_u32(bytes.len() as u32).await?;
    send.write_all(&bytes).await?;
    Ok(())
}

async fn read_json<T: for<'de> Deserialize<'de>>(recv: &mut RecvStream) -> Result<T> {
    let len = recv.read_u32().await? as usize;
    if len > MAX_FRAME {
        bail!("frame too large");
    }
    let mut bytes = vec![0u8; len];
    recv.read_exact(&mut bytes).await.map_err(|e| anyhow!("{e}"))?;
    Ok(serde_json::from_slice(&bytes)?)
}

// ---------------------------------------------------------------------------------------------
// Paths
// ---------------------------------------------------------------------------------------------

/// Follows whether a connection's selected path is a direct IP path right now.
pub(crate) struct PathMonitor {
    direct: watch::Receiver<bool>,
}

impl PathMonitor {
    pub(crate) fn spawn(conn: &Connection) -> Self {
        let (tx, rx) = watch::channel(is_direct(conn));
        let conn = conn.clone();

        tokio::spawn(async move {
            let mut events = conn.path_events();
            while let Some(event) = events.next().await {
                if matches!(event, PathEvent::Opened { .. } | PathEvent::Selected { .. } | PathEvent::Closed { .. }) {
                    // Recompute from the connection rather than trusting event order.
                    if tx.send(is_direct(&conn)).is_err() {
                        return;
                    }
                }
            }
        });

        Self { direct: rx }
    }

    fn is_direct(&self) -> bool {
        *self.direct.borrow()
    }

    pub(crate) async fn wait_direct(&mut self, timeout: Duration) -> bool {
        tokio::time::timeout(timeout, self.direct.wait_for(|direct| *direct)).await.map(|r| r.is_ok()).unwrap_or(false)
    }

    /// Resolves when the selected path stops being direct.
    async fn lost_direct(&mut self) {
        let _ = self.direct.wait_for(|direct| !*direct).await;
    }
}

fn is_direct(conn: &Connection) -> bool {
    conn.paths().iter().find(|p| p.is_selected()).map(|p| p.is_ip()).unwrap_or(false)
}

/// The path a connection is using right now: whether it is direct, and the address at the far end.
pub(crate) fn selected_path(conn: &Connection) -> Option<(bool, String)> {
    conn.paths().iter().find(|p| p.is_selected()).map(|p| {
        let remote = match p.remote_addr() {
            iroh::TransportAddr::Ip(ip) => ip.to_string(),
            iroh::TransportAddr::Relay(url) => url.to_string(),
            other => format!("{other:?}"),
        };
        (p.is_ip(), remote)
    })
}

fn code_name(code: u64) -> &'static str {
    match code as u32 {
        CODE_NO_DIRECT_ROUTE => "NoDirectRoute",
        CODE_NOT_AUTHORIZED => "NotAuthorized",
        CODE_DIRECT_PATH_LOST => "DirectPathLost",
        CODE_BLOCKED => "Blocked",
        CODE_RELAYED_MEDIA => "RelayedMediaRefused",
        CODE_REVOKED => "Revoked",
        CODE_NOT_FOUND => "NotFound",
        _ => "TransferFailed",
    }
}

/// The close code and name for Nodearr's reason to refuse a node a file.
fn refusal(reason: Option<&str>) -> (u32, &'static str) {
    match reason {
        Some("NotFound") => (CODE_NOT_FOUND, "NotFound"),
        Some("Revoked") => (CODE_REVOKED, "Revoked"),
        _ => (CODE_NOT_AUTHORIZED, "NotAuthorized"),
    }
}

/// Names a failure from the code the other side closed the connection with.
fn close_name(conn: &Connection) -> &'static str {
    match conn.close_reason() {
        Some(ConnectionError::ApplicationClosed(close)) => code_name(close.error_code.into_inner()),
        Some(_) => "ConnectionLost",
        None => "TransferFailed",
    }
}

// ---------------------------------------------------------------------------------------------
// Sending
// ---------------------------------------------------------------------------------------------

/// Serves one connection from a node that wants a file.
pub async fn serve(transfers: Arc<Transfers>, conn: Connection, queue: mpsc::Sender<Incoming>) {
    let remote = conn.remote_id();
    let upload = transfers.next_upload.fetch_add(1, Ordering::Relaxed);

    // Registered before anything is sent, so a node that stops being trusted can be cut off.
    transfers.uploads.lock().unwrap().insert(upload, (remote, conn.clone()));
    if let Err(err) = send_file(&transfers, &conn, remote, upload, &queue).await {
        tracing::debug!("upload to {remote} ended: {err:#}");
        // Anything not already explained: the other node went away, or cancelled.
        transfers.upload_failed(upload, "Stopped", format!("{err:#}"));
    }
    transfers.uploads.lock().unwrap().remove(&upload);
}

async fn send_file(transfers: &Transfers, conn: &Connection, remote: EndpointId, upload: u64, queue: &mpsc::Sender<Incoming>) -> Result<()> {
    let mut paths = PathMonitor::spawn(conn);
    let (mut send, mut recv) = conn.accept_bi().await.map_err(|e| anyhow!("accept_bi: {e}"))?;
    let request: FileRequest = read_json(&mut recv).await?;

    // Nodearr decides, on every request, whether this node may have this file right now.
    let (reply, answer) = oneshot::channel();
    queue
        .send(Incoming { remote, kind: IncomingKind::Transfer { file: request.file.clone() }, reply })
        .await
        .map_err(|_| anyhow!("shutting down"))?;

    let path = match tokio::time::timeout(Duration::from_secs(30), answer).await {
        Ok(Ok(Reply::File(path, known))) => {
            if let Some(known) = known {
                transfers.remember(&path, known);
            }
            path
        }
        Ok(Ok(Reply::Reject(reason))) => {
            let (code, name) = refusal(reason.as_deref());
            conn.close(VarInt::from_u32(code), name.as_bytes());
            bail!("refused: {name}");
        }
        _ => {
            conn.close(VarInt::from_u32(CODE_NOT_AUTHORIZED), b"NotAuthorized");
            bail!("Nodearr did not answer");
        }
    };

    // From here on it is an upload worth showing. A request that was refused never was one.
    transfers.upload_states.lock().unwrap().insert(
        upload,
        UploadState {
            id: upload.to_string(),
            remote_node_id: remote.to_string(),
            file: request.file.clone(),
            path: path.display().to_string(),
            state: "preparing",
            bytes_sent: 0,
            size: 0,
            failure: None,
            finished: None,
        },
    );

    // The hash is fixed before any data moves. Usually it was worked out in the background already.
    let FileHash { size, blake3, .. } = match transfers.hash(&path).await {
        Ok(hashed) => hashed,
        Err(err) => {
            transfers.upload_failed(upload, "NotFound", format!("{err:#}"));
            conn.close(VarInt::from_u32(CODE_NOT_FOUND), b"NotFound");
            bail!("cannot read {}: {err:#}", path.display());
        }
    };

    let resume = request.offset > 0 && request.offset <= size && request.if_hash.as_deref() == Some(blake3.as_str());
    let offset = if resume { request.offset } else { 0 };
    write_json(&mut send, &FileHeader { size, blake3, offset }).await?;
    transfers.upload_update(upload, |u| {
        u.state = "sending";
        u.size = size;
        u.bytes_sent = offset;
    });

    let mut source = tokio::fs::File::open(&path).await?;
    source.seek(std::io::SeekFrom::Start(offset)).await?;

    let mut buffer = vec![0u8; CHUNK];
    let mut sent = 0u64;

    loop {
        let read = source.read(&mut buffer).await?;
        if read == 0 {
            break;
        }

        if !paths.is_direct() && !paths.wait_direct(transfers.direct_timeout).await {
            let (code, name): (u32, &[u8]) = if sent == 0 { (CODE_NO_DIRECT_ROUTE, b"NoDirectRoute") } else { (CODE_DIRECT_PATH_LOST, b"DirectPathLost") };
            transfers.upload_failed(upload, if sent == 0 { "NoDirectRoute" } else { "DirectPathLost" }, format!("no direct path within {}s", transfers.direct_timeout.as_secs()));
            let _ = send.reset(VarInt::from_u32(code));
            conn.close(VarInt::from_u32(code), name);
            bail!("no direct path within {}s", transfers.direct_timeout.as_secs());
        }

        tokio::select! {
            written = send.write_all(&buffer[..read]) => written.map_err(|e| anyhow!("write: {e}"))?,
            _ = paths.lost_direct() => {
                transfers.upload_failed(upload, "DirectPathLost", "the direct path to the other node went away");
                // Drop everything queued for this stream so none of it is sent again over a relay.
                let _ = send.reset(VarInt::from_u32(CODE_DIRECT_PATH_LOST));
                conn.close(VarInt::from_u32(CODE_DIRECT_PATH_LOST), b"DirectPathLost");
                bail!("selected path stopped being direct");
            }
        }
        sent += read as u64;
        transfers.upload_update(upload, |u| u.bytes_sent = offset + sent);

        if transfers.upload_limit > 0 {
            tokio::time::sleep(Duration::from_secs_f64(read as f64 / transfers.upload_limit as f64)).await;
        }
    }

    send.finish().map_err(|e| anyhow!("finish: {e}"))?;
    // The receiver answers with one byte once it has checked the hash.
    let verdict = tokio::time::timeout(Duration::from_secs(120), recv.read_to_end(1)).await;
    conn.close(VarInt::from_u32(CODE_DONE), b"done");

    match verdict {
        Ok(Ok(answer)) if answer == [0] => {
            transfers.upload_failed(upload, "VerificationFailed", "the other node said what arrived did not match the hash");
        }
        _ => transfers.upload_update(upload, |u| {
            u.state = "completed";
            u.bytes_sent = size;
        }),
    }
    Ok(())
}

// ---------------------------------------------------------------------------------------------
// Receiving
// ---------------------------------------------------------------------------------------------

pub struct DownloadRequest {
    pub id: String,
    pub target: EndpointAddr,
    pub file: String,
    /// Where the bytes are written. Nodearr moves the file into place after it is verified.
    pub dest: PathBuf,
    /// The hash a previous attempt was told. With it, what is already at `dest` is kept.
    pub expected_hash: Option<String>,
}

pub fn start(transfers: &Arc<Transfers>, endpoint: Endpoint, request: DownloadRequest) {
    let mut downloads = transfers.downloads.lock().unwrap();

    if let Some(existing) = downloads.get(&request.id) {
        // Asked twice. Leave a transfer that is under way alone.
        if matches!(existing.state.lock().unwrap().state, "connecting" | "receiving") {
            return;
        }
    }

    let state = Arc::new(Mutex::new(DownloadState { id: request.id.clone(), state: "connecting", bytes_done: 0, size: 0, blake3: None, failure: None }));
    let task_state = state.clone();
    let id = request.id.clone();

    let task = tokio::spawn(async move {
        if let Err(failure) = receive_file(&endpoint, &request, &task_state).await {
            let mut state = task_state.lock().unwrap();
            state.state = "failed";
            state.failure = Some(failure);
        }
    });

    downloads.insert(id, Download { state, abort: task.abort_handle() });
}

fn failure(kind: &'static str, message: impl Into<String>) -> Failure {
    Failure { kind, message: message.into() }
}

async fn receive_file(endpoint: &Endpoint, request: &DownloadRequest, state: &Mutex<DownloadState>) -> std::result::Result<(), Failure> {
    let disk = |err: std::io::Error| failure("DiskError", err.to_string());

    let conn = match tokio::time::timeout(Duration::from_secs(30), endpoint.connect(request.target.clone(), TRANSFER_ALPN)).await {
        Ok(Ok(conn)) => conn,
        Ok(Err(err)) => return Err(failure("NoRoute", format!("{err:#}"))),
        Err(_) => return Err(failure("NoRoute", "timed out connecting")),
    };
    let paths = PathMonitor::spawn(&conn);

    // What a previous attempt left is only worth keeping if its hash is known.
    let held = match (&request.expected_hash, tokio::fs::metadata(&request.dest).await) {
        (Some(_), Ok(metadata)) if metadata.is_file() => metadata.len(),
        _ => 0,
    };

    let (mut send, mut recv) = conn.open_bi().await.map_err(|err| failure(close_name(&conn), format!("open_bi: {err}")))?;
    write_json(&mut send, &FileRequest { file: request.file.clone(), offset: held, if_hash: request.expected_hash.clone() })
        .await
        .map_err(|err| failure(close_name(&conn), format!("request: {err:#}")))?;

    let header: FileHeader = match tokio::time::timeout(HEADER_TIMEOUT, read_json(&mut recv)).await {
        Ok(Ok(header)) => header,
        Ok(Err(err)) => return Err(failure(close_name(&conn), format!("header: {err:#}"))),
        Err(_) => return Err(failure("TransferFailed", "the node did not start sending")),
    };

    if let Some(parent) = request.dest.parent() {
        tokio::fs::create_dir_all(parent).await.map_err(disk)?;
    }

    let resumed = header.offset > 0 && header.offset == held;
    let mut hasher = blake3::Hasher::new();
    let mut file = if resumed {
        // The hash covers the whole file, so what is already here is read back into it.
        let dest = request.dest.clone();
        let offset = header.offset;
        hasher = tokio::task::spawn_blocking(move || hash_start(&dest, offset))
            .await
            .map_err(|err| failure("DiskError", err.to_string()))?
            .map_err(disk)?;
        tokio::fs::OpenOptions::new().append(true).open(&request.dest).await.map_err(disk)?
    } else if header.offset > 0 {
        return Err(failure("TransferFailed", "the node resumed from the wrong place"));
    } else {
        tokio::fs::File::create(&request.dest).await.map_err(disk)?
    };

    let mut bytes = header.offset;
    {
        let mut state = state.lock().unwrap();
        state.state = "receiving";
        state.size = header.size;
        state.bytes_done = bytes;
        state.blake3 = Some(header.blake3.clone());
    }

    let mut buffer = vec![0u8; CHUNK];
    loop {
        match recv.read(&mut buffer).await {
            Ok(Some(read)) => {
                if !paths.is_direct() {
                    // The sender should never let this happen. Refuse the data if it does.
                    let _ = recv.stop(VarInt::from_u32(CODE_RELAYED_MEDIA));
                    conn.close(VarInt::from_u32(CODE_RELAYED_MEDIA), b"RelayedMedia");
                    return Err(failure("RelayedMediaRefused", "file data arrived while the selected path was a relay"));
                }
                hasher.update(&buffer[..read]);
                file.write_all(&buffer[..read]).await.map_err(disk)?;
                bytes += read as u64;
                state.lock().unwrap().bytes_done = bytes;
            }
            Ok(None) => break,
            Err(err) => {
                // What arrived stays on disk, so the next attempt can carry on from it.
                let _ = file.flush().await;
                let kind = match &err {
                    ReadError::Reset(code) => code_name(code.into_inner()),
                    _ => close_name(&conn),
                };
                return Err(failure(kind, err.to_string()));
            }
        }
    }
    file.flush().await.map_err(disk)?;
    file.sync_all().await.map_err(disk)?;
    drop(file);

    let verified = bytes == header.size && hasher.finalize().to_hex().to_string() == header.blake3;
    let _ = send.write_all(&[verified as u8]).await;
    let _ = send.finish();
    let _ = tokio::time::timeout(Duration::from_secs(5), send.stopped()).await;
    conn.close(VarInt::from_u32(CODE_DONE), b"done");

    if !verified {
        // A file that does not match is worth nothing, not even as a start.
        let _ = tokio::fs::remove_file(&request.dest).await;
        return Err(failure("VerificationFailed", format!("received {bytes} of {} bytes and the BLAKE3 hash did not match", header.size)));
    }

    state.lock().unwrap().state = "completed";
    Ok(())
}
