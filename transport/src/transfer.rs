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
    time::{Duration, SystemTime},
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

struct Download {
    state: Arc<Mutex<DownloadState>>,
    abort: AbortHandle,
}

struct CachedHash {
    size: u64,
    modified: SystemTime,
    blake3: String,
}

pub struct Transfers {
    direct_timeout: Duration,
    /// Bytes per second each upload may send. Zero means no limit.
    upload_limit: u64,
    downloads: Mutex<HashMap<String, Download>>,
    uploads: Mutex<HashMap<u64, (EndpointId, Connection)>>,
    next_upload: AtomicU64,
    hashes: Mutex<HashMap<PathBuf, CachedHash>>,
}

impl Transfers {
    pub fn new(direct_timeout: Duration, upload_limit: u64) -> Self {
        Self {
            direct_timeout,
            upload_limit,
            downloads: Mutex::new(HashMap::new()),
            uploads: Mutex::new(HashMap::new()),
            next_upload: AtomicU64::new(1),
            hashes: Mutex::new(HashMap::new()),
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

    /// Cuts every upload to a node. Used when Nodearr stops trusting it part way through.
    pub fn disconnect(&self, node: EndpointId) -> usize {
        let uploads = self.uploads.lock().unwrap();
        let mut closed = 0;
        for (remote, conn) in uploads.values() {
            if *remote == node {
                conn.close(VarInt::from_u32(CODE_REVOKED), b"Revoked");
                closed += 1;
            }
        }
        closed
    }

    /// The size and BLAKE3 hash of a file, worked out once for each version of it.
    async fn hash(&self, path: &Path) -> Result<(u64, String)> {
        let metadata = tokio::fs::metadata(path).await?;
        if !metadata.is_file() {
            bail!("not a file");
        }
        let size = metadata.len();
        let modified = metadata.modified()?;

        if let Some(cached) = self.hashes.lock().unwrap().get(path) {
            if cached.size == size && cached.modified == modified {
                return Ok((size, cached.blake3.clone()));
            }
        }

        let owned = path.to_path_buf();
        let blake3 = tokio::task::spawn_blocking(move || hash_start(&owned, u64::MAX)).await??.finalize().to_hex().to_string();
        self.hashes.lock().unwrap().insert(path.to_path_buf(), CachedHash { size, modified, blake3: blake3.clone() });
        Ok((size, blake3))
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
struct PathMonitor {
    direct: watch::Receiver<bool>,
}

impl PathMonitor {
    fn spawn(conn: &Connection) -> Self {
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

    async fn wait_direct(&mut self, timeout: Duration) -> bool {
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
    if let Err(err) = send_file(&transfers, &conn, remote, &queue).await {
        tracing::debug!("upload to {remote} ended: {err:#}");
    }
    transfers.uploads.lock().unwrap().remove(&upload);
}

async fn send_file(transfers: &Transfers, conn: &Connection, remote: EndpointId, queue: &mpsc::Sender<Incoming>) -> Result<()> {
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
        Ok(Ok(Reply::File(path))) => path,
        Ok(Ok(Reply::Reject(reason))) => {
            let (code, name): (u32, &[u8]) = match reason.as_deref() {
                Some("NotFound") => (CODE_NOT_FOUND, b"NotFound"),
                Some("Revoked") => (CODE_REVOKED, b"Revoked"),
                _ => (CODE_NOT_AUTHORIZED, b"NotAuthorized"),
            };
            conn.close(VarInt::from_u32(code), name);
            bail!("refused: {}", String::from_utf8_lossy(name));
        }
        _ => {
            conn.close(VarInt::from_u32(CODE_NOT_AUTHORIZED), b"NotAuthorized");
            bail!("Nodearr did not answer");
        }
    };

    // The hash is fixed before any data moves.
    let (size, blake3) = match transfers.hash(&path).await {
        Ok(hashed) => hashed,
        Err(err) => {
            conn.close(VarInt::from_u32(CODE_NOT_FOUND), b"NotFound");
            bail!("cannot read {}: {err:#}", path.display());
        }
    };

    let resume = request.offset > 0 && request.offset <= size && request.if_hash.as_deref() == Some(blake3.as_str());
    let offset = if resume { request.offset } else { 0 };
    write_json(&mut send, &FileHeader { size, blake3, offset }).await?;

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
            let _ = send.reset(VarInt::from_u32(code));
            conn.close(VarInt::from_u32(code), name);
            bail!("no direct path within {}s", transfers.direct_timeout.as_secs());
        }

        tokio::select! {
            written = send.write_all(&buffer[..read]) => written.map_err(|e| anyhow!("write: {e}"))?,
            _ = paths.lost_direct() => {
                // Drop everything queued for this stream so none of it is sent again over a relay.
                let _ = send.reset(VarInt::from_u32(CODE_DIRECT_PATH_LOST));
                conn.close(VarInt::from_u32(CODE_DIRECT_PATH_LOST), b"DirectPathLost");
                bail!("selected path stopped being direct");
            }
        }
        sent += read as u64;

        if transfers.upload_limit > 0 {
            tokio::time::sleep(Duration::from_secs_f64(read as f64 / transfers.upload_limit as f64)).await;
        }
    }

    send.finish().map_err(|e| anyhow!("finish: {e}"))?;
    // The receiver answers with one byte once it has checked the hash.
    let _ = tokio::time::timeout(Duration::from_secs(120), recv.read_to_end(1)).await;
    conn.close(VarInt::from_u32(CODE_DONE), b"done");
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
