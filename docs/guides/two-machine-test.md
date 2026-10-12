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
8. Both sides: **Nodes > Trusted Nodes** shows the other as trusted. Within a few seconds of pairing the Connection column fills in by itself. "Check connection" asks again at any time, and takes up to ten seconds. The column says one of two things:
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
- **NoDirectRoute.** The nodes could talk through a relay and could not reach each other directly. Nothing of the file was sent. The message, in Nodearr and in Sonarr's or Radarr's history, goes on to say what the last connection check found and which side to look at first. Write that down too. This is Nodearr working as designed on a network that does not allow a direct route. Phone networks and some café networks are like this. "Check connection" will have said "Relay only" beforehand. It is also what a firewall blocking Nodearr on either machine looks like, so rule that out first. Then try forwarding UDP 6633 on the side that has a router you control, and check again.
- **PeerUnreachable.** The nodes could not talk at all. Check that both are running and that "Check connection" works.

One "Direct" and one completed transfer between two home networks, with no port forwarded, is the result that matters most. A `NoDirectRoute` on a phone hotspot is worth recording and is not a failure of the test.

## The easy version: your own machine and a rented one

A small Linux machine from a cloud provider makes a second network in a few minutes, with nobody else needed. These steps are for an EC2 instance. Any provider's Linux machine with a public address works the same way.

Know what it shows before relying on it. A cloud machine has a public address of its own, which makes it the easiest node there is to reach. A direct connection to it shows that your own side works: your router, your firewall, and Nodearr across the real internet. It does not show that two homes can find each other. That takes a second run with both ends behind ordinary routers, such as your machine and a laptop on a phone's hotspot.

These commands have not been run on AWS by whoever wrote this guide. They are the usual ones. This is also the first run of the Linux deployment with the host's own network, so write down anything that does not match.

### 1. Rent the machine

In the EC2 console, launch an instance:

- **Ubuntu Server 24.04**, and **64-bit (x86)**. There is no Arm image of Nodearr yet, so not a Graviton type.
- **t3.medium**. Nodearr with Sonarr and Radarr wants about 4 GB.
- **30 GB** of storage.
- A key pair, saved as a `.pem` file.
- Under network settings, allow **SSH from My IP** and nothing else.

Note its public address. It is billed by the second, and a stopped instance still pays for its disk.

### 2. Send it the image

On your own machine, from the repository root:

```sh
docker build -t nodearr .
docker save nodearr -o nodearr-image.tar
scp -i key.pem nodearr-image.tar docker/docker-compose.yml ubuntu@ITS-ADDRESS:~
```

Building on the rented machine is possible and not worth it. The build wants far more memory and time than the test does.

### 3. Start it there

Connect. The `-L` parts bring its web pages to your own machine and nobody else's:

```sh
ssh -i key.pem -L 16633:localhost:6633 -L 18989:localhost:8989 -L 17878:localhost:7878 ubuntu@ITS-ADDRESS
```

Then, on the rented machine:

```sh
sudo apt-get update && sudo apt-get install -y docker.io docker-compose-v2
sudo docker load -i nodearr-image.tar
mkdir -p nodearr/tv nodearr/movies && mv docker-compose.yml nodearr/ && cd nodearr
sed -i "s#/path/to/tv#$PWD/tv#; s#/path/to/movies#$PWD/movies#" docker-compose.yml
sudo docker compose up -d
```

Leave `network_mode: host` as it is. While the SSH connection is open, the rented machine's pages are on your own machine at:

- Nodearr: `http://localhost:16633`
- Sonarr: `http://localhost:18989`
- Radarr: `http://localhost:17878`

Your own Nodearr is still at `http://localhost:6633`. The different numbers keep the two apart.

### 4. Set it up as the side that wants the file

Receiving is the cheaper way round. Data going into a cloud machine is free, and data coming out is charged after a monthly allowance.

1. In the rented Sonarr: add `/tv` as a root folder, then add a series you have at home. It will have no files.
2. In the rented Nodearr, **Settings > Applications**, add Sonarr. Nodearr's address is `http://host.docker.internal:6633`. Sonarr's is `http://localhost:8989`. The API key is in Sonarr under Settings > General.
3. Carry on from "Set it up" above, with your own machine as the side that has the file.

### 5. Run it twice

1. **As it is.** No UDP is allowed in, which is as near as a cloud machine gets to a home with no port forwarded. Press "Check connection" on both sides, then search and grab in the rented Sonarr.
2. **With the port open.** In the instance's security group add an inbound rule: Custom UDP, port 6633, from My IP. Check the connection again.

Write down both, using the table above.

### 6. Stop paying

Terminate the instance when you are done. Stopping it is not enough to end the charge for its disk.

## Things worth trying once it works

- Hide the series on the sharing side (**Catalog > Sharing**), and search again on the other side. It should find nothing.
- Revoke the other node in the middle of a transfer. The transfer should stop, and Sonarr's history should say why.
- Restart the receiving Nodearr container in the middle of a transfer. It should carry on from where it was.
