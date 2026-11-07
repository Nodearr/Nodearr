# ADR 0024: Update Checks and Usage Reports on nodearr.com

## Status

Accepted on 2026-11-04 ([#131](https://github.com/Nodearr/Nodearr/issues/131)). Update checks, automatic updates and the switch for both hosts were built on 2026-11-06 ([#134](https://github.com/Nodearr/Nodearr/issues/134)): see the amendment. Usage reports were built the same day ([#133](https://github.com/Nodearr/Nodearr/issues/133)): see the second amendment.

## Context

ADR 0018 took out everything the code Nodearr came from used to call home: the update check, error reporting, analytics, and the time and notification checks. Since then Nodearr has contacted only the Arr apps it is given, the nodes its owner trusts and the relay it is configured with.

Two things now need Nodearr to contact the project:

- **Updates.** People should hear about a new version, and native installs should be able to install it themselves (#134). That needs a way to ask what the newest release is.
- **Usage data.** The maintainer needs to know roughly how many nodes run, on which platforms, versions and Arr apps, and how often transfers fail and why (#133). That needs a report.

[Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/) have both. Their **Send Anonymous Usage Data** setting is on by default (`AnalyticsEnabled`, default `true`, in each app's `ConfigFileProvider`). Their update check runs every 6 hours (`Interval = 6 * 60` in `TaskManager`) and carries the usage data with it: version, OS, architecture, runtime, database type and branch, and with usage data on, whether the install has been active in the past month.

The project owns `nodearr.com`. The website is served from it by GitHub Pages, built from [Nodearr/nodearr.github.io](https://github.com/Nodearr/nodearr.github.io). Nothing else runs on it.

## Decision

### Nodearr may contact two hosts on nodearr.com

Each host has one job, its own setting, and an environment override (ADR 0019).

| Host | What it is for | What Nodearr sends | Runs on |
| --- | --- | --- | --- |
| `nodearr.com` | The website, as now: the user guide, downloads and changelog. It gains `/privacy` and `/stats`. | Nothing. People browse it; Nodearr never calls it. | GitHub Pages, unchanged. |
| `updates.nodearr.com` | A signed, static update manifest per branch. | A plain `GET` of a static file, every 6 hours. No identifier, no query string, no cookies. | Static hosting, for example a second GitHub Pages repository. One Pages site takes one custom domain, and keeping the manifest apart from the website lets it move to a CDN without a change to Nodearr. |
| `usage.nodearr.com` | Takes in the anonymous usage report. Write-only. | One `POST` every 6 hours, with the report and nothing else. | A service that can run code, which GitHub Pages can't: for example a Cloudflare Worker or a small virtual machine. Its code is public, so what it keeps can be checked. |

Nothing else is contacted that the owner did not configure. The rest of ADR 0018 stands: no error reporting, no tracking script in the UI, no time or notification checks.

- **The update check and the usage report are separate.** They are different jobs. Sonarr's schedule is kept, every 6 hours, but not its way of sending usage data inside the update check. Each is its own scheduled task, to its own host, with its own setting, and turning one off doesn't change the other.
- **Packages stay on GitHub Releases.** The manifest points to them and carries each package's SHA-256, so `updates.nodearr.com` serves a few kilobytes and never a package.
- **`usage.` is named after the setting**, **Send anonymous usage data**, so someone reading a firewall or DNS log can tell what it is and block it.
- **What people look at stays on `nodearr.com`.** The public totals are `nodearr.com/stats`. Only what Nodearr itself calls gets its own host, so the list stays short and each can move on its own.

### Defaults

- **Check for updates** is on. Whether an update is also installed by itself depends on how Nodearr was installed, and is decided in #134.
- **Send anonymous usage data** is on, as in Sonarr and Radarr.

### One switch turns both off

One setting, with an environment key (`Nodearr__Network__ContactNodearr=false` or similar, named when it's built), stops Nodearr contacting either host, whatever the other two settings say. It gives back ADR 0018's original promise to anyone who wants it, and an acceptance check can rely on it. Whichever of #133 and #134 is built first adds the switch.

### Rules for anything sent to nodearr.com

- **Nothing that identifies a node or its owner.** No title, path, file name, node ID, friend's ID or name, IP address, host name, user name, API key or Arr URL, and no exact count that could single a node out.
- **Not over the peer transport.** It's an ordinary HTTPS request from Nodearr's backend, through the configured proxy if there is one. It never goes through a relay.
- **A failure is quiet.** Nodearr logs it at debug level and tries again at the next scheduled time. It raises no health warning, because it isn't the owner's problem to fix.
- **`nodearr.com/privacy` lists exactly what each host receives**, what is kept and for how long. The address redirects to **What anyone else sees** in the user guide's privacy page (`docs/user-guide/privacy-and-security.md`), so the description lives beside the code: a change to what is sent changes that section in the same pull request.
- **No DNS until it's needed.** `updates.` and `usage.` are set up when #134 and #133 are built. A host that doesn't answer is harmless, because a failure is quiet.

## Consequences

### Positive

- Owners hear about new versions, and native installs can keep themselves up to date. Fewer friends are left on old releases.
- The maintainer learns which platforms, versions and Arr apps are in use, and how often ADR 0011's direct-only rule stops a transfer in real homes, instead of guessing.
- Both are where Sonarr and Radarr users expect them, with the same defaults and the same schedule.

### Negative / trade-offs

- **Any request to nodearr.com shows that an IP address runs Nodearr**, to whoever hosts it. The update check carries no identifier, so on its own it can't link two requests. The usage service must not store or log IP addresses, and says so on `/privacy`. The switch stops both.
- **Usage data is on by default.** Owners who don't read the settings will send it. That is the Arr apps' choice too, and the report holds nothing that identifies them.
- **Two services to run.** The manifest is static and cheap. The usage service needs something that runs code, which costs money or a free tier's limits, and needs looking after.
- **Updating becomes a way in.** Someone who could change the manifest could point nodes at a package of their choosing. #134 signs the manifest with a key the release workflow doesn't hold, and Nodearr checks it with a public key built into it.

## Evidence

None yet: this records a decision before it is built. #133 and #134 carry the tests and acceptance checks. The check in `acceptance/Run-S8Docker.ps1` that a node contacts only the Sonarr it was given keeps passing until then, and afterwards runs with the switch off. A second node with the switch on is checked to contact only its Arr app and the two hosts above.

## Amendments

### 2026-11-06: update checks and automatic updates are built ([#134](https://github.com/Nodearr/Nodearr/issues/134))

**The manifest.** `https://updates.nodearr.com/v1/main.json` holds the newest release: its version, date, a link to its section of the changelog on `nodearr.com`, the changes listed in that section, an optional `rollout` percentage, and for each platform the package's GitHub Releases address, size and SHA-256. `main.json.sig`, beside it, is an SSH signature over the file's exact bytes. Nodearr fetches both with plain `GET`s every 6 hours. Neither request carries an identifier, a query string or a cookie.

**The signature.** It's made with `ssh-keygen -Y sign` and the namespace `nodearr-update`, using the RSA key the maintainer signs tags with. The maintainer signs at release time through their own key agent, so the release workflow never holds the key. The namespace means a signature made for git can't be passed off as one for a manifest, or the other way round. Nodearr has the public key built in and checks `rsa-sha2-512` and `rsa-sha2-256` signatures itself, with the RSA support .NET already has. That is why it's an SSH signature and not [minisign](https://jedisct1.github.io/minisign/): minisign uses Ed25519, which .NET can't check without another package. `build/update-signers` holds the same key for `ssh-keygen -Y verify`, which the workflow uses.

A manifest whose signature doesn't check out isn't used at all: nothing is offered and nothing is installed, and **System > Status** shows a warning until a later check finds a good one. A manifest that can't be fetched stays quiet, as above.

**Never down.** A manifest whose version isn't newer than the one running is ignored, so a stale or replayed manifest can't take a node back to an older release. Versions compare as [semantic versions](https://semver.org/), pre-release tag included.

**Rollout.** With `rollout` below 100, a node takes the release only if a number it picked once, from 1 to 100, is no higher. The number stays in the node's own database and is never sent.

**The settings,** on **Settings > General**, each with an environment key (ADR 0019):

| Setting | Environment key | Default |
| --- | --- | --- |
| **Contact nodearr.com** | `Nodearr__Network__ContactNodearr` | On. Off stops both hosts being contacted, whatever the other settings say. |
| **Check for updates** | `Nodearr__Update__Check` | On |
| **Install updates automatically** | `Nodearr__Update__Automatically` | On for Windows, as inherited from Sonarr. Off elsewhere. |

For testing, and only in the environment, `Nodearr__Update__ManifestUrl` points the check at another folder and `Nodearr__Update__PublicKey` names the key it trusts in place of the built-in one. Whoever sets a node's environment can already replace its program, so this grants nothing new. Neither is shown on any page.

**How each kind of install updates:**

| Install | What happens |
| --- | --- |
| Windows installer, tray app or service | The inherited updater, `Nodearr.Update`, replaces the program folder. The installer now puts Nodearr in `C:\ProgramData\Nodearr\bin`, as Sonarr and Radarr put theirs in `ProgramData`, and gives Everyone the right to change that folder, so the service, running as `LocalService`, can update itself without asking for administrator rights. The firewall rule names the sidecar's path in that folder, which an update doesn't change. |
| Windows zip, Linux and macOS archives | The same updater, if Nodearr can write to the folder it runs from. If it can't, Nodearr only says there is an update. |
| Docker | Never updates itself. **System > Updates** shows the new version and the `docker pull` command. The image says so in its `package_info`. |
| Package managers (`External`) | Only says there is an update. |

The updater stops Nodearr and its sidecar, copies the new files over the old, and starts Nodearr again. If Nodearr doesn't answer `/ping` within 3 minutes, the updater stops it, puts back the old program, database and `config.xml`, starts the old version, and writes why to its log in `UpdateLogs`.

**When it installs.** An update found while **Install updates automatically** is on waits for a quiet moment: no transfer queued or under way, no upload, and no catalog refresh or exchange. Nodearr looks again every 5 minutes. After 24 hours of waiting it installs anyway: a transfer cut off by the restart picks up from what had arrived (ADR 0016). **Install now** on **System > Updates** doesn't wait. The database is backed up first, as inherited.

**What people see.** **System > Updates** shows the version running, when the last check was, and a newer version with its changes, with **Install now** where this install can update itself, the `docker pull` command in a container, and otherwise why it can't and where to get the package. **System > Status** shows a notice once an update has been available for 14 days.

**Publishing.** After the release workflow drafts a release, the maintainer runs `build/Publish-UpdateManifest.ps1`. It builds the manifest from the draft's packages, `SHA256SUMS` and the changelog, signs it, checks the signature, and attaches both files to the draft. When the release is published, `.github/workflows/website.yml` checks the signature again and copies the two files to [Nodearr/updates](https://github.com/Nodearr/updates), the GitHub Pages repository behind `updates.nodearr.com`, unless the manifest there is already for a later version. A release published without a signed manifest is skipped, with a warning.

**Trade-offs.**

- **Anyone with an account on a Windows PC can change Nodearr's program files**, because Everyone may change `ProgramData\Nodearr\bin`. It's what Sonarr and Radarr do, and it's what lets the service update itself. Until now the installer used Program Files to avoid this.
- **After an update, Windows still lists the version that was installed**, under **Installed apps**, because the updater doesn't run the installer. Sonarr has the same gap.
- **One key signs tags and manifests.** If it's lost or exposed, a new release has to ship a new public key before manifests signed with it are trusted, and nodes on older releases need updating by hand.

**Evidence.**

- Unit tests: signatures written by `ssh-keygen` accepted, and refused when one byte or the line endings change, for another namespace or another key, under 2048 bits, or with the old `ssh-rsa` (SHA-1) type; versions ordered as semver.org does; older and equal versions ignored; rollout respected, its number picked once and kept; nothing asked with either setting off, and **Contact nodearr.com** winning over **Check for Updates**; an automatic update waiting while a transfer, an upload or a catalog exchange runs, and going after 24 hours; the environment overrides shown as fixed; the updater keeping a version that answers and putting back one that doesn't, and stopping only its own sidecar.
- `acceptance/Run-Updates.ps1`, 13 of 13, against a manifest signed by `build/Publish-UpdateManifest.ps1` and served on this machine: the release noticed, the requests two plain `GET`s, a tampered manifest fetched twice and refused with a warning, an older one ignored, and nothing asked with either setting off.
- `acceptance/Run-S8Docker.ps1`, 24 of 24: the node with **Contact nodearr.com** off asked only its Sonarr, and a node given nothing, with it on, asked only `updates.nodearr.com` and said it runs in a container.
- A manifest signed through 1Password with the release key was accepted by `ssh-keygen -Y verify` against `build/update-signers`, and by a node trusting only the built-in key. `op-ssh-sign` names its signature after the file without its extension, which the script now allows for.
- Real installs on Windows 11, each from 0.10.0-test.1 to test.2 built from this branch, against a local manifest:
  - a zip, run as `Nodearr.Console.exe`: **Install Now** took 12 s, with one new sidecar from the same folder and a database backup first. The same install as the tray app, offered a test.3 with a broken `Nodearr.Core.dll`, was put back after 3 minutes, its database and `config.xml` too, and the tray app answered 2 s later;
  - the installer over a 0.1.1 install in Program Files: Nodearr moved to `C:\ProgramData\Nodearr\bin`, the Program Files copy gone, Everyone allowed to change the folder, the firewall rule naming the new path. As the tray app, **Install Now** took 14 s and asked for no administrator rights. Installed again as the service, running as `LocalService`, it updated itself in 10 s and ran again as the service;
  - a first attempt made while the disk was full stopped while backing up the old program, changed nothing and started the old version again.
- A Linux archive (linux-x64, glibc) unpacked in a Debian 12 container, with no service manager: **Install Now** took 20 s, with one new sidecar and the programs still executable, and a broken test.3 was put back after 211 s. The first run of this showed Nodearr calling the container a Docker install, from `/.dockerenv`, and refusing to update. As in Sonarr, only the image's `package_info` says so now. macOS, systemd and a package manager were not tried.

### 2026-11-06: usage reports are built ([#133](https://github.com/Nodearr/Nodearr/issues/133))

**The report.** Every 6 hours the **Send Usage Report** task posts one JSON report to `https://usage.nodearr.com/v1/report`, if **Send Anonymous Usage Data** and **Contact nodearr.com** are both on. It's its own task, apart from the update check, as decided above. A new node sends its first report 6 hours after it first starts; a node that was off when one was due sends it soon after it starts again. A failure is logged at debug level and not tried again until the next run. The report describes the past 7 days, from the statistics roll-up of [#132](https://github.com/Nodearr/Nodearr/issues/132) and the node's own settings. Every field is in `usage/schema/v1/report.schema.json` and in the privacy page's **Anonymous usage report**, with an example:

- the Nodearr version, `main` or `other` for the branch, the platform the package is for, the .NET version, the database, and how it was installed (the Windows installer as tray app or service, a zip, an archive, Docker, a package manager, or a development build);
- `active`, as Sonarr's: whether the node moved a file or answered a grab in the past 30 days;
- each Arr app's kind and major and minor version, asked from the app's `/system/status` at most once an hour;
- ranges in place of counts: friends (`0`, `1`, `2-5`, `6-10`, `11+`), titles shared per kind of app, transfers finished and failed each way, failures by reason, data moved and titles sent;
- friends by what the past week's connection checks found (direct, relay only, none), whether the public port varies, whether UDP gets out, the relay setting (`n0`, `own`, `off`) and Docker Desktop;
- which features are on, and the UI language.

A failure reason the schema doesn't know is sent as `Other`, a branch name other than `main` as `other`, and a version that isn't major and minor as `unknown`, so that free text never leaves the node.

**The ID.** Random, kept in the node's database for the UTC day it was made, and replaced on the next. A restart the same day keeps it. Nothing else in a report could link two days.

**The settings,** on **Settings > General** under **Usage Data**, with the environment key `Nodearr__Usage__Send` (ADR 0019). **Show Report** shows the exact JSON the next run would send, from `GET /api/v5/usage`. The report sent is written to the debug log. For testing, and only in the environment, `Nodearr__Usage__Url` sends the report somewhere else. The setting inherited from Sonarr, `AnalyticsEnabled` (`Nodearr__Log__AnalyticsEnabled`), did nothing in Nodearr, and is gone: there is one setting for usage data.

**`usage.nodearr.com`** is a Cloudflare Worker with a D1 database, in `usage/` in this repository, so what it keeps can be checked beside the code that sends to it. It:

- refuses, with 400, a report that doesn't match the schema, which allows no field it doesn't list;
- never reads the address a request came from or any header but `Content-Type` and `Content-Length`, has Workers Logs off and no Logpush, and stores a report with only its ID and the UTC day it arrived, keeping the latest report for each ID;
- adds up each UTC day once it's over, keeps those totals, and removes reports after 90 days;
- publishes the totals at `/v1/stats.json`, with any count under 10 given as `null`, and the schema at `/v1/schema.json`.

`nodearr.com/stats` shows the totals, read from `/v1/stats.json` in the browser. `nodearr.com/privacy` already points to the privacy page, which now lists every field.

**Trade-offs.**

- **Cloudflare sees the address a report comes from**, as any host would, while it answers. The Worker never reads it. Cloudflare's own handling is under its privacy policy, not this project's.
- **Anyone can post a report that matches the schema.** The totals are a guide, not a census: nothing stops someone sending made-up reports, and nothing could without identifying nodes. A Cloudflare rate limit can be added if it becomes a problem.
- **A node's clock decides its midnight.** A node whose clock is wrong sends its new ID a little early or late. The ingest keeps the latest report per ID under the day it arrived, so a node counts once either way.

**Evidence.**

- Unit tests: a report from a busy node and from a new one matches the schema, checked by a test that reads the schema itself; nothing that names a friend, a node, an app's address, an API key, an IP address, a relay, a title, a share list or a path appears in it; counts given as ranges at each boundary; only the past 7 days counted, and failures asked for again left out; `active` true at 29 days and false at 31, and while a transfer runs; the ID the same at 23:59:59.999 UTC as at midnight, different a millisecond later, following UTC and not the local clock, and kept in the database; the environment key and **Contact nodearr.com** winning over the saved setting; one `POST` with no cookies, nothing sent with either setting off, a refused or unreachable report failing quietly and sent once; the task every 6 hours, due at start-up when overdue.
- `usage/`: 20 tests with `node --test`, against SQLite through a stand-in for D1: a report taken and stored with only its ID, day and body, an unknown field anywhere refused, a later report for an ID replacing the earlier, a day added up once it's over and only once, counts under 10 published as `null`, reports removed after 90 days. Run in `wrangler dev --local`, the Worker took the example report with 204, refused one with an unknown field with 400, and served the schema.
- `acceptance/Run-S8Docker.ps1`: node A, after its transfers, sends its report to a stand-in for `usage.nodearr.com`. The report is checked against the schema with PowerShell's `Test-Json`, and for the node IDs, names, host names, paths, titles, keys and IP addresses of the deployment.
