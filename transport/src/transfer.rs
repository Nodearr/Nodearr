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
    endpoint::{Connection, ConnectionError, ReadError, RecvStream, SendStream, VarInt},
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
    /// The receiving node's transfer id, used to keep retries in one upload history row.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    transfer: Option<String>,
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
    pub attempts: u32,
    pub last_failure: Option<Failure>,
    #[serde(skip)]
    transfer: Option<String>,
    #[serde(skip)]
    blake3: Option<String>,
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
    /// Bytes per second each download may take. Zero means no limit.
    download_limit: u64,
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
    pub fn new(direct_timeout: Duration, upload_limit: u64, download_limit: u64) -> Self {
        Self {
            direct_timeout,
            upload_limit,
            download_limit,
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

    fn track_upload_attempt(&self, id: u64, remote: EndpointId, request: &FileRequest, path: &Path, blake3: &str) -> u64 {
        let mut states = self.upload_states.lock().unwrap();
        let remote_id = remote.to_string();
        let matching = states.iter().find_map(|(old_id, old)| {
            (*old_id != id && is_retry_request(old, &remote_id, request)).then_some(*old_id)
        });

        if let Some(previous_id) = matching {
            states.remove(&id);
            let previous = states.get_mut(&previous_id).expect("matched upload remains present");
            previous.attempts += 1;
            previous.last_failure = previous.failure.take();
            previous.state = "preparing";
            previous.path = path.display().to_string();
            previous.failure = None;
            previous.bytes_sent = request.offset;
            previous.size = 0;
            previous.blake3 = Some(blake3.to_string());
            previous.transfer = request.transfer.clone().or_else(|| previous.transfer.clone());
            previous.finished = None;
            return previous_id;
        }

        if let Some(upload) = states.get_mut(&id) {
            upload.blake3 = Some(blake3.to_string());
        }
        id
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

#[derive(Clone, Debug, PartialEq, Eq)]
enum SelectedPath {
    None,
    Direct { local: String, remote: String },
    Relay { local: String, remote: String },
}

impl SelectedPath {
    fn is_direct(&self) -> bool {
        matches!(self, Self::Direct { .. })
    }

    fn description(&self) -> String {
        match self {
            Self::None => "none".to_string(),
            Self::Direct { local, remote } => format!("direct {local} -> {remote}"),
            Self::Relay { local, remote } => format!("relay {local} -> {remote}"),
        }
    }
}

fn is_retry_request(old: &UploadState, remote: &str, request: &FileRequest) -> bool {
    if old.remote_node_id != remote {
        return false;
    }

    let correlated = request.transfer.as_ref().is_some_and(|transfer| old.transfer.as_ref() == Some(transfer));
    let legacy_resume = request.transfer.is_none()
        && old.transfer.is_none()
        && old.file == request.file
        && old.failure.is_some()
        && request.offset > 0
        && request.offset == old.bytes_sent
        && request.if_hash.as_deref() == old.blake3.as_deref();

    correlated || legacy_resume
}

/// Follows a connection's selected path and keeps route types distinct during path changes.
pub(crate) struct PathMonitor {
    selected: watch::Receiver<SelectedPath>,
}

impl PathMonitor {
    pub(crate) fn spawn(conn: &Connection) -> Self {
        let mut events = conn.path_events();
        let (tx, rx) = watch::channel(selected_path_state(conn));
        let conn = conn.clone();
        let remote = conn.remote_id();

        tokio::spawn(async move {
            while let Some(event) = events.next().await {
                let state = selected_path_state(&conn);
                tracing::info!(remote = %remote, event = ?event, selected = %state.description(), "iroh path event");
                if tx.send(state).is_err() {
                    return;
                }
            }
        });

        Self { selected: rx }
    }

    fn is_direct(&self) -> bool {
        self.selected.borrow().is_direct()
    }

    fn selected(&self) -> SelectedPath {
        self.selected.borrow().clone()
    }

    pub(crate) async fn wait_direct(&mut self, timeout: Duration) -> bool {
        tokio::time::timeout(timeout, async {
            loop {
                match self.selected() {
                    SelectedPath::Direct { .. } => return true,
                    SelectedPath::Relay { .. } | SelectedPath::None => {
                        if self.selected.changed().await.is_err() {
                            return false;
                        }
                    }
                }
            }
        }).await.unwrap_or(false)
    }

    async fn wait_direct_after_loss(&mut self, timeout: Duration) -> bool {
        tokio::time::timeout(timeout, async {
            loop {
                match self.selected() {
                    SelectedPath::Direct { .. } => return true,
                    SelectedPath::Relay { .. } => return false,
                    SelectedPath::None => {
                        if self.selected.changed().await.is_err() {
                            return false;
                        }
                    }
                }
            }
        }).await.unwrap_or(false)
    }

    async fn changed(&mut self) -> bool {
        self.selected.changed().await.is_ok()
    }
}

fn selected_path_state(conn: &Connection) -> SelectedPath {
    conn.paths().iter().find(|p| p.is_selected()).map(|path| {
        let local = format!("{:?}", path.local_addr());
        let remote = match path.remote_addr() {
            iroh::TransportAddr::Ip(ip) => ip.to_string(),
            iroh::TransportAddr::Relay(url) => url.to_string(),
            other => format!("{other:?}"),
        };
        if path.is_ip() { SelectedPath::Direct { local, remote } } else { SelectedPath::Relay { local, remote } }
    }).unwrap_or(SelectedPath::None)
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
    let mut tracked_upload = upload;
    if let Err(err) = send_file(&transfers, &conn, remote, &mut tracked_upload, &queue).await {
        tracing::info!(remote = %remote, upload = tracked_upload, close_reason = ?conn.close_reason(), error = %format!("{err:#}"), "upload stream ended");
        // Anything not already explained: the other node went away, or cancelled.
        transfers.upload_failed(tracked_upload, "Stopped", format!("{err:#}"));
    }
    transfers.uploads.lock().unwrap().remove(&upload);
    transfers.uploads.lock().unwrap().remove(&tracked_upload);
    tracing::info!(remote = %remote, upload = tracked_upload, close_reason = ?conn.close_reason(), "upload connection closed");
}

async fn send_file(transfers: &Transfers, conn: &Connection, remote: EndpointId, upload: &mut u64, queue: &mpsc::Sender<Incoming>) -> Result<()> {
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
        *upload,
        UploadState {
            id: upload.to_string(),
            remote_node_id: remote.to_string(),
            file: request.file.clone(),
            path: path.display().to_string(),
            state: "preparing",
            bytes_sent: 0,
            size: 0,
            failure: None,
            attempts: 1,
            last_failure: None,
            transfer: request.transfer.clone(),
            blake3: None,
            finished: None,
        },
    );

    // The hash is fixed before any data moves. Usually it was worked out in the background already.
    let FileHash { size, blake3, .. } = match transfers.hash(&path).await {
        Ok(hashed) => hashed,
        Err(err) => {
            transfers.upload_failed(*upload, "NotFound", format!("{err:#}"));
            conn.close(VarInt::from_u32(CODE_NOT_FOUND), b"NotFound");
            bail!("cannot read {}: {err:#}", path.display());
        }
    };

    let previous_upload = *upload;
    *upload = transfers.track_upload_attempt(previous_upload, remote, &request, &path, &blake3);
    if *upload != previous_upload {
        let mut uploads = transfers.uploads.lock().unwrap();
        if let Some((_, connection)) = uploads.remove(&previous_upload) {
            uploads.insert(*upload, (remote, connection));
        }
    }

    let resume = request.offset > 0 && request.offset <= size && request.if_hash.as_deref() == Some(blake3.as_str());
    let offset = if resume { request.offset } else { 0 };
    write_json(&mut send, &FileHeader { size, blake3, offset }).await?;
    transfers.upload_update(*upload, |u| {
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
            let no_bytes_sent = offset + sent == 0;
            let (code, name): (u32, &[u8]) = if no_bytes_sent { (CODE_NO_DIRECT_ROUTE, b"NoDirectRoute") } else { (CODE_DIRECT_PATH_LOST, b"DirectPathLost") };
            transfers.upload_failed(*upload, if no_bytes_sent { "NoDirectRoute" } else { "DirectPathLost" }, format!("selected path is {}; no direct path within {}s", paths.selected().description(), transfers.direct_timeout.as_secs()));
            let _ = send.reset(VarInt::from_u32(code));
            conn.close(VarInt::from_u32(code), name);
            bail!("no direct path within {}s", transfers.direct_timeout.as_secs());
        }

        let mut path_lost = false;
        {
            let write = send.write_all(&buffer[..read]);
            tokio::pin!(write);
            loop {
                tokio::select! {
                    written = &mut write => {
                        written.map_err(|e| anyhow!("write: {e}"))?;
                        break;
                    }
                    changed = paths.changed() => {
                        if !changed { bail!("path monitor stopped"); }
                        match paths.selected() {
                            SelectedPath::Direct { .. } => continue,
                            SelectedPath::None if paths.wait_direct_after_loss(transfers.direct_timeout).await => continue,
                            SelectedPath::None | SelectedPath::Relay { .. } => {
                                path_lost = true;
                                break;
                            }
                        }
                    }
                }
            }
        }
        if path_lost {
            let no_bytes_sent = offset + sent == 0;
            let (code, name): (u32, &[u8]) = if no_bytes_sent { (CODE_NO_DIRECT_ROUTE, b"NoDirectRoute") } else { (CODE_DIRECT_PATH_LOST, b"DirectPathLost") };
            let kind = if no_bytes_sent { "NoDirectRoute" } else { "DirectPathLost" };
            transfers.upload_failed(*upload, kind, format!("selected path is {}; no direct path within {}s", paths.selected().description(), transfers.direct_timeout.as_secs()));
            let _ = send.reset(VarInt::from_u32(code));
            conn.close(VarInt::from_u32(code), name);
            bail!("selected path is not direct: {}", paths.selected().description());
        }
        sent += read as u64;
        transfers.upload_update(*upload, |u| u.bytes_sent = offset + sent);

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
            // The hash given was likely out of date: the file changed without its size or modified
            // time changing. It is worked out afresh next time. Nodearr drops its copy too.
            transfers.hashes.lock().unwrap().remove(&path);
            transfers.upload_failed(*upload, "VerificationFailed", "the other node said what arrived did not match the hash");
        }
        _ => transfers.upload_update(*upload, |u| {
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
    let limit = transfers.download_limit;

    let task = tokio::spawn(async move {
        if let Err(failure) = receive_file(&endpoint, &request, &task_state, limit).await {
            tracing::info!(transfer = %request.id, kind = failure.kind, message = %failure.message, "download stream ended");
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

/// `limit` is the most bytes per second it may take, zero for no limit.
async fn receive_file(endpoint: &Endpoint, request: &DownloadRequest, state: &Mutex<DownloadState>, limit: u64) -> std::result::Result<(), Failure> {
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
    write_json(&mut send, &FileRequest { file: request.file.clone(), offset: held, if_hash: request.expected_hash.clone(), transfer: Some(request.id.clone()) })
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

                // Reading more slowly holds the sender back too: QUIC's flow control stops it
                // once what is not yet read fills the window.
                if limit > 0 {
                    tokio::time::sleep(Duration::from_secs_f64(read as f64 / limit as f64)).await;
                }
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

#[cfg(test)]
mod tests {
    use super::*;

    fn prior_upload() -> UploadState {
        UploadState {
            id: "1".into(),
            remote_node_id: "peer-a".into(),
            file: "movie.mkv".into(),
            path: "/library/movie.mkv".into(),
            state: "failed",
            bytes_sent: 512,
            size: 1024,
            failure: Some(Failure { kind: "DirectPathLost", message: "route changed".into() }),
            attempts: 1,
            last_failure: None,
            transfer: Some("download-a".into()),
            blake3: Some("hash-a".into()),
            finished: Some(Instant::now()),
        }
    }

    #[test]
    fn direct_path_changes_keep_the_direct_state_and_distinguish_addresses() {
        let first = SelectedPath::Direct { local: "192.168.1.10:1".into(), remote: "192.168.1.20:2".into() };
        let second = SelectedPath::Direct { local: "10.0.0.10:1".into(), remote: "10.0.0.20:2".into() };

        assert!(first.is_direct());
        assert!(second.is_direct());
        assert_ne!(first, second);
        assert!(!SelectedPath::None.is_direct());
        assert!(!SelectedPath::Relay { local: "relay".into(), remote: "relay".into() }.is_direct());
    }

    #[tokio::test]
    async fn a_missing_route_waits_for_direct_recovery_but_a_selected_relay_is_rejected() {
        let (tx, rx) = watch::channel(SelectedPath::None);
        let mut monitor = PathMonitor { selected: rx };
        let recovery = tokio::spawn(async move {
            tokio::time::sleep(Duration::from_millis(10)).await;
            tx.send(SelectedPath::Direct { local: "192.168.1.10:1".into(), remote: "192.168.1.20:2".into() }).unwrap();
        });

        assert!(monitor.wait_direct(Duration::from_secs(1)).await);
        recovery.await.unwrap();

        let (_tx, rx) = watch::channel(SelectedPath::None);
        let mut monitor = PathMonitor { selected: rx };
        assert!(!monitor.wait_direct(Duration::from_millis(10)).await);

        let (tx, rx) = watch::channel(SelectedPath::Relay { local: "relay".into(), remote: "relay".into() });
        let mut monitor = PathMonitor { selected: rx };
        let recovery = tokio::spawn(async move {
            tokio::time::sleep(Duration::from_millis(10)).await;
            tx.send(SelectedPath::Direct { local: "192.168.1.10:1".into(), remote: "192.168.1.20:2".into() }).unwrap();
        });
        assert!(monitor.wait_direct(Duration::from_secs(1)).await);
        recovery.await.unwrap();

        let (_tx, rx) = watch::channel(SelectedPath::Relay { local: "relay".into(), remote: "relay".into() });
        let mut monitor = PathMonitor { selected: rx };
        assert!(!monitor.wait_direct_after_loss(Duration::from_secs(1)).await);
    }

    #[test]
    fn old_file_requests_without_a_transfer_id_are_readable_and_optional_ids_are_omitted() {
        let old: FileRequest = serde_json::from_str(r#"{"file":"movie.mkv","offset":512,"if_hash":"hash-a"}"#).unwrap();
        assert!(old.transfer.is_none());

        let new = FileRequest { file: "movie.mkv".into(), offset: 512, if_hash: Some("hash-a".into()), transfer: None };
        assert!(serde_json::to_value(new).unwrap().get("transfer").is_none());
    }

    #[test]
    fn retries_group_by_transfer_id_and_peer_and_legacy_retries_require_matching_resume_data() {
        let prior = prior_upload();
        let same = FileRequest { file: "movie.mkv".into(), offset: 0, if_hash: None, transfer: Some("download-a".into()) };
        assert!(is_retry_request(&prior, "peer-a", &same));
        assert!(!is_retry_request(&prior, "peer-b", &same));

        let separate = FileRequest { transfer: Some("download-b".into()), ..same };
        assert!(!is_retry_request(&prior, "peer-a", &separate));

        let mut legacy = prior;
        legacy.transfer = None;
        let resumed = FileRequest { file: "movie.mkv".into(), offset: 512, if_hash: Some("hash-a".into()), transfer: None };
        assert!(is_retry_request(&legacy, "peer-a", &resumed));
        let wrong_hash = FileRequest { if_hash: Some("different".into()), ..resumed };
        assert!(!is_retry_request(&legacy, "peer-a", &wrong_hash));
    }
}
