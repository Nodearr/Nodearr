# Releases

A Nodearr release matches Sonarr's and Radarr's own downloads, so it can be installed where they are: one package per platform, and an image for Docker.

## What a release has

| Package | For |
| --- | --- |
| `Nodearr.<version>.linux-x64.tar.gz` | Linux on x86-64, glibc (Debian, Ubuntu, Fedora, ...) |
| `Nodearr.<version>.linux-arm64.tar.gz` | Linux on 64-bit ARM, glibc (Raspberry Pi OS 64-bit, ...) |
| `Nodearr.<version>.linux-arm.tar.gz` | Linux on 32-bit ARM, glibc |
| `Nodearr.<version>.linux-musl-x64.tar.gz` | Linux on x86-64, musl (Alpine) |
| `Nodearr.<version>.linux-musl-arm64.tar.gz` | Linux on 64-bit ARM, musl (Alpine) |
| `Nodearr.<version>.osx-x64.tar.gz` | macOS on Intel |
| `Nodearr.<version>.osx-arm64.tar.gz` | macOS on Apple silicon |
| `Nodearr.<version>.win-x64.zip` | Windows, 64-bit |
| `Nodearr.<version>.win-x86.zip` | Windows, 32-bit |
| `Nodearr.<version>.win-x64-installer.exe` | Windows, 64-bit, installed |
| `Nodearr.<version>.win-x86-installer.exe` | Windows, 32-bit, installed |
| `SHA256SUMS` | The checksum of each package |

The image is `ghcr.io/<owner>/nodearr`, for `linux/amd64` and `linux/arm64`. `0.1.0` is tagged `0.1.0`, `0.1` and `latest`. A pre-release such as `0.1.0-beta.1` is tagged with only its own version.

Each package is one folder, `Nodearr`, holding:

- the application, published self-contained, so the machine needs no .NET;
- the web UI;
- the transport sidecar, `nodearr-transport`;
- the licence and the notices (see "Licences" below).

On Windows it also holds the tray application, `Nodearr.exe`, and `ServiceInstall.exe` / `ServiceUninstall.exe`. On Linux and macOS, start it with `./Nodearr`.

Every Linux sidecar is a statically linked musl binary. It runs on glibc systems too, whatever their glibc version, so the glibc and musl packages for an architecture carry the same one.

### What the machine needs

Linux needs ICU: `libicu` on Debian and Ubuntu (`libicu72` on bookworm), `icu-libs` on Alpine. SQLite comes with the package. On a bare `debian:bookworm-slim` and a bare `alpine:3.20`, with nothing installed but ICU and curl, each Linux package started, served the UI, and ran its sidecar.

Where each package has been run:

- `linux-x64`, `linux-musl-x64`, `win-x64` and `win-x86`: on an x86-64 machine.
- `linux-arm64` and `linux-arm`: emulated with QEMU. Not yet on real ARM hardware. Under QEMU, .NET on 32-bit ARM crashes unless `DOTNET_EnableWriteXorExecute=0` is set. That is a known problem with .NET under emulation, but it has not been ruled out on a real board.
- `linux-musl-arm64`, `osx-x64` and `osx-arm64`: built by the release workflow only. Not yet run.

macOS marks a downloaded archive as quarantined. As with Sonarr, clear it before the first start: `xattr -dr com.apple.quarantine Nodearr`.

### On Windows: the installer

The installer puts Nodearr in Program Files and asks how it should start, as Sonarr's does:

- **When you sign in, in the tray.** The default. A shortcut in your Startup folder runs `Nodearr.exe`, which shows an icon in the system tray. Its menu opens Nodearr or exits it. It runs as you, so it reads whatever your account can.
- **When the computer starts, as a Windows service.** It runs whether anyone is signed in, as the `LocalService` account. That account may not be able to read your library, or a network share, until the service is given an account that can: Services, Nodearr, Properties, Log On. System > Status says when Nodearr cannot read the library.
- **Not automatically.** Start it from the Start menu when you want it.

Either way, the installer adds a Windows Firewall rule that lets UDP in to `nodearr-transport.exe`, which is how other nodes reach this one. Windows would otherwise ask the first time. A service cannot ask, and would be blocked without anyone knowing. Uninstalling takes the rule out.

As Sonarr's installer does, it also opens Nodearr's web port, 6633, so Sonarr and Radarr on other machines, or in Docker, can reach it. The page still asks whoever opens it to sign in. The rule is called Nodearr, and uninstalling takes it out with the other.

The data folder, `C:\ProgramData\Nodearr`, is the same for the tray app, the service and the zip, and neither installing nor uninstalling touches it: the node keeps its key and its friends.

The installer is built with Inno Setup 6 from the folder `Build-Release.ps1` stages, so it holds exactly what the zip holds: `pwsh build/Build-Installer.ps1 -Runtime win-x64 -Version 0.1.0`.

From the zip, `ServiceInstall.exe` and `ServiceUninstall.exe`, run as administrator, do what the installer's service option does, firewall rule included.

### Not in a release

- **FreeBSD.** Sonarr and Radarr release for it with a .NET runtime the community builds, from a package source of their own. Nodearr's sidecar is built on iroh, and iroh on FreeBSD has not been tried. Until both have been, there is no FreeBSD package.
- **A macOS app bundle** (`Nodearr.app`). Sonarr has one. Nodearr has the plain archive for now.
- **An updater.** Nodearr does not update itself (ADR 0018). A new version is installed over the old one, or pulled as a new image.

### Licences

Nodearr is GPL-3.0, and a release carries other people's work under their own licences. Every package and the image hold:

| File | What it is |
| --- | --- |
| `LICENSE` | Nodearr's licence, GPL-3.0. |
| `NOTICE.md` | What Nodearr is derived from: Sonarr and Prowlarr. |
| `THIRD-PARTY-NOTICES.md` | Every Rust crate in the sidecar, NuGet package in the application and npm package in the web UI, with the text of each licence. |
| `dotnet-LICENSE.txt`, `dotnet-THIRD-PARTY-NOTICES.txt` | The .NET runtime's, in the self-contained packages, copied from the SDK that built them. The image is built on Microsoft's `aspnet` image, which carries its own. |

Two entries need more than a licence text, and `THIRD-PARTY-NOTICES.md` gives it: `ffprobe`, a build of FFmpeg under the LGPL 3.0, says where its corresponding source is, and the two packages under the MPL 2.0, `attohttpc` and `IPAddressRange`, say where theirs is.

`THIRD-PARTY-NOTICES.md` is generated, and committed so that a release does not need the tools that make it. Run this whenever a dependency is added, removed or updated, and commit the result:

```sh
pwsh build/Build-Notices.ps1
```

It needs cargo-about (`cargo install cargo-about --locked --features cli`), a restored solution, and a fresh production build of the web UI, whose source maps say which npm packages are in it. It stops on a licence it has no text for, or a package that names none, and says what to add under `build/notices/`.

## Making a release

Push a tag:

```sh
git tag v0.1.0
```

```sh
git push origin v0.1.0
```

`.github/workflows/release.yml` builds every package, drafts a GitHub release with them and `SHA256SUMS` attached, and pushes the image. The draft is not published until someone publishes it from the Releases page.

A version is three numbers with an optional pre-release tag: `0.1.0`, `0.1.0-beta.1`. The application reports the three numbers and a fourth, `0.1.0.0`, without the tag. A tag with a pre-release part makes the GitHub release a pre-release.

Run the workflow by hand from the Actions tab, with a version, to build the packages without drafting or pushing anything. They are kept as the run's artifacts.

Publishing the release rebuilds [nodearr.com](https://nodearr.com) with that release's user guide, changelog and API description: `.github/workflows/website.yml` tells [Nodearr/nodearr.github.io](https://github.com/Nodearr/nodearr.github.io) to. That needs the secret `SITE_DISPATCH_TOKEN`, a fine-grained token for that repository with Contents set to read and write. Without it the workflow warns, and the site's Deploy workflow can be run by hand. The site never shows the docs of `main`, so what it describes is what can be downloaded.

## Building one package yourself

The scripts in `build/` are what the workflow runs. Each needs PowerShell 7.

1. Build the web UI:

   ```sh
   yarn build --env production
   ```

2. Build the sidecar for the platform. Linux sidecars are cross-compiled with `cargo-zigbuild`, which brings Zig with it:

   ```sh
   pip install cargo-zigbuild
   ```

   ```sh
   pwsh build/Build-Transport.ps1 -Runtime linux-arm64
   ```

   It prints where the binary is. macOS and Windows sidecars are built with plain cargo on macOS and Windows.

3. Package it:

   ```sh
   pwsh build/Build-Release.ps1 -Runtime linux-arm64 -Version 0.1.0-test.1 -TransportBin transport/target/aarch64-unknown-linux-musl/release/nodearr-transport
   ```

   The archive is written to `_artifacts`.

Build Linux and macOS packages on Linux or macOS. `tar` on Windows cannot mark `Nodearr` and `nodearr-transport` as executable, and the script warns when it is asked to. Build macOS packages on macOS: the .NET SDK signs the executable only there, and Apple silicon will not run it unsigned.

The image for both architectures:

```sh
docker buildx build --platform linux/amd64,linux/arm64 --build-arg VERSION=0.1.0 -t nodearr .
```

The web UI and the application build once, on the machine doing the build. Only the sidecar is built per architecture, cross-compiled.

## Continuous integration

`.github/workflows/ci.yml` runs on every push and pull request:

- The web UI: lint, style lint, and a production build.
- The application on Linux and on Windows: a build, then the unit tests one assembly at a time. As in Sonarr's own CI, it leaves out the manual, integration and automation tests, and the tests for the other operating system.
- The sidecar: build and tests.
- The image, built for amd64.

The acceptance scripts in `acceptance/` are not part of CI. They start Sonarr and Radarr containers, and some take many minutes.
