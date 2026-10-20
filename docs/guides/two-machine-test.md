# Testing Nodearr between two machines

Everything in `acceptance/` runs on one machine. This guide tests two people, two networks, and one file going from one to the other. The home-to-home case passed on 2026-10-14 ([ADR 0011](../adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md#2026-10-14-two-home-networks)). Use this to check another pair of networks; the [V1 plan](../plans/v1-implementation-plan.md#what-is-still-open) lists what still needs a run.

It needs two machines on different networks. A laptop on a phone hotspot or a café's Wi-Fi counts as a second network.

**Two Windows machines with nothing set up**, no [Docker](https://docs.docker.com/) and no [Sonarr](https://sonarr.tv/) or [Radarr](https://radarr.video/), have a guide and a script of their own: [two-machine-test-windows.md](two-machine-test-windows.md). The rest of this page is for machines that run Docker, or that already run Sonarr or Radarr.

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

Copy `docker/docker-compose.yml`, set the two library paths, and change Nodearr's `image:` to `nodearr` to use the image built above. Then start it:

```sh
docker compose -f docker/docker-compose.yml up -d
```

Two things in that file decide whether the test is fair:

- **`/downloads` must be the same folder in Nodearr, Sonarr and Radarr.** If you already run Sonarr and Radarr, add your existing downloads folder to the Nodearr service at the same path they use, and drop the Sonarr and Radarr services from the file.
- **Networking.** On Linux keep `network_mode: host`. On Docker Desktop (Windows or macOS) use the published ports instead, as the file describes.

## The firewall on each machine

Check this before anything else. It is the one obstacle that no test on a single machine can show: both acceptance suites that run Nodearr outside Docker passed on a machine whose firewall was blocking it at the time, because both nodes were on that machine.

Nodearr needs incoming UDP on its port, which is 6633 in the Docker image. What that takes depends on how it runs:

- **Windows, without Docker.** Windows asks once whether to allow `nodearr-transport`. It may not ask until Nodearr is given a fixed port in **Settings > Node**. Say yes, and tick the kind of network you are on, including Public if that is what Windows calls it. If the question was missed or closed, Windows keeps a rule that blocks it. Open **Windows Security > Firewall & network protection > Allow an app through firewall**, find `nodearr-transport` and tick it there.
- **Docker Desktop, on Windows or macOS.** The published port belongs to Docker Desktop, not to Nodearr. Docker Desktop's own firewall rule may cover private networks only. On a network Windows calls Public, such as a café's, allow Docker Desktop on public networks too, in the same place.
- **Linux, with `network_mode: host`.** The host's firewall applies. With ufw: `sudo ufw allow 6633/udp`.

A blocked firewall and a router that needs a port forwarded look the same from inside Nodearr: **Check connection** says "Relay only", and **System > Status** warns that there is no direct route. Look at the firewall first. It is on a machine you can reach, and it is the quicker one to rule out.

## Set it up

On both sides, in Nodearr at `http://localhost:6633`:

1. **Settings > Apps.** Add Sonarr and Radarr. Nodearr adds itself to each as an indexer and a download client.
2. **Catalog > Local.** Refresh. Your files should be listed and readable.
3. **System > Status.** There should be no warnings. Two matter here. One says Nodearr cannot read the library, which means the other person's transfers would be refused. The other says Sonarr or Radarr cannot see Nodearr's download folder, which means nothing would ever be imported. Fix either before going on.
4. **Nodes > Trusted Nodes.** The box at the top says what Nodearr has worked out about your network. If it says the network uses a different public port for every destination, expect to need a forwarded port.
5. **Settings > Node.** Give the node a name the other person will recognise. In the Docker image the UDP port is fixed at 6633, which is the port to forward if it comes to that.

Then pair:

6. The side with the file: **Nodes > Invites**, tick "share everything", create an invite, and send the code to the other side by any means.
7. The other side: **Nodes > Invites**, paste the code and redeem it.
8. Both sides: **Nodes > Trusted Nodes** shows the other as trusted. Within a few seconds of pairing the Connection column fills in by itself. **Check connection** asks again at any time, and takes up to ten seconds. The column says one of two things:
   - **Direct.** A direct route was found. Go on and try the transfer.
   - **Relay only.** The nodes can talk and a transfer would fail as `NoDirectRoute`. There is no need to try one to find out. **System > Status** now carries a warning that names the other node. Check [the firewall on each machine](#the-firewall-on-each-machine). If both firewalls let Nodearr in, forward UDP 6633 on a router you control, to the machine running Nodearr. Check again after each change.

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
| **Check connection** said | Direct, relay only, or an error |
| The transfer | Completed, or failed with which name |
| If it completed | Size and how long it took |

The outcomes mean:

- **Completed.** The two nodes found a direct route. The file was verified before Sonarr or Radarr saw it.
- **NoDirectRoute.** The nodes could talk through a relay and could not reach each other directly. Nothing of the file was sent. The message, in Nodearr and in Sonarr's or Radarr's history, goes on to say what the last connection check found and which side to look at first. Write that down too. This is Nodearr working as designed on a network that does not allow a direct route. Phone networks and some café networks are like this. **Check connection** will have said "Relay only" beforehand. It is also what a firewall blocking Nodearr on either machine looks like, so rule that out first. Then try forwarding UDP 6633 on the side that has a router you control, and check again.
- **PeerUnreachable.** The nodes could not talk at all. Check that both are running and that **Check connection** works.

One "Direct" and one completed transfer between two home networks, with no port forwarded, is the result that matters most. A `NoDirectRoute` on a phone hotspot is worth recording and is not a failure of the test.

## The easy version: one script on each machine

`docker/two-machine-test.py` does the whole test as commands. On each machine it starts Nodearr and Radarr, adds Night of the Living Dead (a film in the public domain), and tells Nodearr about Radarr. The sending side makes a test video and imports it. The receiving side pairs, checks the connection, has Radarr search and grab, and prints what to write down. Nothing is clicked, and nobody is asked to sign in.

It needs Docker with the compose plugin, the `nodearr` image and Python 3. Everything it makes lives in Docker volumes and in `~/nodearr-test`, and `down` removes all of it.

The second machine can be a friend's, a laptop on a phone's hotspot, or a small Linux machine rented by the hour. The steps below are for the rented one, on EC2. Know what that shows before relying on it. A cloud machine has a public address of its own, which makes it the easiest node there is to reach. A direct connection to it shows that your own side works. It does not show that two homes can find each other, which takes a run with both ends behind ordinary routers.

### 1. Your own machine: the sending side

From the repository root:

```sh
docker build -t nodearr .
python3 docker/two-machine-test.py send
```

It ends by printing an invite code, the command for the other machine, and the SHA-256 of the file. Keep that window.

### 2. Rent the other machine

In the [Amazon EC2 console](https://console.aws.amazon.com/ec2/), launch an instance:

- **Ubuntu Server 24.04**, and **64-bit (x86)**. The script uses Radarr's x86 image, so not a Graviton type.
- **t3.medium**, with **30 GB** of storage.
- A key pair, saved as a `.pem` file.
- Under network settings, allow **SSH from My IP** and nothing else.

Check the [EC2 pricing](https://aws.amazon.com/ec2/pricing/) and your account's charges before launching. Stopping an instance can leave billable storage; terminate it and remove unneeded volumes when finished.

### 3. Send it the image and the script

```sh
docker save nodearr -o nodearr-image.tar
scp -i KEY.pem nodearr-image.tar docker/two-machine-test.py ubuntu@ITS-ADDRESS:~
```

Two things catch people who run these from WSL on Windows:

- **The key has to live on the Linux side.** `chmod` does nothing to a file under `/mnt/c` or another Windows drive, so SSH goes on refusing the key as too open. Copy it to `~/.ssh/` and `chmod 600` it there.
- **`ssh` may not be Linux's.** Some set-ups, 1Password's among them, make `ssh` in WSL mean Windows' `ssh.exe`, which cannot see `~/.ssh/`. It then says the key file does not exist, right after `ls` has shown it. Use `/usr/bin/ssh`.

### 4. The rented machine: the receiving side

```sh
ssh -i KEY.pem ubuntu@ITS-ADDRESS
```

Then, there:

```sh
sudo apt-get update && sudo apt-get install -y docker.io docker-compose-v2
sudo docker load -i nodearr-image.tar
python3 two-machine-test.py receive 'THE-CODE'
```

It ends with a block headed "What to write down": how the two nodes are connected, whether the film arrived, how long it took, and its SHA-256, which should be the one the sending side printed.

### 5. The second run, with the port open

The first run is with no UDP allowed in, which is as near as a cloud machine gets to a home with no port forwarded. For the second, add an inbound rule to the instance's security group: **Custom UDP, port 6633, from My IP**. Then, on the rented machine:

```sh
python3 two-machine-test.py fetch
```

`fetch` checks the connection again, removes the film and fetches it again.

### 6. Looking at the pages

There is no need to, but to see them, connect with:

```sh
ssh -i KEY.pem -L 16633:localhost:6633 -L 17878:localhost:7878 ubuntu@ITS-ADDRESS
```

The rented machine's Nodearr is then at `http://localhost:16633` and its Radarr at `http://localhost:17878`, on your own machine and nobody else's. Your own are at `http://localhost:6633` and `http://localhost:7878`. While that connection is open its ports are taken, and the scripts in `acceptance/` use one of them, so do not run those at the same time.

These test instances have sign-in disabled and bind their web ports to loopback. Keep those ports private: do not add inbound TCP rules for 6633 or 7878. Use the SSH tunnel to view the remote pages.

### 7. Clear up

On each machine:

```sh
python3 two-machine-test.py down
```

Then terminate the instance. Stopping it does not end the charge for its disk.

### What to expect from Docker Desktop

On Windows and macOS, Docker Desktop puts its own address translation between Nodearr and your router, and it gives a different public port to every destination. Measured on one Windows machine: Nodearr run straight on Windows saw one stable public port, and the same Nodearr in Docker Desktop saw a different one each time. Nodearr reports this on **Nodes > Trusted Nodes** and on **System > Status**, and says there that it is Docker Desktop and not the router.

So a node in Docker Desktop is hard to reach directly unless the other side can be reached first. Against a cloud machine, expect "Relay only" on the first run and "Direct" once UDP 6633 is allowed in there. That is what the first run between two machines found ([ADR 0011](../adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md)). Between two homes, the Docker Desktop side wants UDP 6633 forwarded on its router, or the other side does. Linux with the host's network has none of this.

Docker Desktop's own "Enable host networking" setting does not change this. It was tried on Docker Desktop 4.91 for Windows on 2026-10-13: with it on, the transport in a container on the host's network still reported a different public address for every destination. It also stopped Nodearr's transport from staying up in that container at all, for a reason not yet found, so leave `network_mode: host` to Linux.

### Without Docker Desktop in the way: `--native`

To see what the machine's own network allows, run Nodearr straight on it and only Radarr in a container. This needs a build of the repository, not the image. On Windows, from PowerShell in the repository root, not from WSL:

```sh
dotnet build src/Nodearr.sln
cargo build --release --manifest-path transport/Cargo.toml
python docker\two-machine-test.py send --native
```

If the transport was built somewhere else, add `--transport-bin PATH`. Windows may ask whether to let `nodearr-transport` through its firewall the first time. Say yes.

Send the printed invite code and SHA-256 to the other person. On their machine, run `python3 two-machine-test.py receive 'THE-CODE'`, adding `--native` if their Nodearr should also run outside Docker. Use `fetch` to retry, `status` to inspect the setup and `down` to remove it. Before switching a side between Docker and native mode, run `down` for that side.

This is the run worth making against a cloud machine that allows no UDP in. If the two connect directly, they did it with nothing opened on either side, which is what two homes would have to do. It has been made once, and they did ([ADR 0011](../adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md)).

To make that run clean, start the receiving side fresh on a UDP port it has not used: `down`, then `receive CODE --udp-port 6644`. A firewall goes on allowing a connection it has already seen, so a rule removed while two nodes are connected proves nothing.

### What has and has not been run

The script was run end to end on one machine with both sides: on a bridge network, on the host's network inside Docker's own Linux environment, and with `--native` on Windows for either side and for both. Each time it paired, connected directly, transferred and imported, with the same SHA-256 at both ends. `fetch` was run several times in a row, and a receiving side was given a second sending side's code.

Between two machines it has been run by the project's owner, between a Windows PC and an EC2 instance: with the PC's Nodearr in Docker Desktop, and with `--native`. The results are in ADR 0011. It has not been run between two home networks.

## Things worth trying once it works

- Hide the series on the sharing side (**Catalog > Sharing**), and search again on the other side. It should find nothing.
- Revoke the other node in the middle of a transfer. The transfer should stop, and Sonarr's history should say why.
- Restart the receiving Nodearr container in the middle of a transfer. It should carry on from where it was.
