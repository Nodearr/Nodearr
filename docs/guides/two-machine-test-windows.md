# The two-machine test on Windows, from nothing

This is the two-machine test for two people on Windows who have nothing set up: no Docker, no Sonarr or Radarr, no Python. One script, `tools/two-machine-test.ps1`, does the whole test on each machine. It needs only the PowerShell that comes with Windows.

The two machines need to be on different networks: two homes, or a home and a phone's hotspot. What the test is for, and what each result means, is in [two-machine-test.md](two-machine-test.md).

## What the script does

On each machine it:

- unpacks Nodearr from its release package;
- downloads Radarr's own Windows package from Radarr's GitHub releases, and checks it against the SHA-256 GitHub publishes for it;
- starts both, straight on Windows, with their pages reachable from that machine only;
- adds Night of the Living Dead (a film in the public domain) to Radarr, and tells Nodearr about Radarr.

Then:

- **The sending side** downloads ffmpeg in the same way, makes a test video for the film, about 100 MB, imports it into Radarr, and prints an invite code.
- **The receiving side** pairs with that code, checks how the two machines are connected, has Radarr search for the film and grab it from the sending side, and prints what happened.

Nothing is installed, nothing is clicked, and nobody is asked to sign in. Everything lives in `nodearr-test-windows` in your user folder, and `down` removes it.

## Before you start: both of you

1. **Download the Nodearr package.** On the [Releases](https://github.com/Nodearr/Nodearr/releases) page, open the newest release and download `Nodearr.<version>.win-x64.zip` and `SHA256SUMS` into your **Downloads** folder. Leave the zip as it is. The script finds it there, unpacks it and checks it against `SHA256SUMS`.
2. **Download the script.** In the repository, open `tools/two-machine-test.ps1` and use **Download raw file**. Put it in Downloads as well.
3. **Open PowerShell in Downloads.** Start Windows PowerShell and run `cd ~\Downloads`.

Each command below starts with `powershell -ExecutionPolicy Bypass -File` so downloaded scripts can run under the usual execution policy. That setting is for this one command only.

The Nodearr checksum check is skipped if `SHA256SUMS` is missing or does not list the zip. Check the script's output says it matches the release's `SHA256SUMS` before relying on that check.

## The sending side

The person who sends runs:

```powershell
powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 send
```

It takes a few minutes, most of it downloading and making the video. It ends with a block like this:

```
On the other machine, run this. The code works once and for 24 hours:

  powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 receive 'THE-CODE'

The file it should end up with has this SHA-256:
  4f2a...
```

Send both, the command and the SHA-256, to the other person by any means. Then leave the machine on, with that window open or not. Nodearr and Radarr go on running in the background.

## The receiving side

The other person runs the command they were sent:

```powershell
powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 receive 'THE-CODE'
```

It ends with a block headed **What to write down**: how the two machines connected, whether the film arrived, how long it took, and its SHA-256. If it arrived, the SHA-256 is the same as the one the sending side printed.

## When Windows asks about the firewall

The first time Nodearr starts, Windows may ask whether to let `nodearr-transport` through its firewall. **Allow it.** Tick public networks as well as private if Windows offers both, because Windows calls some home networks public.

That question can open behind other windows. If it is cancelled, Windows adds a rule that blocks `nodearr-transport`, and the script warns about it. To fix that, open **Windows Security > Firewall & network protection > Allow an app through firewall**, find `nodearr-transport` and tick it.

Or skip the question: open PowerShell **as administrator** and add `-OpenFirewall` to `send` or `receive`. The script then adds a rule that lets UDP in to `nodearr-transport`, before it first starts, and `down` takes the rule away again.

## If it did not work

On either side:

```powershell
powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 status
```

It prints where the two pages are, what this node knows about its network, how it last found the other node, and anything Nodearr warns about.

On the receiving side, to try the transfer again after changing something:

```powershell
powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 fetch
```

It checks the connection again, removes the film and fetches it again.

What the results mean:

- **"Check connection" says Direct, and the transfer completed.** That is the result the test is for.
- **"Relay only", and the transfer failed as `NoDirectRoute`.** The two nodes can talk, but cannot reach each other directly, so no file was sent. Check the firewall on **both** machines first. If both allow `nodearr-transport`, forward UDP 6633 to that machine on whichever router you control, then run `fetch` again.
- **Could not reach the other node.** Check that the sending side is still on, and run `status` there.

Each side's pages are at `http://localhost:6633` for Nodearr and `http://localhost:7878` for Radarr, on that machine only. There is no need to open them, but they show the same things in more detail.

## Clearing up

On each machine, when you are done:

```powershell
powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 down
```

It stops Nodearr and Radarr and removes the `nodearr-test-windows` folder: Radarr, ffmpeg, the test film and both pages' data. The zip and the script in Downloads stay where they were.

## Options

| Option | What for |
| --- | --- |
| `-OpenFirewall` | Add the firewall rule instead of waiting for Windows to ask. Needs PowerShell as administrator. |
| `-NoPortMapping` | Stop Nodearr asking the router to open its port, which it does by default. See below. |
| `-Name 'Alex'` | What the other side sees this node called. Default: Sender or Receiver. |
| `-NodearrZip PATH` | Use a Nodearr package that is not in Downloads. |
| `-SizeMB 500` | A bigger test video, on the sending side. Default 100. |
| `-HttpPort`, `-UdpPort`, `-RadarrPort` | Other ports, if 6633 or 7878 is already taken. |
| `-Dir PATH` | Keep everything somewhere other than `nodearr-test-windows` in your user folder. |

## The run with nothing opened

Nodearr asks the router to open its UDP port for it, through UPnP, NAT-PMP or PCP, and many home routers do so without asking anyone. So a run where nobody forwarded a port may still have had one open. To show that two networks connect with nothing opened on either, run both sides again with port mapping off, on a UDP port neither has used:

```powershell
powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 down
```

Then, on the sending side:

```powershell
powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 send -NoPortMapping -UdpPort 6655
```

And on the receiving side, with the new code:

```powershell
powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 receive 'THE-CODE' -NoPortMapping -UdpPort 6656
```

A new port matters because a mapping the router already made for 6633 lasts a while after Nodearr stops asking for it.

## What has and has not been run

Run end to end on one Windows 11 machine on 2026-10-14, with both sides on it, in Windows PowerShell 5.1, with an early build of Nodearr, from before the public 0.1.0 release, and Radarr 6.4.4:

- **`send`** downloaded Radarr and ffmpeg, made an 85 MB video, imported it, and printed an invite.
- **`receive`**, on other ports, paired, connected directly, had Radarr grab the film, and imported it in 8 seconds, with the same SHA-256 as the sending side.
- **`fetch`** did it again.
- **`status`** and **`down`** worked. `down` left no process and no folder behind.

The same day it was run twice between two homes, on two Windows machines behind ordinary routers: once as it comes, and once with `-NoPortMapping` on fresh UDP ports. Both times the connection check said Direct, and the film arrived in 32 seconds with the same SHA-256 at both ends (ADR 0011, "two home networks").

The script keeps to its own folder. A folder it did not make, such as `~\nodearr-test` from `docker/two-machine-test.py`, is refused and left as it is.
