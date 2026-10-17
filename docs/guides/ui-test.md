# Testing Nodearr by hand, in its own pages

This is a test of Nodearr as someone would use it: everything is done by clicking, in Nodearr's pages and in Sonarr's and Radarr's. One script, `tools/ui-test.ps1`, sets the scene, and then gets out of the way.

There are two ways to run it:

- **Solo**: you play both friends, on one machine. Two complete set-ups, A and B, each with its own Nodearr, Sonarr and Radarr, in two browser windows.
- **With a friend**: you on your machine, a friend on theirs, on different networks: two homes, or a home and a phone's hotspot. Each of you runs one set-up.

Where the two differ, the step says so, marked **Solo** or **With a friend**. Everything else is the same.

The scripted tests in [two-machine-test-windows.md](two-machine-test-windows.md) drive the same things with commands, and say what each result means. This guide is for seeing what a person sees.

## What the script does

On each set-up it:

- unpacks Nodearr from its release package, and starts it as a new install, which asks you to set up signing in;
- downloads Sonarr and Radarr from their GitHub releases, checks them against the SHA-256 each release publishes, and starts them with signing in turned off;
- gives each a small library of test videos: test patterns with a tone, named as releases are, so Sonarr and Radarr import them as real titles.

| | Library A | Library B |
| --- | --- | --- |
| Radarr | Night of the Living Dead (1968) | Plan 9 from Outer Space (1957) |
| Sonarr | Pingu, S01E01 to S01E03 | Bluey, S01E01 to S01E03 |

Each library has what the other lacks, so each side has something to share and something to fetch.

It never touches Nodearr itself: no app is connected, no friend added, nothing shared. That is the test. Nothing is installed, and every page is reachable from your machine only. Everything lives in `nodearr-ui-test` in your user folder, and `down` removes it.

| | Nodearr | Sonarr | Radarr |
| --- | --- | --- | --- |
| Set-up A | http://localhost:26633 | http://localhost:28989 | http://localhost:27878 |
| Set-up B | http://127.0.0.1:26643 | http://localhost:28990 | http://localhost:27879 |

These ports are not Nodearr's, Sonarr's or Radarr's usual ones, so an install you already have keeps running. B's Nodearr is opened at `127.0.0.1` rather than `localhost`: a browser keeps one sign-in per name, whatever the port, so with both at `localhost` signing in to one would sign you out of the other.

## Before you start

You need Windows 10 or 11, about 1 GB free, and an internet connection. The script uses the PowerShell that comes with Windows. Sonarr and Radarr need the internet to look up titles and download their posters.

1. **Download the Nodearr package.** From the newest release on [Releases](https://github.com/Nodearr/Nodearr/releases), download `Nodearr.<version>.win-x64.zip` and `SHA256SUMS` into your **Downloads** folder. Leave the zip as it is. The script finds it there and checks it. Use a release from after Send to a friend and posters were added: version 0.2.0 or later.
   - *From the source instead*: build a package with `build/Build-Release.ps1` and give the script its path, `-NodearrZip PATH`.
2. **Download the script.** In the repository, open `tools/ui-test.ps1` and use **Download raw file**. Put it in Downloads as well.
3. **Open PowerShell in Downloads.** In File Explorer, open Downloads, then **File > Open Windows PowerShell**. Or start PowerShell and run `cd ~\Downloads`.

Each command starts with `powershell -ExecutionPolicy Bypass -File`, because Windows will not run a downloaded script otherwise. That setting is for that one command only.

## 1. Set up

**Solo**, both set-ups on your machine:

```powershell
powershell -ExecutionPolicy Bypass -File ui-test.ps1 up -Solo
```

**With a friend**, you take library A and your friend library B. You run:

```powershell
powershell -ExecutionPolicy Bypass -File ui-test.ps1 up -Library A
```

and your friend, on their machine:

```powershell
powershell -ExecutionPolicy Bypass -File ui-test.ps1 up -Library B
```

The first run takes a few minutes: it downloads Sonarr, Radarr and ffmpeg, and makes the test videos. It ends with a card like this, for each set-up:

```text
Set-up A
  Nodearr  http://localhost:26633
  Sonarr   http://localhost:28989   API key 3f9c...
  Radarr   http://localhost:27878   API key 81ab...
  Library  Night of the Living Dead, and Pingu S01E01 to S01E03
  Nodearr's UDP port, for another machine: 26633
```

Keep it open. `ui-test.ps1 status` shows it again, and it is saved in `nodearr-ui-test\test-card.txt`.

**Windows Firewall.** When Nodearr starts, Windows may ask whether to let `nodearr-transport` through. Allow it.

- **With a friend**, this matters. Without it, your friend's node cannot reach yours directly, and files cannot move. The question can open behind other windows. If you cancelled it, see [If something goes wrong](#if-something-goes-wrong).
- **Solo**, both nodes are on one machine and do not need it. Allowing it does no harm.
- To skip the question, run the command from PowerShell opened as administrator, with `-OpenFirewall` added. That adds a rule for this test, which `down` removes.

**With a friend**: from here on, each of you does each step on your own set-up, unless a step says who.

## 2. Sign in for the first time

Open your Nodearr's address from the card. **Solo**: open A in one browser window and B in another.

- [ ] Nodearr asks you to choose how to sign in, and to set a username and password. Do so, and press **Save**.
- [ ] Then it welcomes you, with three steps on Home: connect your apps, choose your name, connect with a friend.
- [ ] The next time you open or reload the page, it asks you to sign in with them, once. From then on it remembers you.

## 3. Connect Sonarr and Radarr

On Home, in **Connect your Arr apps**, press **Connect Sonarr**.

1. In **Application Server**, put your Sonarr's address from the card, such as `http://localhost:28989`. It is not Sonarr's usual 8989.
2. In **API Key**, put your Sonarr's API key from the card.
3. Leave **Nodearr Server** as it is. Both run on this machine.
4. Press **Test**, then **Save**.

Do the same with **Connect Radarr**, with Radarr's address and key.

- [ ] Both save without an error, and the first step on Home is ticked.
- [ ] **Catalog > Local** lists your library's film and series, three episode files and one film file.
- [ ] In your Sonarr, **Settings > Indexers** and **Settings > Download Clients** each list one called **Nodearr**. So does your Radarr. Nodearr added them.

## 4. Choose your name

On Home, in **Choose your name**, type a name, such as yours, and press **Use this name**. It is what your friend sees you called.

- **Solo**: call them something you can tell apart, such as "Alex A" and "Alex B".

## 5. Become friends

One of you invites, the other uses the invite. **Solo**: A invites, B uses it.

**The one who invites**, on Home, in **Connect with a friend**:

1. Leave **Share my whole library with them** ticked. It is the default.
2. Press **Create an invite**, and copy the code.
3. **With a friend**: send it any way you like: a message, an email. Anyone who has it can use it until it is used or expires, so send it only to them. **Solo**: paste it into B's page.

**The one invited**, on Home, under **Have a code?**: paste the code, leave the box ticked, and press **Connect**.

- [ ] Both Homes now say **You're all set**, with the other under **Friends**, and **From your friends** counts what the other has that is new to your apps.
- [ ] **Nodes > Trusted Nodes** lists the other, as **Trusted**.
- [ ] Press **Check connection** there. **Connection** says **Direct**.
  - **Solo**: always Direct.
  - **With a friend**: Direct is what you want. **Relay only** means your two nodes can talk but files cannot move between them. See [If something goes wrong](#if-something-goes-wrong).

## 6. Browse what your friend has

Open **Catalog > Peer Catalog**. If it is empty, press **Sync Peer Catalogs**. A node fetches its friends' catalogs every 15 minutes, and at once after pairing.

- [ ] It lists your friend's film and three episodes, each with its poster, marked **Not in library**.
- [ ] Press **Posters**, top right: a grid with one tile for the film and one for the series, with three files. Press **Table** to go back.
- [ ] Each poster appeared within a second or two. Reload the page: they appear at once now. Nodearr kept a copy.

## 7. Choose what to share

On the side that has Pingu, A, open **Catalog > Sharing**.

1. Under **Hidden**, open **Pingu** with its arrow, tick **S01E02**, and press **Save**.
2. On the other side, B, look at **Catalog > Peer Catalog**.

- [ ] Within a few seconds, S01E02 is gone from B's list, without pressing anything. Taking something back is immediate.

Then, on A, untick it under Hidden and save. On B, press **Sync Peer Catalogs**.

- [ ] S01E02 is back. Something newly shared waits for the next sync, or the button.

## 8. Send a friend something, and get it

**A sends.** On A, in **Catalog > Sharing**, under **Send to a friend**:

1. Type *Pingu* in the filter and pick it. Leave it whole: the whole series.
2. Under **To**, pick B's name.
3. Press **Send**.

- [ ] It says "Sent Pingu to ...", and under **Sent** the send shows **Told** within a few seconds.

**B gets it.** On B's Home:

- [ ] A card says "... sent you Pingu", with **Root Folder**, **Quality Profile** and **Series Type** filled in.
- [ ] Press **Get it**. The card says it is asking Sonarr, then "Added to Sonarr, monitoring the whole series, and searching for 3 episodes": the three A has. Sonarr monitors the whole series, so it looks for the rest on its own later, as it would for any series.
- [ ] **Activity > Transfers** shows Pingu's three episodes arriving from A, one after another, then done.
- [ ] In B's Sonarr, **Series > Pingu** shows the three episodes with files. Sonarr imported what Nodearr fetched.

**Then the other way round.** B sends **Plan 9 from Outer Space** to A, and A presses **Get it** on its card.

- [ ] A's Radarr adds the film, grabs it through Nodearr, and imports it. The film is larger than an episode, so give it a minute.

## 9. Fetch through the app's own search

This is how Nodearr works day to day: Sonarr or Radarr search, and find what friends have among their other results.

On B's Radarr, add **Night of the Living Dead**: **Movies > Add New**, search for it, and pick the one from **1968** (there are several). Untick **Start search for missing movie**, which Radarr ticks for you, and press **Add Movie**. Then open the film and press **Interactive Search** at the top.

- [ ] A release from the indexer **Nodearr** is listed, with its size and quality.
- [ ] Press its download button. Nodearr's **Activity > Transfers** shows it arriving from A, and Radarr imports it.

## 10. Optional: send to someone who is not a friend yet

This is how a friendship can start: "I have that, let me send it to you."

1. On A, **Nodes > Trusted Nodes**, press **Revoke trust** on B. The two are no longer friends.
2. On A, in **Catalog > Sharing**, under **Send to a friend**, pick **Night of the Living Dead**, and under **To** pick **Someone new, with an invite**. Leave **Share everything except Hidden with them too** ticked, press **Send**, and copy the invite code it shows.
3. On B, **Nodes > Invites**, paste the code under **Redeem Invite**, and press **Redeem**.

- [ ] The two are friends again, and A's send shows **Told**.
- [ ] B's Home shows a card for Night of the Living Dead. B has it from step 9, so it says Radarr has it already.

## 11. Look around

- **System > Status**: no warnings you did not expect.
- **Activity > Transfers**: every transfer done, none failed.
- **Home**: the overview says what needs attention, what your friend has, and how you are connected.

## What to write down

For each step, whether each box held, and anything that surprised you: a page that was slow, a word that was unclear, something you looked for and did not find. Send that, with:

- `powershell -ExecutionPolicy Bypass -File ui-test.ps1 status`, what it prints;
- Nodearr's log, `nodearr-ui-test\a\nodearr-data\logs\nodearr.txt` (or `b\`), where something failed;
- **With a friend**: what kind of network each of you was on (home, phone hotspot, office, mobile data), and what **Connection** said in step 5.

## If something goes wrong

**Relay only, with a friend.** The two nodes reach each other only through the relay, which never carries files.

- Check the firewall on each machine: **Windows Defender Firewall > Advanced settings > Inbound Rules**. A rule for `nodearr-transport` set to **Block** wins over any allow; it is what Windows adds when its question is cancelled. Delete it, or run `up` again with `-OpenFirewall` from an administrator PowerShell. `status` warns of such a rule.
- Some networks cannot be reached directly at all, such as mobile data on many phones. Try a home network on both sides.
- If one of you can, forward UDP port 26633 (A) or 26643 (B) on the router to that machine. Nodearr also asks the router to do this itself, where the router allows it.
- Then press **Check connection** again.

**A port is already in use.** The script says which. Something else on the machine is using it: stop it, or stop the earlier run with `stop`.

**Nodearr does not start.** Its log is `nodearr-ui-test\a\nodearr-data\logs\nodearr.txt` (or `b\`).

**Sonarr or Radarr cannot find the title, or posters are missing.** They need the internet to look titles up and download posters. Run `up` again once you are online.

**A file never arrives.** **Activity > Transfers** says why it stopped. **Nodes > Trusted Nodes** says whether the friend is reachable now.

## Stopping and clearing up

```powershell
powershell -ExecutionPolicy Bypass -File ui-test.ps1 stop
```

stops everything and keeps it all, to start again as it was with `up`.

```powershell
powershell -ExecutionPolicy Bypass -File ui-test.ps1 down
```

stops everything and removes `nodearr-ui-test`, and the firewall rule if `-OpenFirewall` added one.

## What has and has not been run

The script was run solo on Windows 11, from the PowerShell that comes with Windows, and steps 2 to 9 were done in the pages as written. Doing them changed four things before this guide was published: the overview on Home now counts what is new to your apps, a help text about invites was out of date, a whole series sent was searched for episode by episode down to ones the friend did not have, and B's Nodearr is opened at `127.0.0.1`.

It has not yet been run with a friend on another network.
