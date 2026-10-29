//! Nodearr's peer transport sidecar.
//!
//! Nodearr (.NET) starts this process and talks to it over a loopback HTTP API guarded by a
//! shared token. The sidecar owns the node's secret key and its iroh endpoint. It carries
//! opaque request and response messages between trusted nodes and tells Nodearr which node
//! each incoming message came from. iroh proves that identity during the connection
//! handshake, so Nodearr never has to check a signature itself.
//!
//! What the messages mean, and who is trusted, is decided in Nodearr. The one policy the
//! sidecar enforces is the block list, because a blocked node must be turned away when its
//! connection is accepted, before it can send anything, and what is already open to it closed.
//!
//! Control connections this node opens stay open while they are in use, in `connections.rs`.
//! Files move on a second protocol, in `transfer.rs`. Nodearr still decides who may have what.

mod connections;
mod logging;
mod transfer;

use std::{
    collections::{HashMap, HashSet},
    net::SocketAddr,
    path::{Path, PathBuf},
    str::FromStr,
    sync::{
        atomic::{AtomicU64, Ordering},
        Arc, Mutex, RwLock,
    },
    time::Duration,
};

use anyhow::{anyhow, bail, Context, Result};
use axum::{
    extract::{Path as UrlPath, Query, Request, State},
    http::StatusCode,
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::{delete, get, post, put},
    Json, Router,
};
use base64::{engine::general_purpose::STANDARD as BASE64, Engine};
use clap::{Parser, ValueEnum};
use iroh::{
    endpoint::{presets, Connection, ConnectionError, PortmapperConfig, RecvStream, SendStream, VarInt},
    Endpoint, EndpointAddr, EndpointId, RelayMap, RelayMode, RelayUrl, SecretKey, TransportAddr, Watcher,
};
use serde::{Deserialize, Serialize};
use serde_json::json;
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    sync::{mpsc, oneshot},
};

use connections::{ControlConnections, Live};

/// Control messages between nodes: pairing, liveness, and later catalog exchange.
pub(crate) const CONTROL_ALPN: &[u8] = b"nodearr/control/1";
const KEY_FILE: &str = "node.key";
const TOKEN_ENV: &str = "NODEARR_TRANSPORT_TOKEN";
const TOKEN_HEADER: &str = "x-nodearr-token";
const MAX_MESSAGE: usize = 4 * 1024 * 1024;

pub(crate) const CODE_DONE: u32 = 0;
pub(crate) const CODE_NOT_AUTHORIZED: u32 = 2;
pub(crate) const CODE_BLOCKED: u32 = 5;
/// Ends a transfer, or a stream that carried a message, when there was no direct path to send on.
pub(crate) const CODE_NO_DIRECT_ROUTE: u32 = 3;
/// Refuses file data, or a message that may not cross a relay, that came through one.
pub(crate) const CODE_RELAYED: u32 = 6;

#[derive(Parser)]
#[command(about = "Nodearr peer transport sidecar")]
struct Args {
    /// Folder that holds this node's secret key. The key is created on first run.
    #[arg(long)]
    data_dir: PathBuf,
    /// Loopback address for the control API. Port 0 picks a free port.
    #[arg(long, default_value = "127.0.0.1:0")]
    listen: SocketAddr,
    #[arg(long, value_enum, default_value_t = Relay::N0)]
    relay: Relay,
    /// Relay URL for `--relay custom`.
    #[arg(long)]
    relay_url: Option<RelayUrl>,
    /// Do not ask the router for a port mapping (UPnP, NAT-PMP, PCP).
    #[arg(long)]
    no_port_mapping: bool,
    /// UDP port peers connect to. Zero picks a free one, which is fine unless the port has to be
    /// published or forwarded, as it does for a container that is not on the host's network.
    #[arg(long, default_value_t = 0)]
    bind_port: u16,
    /// How long a transfer, or a message that may not cross a relay, waits for a direct path
    /// before it gives up.
    #[arg(long, default_value_t = 20)]
    direct_timeout_secs: u64,
    /// How long a control connection this node opened stays open once nothing is using it.
    #[arg(long, default_value_t = 180)]
    control_idle_secs: u64,
    /// The most each upload may send, in kilobytes per second. Zero means no limit.
    #[arg(long, default_value_t = 0)]
    upload_limit_kbps: u64,
    /// The most all uploads together may send, in kilobytes per second. Zero means no limit.
    #[arg(long, default_value_t = 0)]
    total_upload_limit_kbps: u64,
    /// The most each download may take, in kilobytes per second. Zero means no limit.
    #[arg(long, default_value_t = 0)]
    download_limit_kbps: u64,
    /// How many uploads may run at once. Further requests wait their turn. Zero means no limit.
    #[arg(long, default_value_t = 0)]
    max_uploads: usize,
}

#[derive(ValueEnum, Clone, Copy, PartialEq, Eq, Debug)]
enum Relay {
    /// No relay. Peers must have a reachable IP address for each other.
    Disabled,
    /// n0's public relays, used for coordination.
    N0,
    /// The relay given by --relay-url.
    Custom,
}

/// Something a peer sent that Nodearr has to decide about.
pub(crate) struct Incoming {
    pub remote: EndpointId,
    pub kind: IncomingKind,
    pub reply: oneshot::Sender<Reply>,
}

pub(crate) enum IncomingKind {
    /// A control message. Nodearr answers with a message of its own. `direct` is false when any
    /// of it came through a relay.
    Message { body: Vec<u8>, direct: bool },
    /// A request for a file. Nodearr answers with where the file is, or refuses.
    Transfer { file: String },
}

pub(crate) enum Reply {
    /// The answer to a message. With `direct`, it is sent only on a direct path.
    Body { body: Vec<u8>, direct: bool },
    /// Where the file is, and its hash if Nodearr already knows it.
    File(PathBuf, Option<transfer::FileHash>),
    Reject(Option<String>),
}

struct App {
    endpoint: Endpoint,
    transfers: Arc<transfer::Transfers>,
    live: Arc<Live>,
    control: ControlConnections,
    direct_timeout: Duration,
    token: String,
    blocked: RwLock<HashSet<EndpointId>>,
    incoming: tokio::sync::Mutex<mpsc::Receiver<Incoming>>,
    awaiting_reply: Mutex<HashMap<u64, oneshot::Sender<Reply>>>,
    next_id: AtomicU64,
}

impl App {
    /// The sidecar's state, and where the peer side puts what other nodes send.
    fn new(endpoint: Endpoint, transfers: transfer::Transfers, token: String, control_idle: Duration, direct_timeout: Duration) -> (Arc<Self>, mpsc::Sender<Incoming>) {
        let (incoming_tx, incoming_rx) = mpsc::channel(64);
        let live = Arc::new(Live::default());
        let app = Arc::new(Self {
            control: ControlConnections::new(endpoint.clone(), live.clone(), control_idle, direct_timeout),
            direct_timeout,
            endpoint,
            transfers: Arc::new(transfers),
            live,
            token,
            blocked: RwLock::new(HashSet::new()),
            incoming: tokio::sync::Mutex::new(incoming_rx),
            awaiting_reply: Mutex::new(HashMap::new()),
            next_id: AtomicU64::new(1),
        });
        (app, incoming_tx)
    }
}

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_ansi(false)
        .event_format(logging::SingleLine)
        .with_env_filter(tracing_subscriber::EnvFilter::try_from_default_env().unwrap_or_else(|_| "nodearr_transport=info,warn".into()))
        .with_writer(std::io::stderr)
        .init();

    let args = Args::parse();
    let token = std::env::var(TOKEN_ENV).with_context(|| format!("{TOKEN_ENV} must be set"))?;
    if token.len() < 16 {
        bail!("{TOKEN_ENV} is too short");
    }
    if !args.listen.ip().is_loopback() {
        bail!("--listen must be a loopback address");
    }

    let endpoint = bind(&args).await?;
    let transfers = transfer::Transfers::new(Duration::from_secs(args.direct_timeout_secs), args.upload_limit_kbps * 1024, args.total_upload_limit_kbps * 1024, args.download_limit_kbps * 1024, args.max_uploads);
    let (app, incoming_tx) = App::new(endpoint.clone(), transfers, token, Duration::from_secs(args.control_idle_secs), Duration::from_secs(args.direct_timeout_secs));

    tokio::spawn(accept_loop(app.clone(), incoming_tx));

    let router = Router::new()
        .route("/v1/identity", get(identity))
        .route("/v1/network", get(network))
        .route("/v1/probe", post(probe))
        .route("/v1/blocked", put(set_blocked))
        .route("/v1/request", post(send_request))
        .route("/v1/close", post(close_control))
        .route("/v1/incoming", get(next_incoming))
        .route("/v1/incoming/{id}", post(answer_incoming))
        .route("/v1/transfers", get(list_transfers).post(start_transfer))
        .route("/v1/transfers/{id}", delete(cancel_transfer))
        .route("/v1/uploads", get(list_uploads))
        .route("/v1/uploads/{id}/stop", post(stop_upload))
        .route("/v1/hash", post(hash_file))
        .route("/v1/disconnect", post(disconnect))
        .layer(middleware::from_fn_with_state(app.clone(), require_token))
        .with_state(app.clone());

    let listener = tokio::net::TcpListener::bind(args.listen).await?;
    let api = listener.local_addr()?;

    // Nodearr reads this line to learn where the API is.
    println!("{}", json!({ "event": "ready", "api": api.to_string(), "node_id": endpoint.id().to_string() }));

    tokio::select! {
        served = axum::serve(listener, router) => served?,
        _ = parent_gone() => {}
        _ = tokio::signal::ctrl_c() => {}
    }

    endpoint.close().await;
    Ok(())
}

/// Resolves when stdin closes, which is how the sidecar notices that Nodearr has gone away.
async fn parent_gone() {
    let mut stdin = tokio::io::stdin();
    let mut buffer = [0u8; 256];
    loop {
        match stdin.read(&mut buffer).await {
            Ok(0) | Err(_) => return,
            Ok(_) => {}
        }
    }
}

fn load_key(data_dir: &Path) -> Result<SecretKey> {
    std::fs::create_dir_all(data_dir)?;
    let path = data_dir.join(KEY_FILE);

    if path.exists() {
        let bytes: [u8; 32] = hex::decode(std::fs::read_to_string(&path)?.trim())?
            .try_into()
            .map_err(|_| anyhow!("{} does not hold a 32 byte key", path.display()))?;
        return Ok(SecretKey::from_bytes(&bytes));
    }

    let key = SecretKey::generate();
    std::fs::write(&path, hex::encode(key.to_bytes()))?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o600))?;
    }
    Ok(key)
}

async fn bind(args: &Args) -> Result<Endpoint> {
    // No address lookup service: nodes learn each other's addresses from invites and from each other.
    let mut builder = Endpoint::builder(presets::Minimal)
        .secret_key(load_key(&args.data_dir)?)
        .alpns(vec![CONTROL_ALPN.to_vec(), transfer::TRANSFER_ALPN.to_vec()]);

    builder = match args.relay {
        Relay::Disabled => builder.relay_mode(RelayMode::Disabled),
        Relay::N0 => builder.relay_mode(RelayMode::Default),
        Relay::Custom => {
            let url = args.relay_url.clone().context("--relay custom needs --relay-url")?;
            builder.relay_mode(RelayMode::Custom(RelayMap::from(url)))
        }
    };
    if args.no_port_mapping {
        builder = builder.portmapper_config(PortmapperConfig::Disabled);
    }
    if args.bind_port != 0 {
        builder = builder
            .bind_addr(format!("0.0.0.0:{}", args.bind_port))
            .map_err(|e| anyhow!("cannot use UDP port {}: {e}", args.bind_port))?;
    }

    let endpoint = builder.bind().await.map_err(|e| anyhow!("bind failed: {e:#}"))?;
    if args.relay != Relay::Disabled {
        // Best effort: a home relay makes the first identity answer complete.
        let _ = tokio::time::timeout(Duration::from_secs(5), endpoint.online()).await;
    }
    Ok(endpoint)
}

// ---------------------------------------------------------------------------------------------
// Peer side
// ---------------------------------------------------------------------------------------------

async fn accept_loop(app: Arc<App>, queue: mpsc::Sender<Incoming>) {
    while let Some(incoming) = app.endpoint.accept().await {
        let app = app.clone();
        let queue = queue.clone();
        tokio::spawn(async move {
            let Ok(conn) = incoming.await else { return };
            let remote = conn.remote_id();

            // A blocked node is refused here, before it can open a stream.
            if app.blocked.read().unwrap().contains(&remote) {
                conn.close(VarInt::from_u32(CODE_BLOCKED), b"Blocked");
                return;
            }
            // Listed so that blocking the node later closes it.
            app.live.track(&conn);

            if conn.alpn() == transfer::TRANSFER_ALPN {
                transfer::serve(app.transfers.clone(), conn, queue).await;
                return;
            }

            // The other node may keep this connection open and send many messages on it, some at
            // once. Each is served on its own, so one waiting for a direct path holds up no other.
            let paths = transfer::PathMonitor::spawn(&conn);
            while let Ok((send, recv)) = conn.accept_bi().await {
                if app.blocked.read().unwrap().contains(&remote) {
                    conn.close(VarInt::from_u32(CODE_BLOCKED), b"Blocked");
                    break;
                }
                let stream = Stream { conn: conn.clone(), remote, paths: paths.clone() };
                tokio::spawn(serve_stream(stream, send, recv, queue.clone(), app.direct_timeout));
            }
        });
    }
}

/// One message stream on a control connection.
struct Stream {
    conn: Connection,
    remote: EndpointId,
    paths: transfer::PathMonitor,
}

/// Hands a message to Nodearr and sends back its answer. An answer that may not cross a relay is
/// sent only while the path is direct (see `transfer::send_direct`). Without one, the stream is
/// reset with `NoDirectRoute`, and the connection stays open for messages that may cross a relay.
async fn serve_stream(stream: Stream, mut send: SendStream, mut recv: RecvStream, queue: mpsc::Sender<Incoming>, direct_timeout: Duration) {
    let Stream { conn, remote, mut paths } = stream;
    let Ok(body) = read_frame(&mut recv).await else { return };
    // Counted over the connection's whole life, not around this message. The frames of streams
    // not accepted yet may already be in, so no count taken when a stream is accepted could say
    // which of them came with which message. A connection that has carried anything through a
    // relay vouches for nothing on it, and a node of this version sends what may not cross one on
    // a connection that has not. Nodearr decides whether it matters: it does for a catalog, not
    // for a ping.
    let direct = transfer::stream_frames_not_direct(&conn).received == 0;

    let (reply, answer) = oneshot::channel();
    if queue.send(Incoming { remote, kind: IncomingKind::Message { body, direct }, reply }).await.is_err() {
        return;
    }

    match tokio::time::timeout(Duration::from_secs(30), answer).await {
        Ok(Ok(Reply::Body { body, direct: true })) => {
            match transfer::send_direct(&mut send, &body, &mut paths, direct_timeout).await {
                Ok(()) => {}
                Err(transfer::NotSent::NoDirectPath(message)) if conn.close_reason().is_none() => tracing::info!(remote = %remote, "held an answer back: {message}"),
                Err(err) => tracing::debug!(remote = %remote, close_reason = ?conn.close_reason(), "answer not sent: {err:?}"),
            }
        }
        Ok(Ok(Reply::Body { body, direct: false })) => {
            if write_frame(&mut send, &body).await.is_ok() {
                let _ = send.finish();
            }
        }
        Ok(Ok(Reply::Reject(Some(reason)))) if reason == "NoDirectRoute" => {
            // Something on this connection came through a relay. The other node is told so, and
            // may send it again on a connection that carried nothing through one.
            tracing::info!(remote = %remote, "refused a message that came through a relay");
            let _ = send.reset(VarInt::from_u32(CODE_RELAYED));
        }
        _ => {
            // Nodearr refused the message, answered with the wrong kind of thing, or never answered.
            conn.close(VarInt::from_u32(CODE_NOT_AUTHORIZED), b"NotAuthorized");
        }
    }
}

/// Reads one message. An error from the stream keeps its type, so that a reset and its code can
/// be told apart.
pub(crate) async fn read_frame(recv: &mut RecvStream) -> Result<Vec<u8>> {
    let mut len = [0u8; 4];
    recv.read_exact(&mut len).await?;
    let len = u32::from_be_bytes(len) as usize;
    if len > MAX_MESSAGE {
        bail!("message of {len} bytes is over the limit");
    }
    let mut body = vec![0u8; len];
    recv.read_exact(&mut body).await?;
    Ok(body)
}

pub(crate) async fn write_frame(send: &mut SendStream, body: &[u8]) -> Result<()> {
    send.write_u32(body.len() as u32).await?;
    send.write_all(body).await?;
    Ok(())
}

// ---------------------------------------------------------------------------------------------
// Control API for Nodearr
// ---------------------------------------------------------------------------------------------

async fn require_token(State(app): State<Arc<App>>, request: Request, next: Next) -> Response {
    let presented = request.headers().get(TOKEN_HEADER).and_then(|v| v.to_str().ok());
    if presented != Some(app.token.as_str()) {
        return StatusCode::UNAUTHORIZED.into_response();
    }
    next.run(request).await
}

async fn identity(State(app): State<Arc<App>>) -> Json<serde_json::Value> {
    let addr = app.endpoint.addr();
    Json(json!({
        "node_id": app.endpoint.id().to_string(),
        "relay_url": addr.relay_urls().next().map(|u| u.to_string()),
        "ip_addrs": addr.ip_addrs().map(|a| a.to_string()).collect::<Vec<_>>(),
    }))
}

/// What kind of network this node is on, as far as iroh has worked out. Everything is absent
/// until its first report is in, and when relays are off, because the report needs them.
async fn network(State(app): State<Arc<App>>) -> Json<serde_json::Value> {
    let addr = app.endpoint.addr();
    let report = app.endpoint.net_report().get();

    Json(json!({
        "relay_url": addr.relay_urls().next().map(|u| u.to_string()),
        "ip_addrs": addr.ip_addrs().map(|a| a.to_string()).collect::<Vec<_>>(),
        "udp": report.as_ref().map(|r| r.has_udp()),
        // True on a network that gives a different public port for each destination. Two nodes
        // can rarely find a direct path through one of those without a forwarded port.
        "mapping_varies": report.as_ref().and_then(|r| r.mapping_varies_by_dest()),
        "public_address": report.as_ref().and_then(|r| r.global_v4).map(|a| a.to_string()),
        "captive_portal": report.as_ref().and_then(|r| r.captive_portal),
    }))
}

#[derive(Deserialize)]
struct ProbeBody {
    node_id: String,
    relay_url: Option<String>,
    #[serde(default)]
    ip_addrs: Vec<String>,
    /// How long to give a direct path to appear once the nodes are connected.
    wait_ms: Option<u64>,
}

/// Connects to a peer and says what path the connection settled on. Files only ever move over a
/// direct path, so this answers "would a transfer work" without moving one. It always opens a
/// connection of its own, so that it sees the route as a new connection would, and a kept
/// control connection that still works cannot hide a change such as a new firewall rule.
async fn probe(State(app): State<Arc<App>>, Json(body): Json<ProbeBody>) -> Json<serde_json::Value> {
    let failed = |kind: &str, message: String| Json(json!({ "error": { "kind": kind, "message": message } }));

    let target = match parse_address(&body.node_id, body.relay_url.as_deref(), &body.ip_addrs) {
        Ok(target) => target,
        Err(message) => return failed("BadRequest", message),
    };

    let conn = match connections::connect_once(&app.endpoint, &app.live, target, Duration::from_secs(20)).await {
        Ok(conn) => conn,
        Err(err) => return failed("NoRoute", format!("{err:#}")),
    };

    let started = std::time::Instant::now();
    let mut paths = transfer::PathMonitor::spawn(&conn);
    let direct = paths.wait_direct(Duration::from_millis(body.wait_ms.unwrap_or(10_000).min(60_000))).await;

    if let Some(ConnectionError::ApplicationClosed(close)) = conn.close_reason() {
        if close.error_code.into_inner() as u32 == CODE_BLOCKED {
            return failed("Blocked", "the peer has blocked this node".into());
        }
    }

    let selected = transfer::selected_path(&conn);
    conn.close(VarInt::from_u32(CODE_DONE), b"done");

    Json(json!({
        "path": match (direct, &selected) { (true, _) => "direct", (false, Some(_)) => "relay", (false, None) => "none" },
        "remote": selected.map(|(_, remote)| remote),
        "waited_ms": started.elapsed().as_millis() as u64,
    }))
}

#[derive(Deserialize)]
struct BlockedBody {
    node_ids: Vec<String>,
}

async fn set_blocked(State(app): State<Arc<App>>, Json(body): Json<BlockedBody>) -> Response {
    let mut parsed = HashSet::new();
    for id in &body.node_ids {
        match EndpointId::from_str(id) {
            Ok(id) => {
                parsed.insert(id);
            }
            Err(_) => return (StatusCode::BAD_REQUEST, format!("bad node id {id}")).into_response(),
        }
    }
    *app.blocked.write().unwrap() = parsed.clone();

    // Whatever is already open to a blocked node closes now, whichever side opened it and whether
    // it carries messages or files. Otherwise a kept connection would let it go on sending.
    for node in parsed {
        let closed = app.live.close(node, CODE_BLOCKED, b"Blocked");
        if closed > 0 {
            tracing::info!(remote = %node, closed, "closed connections to a blocked node");
        }
    }
    StatusCode::NO_CONTENT.into_response()
}

#[derive(Deserialize)]
struct RequestBody {
    node_id: String,
    relay_url: Option<String>,
    #[serde(default)]
    ip_addrs: Vec<String>,
    body: String,
    timeout_ms: Option<u64>,
    /// The message and its answer may not cross a relay. Nodearr says so for catalogs, posters
    /// and what friends send each other. The sidecar does not know what a message is.
    #[serde(default)]
    direct: bool,
}

#[derive(Serialize)]
#[serde(untagged)]
enum RequestResult {
    Ok { body: String },
    Failed { error: Failure },
}

#[derive(Serialize)]
struct Failure {
    kind: &'static str,
    message: String,
}

fn failed(kind: &'static str, message: impl Into<String>) -> Json<RequestResult> {
    Json(RequestResult::Failed { error: Failure { kind, message: message.into() } })
}

/// Where a node can be reached, as Nodearr describes it.
fn parse_address(node_id: &str, relay_url: Option<&str>, ip_addrs: &[String]) -> std::result::Result<EndpointAddr, String> {
    let id = EndpointId::from_str(node_id).map_err(|_| "bad node id".to_string())?;

    let mut addrs = Vec::new();
    if let Some(relay) = relay_url.filter(|r| !r.is_empty()) {
        addrs.push(TransportAddr::Relay(RelayUrl::from_str(relay).map_err(|_| "bad relay url".to_string())?));
    }
    for ip in ip_addrs {
        addrs.push(TransportAddr::Ip(SocketAddr::from_str(ip).map_err(|_| format!("bad address {ip}"))?));
    }

    Ok(EndpointAddr::from_parts(id, addrs))
}

async fn send_request(State(app): State<Arc<App>>, Json(request): Json<RequestBody>) -> Json<RequestResult> {
    let target = match parse_address(&request.node_id, request.relay_url.as_deref(), &request.ip_addrs) {
        Ok(target) => target,
        Err(message) => return failed("BadRequest", message),
    };
    let Ok(body) = BASE64.decode(&request.body) else {
        return failed("BadRequest", "body is not base64");
    };

    let timeout = Duration::from_millis(request.timeout_ms.unwrap_or(20_000));
    match app.control.exchange(target, &body, timeout, request.direct).await {
        Ok(body) => Json(RequestResult::Ok { body: BASE64.encode(body) }),
        Err((kind, message)) => failed(kind, message),
    }
}

#[derive(Deserialize)]
struct CloseBody {
    node_id: String,
}

/// Closes the control connections this node opened to a node, kept or replaced. Nodearr calls
/// this when trust in the node ends, so that nothing stays open to it until the idle timeout.
async fn close_control(State(app): State<Arc<App>>, Json(body): Json<CloseBody>) -> Response {
    match EndpointId::from_str(&body.node_id) {
        Ok(node) => Json(json!({ "closed": app.control.close(node).await })).into_response(),
        Err(_) => (StatusCode::BAD_REQUEST, "bad node id").into_response(),
    }
}

#[derive(Deserialize)]
struct WaitQuery {
    wait_ms: Option<u64>,
}

/// Long poll: answers with the next message a peer sent, or 204 when none arrived in time.
async fn next_incoming(State(app): State<Arc<App>>, Query(query): Query<WaitQuery>) -> Response {
    let wait = Duration::from_millis(query.wait_ms.unwrap_or(25_000).min(60_000));
    let mut queue = app.incoming.lock().await;

    match tokio::time::timeout(wait, queue.recv()).await {
        Ok(Some(incoming)) => {
            let id = app.next_id.fetch_add(1, Ordering::Relaxed);
            app.awaiting_reply.lock().unwrap().insert(id, incoming.reply);
            let remote = incoming.remote.to_string();
            match incoming.kind {
                IncomingKind::Message { body, direct } => Json(json!({ "id": id, "remote_node_id": remote, "kind": "message", "body": BASE64.encode(body), "direct": direct })).into_response(),
                IncomingKind::Transfer { file } => Json(json!({ "id": id, "remote_node_id": remote, "kind": "transfer", "file": file })).into_response(),
            }
        }
        _ => StatusCode::NO_CONTENT.into_response(),
    }
}

#[derive(Deserialize)]
struct AnswerBody {
    /// The answer to a message.
    body: Option<String>,
    /// The answer to a file request: where the file is on this machine.
    path: Option<String>,
    /// The file's hash, if Nodearr worked it out before. Used only if the file has not changed.
    hash: Option<transfer::FileHash>,
    /// The answer to a message may not cross a relay: it waits for a direct path.
    #[serde(default)]
    direct: bool,
    #[serde(default)]
    reject: bool,
    /// Why a file request was refused: NotAuthorized, Revoked or NotFound. A message refused as
    /// NoDirectRoute came through a relay when it should not have. Any other refusal of a message
    /// closes the connection.
    reason: Option<String>,
}

async fn answer_incoming(State(app): State<Arc<App>>, UrlPath(id): UrlPath<u64>, Json(answer): Json<AnswerBody>) -> StatusCode {
    let Some(reply) = app.awaiting_reply.lock().unwrap().remove(&id) else {
        return StatusCode::NOT_FOUND;
    };

    let reply_with = if answer.reject {
        Reply::Reject(answer.reason)
    } else if let Some(path) = answer.path {
        Reply::File(PathBuf::from(path), answer.hash)
    } else {
        match answer.body.map(|b| BASE64.decode(b)) {
            Some(Ok(body)) => Reply::Body { body, direct: answer.direct },
            _ => Reply::Reject(None),
        }
    };
    let _ = reply.send(reply_with);
    StatusCode::NO_CONTENT
}

#[derive(Deserialize)]
struct StartTransferBody {
    id: String,
    node_id: String,
    relay_url: Option<String>,
    #[serde(default)]
    ip_addrs: Vec<String>,
    file: String,
    dest: String,
    expected_hash: Option<String>,
}

async fn start_transfer(State(app): State<Arc<App>>, Json(body): Json<StartTransferBody>) -> Response {
    let target = match parse_address(&body.node_id, body.relay_url.as_deref(), &body.ip_addrs) {
        Ok(target) => target,
        Err(message) => return (StatusCode::BAD_REQUEST, message).into_response(),
    };

    let request = transfer::DownloadRequest {
        id: body.id,
        target,
        file: body.file,
        dest: PathBuf::from(body.dest),
        expected_hash: body.expected_hash.filter(|h| !h.is_empty()),
    };
    transfer::start(&app.transfers, app.endpoint.clone(), app.live.clone(), request);
    StatusCode::ACCEPTED.into_response()
}

async fn list_transfers(State(app): State<Arc<App>>) -> Json<Vec<transfer::DownloadState>> {
    Json(app.transfers.list())
}

async fn list_uploads(State(app): State<Arc<App>>) -> Json<Vec<transfer::UploadState>> {
    Json(app.transfers.list_uploads())
}

async fn cancel_transfer(State(app): State<Arc<App>>, UrlPath(id): UrlPath<String>) -> StatusCode {
    if app.transfers.cancel(&id) {
        StatusCode::NO_CONTENT
    } else {
        StatusCode::NOT_FOUND
    }
}

#[derive(Deserialize)]
struct HashBody {
    path: String,
    /// What Nodearr was told last time. Answered from this, without reading the file, when the
    /// file's size and modified time have not changed.
    known: Option<transfer::FileHash>,
}

/// Works out a file's hash ahead of any request for it. Nodearr calls this one file at a time.
async fn hash_file(State(app): State<Arc<App>>, Json(body): Json<HashBody>) -> Response {
    let path = PathBuf::from(body.path);
    if let Some(known) = body.known {
        app.transfers.remember(&path, known);
    }
    match app.transfers.hash(&path).await {
        Ok(hashed) => Json(hashed).into_response(),
        Err(err) => (StatusCode::NOT_FOUND, format!("{err:#}")).into_response(),
    }
}

#[derive(Deserialize)]
struct StopUploadBody {
    /// NotAuthorized, Revoked or NotFound, as for a refused request.
    reason: Option<String>,
}

/// Cuts one upload that Nodearr no longer allows.
async fn stop_upload(State(app): State<Arc<App>>, UrlPath(id): UrlPath<String>, Json(body): Json<StopUploadBody>) -> StatusCode {
    match id.parse::<u64>() {
        Ok(id) if app.transfers.stop_upload(id, body.reason.as_deref()) => StatusCode::NO_CONTENT,
        _ => StatusCode::NOT_FOUND,
    }
}

#[derive(Deserialize)]
struct DisconnectBody {
    node_id: String,
}

/// Cuts every upload to a node.
async fn disconnect(State(app): State<Arc<App>>, Json(body): Json<DisconnectBody>) -> Response {
    match EndpointId::from_str(&body.node_id) {
        Ok(node) => Json(json!({ "closed": app.transfers.disconnect(node) })).into_response(),
        Err(_) => (StatusCode::BAD_REQUEST, "bad node id").into_response(),
    }
}

#[cfg(test)]
mod tests {
    use rustls::NamedGroup;

    use super::*;

    /// A test node: the sidecar, and what its stand-in for Nodearr was sent. Each message is kept
    /// with whether all of it came on a direct path.
    #[derive(Clone)]
    struct TestNode {
        app: Arc<App>,
        heard: Arc<Mutex<Vec<(String, bool)>>>,
    }

    impl std::ops::Deref for TestNode {
        type Target = App;

        fn deref(&self) -> &App {
            &self.app
        }
    }

    impl TestNode {
        fn heard(&self) -> Vec<(String, bool)> {
            self.heard.lock().unwrap().clone()
        }
    }

    /// How long a message that may not cross a relay waits for a direct path in these tests.
    const DIRECT_WAIT: Duration = Duration::from_secs(1);

    /// A node on loopback with no relay, so every path is direct.
    async fn node(control_idle: Duration) -> TestNode {
        node_with_key(SecretKey::generate(), control_idle).await
    }

    async fn node_with_key(key: SecretKey, control_idle: Duration) -> TestNode {
        let endpoint = Endpoint::builder(presets::Minimal)
            .secret_key(key)
            .alpns(vec![CONTROL_ALPN.to_vec(), transfer::TRANSFER_ALPN.to_vec()])
            .relay_mode(RelayMode::Disabled)
            .clear_ip_transports()
            .bind_addr("127.0.0.1:0")
            .unwrap()
            .bind()
            .await
            .unwrap();
        start(endpoint, control_idle)
    }

    /// A node with no IP address of its own: it reaches others only through the relay.
    async fn relay_only(relay: &RelayMap) -> TestNode {
        let endpoint = Endpoint::builder(presets::Minimal)
            .secret_key(SecretKey::generate())
            .alpns(vec![CONTROL_ALPN.to_vec(), transfer::TRANSFER_ALPN.to_vec()])
            .relay_mode(RelayMode::Custom(relay.clone()))
            .ca_tls_config(iroh::tls::CaTlsConfig::insecure_skip_verify())
            .clear_ip_transports()
            .bind()
            .await
            .unwrap();
        tokio::time::timeout(Duration::from_secs(10), endpoint.online()).await.expect("the relay to take the node");
        start(endpoint, Duration::from_secs(60))
    }

    /// Starts the sidecar's peer side, and a stand-in for Nodearr. It treats a message that starts
    /// with "content" as one that may not cross a relay: it refuses one that came through one, and
    /// its answer waits for a direct path. A "question" may arrive any way, as a catalog request
    /// from an older node may, but its answer is content. Anything else, such as a ping, may cross
    /// a relay both ways. Every message is answered with its own body. File requests are left
    /// waiting.
    fn start(endpoint: Endpoint, control_idle: Duration) -> TestNode {
        let (app, queue) = App::new(endpoint, transfer::Transfers::new(Duration::from_secs(5), 0, 0, 0, 0), "t".repeat(16), control_idle, DIRECT_WAIT);
        tokio::spawn(accept_loop(app.clone(), queue));

        let node = TestNode { app, heard: Arc::default() };
        let nodearr = node.clone();
        tokio::spawn(async move {
            let mut waiting = Vec::new();
            while let Some(incoming) = nodearr.app.incoming.lock().await.recv().await {
                match incoming.kind {
                    IncomingKind::Message { body, direct } => {
                        let text = String::from_utf8(body.clone()).unwrap();
                        nodearr.heard.lock().unwrap().push((text.clone(), direct));
                        let reply = if text.starts_with("content") && !direct {
                            Reply::Reject(Some("NoDirectRoute".into()))
                        } else {
                            Reply::Body { body, direct: text.starts_with("content") || text.starts_with("question") }
                        };
                        let _ = incoming.reply.send(reply);
                    }
                    IncomingKind::Transfer { .. } => waiting.push(incoming.reply),
                }
            }
        });
        node
    }

    fn id(app: &App) -> EndpointId {
        app.endpoint.id()
    }

    /// Sends a message that may cross a relay.
    async fn ask(from: &App, to: &App, body: &str) -> std::result::Result<String, connections::Failure> {
        exchange(from, to, body, false).await
    }

    /// Sends a message that may not cross a relay.
    async fn ask_direct(from: &App, to: &App, body: &str) -> std::result::Result<String, connections::Failure> {
        exchange(from, to, body, true).await
    }

    async fn exchange(from: &App, to: &App, body: &str, direct: bool) -> std::result::Result<String, connections::Failure> {
        let answer = from.control.exchange(to.endpoint.addr(), body.as_bytes(), Duration::from_secs(5), direct).await?;
        Ok(String::from_utf8(answer).unwrap())
    }

    /// Waits up to five seconds for something that happens a moment after its cause.
    async fn eventually(what: &str, check: impl Fn() -> bool) {
        let deadline = std::time::Instant::now() + Duration::from_secs(5);
        while !check() {
            assert!(std::time::Instant::now() < deadline, "timed out waiting until {what}");
            tokio::time::sleep(Duration::from_millis(20)).await;
        }
    }

    async fn block(app: &Arc<App>, node: EndpointId) {
        set_blocked(State(app.clone()), Json(BlockedBody { node_ids: vec![node.to_string()] })).await;
    }

    #[tokio::test]
    async fn several_exchanges_to_one_friend_use_one_connection() {
        let a = node(Duration::from_secs(60)).await;
        let b = node(Duration::from_secs(60)).await;

        assert_eq!(ask(&a, &b, "ping").await.unwrap(), "ping");
        assert_eq!(ask(&a, &b, "catalog").await.unwrap(), "catalog");

        // A library's posters, asked for all at once, wait for the one connection between them.
        let posters: Vec<_> = (0..8).map(|n| {
            let (a, b) = (a.clone(), b.clone());
            tokio::spawn(async move { ask(&a, &b, &format!("poster {n}")).await })
        }).collect();
        for (n, poster) in posters.into_iter().enumerate() {
            assert_eq!(poster.await.unwrap().unwrap(), format!("poster {n}"));
        }

        assert_eq!(b.live.count(id(&a)), 1);
        assert!(a.control.is_open(id(&b)).await);
    }

    #[tokio::test]
    async fn a_kept_connection_closes_after_the_idle_timeout() {
        let a = node(Duration::from_millis(300)).await;
        let b = node(Duration::from_secs(60)).await;

        ask(&a, &b, "ping").await.unwrap();
        assert_eq!(b.live.count(id(&a)), 1);

        eventually("the other node sees the connection close", || b.live.count(id(&a)) == 0).await;
        assert!(!a.control.is_open(id(&b)).await);

        // The next message opens a new one.
        assert_eq!(ask(&a, &b, "ping").await.unwrap(), "ping");
        assert_eq!(b.live.count(id(&a)), 1);
    }

    #[tokio::test]
    async fn a_dead_connection_is_replaced_on_the_next_exchange() {
        let a = node(Duration::from_secs(60)).await;
        let b = node(Duration::from_secs(60)).await;
        ask(&a, &b, "ping").await.unwrap();

        // The other side drops it, as a node that restarts or slept does. Asked again at once, the
        // message goes on a new connection, whether or not the close has arrived yet.
        assert_eq!(b.live.close(id(&a), CODE_DONE, b"gone"), 1);
        assert_eq!(ask(&a, &b, "again").await.unwrap(), "again");
        eventually("only the new connection is open", || b.live.count(id(&a)) == 1).await;

        // The other node's sidecar restarts with the same key on a new port.
        let key = b.endpoint.secret_key().clone();
        b.endpoint.close().await;
        let restarted = node_with_key(key, Duration::from_secs(60)).await;
        assert_eq!(ask(&a, &restarted, "after restart").await.unwrap(), "after restart");
    }

    #[tokio::test]
    async fn blocking_a_node_closes_its_control_and_transfer_connections_from_either_side() {
        let a = node(Duration::from_secs(60)).await;
        let b = node(Duration::from_secs(60)).await;

        // Control connections opened by each side.
        ask(&a, &b, "ping").await.unwrap();
        ask(&b, &a, "ping").await.unwrap();

        // A file B asked A for, and one A asked B for. Each waits on the sending node's Nodearr.
        let request = |dest: &str| transfer::DownloadRequest { id: dest.into(), target: EndpointAddr::new(EndpointId::from_bytes(&[0; 32]).unwrap()), file: "movie.mkv".into(), dest: std::env::temp_dir().join(dest), expected_hash: None };
        transfer::start(&b.transfers, b.endpoint.clone(), b.live.clone(), transfer::DownloadRequest { target: a.endpoint.addr(), ..request("nodearr-test-b") });
        transfer::start(&a.transfers, a.endpoint.clone(), a.live.clone(), transfer::DownloadRequest { target: b.endpoint.addr(), ..request("nodearr-test-a") });
        eventually("both transfers are connected", || a.live.count(id(&b)) == 4 && b.live.count(id(&a)) == 4).await;

        block(&a.app, id(&b)).await;

        assert_eq!(a.live.count(id(&b)), 0, "closed on the blocking side at once");
        eventually("the blocked node sees every connection closed", || b.live.count(id(&a)) == 0).await;
        assert!(!a.control.is_open(id(&b)).await);
        assert!(!b.control.is_open(id(&a)).await);

        // The blocked node cannot come back on a new connection either.
        assert_eq!(ask(&b, &a, "ping").await.unwrap_err().0, "Blocked");
    }

    #[tokio::test]
    async fn ending_trust_closes_the_kept_connection() {
        let a = node(Duration::from_secs(60)).await;
        let b = node(Duration::from_secs(60)).await;
        ask(&a, &b, "ping").await.unwrap();

        let closed = close_control(State(a.app.clone()), Json(CloseBody { node_id: id(&b).to_string() })).await;
        assert_eq!(closed.status(), StatusCode::OK);

        assert!(!a.control.is_open(id(&b)).await);
        eventually("the other node sees it close", || b.live.count(id(&a)) == 0).await;
        // Nothing was kept, so there is nothing to close a second time.
        assert!(!a.control.close(id(&b)).await);
    }

    #[tokio::test]
    async fn over_a_direct_path_content_goes_through_and_the_other_node_knows_it_came_direct() {
        let a = node(Duration::from_secs(60)).await;
        let b = node(Duration::from_secs(60)).await;

        assert_eq!(ask_direct(&a, &b, "content: catalog").await.unwrap(), "content: catalog");
        assert_eq!(b.heard(), vec![("content: catalog".to_string(), true)]);
    }

    #[tokio::test]
    async fn over_a_relay_only_path_content_is_never_sent_but_a_ping_goes_through() {
        let (relay, _, _server) = iroh::test_utils::run_relay_server().await.unwrap();
        let a = relay_only(&relay).await;
        let b = relay_only(&relay).await;

        // Finding a path, and saying there is none, needs pings over the relay.
        assert_eq!(ask(&a, &b, "ping").await.unwrap(), "ping");

        let started = std::time::Instant::now();
        let (kind, message) = ask_direct(&a, &b, "content: catalog").await.unwrap_err();
        assert_eq!(kind, "NoDirectRoute", "{message}");
        assert!(started.elapsed() >= DIRECT_WAIT, "it waited for a direct path first");
        // Not a byte of it reached the other node.
        assert_eq!(b.heard(), vec![("ping".to_string(), false)]);

        // The connection is still there for what may cross the relay.
        assert_eq!(ask(&a, &b, "ping again").await.unwrap(), "ping again");
        assert_eq!(b.live.count(id(&a)), 1);
    }

    // Frames of streams not yet accepted may already be in when one is accepted, so a count taken
    // per stream would miss them. Every message here crossed the relay, and each must say so.
    #[tokio::test]
    async fn every_message_that_came_through_a_relay_is_known_to_have_however_many_arrive_at_once() {
        let (relay, _, _server) = iroh::test_utils::run_relay_server().await.unwrap();
        let a = relay_only(&relay).await;
        let b = relay_only(&relay).await;

        let asks: Vec<_> = (0..32).map(|n| {
            let (a, b) = (a.clone(), b.clone());
            tokio::spawn(async move { ask(&a, &b, &format!("ping {n}")).await })
        }).collect();
        for asked in asks {
            asked.await.unwrap().unwrap();
        }

        let heard = b.heard();
        assert_eq!(heard.len(), 32);
        let marked_direct: Vec<_> = heard.iter().filter(|(_, direct)| *direct).collect();
        assert!(marked_direct.is_empty(), "{} of 32 were taken for direct: {marked_direct:?}", marked_direct.len());
    }

    // A ping sent before hole punching has found a path goes through the relay. Content sent on
    // that connection afterwards would be refused by the other node, which cannot tell the ping's
    // frames from the content's, so it goes on a new connection once the direct path is there.
    #[tokio::test]
    async fn content_goes_on_a_new_connection_when_the_kept_one_carried_a_ping_through_the_relay() {
        let (relay, _, _server) = iroh::test_utils::run_relay_server().await.unwrap();
        let a = relay_and_loopback(&relay).await;
        let b = relay_and_loopback(&relay).await;
        // Only the relay address, so the connection starts there, as it does between two homes.
        let relay_url = b.endpoint.addr().relay_urls().next().cloned().unwrap();
        let through_relay = EndpointAddr::from_parts(id(&b), [TransportAddr::Relay(relay_url)]);

        a.control.exchange(through_relay.clone(), b"ping", Duration::from_secs(5), false).await.unwrap();
        assert_eq!(b.heard(), vec![("ping".to_string(), false)], "the ping went before a direct path was found");

        let answer = a.control.exchange(through_relay, b"content: catalog", Duration::from_secs(5), true).await.unwrap();
        assert_eq!(answer, b"content: catalog");
        // Sent once, on the new connection, not refused first on the old one and then sent again.
        assert_eq!(b.heard(), vec![("ping".to_string(), false), ("content: catalog".to_string(), true)]);
        assert_eq!(b.live.count(id(&a)), 2, "the content went on a connection of its own");
    }

    // Trust ending must not leave open a connection that was replaced for content and is waiting
    // out its idle timeout: the replaced one closes along with the kept one.
    #[tokio::test]
    async fn closing_a_node_closes_the_connections_replaced_for_content_too() {
        let (relay, _, _server) = iroh::test_utils::run_relay_server().await.unwrap();
        let a = relay_and_loopback(&relay).await;
        let b = relay_and_loopback(&relay).await;
        let relay_url = b.endpoint.addr().relay_urls().next().cloned().unwrap();
        let through_relay = EndpointAddr::from_parts(id(&b), [TransportAddr::Relay(relay_url)]);
        a.control.exchange(through_relay.clone(), b"ping", Duration::from_secs(5), false).await.unwrap();
        a.control.exchange(through_relay, b"content: catalog", Duration::from_secs(5), true).await.unwrap();
        assert_eq!(b.live.count(id(&a)), 2);

        assert!(a.control.close(id(&b)).await);

        let closed = async {
            while b.live.count(id(&a)) > 0 || a.live.count(id(&b)) > 0 {
                tokio::time::sleep(Duration::from_millis(20)).await;
            }
        };
        tokio::time::timeout(Duration::from_secs(5), closed).await.expect("every connection between the two nodes to close");
        assert!(!a.control.close(id(&b)).await, "nothing is left to close");
    }

    // Waiting for a direct path must not hide why a connection ended: a node that blocked this one
    // says so, through the relay as directly.
    #[tokio::test]
    async fn a_node_reached_through_a_relay_that_blocked_this_one_says_blocked_for_content_too() {
        let (relay, _, _server) = iroh::test_utils::run_relay_server().await.unwrap();
        let a = relay_only(&relay).await;
        let b = relay_only(&relay).await;
        ask(&a, &b, "ping").await.unwrap();

        block(&b.app, id(&a)).await;

        assert_eq!(ask_direct(&a, &b, "content: catalog").await.unwrap_err().0, "Blocked");
        assert_eq!(ask(&a, &b, "ping").await.unwrap_err().0, "Blocked");
    }

    /// A node reachable on loopback and through the relay.
    async fn relay_and_loopback(relay: &RelayMap) -> TestNode {
        let endpoint = Endpoint::builder(presets::Minimal)
            .secret_key(SecretKey::generate())
            .alpns(vec![CONTROL_ALPN.to_vec(), transfer::TRANSFER_ALPN.to_vec()])
            .relay_mode(RelayMode::Custom(relay.clone()))
            .ca_tls_config(iroh::tls::CaTlsConfig::insecure_skip_verify())
            .clear_ip_transports()
            .bind_addr("127.0.0.1:0")
            .unwrap()
            .bind()
            .await
            .unwrap();
        tokio::time::timeout(Duration::from_secs(10), endpoint.online()).await.expect("the relay to take the node");
        start(endpoint, Duration::from_secs(60))
    }

    #[tokio::test]
    async fn the_other_node_holds_content_back_and_refuses_content_that_came_through_a_relay() {
        let (relay, _, _server) = iroh::test_utils::run_relay_server().await.unwrap();
        let a = relay_only(&relay).await;
        let b = relay_only(&relay).await;

        // An older node asks over the relay without waiting. The question arrives, and is answered
        // with content, which the answering node holds back.
        let (kind, message) = ask(&a, &b, "question: catalog").await.unwrap_err();
        assert_eq!(kind, "NoDirectRoute", "{message}");
        assert_eq!(b.heard(), vec![("question: catalog".to_string(), false)]);

        // An older node sends content over the relay. Nodearr is told it came through one, and
        // refuses it.
        let (kind, message) = ask(&a, &b, "content: sent").await.unwrap_err();
        assert_eq!(kind, "NoDirectRoute", "{message}");
        assert_eq!(b.heard().last(), Some(&("content: sent".to_string(), false)));

        // Neither closed the connection.
        assert_eq!(ask(&a, &b, "ping").await.unwrap(), "ping");
        assert_eq!(b.live.count(id(&a)), 1);
    }

    /// Connections between nodes offer the post-quantum hybrid first (issue #24). This fails to
    /// build without the aws-lc-rs provider, and fails if prefer-post-quantum is dropped.
    #[test]
    fn key_exchange_offers_x25519mlkem768_first() {
        let provider = rustls::crypto::aws_lc_rs::default_provider();
        let groups: Vec<NamedGroup> = provider.kx_groups.iter().map(|group| group.name()).collect();
        assert_eq!(groups.first(), Some(&NamedGroup::X25519MLKEM768));
        assert!(groups.contains(&NamedGroup::X25519), "older nodes still need plain X25519");
    }
}
