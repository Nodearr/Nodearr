# Testing Nodearr between two machines

Everything in `acceptance/` runs on one machine. This is the test that cannot: two people, two networks, one file going from one home to the other. It is the last open question in the V1 plan, and it decides whether ADR 0011 (files only ever travel over a direct connection) can be accepted.

It needs two machines on different networks. A laptop on a phone hotspot or a café's Wi-Fi counts as a second network.

## What each side needs

- Docker.
- Sonarr or Radarr, with at least one series or movie. One side needs a file for it. The other side needs the same series or movie with that file missing.
- The Nodearr image. Build it once, from the repository root:

  ```sh
  docker build -t nodearr .
  ```

  To give it to the other person without them building it:

  ```sh
  docker save nodearr -o nodearr-image.tar
  ```

  ```sh
  docker load -i nodearr-image.tar
  ```

## Start it

Copy `docker/docker-compose.yml`, set the two library paths, and start it:

```sh
docker compose -f docker/docker-compose.yml up -d
```

Two things in that file decide whether the test is fair:

- **`/downloads` must be the same folder in Nodearr, Sonarr and Radarr.** If you already run Sonarr and Radarr, add your existing downloads folder to the Nodearr service at the same path they use, and drop the Sonarr and Radarr services from the file.
- **Networking.** On Linux keep `network_mode: host`. On Docker Desktop (Windows or macOS) use the published ports instead, as the file describes.

## The firewall on each machine

Check this before anything else. It is the one obstacle that no test on a single machine can show: both acceptance suites that run Nodearr outside Docker passed on a machine whose firewall was blocking it at the time, because both nodes were on that machine.

Nodearr needs incoming UDP on its port, which is 6633 in the Docker image. What that takes depends on how it runs:

- **Windows, without Docker.** Windows asks once whether to allow `nodearr-transport`. It may not ask until Nodearr is given a fixed port in Settings > Node. Say yes, and tick the kind of network you are on, including Public if that is what Windows calls it. If the question was missed or closed, Windows keeps a rule that blocks it. Open **Windows Security > Firewall & network protection > Allow an app through firewall**, find `nodearr-transport` and tick it there.
- **Docker Desktop, on Windows or macOS.** The published port belongs to Docker Desktop, not to Nodearr. Docker Desktop's own firewall rule may cover private networks only. On a network Windows calls Public, such as a café's, allow Docker Desktop on public networks too, in the same place.
- **Linux, with `network_mode: host`.** The host's firewall applies. With ufw: `sudo ufw allow 6633/udp`.

A blocked firewall and a router that needs a port forwarded look the same from inside Nodearr: "Check connection" says "Relay only", and System > Status warns that there is no direct route. Look at the firewall first. It is on a machine you can reach, and it is the quicker one to rule out.

## Set it up

On both sides, in Nodearr at `http://localhost:6633`:

1. **Settings > Applications.** Add Sonarr and Radarr. Nodearr adds itself to each as an indexer and a download client.
2. **Catalog > Local.** Refresh. Your files should be listed and readable.
3. **System > Status.** There should be no warnings. Two matter here. One says Nodearr cannot read the library, which means the other person's transfers would be refused. The other says Sonarr or Radarr cannot see Nodearr's download folder, which means nothing would ever be imported. Fix either before going on.
4. **Nodes > Trusted Nodes.** The box at the top says what Nodearr has worked out about your network. If it says the network uses a different public port for every destination, expect to need a forwarded port.
5. **Settings > Node.** Give the node a name the other person will recognise. In the Docker image the UDP port is fixed at 6633, which is the port to forward if it comes to that.

Then pair:

6. The side with the file: **Nodes > Invites**, tick "share everything", create an invite, and send the code to the other side by any means.
7. The other side: **Nodes > Invites**, paste the code and redeem it.
8. Both sides: **Nodes > Trusted Nodes** shows the other as trusted. Press "Check connection". It takes up to ten seconds, and then the Connection column says one of two things:
   - **Direct.** A transfer will work. Go on.
   - **Relay only.** The nodes can talk and a transfer would fail as `NoDirectRoute`. There is no need to try one to find out. System > Status now carries a warning that names the other node. Go back to the firewall section above, on both machines. If both firewalls let Nodearr in, forward UDP 6633 on a router you control, to the machine running Nodearr. Check again after each change.

## Run it

On the side that is missing the file:

1. **Catalog > Peer Catalog** lists what the other side offers, with the file marked missing.
2. In Sonarr or Radarr, search for it. A result from the indexer "Nodearr" appears. Grab it.
3. Watch **Activity > Transfers** in Nodearr. It shows under Receiving on this side, and under Sending on the other side.

## What to write down

For each pair of networks you try:

| | |
| --- | --- |
| Networks | For example: home fibre to phone hotspot |
| How each Nodearr is networked | Host network, or published ports |
| Did each machine's firewall allow incoming UDP 6633? | |
| Was UDP 6633 forwarded on either router? | |
| What each side's network box said | |
| "Check connection" said | Direct, relay only, or an error |
| The transfer | Completed, or failed with which name |
| If it completed | Size and how long it took |

The outcomes mean:

- **Completed.** The two nodes found a direct route. The file was verified before Sonarr or Radarr saw it.
- **NoDirectRoute.** The nodes could talk through a relay and could not reach each other directly. Nothing of the file was sent. This is Nodearr working as designed on a network that does not allow a direct route. Phone networks and some café networks are like this. "Check connection" will have said "Relay only" beforehand. It is also what a firewall blocking Nodearr on either machine looks like, so rule that out first. Then try forwarding UDP 6633 on the side that has a router you control, and check again.
- **PeerUnreachable.** The nodes could not talk at all. Check that both are running and that "Check connection" works.

One "Direct" and one completed transfer between two home networks, with no port forwarded, is the result that matters most. A `NoDirectRoute` on a phone hotspot is worth recording and is not a failure of the test.

## Things worth trying once it works

- Hide the series on the sharing side (**Catalog > Sharing**), and search again on the other side. It should find nothing.
- Revoke the other node in the middle of a transfer. The transfer should stop, and Sonarr's history should say why.
- Restart the receiving Nodearr container in the middle of a transfer. It should carry on from where it was.
