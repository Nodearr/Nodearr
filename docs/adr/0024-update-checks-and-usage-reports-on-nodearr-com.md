# ADR 0024: Update Checks and Usage Reports on nodearr.com

## Status

Accepted on 2026-11-04 ([#131](https://github.com/Nodearr/Nodearr/issues/131)). Not built yet: update checks and automatic updates are [#134](https://github.com/Nodearr/Nodearr/issues/134), and usage reports are [#133](https://github.com/Nodearr/Nodearr/issues/133). Until one of them ships, Nodearr contacts nothing on nodearr.com, as ADR 0018 says.

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
- **`nodearr.com/privacy` lists exactly what each host receives**, what is kept and for how long. A change to what is sent changes that page in the same pull request.
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
