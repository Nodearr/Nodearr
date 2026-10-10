//! S2 spike: send one file between two iroh endpoints, and prove what did and did not
//! travel through a relay.
//!
//! The provider holds every chunk of file data back until the connection's selected path
//! is a direct IP path. The relay may carry the handshake, the request, the file header
//! and hole-punching coordination, but never file data. If no direct path appears, the
//! transfer fails with `NoDirectRoute` and no file data has left the provider.
//!
//! Both sides print one JSON object per line. The last line is the result, with the
//! bytes each network path carried.

use std::{
    net::SocketAddr,
    path::{Path, PathBuf},
    process::ExitCode,
    str::FromStr,
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};

use anyhow::{anyhow, bail, Context, Result};
use clap::{Parser, Subcommand, ValueEnum};
use iroh::{
    endpoint::{presets, Connection, ConnectionError, PathEvent, ReadError, RecvStream, SendStream, VarInt},
    Endpoint, EndpointAddr, EndpointId, RelayMap, RelayMode, RelayUrl, SecretKey, TransportAddr,
};
use n0_future::StreamExt;
use serde::{Deserialize, Serialize};
use serde_json::json;
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    sync::watch,
};

const ALPN: &[u8] = b"nodearr/spike-transfer/0";
const CHUNK: usize = 256 * 1024;

const CODE_OK: u32 = 0;
const CODE_NO_DIRECT_ROUTE: u32 = 1;
const CODE_NOT_AUTHORIZED: u32 = 2;
const CODE_DIRECT_PATH_LOST: u32 = 3;
const CODE_RELAYED_MEDIA: u32 = 4;

#[derive(Parser)]
#[command(about = "Nodearr S2 spike: direct-only file transfer over iroh")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand)]
enum Command {
    /// Offer one file to authorized peers.
    Provide {
        #[arg(long)]
        file: PathBuf,
        /// Endpoint IDs allowed to fetch. Anyone may fetch when this is empty.
        #[arg(long)]
        allow: Vec<EndpointId>,
        /// Exit after serving one connection.
        #[arg(long)]
        once: bool,
        #[command(flatten)]
        net: NetArgs,
    },
    /// Fetch the provider's file.
    Fetch {
        /// The ticket the provider printed: `<endpoint id>;<relay url>;<ip:port>,<ip:port>`.
        #[arg(long)]
        ticket: String,
        #[arg(long)]
        out: PathBuf,
        #[command(flatten)]
        net: NetArgs,
    },
}

#[derive(clap::Args, Clone)]
struct NetArgs {
    /// Which relays this endpoint uses.
    #[arg(long, value_enum, default_value_t = Relay::N0)]
    relay: Relay,
    /// Relay URL for `--relay custom`, for example http://localhost:3340.
    #[arg(long)]
    relay_url: Option<RelayUrl>,
    /// Bind no IP sockets, so only relay paths can exist. Simulates a peer with no direct route.
    #[arg(long)]
    no_ip: bool,
    /// Whether file data may use a relay path. `allow-relay` exists only as a control experiment.
    #[arg(long, value_enum, default_value_t = MediaPolicy::DirectOnly)]
    media: MediaPolicy,
    /// How long to wait for a direct path before giving up.
    #[arg(long, default_value_t = 20)]
    direct_timeout: u64,
    /// File holding this endpoint's secret key. Created when missing.
    #[arg(long)]
    key_file: Option<PathBuf>,
}

#[derive(ValueEnum, Clone, Copy, PartialEq, Eq, Debug)]
enum Relay {
    /// No relay at all. Peers must already know a reachable IP address for each other.
    Disabled,
    /// n0's public relays.
    N0,
    /// The relay given by --relay-url.
    Custom,
}

#[derive(ValueEnum, Clone, Copy, PartialEq, Eq, Debug)]
enum MediaPolicy {
    DirectOnly,
    AllowRelay,
}

#[derive(Serialize, Deserialize)]
struct Header {
    size: u64,
    blake3: String,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum Selected {
    None,
    Relay,
    Direct,
}

#[derive(Serialize, Clone)]
struct PathReport {
    remote: String,
    kind: &'static str,
    open_at_end: bool,
    tx_bytes: u64,
    rx_bytes: u64,
    stream_frames_tx: u64,
    stream_frames_rx: u64,
}

/// Follows one connection's paths: which kind is selected right now, and what each path carried.
struct PathMonitor {
    selected: watch::Receiver<Selected>,
    closed: Arc<Mutex<Vec<PathReport>>>,
}

impl PathMonitor {
    fn spawn(conn: &Connection, role: &'static str) -> Self {
        let (tx, rx) = watch::channel(selected_kind(conn));
        let closed = Arc::new(Mutex::new(Vec::new()));
        let task_closed = closed.clone();
        let conn = conn.clone();

        tokio::spawn(async move {
            let mut events = conn.path_events();
            while let Some(event) = events.next().await {
                match event {
                    PathEvent::Opened { remote_addr, .. } => {
                        emit(json!({ "event": "path_opened", "role": role, "remote": addr_string(&remote_addr) }));
                    }
                    PathEvent::Selected { remote_addr, .. } => {
                        emit(json!({ "event": "path_selected", "role": role, "remote": addr_string(&remote_addr) }));
                    }
                    PathEvent::Closed { remote_addr, last_stats, .. } => {
                        emit(json!({ "event": "path_closed", "role": role, "remote": addr_string(&remote_addr) }));
                        task_closed.lock().unwrap().push(PathReport {
                            remote: addr_string(&remote_addr),
                            kind: addr_kind(&remote_addr),
                            open_at_end: false,
                            tx_bytes: last_stats.udp_tx.bytes,
                            rx_bytes: last_stats.udp_rx.bytes,
                            stream_frames_tx: last_stats.frame_tx.stream,
                            stream_frames_rx: last_stats.frame_rx.stream,
                        });
                    }
                    _ => {}
                }
                // Recompute from the connection rather than trusting event order.
                let _ = tx.send(selected_kind(&conn));
            }
        });

        Self { selected: rx, closed }
    }

    fn is_direct(&self) -> bool {
        *self.selected.borrow() == Selected::Direct
    }

    async fn wait_direct(&mut self, timeout: Duration) -> bool {
        tokio::time::timeout(timeout, self.selected.wait_for(|s| *s == Selected::Direct))
            .await
            .map(|r| r.is_ok())
            .unwrap_or(false)
    }

    /// Resolves when the selected path stops being direct.
    async fn lost_direct(&mut self) {
        let _ = self.selected.wait_for(|s| *s != Selected::Direct).await;
    }

    fn report(&self, conn: &Connection) -> Vec<PathReport> {
        let mut paths = self.closed.lock().unwrap().clone();
        for path in conn.paths().iter() {
            let stats = path.stats();
            paths.push(PathReport {
                remote: addr_string(path.remote_addr()),
                kind: addr_kind(path.remote_addr()),
                open_at_end: true,
                tx_bytes: stats.udp_tx.bytes,
                rx_bytes: stats.udp_rx.bytes,
                stream_frames_tx: stats.frame_tx.stream,
                stream_frames_rx: stats.frame_rx.stream,
            });
        }
        paths
    }
}

fn selected_kind(conn: &Connection) -> Selected {
    match conn.paths().iter().find(|p| p.is_selected()) {
        Some(p) if p.is_ip() => Selected::Direct,
        Some(_) => Selected::Relay,
        None => Selected::None,
    }
}

fn addr_kind(addr: &TransportAddr) -> &'static str {
    if addr.is_ip() {
        "direct"
    } else if addr.is_relay() {
        "relay"
    } else {
        "custom"
    }
}

fn addr_string(addr: &TransportAddr) -> String {
    match addr {
        TransportAddr::Ip(ip) => ip.to_string(),
        TransportAddr::Relay(url) => url.to_string(),
        other => format!("{other:?}"),
    }
}

fn emit(value: serde_json::Value) {
    println!("{value}");
}

/// The final line of output for one transfer.
fn result(role: &str, outcome: &str, detail: String, media_bytes: u64, relayed_media_bytes: u64, started: Instant, paths: Vec<PathReport>) {
    let sum = |kind: &str, f: fn(&PathReport) -> u64| paths.iter().filter(|p| p.kind == kind).map(f).sum::<u64>();
    emit(json!({
        "event": "result",
        "role": role,
        "outcome": outcome,
        "detail": detail,
        "media_bytes": media_bytes,
        "media_bytes_moved_while_relay_selected": relayed_media_bytes,
        "relay_paths": { "tx_bytes": sum("relay", |p| p.tx_bytes), "rx_bytes": sum("relay", |p| p.rx_bytes),
                         "stream_frames_tx": sum("relay", |p| p.stream_frames_tx), "stream_frames_rx": sum("relay", |p| p.stream_frames_rx) },
        "direct_paths": { "tx_bytes": sum("direct", |p| p.tx_bytes), "rx_bytes": sum("direct", |p| p.rx_bytes),
                          "stream_frames_tx": sum("direct", |p| p.stream_frames_tx), "stream_frames_rx": sum("direct", |p| p.stream_frames_rx) },
        "seconds": started.elapsed().as_secs_f64(),
        "paths": paths,
    }));
}

fn load_key(path: Option<&Path>) -> Result<SecretKey> {
    let Some(path) = path else {
        return Ok(SecretKey::generate());
    };
    if path.exists() {
        let bytes: [u8; 32] = hex::decode(std::fs::read_to_string(path)?.trim())?
            .try_into()
            .map_err(|_| anyhow!("{} does not hold a 32 byte key", path.display()))?;
        return Ok(SecretKey::from_bytes(&bytes));
    }
    let key = SecretKey::generate();
    std::fs::write(path, hex::encode(key.to_bytes()))?;
    Ok(key)
}

async fn bind(net: &NetArgs) -> Result<Endpoint> {
    if net.relay == Relay::N0 && net.media == MediaPolicy::AllowRelay {
        bail!("--media allow-relay would push file data through n0's public relays. Use it only with --relay custom.");
    }
    let key = load_key(net.key_file.as_deref())?;
    // No address lookup service: everything a peer needs is in the ticket.
    let mut builder = Endpoint::builder(presets::Minimal).secret_key(key).alpns(vec![ALPN.to_vec()]);

    builder = match net.relay {
        Relay::Disabled => builder.relay_mode(RelayMode::Disabled),
        Relay::N0 => builder.relay_mode(RelayMode::Default),
        Relay::Custom => {
            let url = net.relay_url.clone().context("--relay custom needs --relay-url")?;
            builder.relay_mode(RelayMode::Custom(RelayMap::from(url)))
        }
    };
    if net.no_ip {
        builder = builder.clear_ip_transports();
    }

    let endpoint = builder.bind().await.map_err(|e| anyhow!("bind failed: {e:#}"))?;
    if net.relay != Relay::Disabled {
        // Wait for a home relay so the ticket can name it.
        let _ = tokio::time::timeout(Duration::from_secs(10), endpoint.online()).await;
    }

    let addr = endpoint.addr();
    let relay = addr.relay_urls().next().map(|u| u.to_string()).unwrap_or_default();
    let ips: Vec<String> = addr.ip_addrs().map(|a| a.to_string()).collect();
    emit(json!({
        "event": "bound", "endpoint_id": endpoint.id().to_string(), "relay_mode": format!("{:?}", net.relay),
        "media_policy": format!("{:?}", net.media), "home_relay": relay, "ip_addrs": ips,
        "ticket": format!("{};{};{}", endpoint.id(), relay, ips.join(",")),
    }));
    Ok(endpoint)
}

fn parse_ticket(ticket: &str) -> Result<EndpointAddr> {
    let parts: Vec<&str> = ticket.split(';').collect();
    if parts.len() != 3 {
        bail!("ticket must be `<endpoint id>;<relay url>;<ip:port>,<ip:port>`");
    }
    let id = EndpointId::from_str(parts[0]).map_err(|e| anyhow!("bad endpoint id: {e}"))?;
    let mut addrs = Vec::new();
    if !parts[1].is_empty() {
        addrs.push(TransportAddr::Relay(RelayUrl::from_str(parts[1]).map_err(|e| anyhow!("bad relay url: {e}"))?));
    }
    for ip in parts[2].split(',').filter(|s| !s.is_empty()) {
        addrs.push(TransportAddr::Ip(SocketAddr::from_str(ip)?));
    }
    Ok(EndpointAddr::from_parts(id, addrs))
}

async fn write_frame<T: Serialize>(send: &mut SendStream, value: &T) -> Result<()> {
    let bytes = serde_json::to_vec(value)?;
    send.write_u32(bytes.len() as u32).await?;
    send.write_all(&bytes).await?;
    Ok(())
}

async fn read_frame<T: for<'de> Deserialize<'de>>(recv: &mut RecvStream) -> Result<T> {
    let len = recv.read_u32().await? as usize;
    if len > 64 * 1024 {
        bail!("frame too large");
    }
    let mut bytes = vec![0u8; len];
    recv.read_exact(&mut bytes).await.map_err(|e| anyhow!("{e}"))?;
    Ok(serde_json::from_slice(&bytes)?)
}

fn hash_file(path: &Path) -> Result<(u64, String)> {
    let mut hasher = blake3::Hasher::new();
    let mut file = std::fs::File::open(path)?;
    let size = std::io::copy(&mut file, &mut hasher)?;
    Ok((size, hasher.finalize().to_hex().to_string()))
}

// ---------------------------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------------------------

async fn provide(file: PathBuf, allow: Vec<EndpointId>, once: bool, net: NetArgs) -> Result<ExitCode> {
    // The hash is fixed before any transfer starts.
    let (size, hash) = hash_file(&file)?;
    emit(json!({ "event": "hashed", "size": size, "blake3": hash }));

    let endpoint = bind(&net).await?;
    let mut code = ExitCode::SUCCESS;

    while let Some(incoming) = endpoint.accept().await {
        let conn = match incoming.await {
            Ok(conn) => conn,
            Err(err) => {
                emit(json!({ "event": "handshake_failed", "error": format!("{err:#}") }));
                continue;
            }
        };
        let outcome = serve(&conn, &file, size, &hash, &allow, &net).await;
        if let Err(err) = &outcome {
            emit(json!({ "event": "error", "role": "provide", "error": format!("{err:#}") }));
        }
        if !matches!(outcome, Ok(true)) {
            code = ExitCode::from(1);
        }
        if once {
            break;
        }
    }

    endpoint.close().await;
    Ok(code)
}

/// Returns whether the whole file was sent and acknowledged.
async fn serve(conn: &Connection, file: &Path, size: u64, hash: &str, allow: &[EndpointId], net: &NetArgs) -> Result<bool> {
    let started = Instant::now();
    let remote = conn.remote_id();
    let mut paths = PathMonitor::spawn(conn, "provide");
    emit(json!({ "event": "connected", "role": "provide", "remote": remote.to_string() }));

    // Authorization is checked on every connection, before the request is even read.
    if !allow.is_empty() && !allow.contains(&remote) {
        conn.close(VarInt::from_u32(CODE_NOT_AUTHORIZED), b"NotAuthorized");
        result("provide", "NotAuthorized", format!("{remote} is not on the allow list"), 0, 0, started, paths.report(conn));
        return Ok(false);
    }

    let (mut send, mut recv) = conn.accept_bi().await.map_err(|e| anyhow!("accept_bi: {e}"))?;
    let _request: serde_json::Value = read_frame(&mut recv).await?;
    write_frame(&mut send, &Header { size, blake3: hash.to_string() }).await?;

    let mut source = tokio::fs::File::open(file).await?;
    let mut buffer = vec![0u8; CHUNK];
    let mut sent = 0u64;
    let mut sent_while_relay = 0u64;
    let direct_only = net.media == MediaPolicy::DirectOnly;

    loop {
        let read = source.read(&mut buffer).await?;
        if read == 0 {
            break;
        }

        if direct_only && !paths.is_direct() {
            let waited = Instant::now();
            if !paths.wait_direct(Duration::from_secs(net.direct_timeout)).await {
                let (outcome, code) = if sent == 0 {
                    ("NoDirectRoute", CODE_NO_DIRECT_ROUTE)
                } else {
                    ("DirectPathLost", CODE_DIRECT_PATH_LOST)
                };
                let _ = send.reset(VarInt::from_u32(code));
                conn.close(VarInt::from_u32(code), outcome.as_bytes());
                result("provide", outcome, format!("no direct path within {}s", net.direct_timeout), sent, sent_while_relay, started, paths.report(conn));
                return Ok(false);
            }
            emit(json!({ "event": "direct_path_ready", "role": "provide", "waited_ms": waited.elapsed().as_millis() as u64 }));
        }

        if direct_only {
            tokio::select! {
                written = send.write_all(&buffer[..read]) => written.map_err(|e| anyhow!("write: {e}"))?,
                _ = paths.lost_direct() => {
                    // Drop everything queued for this stream so none of it is retransmitted over the relay.
                    let _ = send.reset(VarInt::from_u32(CODE_DIRECT_PATH_LOST));
                    conn.close(VarInt::from_u32(CODE_DIRECT_PATH_LOST), b"DirectPathLost");
                    result("provide", "DirectPathLost", "selected path stopped being direct mid-transfer".into(), sent, sent_while_relay, started, paths.report(conn));
                    return Ok(false);
                }
            }
        } else {
            if !paths.is_direct() {
                sent_while_relay += read as u64;
            }
            send.write_all(&buffer[..read]).await.map_err(|e| anyhow!("write: {e}"))?;
        }
        sent += read as u64;
    }

    send.finish().map_err(|e| anyhow!("finish: {e}"))?;
    // The fetcher answers with one byte once it has verified the hash.
    let verdict = recv.read_to_end(1).await.map_err(|e| anyhow!("waiting for verdict: {e}"))?;
    let verified = verdict == [1];
    let report = paths.report(conn);
    conn.close(VarInt::from_u32(CODE_OK), b"done");
    result("provide", if verified { "Completed" } else { "VerificationFailed" }, String::new(), sent, sent_while_relay, started, report);
    Ok(verified)
}

// ---------------------------------------------------------------------------------------------
// Fetcher
// ---------------------------------------------------------------------------------------------

async fn fetch(ticket: String, out: PathBuf, net: NetArgs) -> Result<ExitCode> {
    let remote = parse_ticket(&ticket)?;
    let endpoint = bind(&net).await?;
    let started = Instant::now();

    let conn = match tokio::time::timeout(Duration::from_secs(30), endpoint.connect(remote, ALPN)).await {
        Ok(Ok(conn)) => conn,
        Ok(Err(err)) => {
            result("fetch", "NoRoute", format!("connect failed: {err:#}"), 0, 0, started, Vec::new());
            endpoint.close().await;
            return Ok(ExitCode::from(2));
        }
        Err(_) => {
            result("fetch", "NoRoute", "connect timed out after 30s".into(), 0, 0, started, Vec::new());
            endpoint.close().await;
            return Ok(ExitCode::from(2));
        }
    };
    let paths = PathMonitor::spawn(&conn, "fetch");
    emit(json!({ "event": "connected", "role": "fetch", "seconds": started.elapsed().as_secs_f64() }));

    let outcome = receive(&conn, &paths, &out, &net).await;
    let report = paths.report(&conn);
    let (name, detail, received, relayed, code) = match outcome {
        Ok(Received { bytes, relayed, verified: true }) => ("Completed", String::new(), bytes, relayed, 0),
        Ok(Received { bytes, relayed, verified: false }) => ("VerificationFailed", "BLAKE3 mismatch".into(), bytes, relayed, 3),
        Err(Failure { name, detail, bytes, relayed }) => (name, detail, bytes, relayed, 2),
    };
    conn.close(VarInt::from_u32(CODE_OK), b"done");
    result("fetch", name, detail, received, relayed, started, report);
    endpoint.close().await;
    Ok(ExitCode::from(code))
}

struct Received {
    bytes: u64,
    relayed: u64,
    verified: bool,
}

struct Failure {
    name: &'static str,
    detail: String,
    bytes: u64,
    relayed: u64,
}

async fn receive(conn: &Connection, paths: &PathMonitor, out: &Path, net: &NetArgs) -> Result<Received, Failure> {
    let mut bytes = 0u64;
    let mut relayed = 0u64;
    let fail = |conn: &Connection, detail: String, bytes: u64, relayed: u64| Failure { name: close_name(conn), detail, bytes, relayed };

    let (mut send, mut recv) = match conn.open_bi().await {
        Ok(streams) => streams,
        Err(err) => return Err(fail(conn, format!("open_bi: {err}"), 0, 0)),
    };
    if let Err(err) = write_frame(&mut send, &json!({ "want": "file" })).await {
        return Err(fail(conn, format!("request: {err:#}"), 0, 0));
    }
    let header: Header = match read_frame(&mut recv).await {
        Ok(header) => header,
        Err(err) => return Err(fail(conn, format!("header: {err:#}"), 0, 0)),
    };
    emit(json!({ "event": "header", "size": header.size, "blake3": header.blake3 }));

    let partial = out.with_extension("partial");
    let io = |err: std::io::Error, bytes, relayed| Failure { name: "DiskError", detail: err.to_string(), bytes, relayed };
    let mut file = tokio::fs::File::create(&partial).await.map_err(|e| io(e, 0, 0))?;
    let mut hasher = blake3::Hasher::new();
    let mut buffer = vec![0u8; CHUNK];

    loop {
        match recv.read(&mut buffer).await {
            Ok(Some(read)) => {
                if !paths.is_direct() {
                    relayed += read as u64;
                    if net.media == MediaPolicy::DirectOnly {
                        // The provider should never let this happen. Refuse the data if it does.
                        let _ = recv.stop(VarInt::from_u32(CODE_RELAYED_MEDIA));
                        conn.close(VarInt::from_u32(CODE_RELAYED_MEDIA), b"RelayedMedia");
                        let _ = tokio::fs::remove_file(&partial).await;
                        return Err(Failure { name: "RelayedMediaRefused", detail: "file data arrived while the selected path was a relay".into(), bytes, relayed });
                    }
                }
                hasher.update(&buffer[..read]);
                file.write_all(&buffer[..read]).await.map_err(|e| io(e, bytes, relayed))?;
                bytes += read as u64;
            }
            Ok(None) => break,
            Err(err) => {
                let _ = tokio::fs::remove_file(&partial).await;
                let name = match &err {
                    ReadError::Reset(code) => code_name(code.into_inner()),
                    _ => close_name(conn),
                };
                return Err(Failure { name, detail: err.to_string(), bytes, relayed });
            }
        }
    }
    file.flush().await.map_err(|e| io(e, bytes, relayed))?;
    drop(file);

    let verified = bytes == header.size && hasher.finalize().to_hex().to_string() == header.blake3;
    if verified {
        // Only a verified file appears under its final name.
        tokio::fs::rename(&partial, out).await.map_err(|e| io(e, bytes, relayed))?;
    } else {
        let _ = tokio::fs::remove_file(&partial).await;
    }
    let _ = send.write_all(&[verified as u8]).await;
    let _ = send.finish();
    let _ = send.stopped().await;
    Ok(Received { bytes, relayed, verified })
}

fn code_name(code: u64) -> &'static str {
    match code as u32 {
        CODE_NO_DIRECT_ROUTE => "NoDirectRoute",
        CODE_NOT_AUTHORIZED => "NotAuthorized",
        CODE_DIRECT_PATH_LOST => "DirectPathLost",
        CODE_RELAYED_MEDIA => "RelayedMediaRefused",
        _ => "TransferFailed",
    }
}

/// Names the failure from the code the provider closed the connection with.
fn close_name(conn: &Connection) -> &'static str {
    match conn.close_reason() {
        Some(ConnectionError::ApplicationClosed(close)) => code_name(close.error_code.into_inner()),
        Some(_) => "ConnectionLost",
        None => "TransferFailed",
    }
}

#[tokio::main]
async fn main() -> Result<ExitCode> {
    tracing_subscriber::fmt()
        .with_env_filter(tracing_subscriber::EnvFilter::try_from_default_env().unwrap_or_else(|_| "warn".into()))
        .with_writer(std::io::stderr)
        .init();

    match Cli::parse().command {
        Command::Provide { file, allow, once, net } => provide(file, allow, once, net).await,
        Command::Fetch { ticket, out, net } => fetch(ticket, out, net).await,
    }
}
