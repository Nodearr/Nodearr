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
//! connection is accepted, before it can send anything.
//!
//! Files move on a second protocol, in `transfer.rs`. Nodearr still decides who may have what.

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
    Endpoint, EndpointAddr, EndpointId, RelayMap, RelayMode, RelayUrl, SecretKey, TransportAddr,
};
use serde::{Deserialize, Serialize};
use serde_json::json;
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    sync::{mpsc, oneshot},
};

/// Control messages between nodes: pairing, liveness, and later catalog exchange.
const CONTROL_ALPN: &[u8] = b"nodearr/control/1";
const KEY_FILE: &str = "node.key";
const TOKEN_ENV: &str = "NODEARR_TRANSPORT_TOKEN";
const TOKEN_HEADER: &str = "x-nodearr-token";
const MAX_MESSAGE: usize = 4 * 1024 * 1024;

pub(crate) const CODE_DONE: u32 = 0;
pub(crate) const CODE_NOT_AUTHORIZED: u32 = 2;
pub(crate) const CODE_BLOCKED: u32 = 5;

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
    /// How long a transfer waits for a direct path before it gives up.
    #[arg(long, default_value_t = 20)]
    direct_timeout_secs: u64,
    /// The most each upload may send, in kilobytes per second. Zero means no limit.
    #[arg(long, default_value_t = 0)]
    upload_limit_kbps: u64,
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
    /// A control message. Nodearr answers with a message of its own.
    Message(Vec<u8>),
    /// A request for a file. Nodearr answers with where the file is, or refuses.
    Transfer { file: String },
}

pub(crate) enum Reply {
    Body(Vec<u8>),
    File(PathBuf),
    Reject(Option<String>),
}

struct App {
    endpoint: Endpoint,
    transfers: Arc<transfer::Transfers>,
    token: String,
    blocked: RwLock<HashSet<EndpointId>>,
    incoming: tokio::sync::Mutex<mpsc::Receiver<Incoming>>,
    awaiting_reply: Mutex<HashMap<u64, oneshot::Sender<Reply>>>,
    next_id: AtomicU64,
}

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(tracing_subscriber::EnvFilter::try_from_default_env().unwrap_or_else(|_| "warn".into()))
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
    let (incoming_tx, incoming_rx) = mpsc::channel(64);
    let app = Arc::new(App {
        endpoint: endpoint.clone(),
        transfers: Arc::new(transfer::Transfers::new(Duration::from_secs(args.direct_timeout_secs), args.upload_limit_kbps * 1024)),
        token,
        blocked: RwLock::new(HashSet::new()),
        incoming: tokio::sync::Mutex::new(incoming_rx),
        awaiting_reply: Mutex::new(HashMap::new()),
        next_id: AtomicU64::new(1),
    });

    tokio::spawn(accept_loop(app.clone(), incoming_tx));

    let router = Router::new()
        .route("/v1/identity", get(identity))
        .route("/v1/blocked", put(set_blocked))
        .route("/v1/request", post(send_request))
        .route("/v1/incoming", get(next_incoming))
        .route("/v1/incoming/{id}", post(answer_incoming))
        .route("/v1/transfers", get(list_transfers).post(start_transfer))
        .route("/v1/transfers/{id}", delete(cancel_transfer))
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

            if conn.alpn() == transfer::TRANSFER_ALPN {
                transfer::serve(app.transfers.clone(), conn, queue).await;
                return;
            }

            while let Ok((send, recv)) = conn.accept_bi().await {
                if serve_stream(&conn, remote, send, recv, &queue).await.is_err() {
                    break;
                }
            }
        });
    }
}

async fn serve_stream(conn: &Connection, remote: EndpointId, mut send: SendStream, mut recv: RecvStream, queue: &mpsc::Sender<Incoming>) -> Result<()> {
    let body = read_frame(&mut recv).await?;
    let (reply, answer) = oneshot::channel();
    queue.send(Incoming { remote, kind: IncomingKind::Message(body), reply }).await.map_err(|_| anyhow!("shutting down"))?;

    match tokio::time::timeout(Duration::from_secs(30), answer).await {
        Ok(Ok(Reply::Body(body))) => {
            write_frame(&mut send, &body).await?;
            send.finish().map_err(|e| anyhow!("{e}"))?;
            Ok(())
        }
        _ => {
            // Nodearr refused the message, answered with the wrong kind of thing, or never answered.
            conn.close(VarInt::from_u32(CODE_NOT_AUTHORIZED), b"NotAuthorized");
            bail!("rejected")
        }
    }
}

async fn read_frame(recv: &mut RecvStream) -> Result<Vec<u8>> {
    let len = recv.read_u32().await? as usize;
    if len > MAX_MESSAGE {
        bail!("message of {len} bytes is over the limit");
    }
    let mut body = vec![0u8; len];
    recv.read_exact(&mut body).await.map_err(|e| anyhow!("{e}"))?;
    Ok(body)
}

async fn write_frame(send: &mut SendStream, body: &[u8]) -> Result<()> {
    send.write_u32(body.len() as u32).await?;
    send.write_all(body).await?;
    Ok(())
}

/// Sends one message to a peer and waits for its answer.
async fn exchange(endpoint: &Endpoint, target: EndpointAddr, body: &[u8], timeout: Duration) -> std::result::Result<Vec<u8>, (&'static str, String)> {
    let conn = match tokio::time::timeout(timeout, endpoint.connect(target, CONTROL_ALPN)).await {
        Ok(Ok(conn)) => conn,
        Ok(Err(err)) => return Err(("NoRoute", format!("{err:#}"))),
        Err(_) => return Err(("NoRoute", "timed out connecting".into())),
    };

    let result = tokio::time::timeout(timeout, async {
        let (mut send, mut recv) = conn.open_bi().await.map_err(|e| anyhow!("{e}"))?;
        write_frame(&mut send, body).await?;
        send.finish().map_err(|e| anyhow!("{e}"))?;
        read_frame(&mut recv).await
    })
    .await;

    let outcome = match result {
        Ok(Ok(body)) => Ok(body),
        Ok(Err(err)) => Err(match conn.close_reason() {
            Some(ConnectionError::ApplicationClosed(close)) => match close.error_code.into_inner() as u32 {
                CODE_BLOCKED => ("Blocked", "the peer has blocked this node".to_string()),
                CODE_NOT_AUTHORIZED => ("NotAuthorized", "the peer refused the message".to_string()),
                _ => ("Failed", format!("{err:#}")),
            },
            _ => ("Failed", format!("{err:#}")),
        }),
        Err(_) => Err(("Timeout", "the peer did not answer in time".to_string())),
    };

    conn.close(VarInt::from_u32(CODE_DONE), b"done");
    outcome
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
    *app.blocked.write().unwrap() = parsed;
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
    match exchange(&app.endpoint, target, &body, timeout).await {
        Ok(body) => Json(RequestResult::Ok { body: BASE64.encode(body) }),
        Err((kind, message)) => failed(kind, message),
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
                IncomingKind::Message(body) => Json(json!({ "id": id, "remote_node_id": remote, "kind": "message", "body": BASE64.encode(body) })).into_response(),
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
    #[serde(default)]
    reject: bool,
    /// Why a file request was refused: NotAuthorized, Revoked or NotFound.
    reason: Option<String>,
}

async fn answer_incoming(State(app): State<Arc<App>>, UrlPath(id): UrlPath<u64>, Json(answer): Json<AnswerBody>) -> StatusCode {
    let Some(reply) = app.awaiting_reply.lock().unwrap().remove(&id) else {
        return StatusCode::NOT_FOUND;
    };

    let reply_with = if answer.reject {
        Reply::Reject(answer.reason)
    } else if let Some(path) = answer.path {
        Reply::File(PathBuf::from(path))
    } else {
        match answer.body.map(|b| BASE64.decode(b)) {
            Some(Ok(body)) => Reply::Body(body),
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
    transfer::start(&app.transfers, app.endpoint.clone(), request);
    StatusCode::ACCEPTED.into_response()
}

async fn list_transfers(State(app): State<Arc<App>>) -> Json<Vec<transfer::DownloadState>> {
    Json(app.transfers.list())
}

async fn cancel_transfer(State(app): State<Arc<App>>, UrlPath(id): UrlPath<String>) -> StatusCode {
    if app.transfers.cancel(&id) {
        StatusCode::NO_CONTENT
    } else {
        StatusCode::NOT_FOUND
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
