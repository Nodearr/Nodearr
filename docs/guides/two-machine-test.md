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

## Set it up

On both sides, in Nodearr at `http://localhost:6633`:

1. **Settings > Applications.** Add Sonarr and Radarr. Nodearr adds itself to each as an indexer and a download client.
2. **Catalog > Local.** Refresh. Your files should be listed and readable.

Then pair:

3. The side with the file: **Nodes > Invites**, tick "share everything", create an invite, and send the code to the other side by any means.
4. The other side: **Nodes > Invites**, paste the code and redeem it.
5. Both sides: **Nodes > Trusted Nodes** shows the other as trusted. Press "Check connection".

## Run it

On the side that is missing the file:

1. **Catalog > Peer Catalog** lists what the other side offers, with the file marked missing.
2. In Sonarr or Radarr, search for it. A result from the indexer "Nodearr" appears. Grab it.
3. Watch **Activity > Transfers** in Nodearr.

## What to write down

For each pair of networks you try:

| | |
| --- | --- |
| Networks | For example: home fibre to phone hotspot |
| How each Nodearr is networked | Host network, or published ports |
| Was UDP 6633 forwarded on either router? | |
| "Check connection" worked? | |
| The transfer | Completed, or failed with which name |
| If it completed | Size and how long it took |

The outcomes mean:

- **Completed.** The two nodes found a direct route. The file was verified before Sonarr or Radarr saw it.
- **NoDirectRoute.** The nodes could talk through a relay and could not reach each other directly. Nothing of the file was sent. This is Nodearr working as designed on a network that does not allow a direct route. Phone networks and some café networks are like this. Try forwarding UDP 6633 on the side that has a router you control, then try again.
- **PeerUnreachable.** The nodes could not talk at all. Check that both are running and that "Check connection" works.

One completed transfer between two home networks, with no port forwarded, is the result that matters most. A `NoDirectRoute` on a phone hotspot is worth recording and is not a failure of the test.

## Things worth trying once it works

- Hide the series on the sharing side (**Catalog > Sharing**), and search again on the other side. It should find nothing.
- Revoke the other node in the middle of a transfer. The transfer should stop, and Sonarr's history should say why.
- Restart the receiving Nodearr container in the middle of a transfer. It should carry on from where it was.
