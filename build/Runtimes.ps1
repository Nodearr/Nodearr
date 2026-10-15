# Dot-sourced by the release scripts.
#
# The platforms Nodearr is released for: Sonarr's and Radarr's own list, less FreeBSD (see
# docs/guides/releases.md). Each is a .NET runtime identifier, and the Rust target the transport
# sidecar is built for to run beside it.
#
# Every Linux build gets a statically linked musl sidecar. It runs on glibc systems as well, so one
# binary serves both the glibc and the musl package, whatever the distribution's glibc version.

$NodearrRuntimes = [ordered]@{
    'linux-x64'        = 'x86_64-unknown-linux-musl'
    'linux-musl-x64'   = 'x86_64-unknown-linux-musl'
    'linux-arm64'      = 'aarch64-unknown-linux-musl'
    'linux-musl-arm64' = 'aarch64-unknown-linux-musl'
    'linux-arm'        = 'armv7-unknown-linux-musleabihf'
    'osx-x64'          = 'x86_64-apple-darwin'
    'osx-arm64'        = 'aarch64-apple-darwin'
    'win-x64'          = 'x86_64-pc-windows-msvc'
    'win-x86'          = 'i686-pc-windows-msvc'
}

function Get-RustTarget {
    param([Parameter(Mandatory)] [string] $Runtime)

    if (-not $NodearrRuntimes.Contains($Runtime)) {
        throw "Unknown runtime $Runtime. Known: $($NodearrRuntimes.Keys -join ', ')"
    }

    return $NodearrRuntimes[$Runtime]
}
