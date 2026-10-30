//! Coalesces network changes before asking Nodearr to announce its address to friends.

use std::time::Duration;

use iroh::{Endpoint, EndpointAddr, Watcher};
use tokio::sync::watch;

const SETTLE: Duration = Duration::from_secs(3);

pub(crate) fn watch(endpoint: &Endpoint) -> watch::Receiver<Option<EndpointAddr>> {
    let mut watcher = endpoint.watch_addr();
    let (updates, addresses) = watch::channel(watcher.get());
    let (announcements, receiver) = watch::channel(None);
    // Capture the baseline before the forwarding task can deliver its first update.
    tokio::spawn(settle(addresses, announcements));
    tokio::spawn(async move {
        while let Ok(address) = watcher.updated().await {
            if updates.send(address).is_err() {
                break;
            }
        }
    });
    receiver
}

fn settle(mut addresses: watch::Receiver<EndpointAddr>, announcements: watch::Sender<Option<EndpointAddr>>) -> impl std::future::Future<Output = ()> {
    let mut announced = addresses.borrow_and_update().clone();
    async move {
        loop {
            if addresses.changed().await.is_err() {
                return;
            }
            let mut address = addresses.borrow_and_update().clone();

            // Every update restarts the quiet period. No address is reported part way through a
            // network switch, including a switch that ends back at the address already announced.
            loop {
                match tokio::time::timeout(SETTLE, addresses.changed()).await {
                    Ok(Ok(())) => address = addresses.borrow_and_update().clone(),
                    Ok(Err(_)) => return,
                    Err(_) => break,
                }
            }
            if address != announced {
                announced = address.clone();
                if announcements.send(Some(address)).is_err() {
                    return;
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use iroh::{SecretKey, TransportAddr};

    fn address(id: iroh::EndpointId, port: u16) -> EndpointAddr {
        EndpointAddr::new(id).with_addrs([TransportAddr::Ip(format!("127.0.0.1:{port}").parse().unwrap())])
    }

    async fn advance(seconds: u64) {
        tokio::task::yield_now().await;
        tokio::time::advance(Duration::from_secs(seconds)).await;
        tokio::task::yield_now().await;
    }

    #[tokio::test(start_paused = true)]
    async fn burst_announces_only_the_settled_address() {
        let id = SecretKey::generate().public();
        let (updates, addresses) = watch::channel(address(id, 1));
        let (announcements, mut received) = watch::channel(None);
        tokio::spawn(settle(addresses, announcements));
        tokio::task::yield_now().await;
        assert!(!received.has_changed().unwrap());

        for port in 2..5 {
            updates.send(address(id, port)).unwrap();
            advance(1).await;
            assert!(!received.has_changed().unwrap());
        }
        advance(1).await;
        assert!(!received.has_changed().unwrap());
        advance(1).await;
        assert_eq!(*received.borrow_and_update(), Some(address(id, 4)));
        advance(10).await;
        assert!(!received.has_changed().unwrap());

        // A later change is compared with the address just announced, not the startup address.
        updates.send(address(id, 1)).unwrap();
        advance(1).await;
        advance(3).await;
        assert_eq!(*received.borrow_and_update(), Some(address(id, 1)));
    }

    #[tokio::test(start_paused = true)]
    async fn returning_to_the_starting_address_announces_nothing() {
        let id = SecretKey::generate().public();
        let initial = address(id, 1);
        let (updates, addresses) = watch::channel(initial.clone());
        let (announcements, received) = watch::channel(None);
        tokio::spawn(settle(addresses, announcements));
        tokio::task::yield_now().await;
        updates.send(address(id, 2)).unwrap();
        advance(1).await;
        updates.send(initial).unwrap();
        advance(1).await;
        advance(3).await;
        assert!(!received.has_changed().unwrap());
    }

    #[tokio::test(start_paused = true)]
    async fn a_change_before_the_task_starts_is_not_lost() {
        let id = SecretKey::generate().public();
        let (updates, addresses) = watch::channel(address(id, 1));
        let (announcements, mut received) = watch::channel(None);
        let settling = settle(addresses, announcements);
        updates.send(address(id, 2)).unwrap();
        tokio::spawn(settling);
        advance(1).await;
        advance(3).await;
        assert_eq!(*received.borrow_and_update(), Some(address(id, 2)));
    }

    #[tokio::test(start_paused = true)]
    async fn the_same_address_set_in_a_different_order_announces_nothing() {
        let id = SecretKey::generate().public();
        let first = TransportAddr::Ip("127.0.0.1:1".parse().unwrap());
        let second = TransportAddr::Ip("127.0.0.1:2".parse().unwrap());
        let (updates, addresses) = watch::channel(EndpointAddr::new(id).with_addrs([first.clone(), second.clone()]));
        let (announcements, received) = watch::channel(None);
        tokio::spawn(settle(addresses, announcements));
        updates.send(EndpointAddr::new(id).with_addrs([second, first])).unwrap();
        advance(1).await;
        advance(3).await;
        assert!(!received.has_changed().unwrap());
    }

    #[tokio::test(start_paused = true)]
    async fn relay_changes_are_announced_and_unread_announcements_are_replaced() {
        let id = SecretKey::generate().public();
        let initial = address(id, 1);
        let (updates, addresses) = watch::channel(initial.clone());
        let (announcements, mut received) = watch::channel(None);
        tokio::spawn(settle(addresses, announcements));
        tokio::task::yield_now().await;
        for relay in ["https://relay.example.com", "https://other.example.com"] {
            updates.send(initial.clone().with_addrs([TransportAddr::Relay(relay.parse().unwrap())])).unwrap();
            advance(1).await;
            advance(3).await;
        }
        assert_eq!(received.borrow_and_update().as_ref().unwrap().relay_urls().next().unwrap().to_string(), "https://other.example.com/");
    }
}
