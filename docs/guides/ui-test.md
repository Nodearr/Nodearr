# Testing Nodearr by hand, in its own pages

This walkthrough tests Nodearr through its web UI and the interfaces of [Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/). The `tools/ui-test.ps1` script starts isolated test applications and creates sample libraries. You then connect the apps, pair the nodes, share titles and request downloads yourself.

There are two ways to run it:

- **Solo**: you play both friends, on one machine. Two complete set-ups, A and B, each with its own Nodearr, Sonarr and Radarr, in two browser windows.
- **With a friend**: you on your machine, a friend on theirs, on different networks: two homes, or a home and a phone's hotspot. Each of you runs one set-up.

Where the two differ, the step says so, marked **Solo** or **With a friend**. Everything else is the same.

The [scripted Windows tests](two-machine-test-windows.md) drive the same things with commands, and say what each result means. This guide is for seeing what a person sees.

## What the script does

On each set-up it:

- unpacks Nodearr from its release package, and starts it as a new install, which asks you to set up signing in;
- downloads Sonarr and Radarr from their GitHub releases, checks them against the SHA-256 each release publishes, and starts them with signing in turned off;
- gives each a small library of test videos: test patterns with a tone, named as releases are, so Sonarr and Radarr import them as real titles.

| | Library A | Library B |
| --- | --- | --- |
| Radarr | Night of the Living Dead (1968), Nosferatu (1922), Steamboat Willie (1928) | The Little Shop of Horrors (1960), Metropolis (1927), Aladdin and His Wonderful Lamp (1939) |
| Sonarr | Out of the Inkwell (1919), S01E01 to S01E03 | Felix the Cat (1919), S1919E01 to S1919E03 |

Every title is in the public domain. TheTVDB numbers Felix the Cat's seasons by year, so its first season is 1919. Each library has what the other lacks, so each side has something to share and something to fetch.

The script starts test copies of the applications without installing them. It leaves Nodearr's app connections, friends and sharing unconfigured for you to test. Each web UI is reachable only from this machine. Test files and data live in `nodearr-ui-test` under your user folder; `down` removes that folder.

| | Nodearr | Sonarr | Radarr |
| --- | --- | --- | --- |
| Set-up A | http://localhost:26633 | http://localhost:28989 | http://localhost:27878 |
| Set-up B | http://127.0.0.1:26643 | http://localhost:28990 | http://localhost:27879 |

These ports are not Nodearr's, Sonarr's or Radarr's usual ones, so an install you already have keeps running. B's Nodearr is opened at `127.0.0.1` rather than `localhost`: a browser keeps one sign-in per name, whatever the port, so with both at `localhost` signing in to one would sign you out of the other.

## Before you start

You need Windows 10 or 11, about 1 GB free, and an internet connection. The script uses the PowerShell that comes with Windows. Sonarr and Radarr need the internet to look up titles and download their posters.

1. **Download the Nodearr package.** From the newest release on [Releases](https://github.com/Nodearr/Nodearr/releases), download `Nodearr.<version>.win-x64.zip` and `SHA256SUMS` into your **Downloads** folder. Leave the zip as it is. The script finds it there and checks it. Use a release that includes sends and posters: version 0.2.0 or later.
   - *From the source instead*: build a package with `build/Build-Release.ps1` and give the script its path, `-NodearrZip PATH`.
2. **Download the script.** In the repository, open `tools/ui-test.ps1` and use **Download raw file**. Put it in Downloads as well.
3. **Open PowerShell in Downloads.** Start Windows PowerShell and run `cd ~\Downloads`.

Each command starts with `powershell -ExecutionPolicy Bypass -File` so downloaded scripts can run under the usual execution policy. That setting is for that one command only.

The Nodearr checksum check is skipped if `SHA256SUMS` is missing or does not list the zip. Check the card says the package matches the release's `SHA256SUMS`.

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
  Library  Night of the Living Dead, Nosferatu, Steamboat Willie, and Out of the Inkwell S01E01 to S01E03
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

- [ ] Nodearr asks you to choose how to sign in, and to set a username and password. Do so, and select **Save**.
- [ ] Then it welcomes you, with three steps on **Home**: connect your apps, choose your name, connect with a friend.
- [ ] The next time you open or reload the page, it asks you to sign in with them, once. From then on it remembers you.

## 3. Connect Sonarr and Radarr

On **Home**, in **Connect your Arr apps**, select **Connect Sonarr**.

1. In **Application Server**, put your Sonarr's address from the card, such as `http://localhost:28989`. It is not Sonarr's usual 8989.
2. In **API Key**, put your Sonarr's API key from the card.
3. Leave **Nodearr Server** as it is. Both run on this machine.
4. Select **Test**, then **Save**.

Do the same with **Connect Radarr**, with Radarr's address and key.

- [ ] Both save without an error, and the first step on **Home** is ticked.
- [ ] **Catalog > Local** lists your library's film and series, three episode files and one film file.
- [ ] Scroll down and open a series or movie card; closing it returns you to the same place. Repeat on a phone.
- [ ] The series card marks episodes in the library and those missing. Open an episode with a file and use **Send to a friend**; the send form selects that episode. A missing or unreadable episode offers no send action.
- [ ] In a fresh browser, **Show missing episodes** starts off in the local series table and cards. Turn it on to include missing episodes, then reload and reopen a card: the choice stays on. Change it in the card and check that the table follows. Include a series with no downloaded episodes and a catalog with more than one page.
- [ ] A local series or movie card also offers **Send to a friend**. The form selects the title shown on the card.
- [ ] Switch to the light theme and check that the header, sidebar, toolbar, menus and mobile sidebar controls are readable; switch back to dark and check them again.
- [ ] In your Sonarr, **Settings > Indexers** and **Settings > Download Clients** each list one called **Nodearr**. So does your Radarr. Nodearr added them.

## 4. Choose your name

On **Home**, in **Choose your name**, type a name, such as yours, and select **Use this name**. It is what your friend sees you called.

- **Solo**: call them something you can tell apart, such as "Alex A" and "Alex B".

## 5. Become friends

One of you invites, the other uses the invite. **Solo**: A invites, B uses it.

**The one who invites**, on **Home**, in **Connect with a friend**:

1. Leave **Share my library with them** ticked. It is the default. It includes the apps marked **Share With New Friends**, which starts on for Sonarr and Radarr.
2. Select **Create an invite**, and copy the code.
3. **With a friend**: send it any way you like: a message, an email. Anyone who has it can use it until it is used or expires, so send it only to them. **Solo**: paste it into B's page.

**The one invited**, on **Home**, under **Have a code?**: paste the code, leave the box ticked, and select **Connect**.

- [ ] Both Homes now say **You're all set**, with the other under **Friends**, and **From your friends** counts what the other has that is new to your apps.
- [ ] **Nodes > Trusted Nodes** lists the other, as **Trusted**.
- [ ] Select **Check connection** there. **Connection** says **Direct**.
  - **Solo**: the connection should show **Direct**.
  - **With a friend**: check for **Direct**. **Relay only** means your two nodes can talk but files cannot move between them. See [If something goes wrong](#if-something-goes-wrong).

## 6. Browse what your friend has

Open **Catalog > Peer Catalog**. If it is empty, select **Sync Peer Catalogs**. A node fetches its friends' catalogs every 15 minutes, and at once after pairing.

- [ ] It lists your friend's film and three episodes, each with its poster, marked **Not in library**.
- [ ] Select **Posters**: a grid with one tile for the film and one for the series, with three files. Select **Table** to go back.
- [ ] Each poster appeared within a second or two. Reload the page: they appear at once now. Nodearr kept a copy.

Each missing file and peer details card has **Get it**; posters show a download icon on hover, keyboard focus or touch. It opens the same choices as Home's send card. For TV, the dialog offers an episode file, season or whole series. To keep the later walkthrough's library unchanged, run the following checks separately on a fresh kit after connecting, pairing and sharing:

- [ ] On B, use **Get it** on A's movie row. Root folder and profile are filled in. Submit, follow progress in the dialog and **Activity > Transfers**, and confirm Radarr imports it. The row becomes **Have** with no action after the local catalog updates.
- [ ] Open a TV row's Get it immediately after loading the page. The scope choice stays disabled until the plan loads; then switching to **Season** selects an offered season and shows the app choices.
- [ ] On an Out of the Inkwell row, choose **Episode file**. Submit and confirm Sonarr imports only that file's episodes; the others stay unmonitored. Reopen Get it, select **Season**, choose season 1, and confirm the other two files arrive.
- [ ] On a fresh kit, switch to **Posters** and select the series' **Get it**. It starts at **Whole series**. Switch to **Episode file**, **Season**, then back, and check the choices remain usable. Submit; Sonarr monitors the series and searches only offered episodes.
- [ ] While the add command is working, close and reopen the dialog. Its progress remains visible and a second add cannot be submitted. After the app grabs it, the dialog's link opens **Activity > Transfers**.
- [ ] With two Sonarr or Radarr apps connected, choose the second app and confirm its defaults appear. Stop that test app: the dialog says it did not answer. Start it and return to the dialog: it recovers while keeping valid choices.
- [ ] On an app with no root folder, the dialog explains what to add. Add a root folder in the app, return to the dialog, and check Get it becomes usable without reloading. Select another valid root or profile, let the plan refresh, and check it stays selected. Delete that unused choice in the app and check it is replaced. Repeat on Home's send card.
- [ ] With the music fixture from `acceptance/Run-CatalogGetIt.ps1 -KeepRunning`, open the album's Get it from the row and poster. Check Lidarr's root, quality and metadata profiles, then confirm all tracks import as one album. A Whisparr v2 scene instead shows the same explanation as Home.
- [ ] Open a peer series card and use **Get it**: it starts at **Whole series**. Choose **Season** and check the offered season is selected. Close the Get it dialog, open an episode from the series card, and check its **Get it** starts at that episode file. Repeat with a movie card. Closing Get it returns to the details card; closing the card keeps the catalog's scroll position.
- [ ] A **Have** movie or episode card offers no Get it. A series with some missing or upgradable episodes still does. Withdraw the selected offer or revoke the friendship while a card is open: its action disappears or the card reports that it is unavailable.
- [ ] At phone width, the poster download icon is visible without hovering, stays within the image, and opens the same dialog. On desktop, reach it with the keyboard. Tiles with **Have** have no icon and keep the same poster height as actionable tiles.
- [ ] An **Upgrade** has Get it; your Arr app's search rules still apply. A withdrawn offer or revoked friendship cannot be requested from a dialog left open. Catalog requests create no new send card on Home.

### Shared details and updating an older cache

- [ ] On A, open Sam's Felix the Cat series card, select an episode, then open Metropolis. Compare descriptions, genres, network when present, runtime, dates, IDs and file resolutions with the local cards on B. Episode descriptions must belong to the selected episode. Fields absent from the Arr app stay absent.
- [ ] Run `node tools/docs-screenshots.mjs peerMetadata` with the kit environment described in the script. It compares the API resources and checks the visible series, episode and movie cards, then refreshes their guide screenshots. This check works after the fixture titles have been imported too.
- [ ] For upgrade coverage, use B's previous-release package as sender. With both nodes stopped, seed A's disposable cache with the pre-details shape: clear only `PeerCatalogItems.Overview`, `Genres`, `Network`, `Runtime` and `EpisodeDetails`, keeping the peer version, file fingerprints and arrival timestamps. Start A on the old receiver package, sync, and confirm those details stay missing even though B's local cards have them. Stop A and start the new receiver on that same data. After sync, the cards regain their details while the sender version, fingerprints, each file's `FirstSeen` and the peer's `CatalogFirstSyncedAt` remain unchanged. Use only the isolated kit data for this check.

### Availability colors and remembered episode choices

- [ ] Use a separate kit for mixed states: fetch Felix the Cat to A, then remove its S1919E03 file through A's Sonarr. Leave Aladdin outside A's Radarr. Import a PROPER of Metropolis on B while A holds its original, then refresh both catalogs. The peer view on A now has **Have**, **Missing**, **Upgrade** and **Not in library** examples.
- [ ] **Show missing episodes** starts on in the peer table and cards, independently of the local choice. Turn it off: only **Missing** offers disappear, empty seasons and series disappear, and counts describe visible files. Reload and reopen a card to check the choice stays off. Neutral **Not in library** offers and **Upgrade** offers remain visible. The list never includes an episode the friend does not share.
- [ ] Check the same status is green, red, blue or neutral in the table, poster counts, series episode list and individual episode/movie card. Keep the words readable in both light and dark themes.
- [ ] With the mixed states above and the kit on `Nodearr__Theme=auto`, run `node tools/docs-screenshots.mjs catalogAvailability`. It switches the browser's system theme, checks both palettes and the saved defaults across tables and cards, and captures the [dark](../user-guide/images/catalog-availability-dark.png) and [light](../user-guide/images/catalog-availability-light.png) examples. It restores the browser's episode choices afterwards. Run `catalog peerCards peerMetadata` to retake the other catalog screenshots; a missing Felix episode and the Metropolis upgrade must remain actionable.

## 7. Choose what to share

On the side that has Out of the Inkwell, A, open **Catalog > Sharing**.

1. Under **Hidden**, open **Out of the Inkwell** with its arrow, tick **S01E02**, and select **Save**.
2. On the other side, B, look at **Catalog > Peer Catalog**.

- [ ] Within a few seconds, S01E02 is gone from B's list, without pressing anything. Taking something back is immediate.

Then, on A, clear its checkbox under **Hidden** and select **Save**. On B, select **Sync Peer Catalogs**.

- [ ] S01E02 is back. Something newly shared waits for the next sync, or the button.

## 8. Send a friend something, and get it

**A sends.** On A, in **Catalog > Sharing**, under **Send to a friend**:

1. Type *Inkwell* in the filter and pick **Out of the Inkwell**. Leave it whole: the whole series.
2. Under **To**, pick B's name.
3. Select **Send**.

- [ ] It says "Sent Out of the Inkwell to ...", and under **Sent** the send shows **Told** within a few seconds.

**B gets it.** On B's **Home**:

- [ ] A card says "... sent you Out of the Inkwell", with **Root Folder**, **Quality Profile** and **Series Type** filled in.
- [ ] Select **Details**, then close the card and select the small poster. Both open Out of the Inkwell's details over Home. Closing it retains Home's scroll position, and the card's **Get it** opens the existing catalog choices.
- [ ] Send one episode, a season and two selected episodes separately. One episode opens directly. The partial series cards mark only the sent episodes **Sent to you**, keeping those visible even with **Show missing episodes** off. Home's own **Get it** still acts on the original send.
- [ ] Withdraw the sent files in the sender's **Hidden** list, then sync the receiver. Its Home card explains that details are unavailable; its old details endpoint returns 404. Restore sharing. End trust and check the friend's received cards disappear and cannot reopen cached details.
- [ ] For automated browser coverage, prepare whole-series, season, two-episode, single-episode and movie sends from B to A, then run `node tools/docs-screenshots.mjs receivedDetails` with the kit environment described in the script. It checks both controls, selected markers and Get it without navigating away, and retakes `received.png` and `received-details.png`. This named check does not download files or dismiss sends.
- [ ] Select **Get it**. The card says it is asking Sonarr, then "Added to Sonarr, monitoring the whole series, and searching for 3 episodes": the three A has. Sonarr monitors the whole series, so it looks for the rest on its own later, as it would for any series.
- [ ] **Activity > Transfers** shows Out of the Inkwell's three episodes arriving from A, then done. Up to three files arrive at once, the default of **Most Downloads at Once**.
- [ ] In B's Sonarr, **Series > Out of the Inkwell** shows the three episodes with files. Sonarr imported what Nodearr fetched.

**Then the other way round.** B sends **The Little Shop of Horrors** to A, and A selects **Get it** on its card.

- [ ] A's Radarr adds the film, grabs it through Nodearr, and imports it. The film is larger than an episode, so give it a minute.

## 9. Fetch through the app's own search

This is how Nodearr works day to day: Sonarr or Radarr search, and find what friends have among their other results.

On B's Radarr, add **Night of the Living Dead**: **Movies > Add New**, search for it, and pick the one from **1968**, not the 1990 remake. Untick **Start search for missing movie**, which Radarr ticks for you, and select **Add Movie**. Then open the film and select **Interactive Search** at the top.

- [ ] A release from the indexer **Nodearr** is listed, with its size and quality.
- [ ] Press its download button. Nodearr's **Activity > Transfers** shows it arriving from A, and Radarr imports it.

## 10. Optional: send to someone who is not a friend yet

This is how a friendship can start: "I have that, let me send it to you."

1. On A, **Nodes > Trusted Nodes**, select **Revoke trust** on B. The two are no longer friends.
2. On A, in **Catalog > Sharing**, under **Send to a friend**, pick **Night of the Living Dead**, and under **To** pick **Someone new, with an invite**. Leave **Share everything except Hidden with them too** ticked, select **Send**, and copy the invite code it shows.
3. On B, **Nodes > Invites**, paste the code under **Redeem Invite**, and select **Redeem Invite**.

- [ ] The two are friends again, and A's send shows **Told**.
- [ ] B's **Home** shows a card for Night of the Living Dead. B has it from step 9, so it says Radarr has it already.

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
- Then select **Check connection** again.

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

The script was run solo on Windows 11, from the PowerShell that comes with Windows, and steps 2 to 9 were done in the pages as written. Doing them changed four things before this guide was published: the overview on **Home** now counts what is new to your apps, a help text about invites was out of date, a whole series sent was searched for episode by episode down to ones the friend did not have, and B's Nodearr is opened at `127.0.0.1`.

It has not yet been run with a friend on another network.
