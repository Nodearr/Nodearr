//! Connections to other nodes that stay open while they are in use.
//!
//! The sidecar keeps one control connection open to each node Nodearr asks it to reach, and runs
//! each message on a new stream on it. Only the first message after a quiet spell pays for a
//! handshake and hole punching. A connection nobody has used for `idle` is closed, so the relay
//! does not see two nodes connected for hours for nothing (issue #123).
//!
//! Only connections this node opened are kept. Incoming ones are served as before, until the
//! other node closes them. Keeping a connection open trusts nothing: every message still goes to
//! Nodearr, which looks up the node's current state.
//!
//! A message Nodearr marks `direct` (a catalog, a poster, a title sent to a friend) waits for the
//! connection's selected path to be direct, is sent only while it stays direct, and its answer is
//! refused if any of it came through a relay (issue #122). Pairing, pings and revoking may cross a
//! relay: finding a path, and saying there is none, needs them. A kept connection that has carried
//! any of those through a relay is not used for such a message: the other node could not tell its
//! frames from the message's, and would refuse it. A new connection is opened for it instead,
//! once a direct path is there.
//!
//! Every connection, whichever side opened it and whatever it carries, is also listed in `Live`,
//! so that blocking a node can close what is already open to it.

use std::{
    collections::HashMap,
    sync::{
        atomic::{AtomicU64, Ordering},
        Arc, Mutex,
    },
    time::{Duration, Instant},
};

use anyhow::{anyhow, Result};
use iroh::{
    endpoint::{Connection, ConnectionError, ReadError, ReadExactError, VarInt, WeakConnectionHandle},
    Endpoint, EndpointAddr, EndpointId,
};

use crate::{
    read_frame,
    transfer::{send_direct, stream_frames_not_direct, NotSent, PathMonitor},
    write_frame, CODE_BLOCKED, CODE_DONE, CODE_NOT_AUTHORIZED, CODE_NO_DIRECT_ROUTE, CODE_RELAYED, CONTROL_ALPN,
};

/// Every connection to or from another node that is open now. Holds weak handles only, so it
/// never keeps a connection open that its owner has let go.
#[derive(Default)]
pub(crate) struct Live {
    next: AtomicU64,
    open: Mutex<HashMap<u64, (EndpointId, WeakConnectionHandle)>>,
}

impl Live {
    /// Lists a connection until it closes.
    pub fn track(self: &Arc<Self>, conn: &Connection) {
        let id = self.next.fetch_add(1, Ordering::Relaxed);
        let weak = conn.weak_handle();
        // Registered while a strong handle exists, so the close is seen however the connection ends.
        let closed = weak.closed();
        self.open.lock().unwrap().insert(id, (conn.remote_id(), weak));

        let live = self.clone();
        tokio::spawn(async move {
            closed.await;
            live.open.lock().unwrap().remove(&id);
        });
    }

    /// Closes every open connection to or from a node, and says how many there were.
    pub fn close(&self, node: EndpointId, code: u32, reason: &[u8]) -> usize {
        let open: Vec<Connection> = self.open.lock().unwrap().values().filter(|(remote, _)| *remote == node).filter_map(|(_, weak)| weak.upgrade()).collect();
        for conn in &open {
            conn.close(VarInt::from_u32(code), reason);
        }
        open.len()
    }

    #[cfg(test)]
    pub fn count(&self, node: EndpointId) -> usize {
        self.open.lock().unwrap().values().filter(|(remote, weak)| *remote == node && weak.upgrade().is_some_and(|c| c.close_reason().is_none())).count()
    }
}

/// A control connection this node opened, the path it is on, and when it was last used.
struct Cached {
    conn: Connection,
    paths: PathMonitor,
    activity: Mutex<Activity>,
}

struct Activity {
    /// Exchanges running on it now. It is never idle while one is.
    busy: usize,
    last: Instant,
}

/// Marks a connection busy for as long as it is held.
struct Busy<'a>(&'a Cached);

impl<'a> Busy<'a> {
    fn start(cached: &'a Cached) -> Self {
        cached.activity.lock().unwrap().busy += 1;
        Self(cached)
    }
}

impl Drop for Busy<'_> {
    fn drop(&mut self) {
        let mut activity = self.0.activity.lock().unwrap();
        activity.busy -= 1;
        activity.last = Instant::now();
    }
}

/// The connection kept for one node. Locked while it is being replaced, so that exchanges that
/// start together wait for one new connection instead of each opening their own.
type Slot = Arc<tokio::sync::Mutex<Option<Arc<Cached>>>>;

/// Why an exchange failed: the kind Nodearr is told, and a message.
pub(crate) type Failure = (&'static str, String);

pub(crate) struct ControlConnections {
    endpoint: Endpoint,
    live: Arc<Live>,
    /// Every control connection this node opened that is open now, kept or replaced. A replaced
    /// one stays open until it is idle, so closing a node has to find it here.
    opened: Arc<Live>,
    idle: Duration,
    /// How long a message that may not cross a relay waits for a direct path.
    direct_timeout: Duration,
    slots: Mutex<HashMap<EndpointId, Slot>>,
}

impl ControlConnections {
    pub fn new(endpoint: Endpoint, live: Arc<Live>, idle: Duration, direct_timeout: Duration) -> Self {
        Self { endpoint, live, opened: Arc::default(), idle, direct_timeout, slots: Mutex::new(HashMap::new()) }
    }

    fn slot(&self, node: EndpointId) -> Slot {
        self.slots.lock().unwrap().entry(node).or_default().clone()
    }

    /// Sends one message to a node and waits for its answer, on the connection kept for it. A
    /// kept connection that turns out to be dead is replaced, and the message tried once more.
    /// With `direct`, neither the message nor its answer may cross a relay.
    pub async fn exchange(&self, target: EndpointAddr, body: &[u8], timeout: Duration, direct: bool) -> std::result::Result<Vec<u8>, Failure> {
        let node = target.id;
        let slot = self.slot(node);
        let (cached, fresh) = self.ready(&slot, &target, timeout, direct, None).await?;
        let wait = direct.then_some(self.direct_timeout);

        match ask(&cached, body, timeout, wait).await {
            Err(Outcome::Dead(err)) if !fresh => {
                tracing::info!(remote = %node, error = %err, "kept control connection was dead; trying a new one");
                cached.conn.close(VarInt::from_u32(CODE_DONE), b"dead");
                let (cached, _) = self.ready(&slot, &target, timeout, direct, Some(&cached)).await?;
                ask(&cached, body, timeout, wait).await.map_err(Outcome::into_failure)
            }
            Err(Outcome::Relayed) if direct && !fresh => {
                // Something on the kept connection reached the other node through a relay without
                // this side seeing it, so it refused the message. A new connection has nothing
                // behind it.
                tracing::info!(remote = %node, "the other node refused a message on the kept control connection as relayed; trying a new one");
                let (cached, _) = self.ready(&slot, &target, timeout, direct, Some(&cached)).await?;
                ask(&cached, body, timeout, wait).await.map_err(Outcome::into_failure)
            }
            result => result.map_err(Outcome::into_failure),
        }
    }

    /// The connection to send a message on. For one that may not cross a relay, it waits for a
    /// direct path, and a kept connection that has sent anything through a relay is replaced by a
    /// new one once a direct path is there. Waiting first means no new connection is opened, and
    /// no handshake goes through the relay, while there is no direct path to use it on.
    async fn ready(&self, slot: &Slot, target: &EndpointAddr, timeout: Duration, direct: bool, replacing: Option<&Arc<Cached>>) -> std::result::Result<(Arc<Cached>, bool), Failure> {
        let (cached, fresh) = self.connection(slot, target.clone(), timeout, replacing).await?;
        if !direct {
            return Ok((cached, fresh));
        }

        wait_for_direct(&cached, self.direct_timeout).await?;
        if stream_frames_not_direct(&cached.conn).sent == 0 {
            return Ok((cached, fresh));
        }

        tracing::info!(remote = %target.id, "kept control connection has carried messages through a relay; opening a new one for a message that may not cross one");
        let (cached, fresh) = self.connection(slot, target.clone(), timeout, Some(&cached)).await?;
        wait_for_direct(&cached, self.direct_timeout).await?;
        Ok((cached, fresh))
    }

    /// The connection kept for a node, or a new one if there is none, it has closed, or it is the
    /// one `replacing` names. Another exchange may have replaced that one already, and then its
    /// connection is used. Also says whether it is new. A replaced connection is left to close
    /// when nothing has used it for a while, since other exchanges may still be on it, or when
    /// the node is closed (`close`).
    async fn connection(&self, slot: &Slot, target: EndpointAddr, timeout: Duration, replacing: Option<&Arc<Cached>>) -> std::result::Result<(Arc<Cached>, bool), Failure> {
        let node = target.id;
        let mut kept = match tokio::time::timeout(timeout, slot.lock()).await {
            Ok(kept) => kept,
            Err(_) => return Err(("NoRoute", "timed out connecting".into())),
        };

        let usable = |c: &&Arc<Cached>| c.conn.close_reason().is_none() && !replacing.is_some_and(|old| Arc::ptr_eq(old, c));
        if let Some(cached) = kept.as_ref().filter(usable) {
            return Ok((cached.clone(), false));
        }

        let conn = match tokio::time::timeout(timeout, self.endpoint.connect(target, CONTROL_ALPN)).await {
            Ok(Ok(conn)) => conn,
            Ok(Err(err)) => return Err(("NoRoute", format!("{err:#}"))),
            Err(_) => return Err(("NoRoute", "timed out connecting".into())),
        };
        self.live.track(&conn);
        self.opened.track(&conn);
        tracing::info!(remote = %node, "control connection opened");

        let paths = PathMonitor::spawn(&conn);
        let cached = Arc::new(Cached { conn, paths, activity: Mutex::new(Activity { busy: 0, last: Instant::now() }) });
        *kept = Some(cached.clone());
        tokio::spawn(close_when_idle(slot.clone(), cached.clone(), self.idle));
        Ok((cached, true))
    }

    /// Closes every control connection this node opened to a node, the kept one and any replaced
    /// one still open, and says whether there was any. Used when trust in it ends.
    pub async fn close(&self, node: EndpointId) -> bool {
        let slot = self.slots.lock().unwrap().get(&node).cloned();
        if let Some(slot) = slot {
            slot.lock().await.take();
        }
        self.opened.close(node, CODE_DONE, b"closed") > 0
    }

    #[cfg(test)]
    pub async fn is_open(&self, node: EndpointId) -> bool {
        let slot = self.slots.lock().unwrap().get(&node).cloned();
        match slot {
            Some(slot) => slot.lock().await.as_ref().is_some_and(|c| c.conn.close_reason().is_none()),
            None => false,
        }
    }
}

/// Closes a kept connection once nothing has used it for `idle`, and forgets it once it has
/// closed, however that came about.
async fn close_when_idle(slot: Slot, cached: Arc<Cached>, idle: Duration) {
    let remote = cached.conn.remote_id();
    let reason = loop {
        let wake = {
            let activity = cached.activity.lock().unwrap();
            if activity.busy > 0 {
                // Looked at again later. Its last use is set when the exchange ends.
                Some(Instant::now() + idle)
            } else if activity.last.elapsed() >= idle {
                None
            } else {
                Some(activity.last + idle)
            }
        };
        match wake {
            None => {
                cached.conn.close(VarInt::from_u32(CODE_DONE), b"idle");
                break "idle".to_string();
            }
            Some(at) => {
                tokio::select! {
                    reason = cached.conn.closed() => break format!("{reason}"),
                    _ = tokio::time::sleep_until(at.into()) => {}
                }
            }
        }
    };
    tracing::info!(remote = %remote, reason = %reason, "control connection closed");

    let mut kept = slot.lock().await;
    if kept.as_ref().is_some_and(|c| Arc::ptr_eq(c, &cached)) {
        *kept = None;
    }
}

enum Outcome {
    /// The connection had closed under the exchange, for no reason the other node gave. Nothing
    /// was answered, so trying again on a new connection is safe.
    Dead(String),
    /// The other node refused the message because something on the connection had come to it
    /// through a relay. Nothing was answered.
    Relayed,
    Failed(Failure),
}

impl Outcome {
    fn into_failure(self) -> Failure {
        match self {
            Self::Dead(message) => ("Failed", message),
            Self::Relayed => ("NoDirectRoute", "the other node refused it because part of what came to it on the connection came through a relay".to_string()),
            Self::Failed(failure) => failure,
        }
    }
}

/// Waits up to `wait` for the connection's selected path to be direct. A connection that closed
/// meanwhile, as one does when the other node has blocked this one, fails for the reason it gave.
async fn wait_for_direct(cached: &Cached, wait: Duration) -> std::result::Result<(), Failure> {
    let mut paths = cached.paths.clone();
    if paths.is_direct() || paths.wait_direct(wait).await {
        return Ok(());
    }
    if let Some(reason) = cached.conn.close_reason() {
        return Err(failure_from(Some(reason), anyhow!("the connection closed while it waited for a direct path")).into_failure());
    }
    let message = format!("no direct path to the other node within {}s (the selected path is {}), so nothing was sent", wait.as_secs(), paths.describe());
    tracing::info!(remote = %cached.conn.remote_id(), "held a message back: {message}");
    Err(("NoDirectRoute", message))
}

/// One message and its answer, on a new stream on `cached`. With `direct`, the message is sent
/// only while the selected path is direct (see `send_direct`), and an answer any of which came
/// through a relay is refused. The count for that starts before the stream exists, so every frame
/// of the answer arrives after it.
async fn ask(cached: &Cached, body: &[u8], timeout: Duration, direct: Option<Duration>) -> std::result::Result<Vec<u8>, Outcome> {
    let _busy = Busy::start(cached);
    let conn = &cached.conn;
    let not_direct_before = stream_frames_not_direct(conn).received;

    let result = tokio::time::timeout(timeout, async {
        let (mut send, mut recv) = conn.open_bi().await.map_err(|e| failure_from(conn.close_reason(), anyhow!("{e}")))?;
        match direct {
            Some(wait) => {
                let mut paths = cached.paths.clone();
                match send_direct(&mut send, body, &mut paths, wait).await {
                    Ok(()) => {}
                    // The path monitor also stops when the connection closes: say why it did.
                    Err(NotSent::NoDirectPath(message)) if conn.close_reason().is_none() => {
                        tracing::info!(remote = %conn.remote_id(), "held a message back: {message}");
                        return Err(Outcome::Failed(("NoDirectRoute", message)));
                    }
                    Err(NotSent::NoDirectPath(message)) => return Err(failure_from(conn.close_reason(), anyhow!(message))),
                    Err(NotSent::Failed(err)) => return Err(failure_from(conn.close_reason(), err)),
                }
            }
            None => {
                write_frame(&mut send, body).await.map_err(|e| failure_from(conn.close_reason(), e))?;
                send.finish().map_err(|e| failure_from(conn.close_reason(), anyhow!("{e}")))?;
            }
        }
        read_frame(&mut recv).await.map_err(|e| failure_from(conn.close_reason(), e))
    })
    .await;

    match result {
        Ok(Ok(_)) if direct.is_some() && stream_frames_not_direct(conn).received > not_direct_before => {
            tracing::info!(remote = %conn.remote_id(), "refused an answer that came through a relay");
            Err(Outcome::Failed(("NoDirectRoute", "the answer came through a relay and was refused. The other node may be on an older Nodearr, or lost its direct path".to_string())))
        }
        Ok(result) => result,
        Err(_) => Err(Outcome::Failed(("Timeout", "the peer did not answer in time".to_string()))),
    }
}

fn failure_from(reason: Option<ConnectionError>, err: anyhow::Error) -> Outcome {
    // The other node held its answer back because it had no direct path, or refused the message
    // because something came to it through a relay. Either way only that stream ended, and the
    // connection is still good for what may cross a relay.
    if let Some(ReadExactError::ReadError(ReadError::Reset(code))) = err.downcast_ref::<ReadExactError>() {
        match code.into_inner() as u32 {
            CODE_NO_DIRECT_ROUTE => return Outcome::Failed(("NoDirectRoute", "the other node had no direct path to answer on, and sent nothing".to_string())),
            CODE_RELAYED => return Outcome::Relayed,
            _ => {}
        }
    }

    match reason {
        Some(ConnectionError::ApplicationClosed(close)) => match close.error_code.into_inner() as u32 {
            CODE_BLOCKED => Outcome::Failed(("Blocked", "the peer has blocked this node".to_string())),
            CODE_NOT_AUTHORIZED => Outcome::Failed(("NotAuthorized", "the peer refused the message".to_string())),
            _ => Outcome::Dead(format!("{err:#}")),
        },
        // Gone quiet, restarted, or closed here: the other node did not refuse anything.
        Some(ConnectionError::TimedOut | ConnectionError::Reset | ConnectionError::LocallyClosed | ConnectionError::ConnectionClosed(_)) => Outcome::Dead(format!("{err:#}")),
        _ => Outcome::Failed(("Failed", format!("{err:#}"))),
    }
}

/// Opens a connection that is not kept, for a caller that needs to see the route as a new
/// connection would.
pub(crate) async fn connect_once(endpoint: &Endpoint, live: &Arc<Live>, target: EndpointAddr, timeout: Duration) -> Result<Connection> {
    let conn = tokio::time::timeout(timeout, endpoint.connect(target, CONTROL_ALPN)).await.map_err(|_| anyhow!("timed out connecting"))?.map_err(|e| anyhow!("{e:#}"))?;
    live.track(&conn);
    Ok(conn)
}

#[cfg(test)]
mod tests {
    use iroh::endpoint::ApplicationClose;

    use super::*;

    fn closed_with(code: u32) -> Option<ConnectionError> {
        Some(ConnectionError::ApplicationClosed(ApplicationClose { error_code: VarInt::from_u32(code), reason: Default::default() }))
    }

    fn kind(outcome: Outcome) -> std::result::Result<&'static str, &'static str> {
        match outcome {
            Outcome::Dead(_) => Err("dead"),
            Outcome::Relayed => Err("relayed"),
            Outcome::Failed((kind, _)) => Ok(kind),
        }
    }

    #[test]
    fn a_connection_that_went_away_is_tried_again_but_a_refusal_is_not() {
        let err = || anyhow!("stream ended");

        // The other node restarted, went quiet, or the connection was closed here or for no stated reason.
        for reason in [ConnectionError::Reset, ConnectionError::TimedOut, ConnectionError::LocallyClosed] {
            assert_eq!(kind(failure_from(Some(reason), err())), Err("dead"));
        }
        assert_eq!(kind(failure_from(closed_with(CODE_DONE), err())), Err("dead"));

        // The other node said no. Asking again on a new connection would get the same answer.
        assert_eq!(kind(failure_from(closed_with(CODE_BLOCKED), err())), Ok("Blocked"));
        assert_eq!(kind(failure_from(closed_with(CODE_NOT_AUTHORIZED), err())), Ok("NotAuthorized"));

        // The connection is still open: only this stream failed.
        assert_eq!(kind(failure_from(None, err())), Ok("Failed"));
    }
}
