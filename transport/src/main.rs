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
    routing::{get, post, put},
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

const CODE_DONE: u32 = 0;
const CODE_NOT_AUTHORIZED: u32 = 2;
const CODE_BLOCKED: u32 = 5;

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

struct Incoming {
    remote: EndpointId,
    body: Vec<u8>,
    reply: oneshot::Sender<Reply>,
}

enum Reply {
    Body(Vec<u8>),
    Reject,
}

struct App {
    endpoint: Endpoint,
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
        .alpns(vec![CONTROL_ALPN.to_vec()]);

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
    queue.send(Incoming { remote, body, reply }).await.map_err(|_| anyhow!("shutting down"))?;

    match tokio::time::timeout(Duration::from_secs(30), answer).await {
        Ok(Ok(Reply::Body(body))) => {
            write_frame(&mut send, &body).await?;
            send.finish().map_err(|e| anyhow!("{e}"))?;
            Ok(())
        }
        _ => {
            // Nodearr refused the message, or never answered.
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

async fn send_request(State(app): State<Arc<App>>, Json(request): Json<RequestBody>) -> Json<RequestResult> {
    let Ok(id) = EndpointId::from_str(&request.node_id) else {
        return failed("BadRequest", "bad node id");
    };
    let Ok(body) = BASE64.decode(&request.body) else {
        return failed("BadRequest", "body is not base64");
    };

    let mut addrs = Vec::new();
    if let Some(relay) = request.relay_url.as_deref().filter(|r| !r.is_empty()) {
        match RelayUrl::from_str(relay) {
            Ok(url) => addrs.push(TransportAddr::Relay(url)),
            Err(_) => return failed("BadRequest", "bad relay url"),
        }
    }
    for ip in &request.ip_addrs {
        match SocketAddr::from_str(ip) {
            Ok(addr) => addrs.push(TransportAddr::Ip(addr)),
            Err(_) => return failed("BadRequest", format!("bad address {ip}")),
        }
    }

    let timeout = Duration::from_millis(request.timeout_ms.unwrap_or(20_000));
    match exchange(&app.endpoint, EndpointAddr::from_parts(id, addrs), &body, timeout).await {
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
            Json(json!({ "id": id, "remote_node_id": incoming.remote.to_string(), "body": BASE64.encode(incoming.body) })).into_response()
        }
        _ => StatusCode::NO_CONTENT.into_response(),
    }
}

#[derive(Deserialize)]
struct AnswerBody {
    body: Option<String>,
    #[serde(default)]
    reject: bool,
}

async fn answer_incoming(State(app): State<Arc<App>>, UrlPath(id): UrlPath<u64>, Json(answer): Json<AnswerBody>) -> StatusCode {
    let Some(reply) = app.awaiting_reply.lock().unwrap().remove(&id) else {
        return StatusCode::NOT_FOUND;
    };

    let reply_with = match answer.body.filter(|_| !answer.reject).map(|b| BASE64.decode(b)) {
        Some(Ok(body)) => Reply::Body(body),
        _ => Reply::Reject,
    };
    let _ = reply.send(reply_with);
    StatusCode::NO_CONTENT
}
