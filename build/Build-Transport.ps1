#requires -Version 7
<#
.SYNOPSIS
    Builds the nodearr-transport sidecar for one of Nodearr's release platforms.

.DESCRIPTION
    Linux targets are cross-compiled with cargo-zigbuild, which needs Zig, and is the way they are
    built from one Linux machine for every architecture: pip install cargo-zigbuild brings both.
    macOS and Windows targets are built with plain cargo on their own system.

    The sidecar's TLS comes from aws-lc-rs, which compiles the C library AWS-LC with the C compiler
    each of those already uses. 32-bit Windows also needs NASM on the path.

    Prints the path of the built binary.

.PARAMETER Runtime
    The .NET runtime identifier the sidecar is for, such as linux-arm64. See Runtimes.ps1.

.PARAMETER TargetDir
    Cargo's target folder. The default is transport/target.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Runtime,
    [string] $TargetDir
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Runtimes.ps1')

$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$manifest = Join-Path $repo 'transport/Cargo.toml'
$target = Get-RustTarget $Runtime
$TargetDir = $TargetDir ? $TargetDir : (Join-Path $repo 'transport/target')

rustup target add $target | Out-Host
if ($LASTEXITCODE -ne 0) { throw "rustup could not add $target" }

$cargoArgs = @('--release', '--locked', '--manifest-path', $manifest, '--target', $target, '--target-dir', $TargetDir)

if ($target -like '*-linux-*') {
    if (-not (Get-Command cargo-zigbuild -ErrorAction SilentlyContinue)) {
        throw 'Linux targets are built with cargo-zigbuild: pip install cargo-zigbuild'
    }
    cargo zigbuild @cargoArgs | Out-Host
}
else {
    if ($target -eq 'i686-pc-windows-msvc' -and -not (Get-Command nasm -ErrorAction SilentlyContinue)) {
        throw '32-bit Windows needs NASM on the path for AWS-LC: https://www.nasm.us/'
    }

    cargo build @cargoArgs | Out-Host
}

if ($LASTEXITCODE -ne 0) { throw "The sidecar did not build for $target" }

$binary = Join-Path $TargetDir "$target/release/nodearr-transport$($target -like '*windows*' ? '.exe' : '')"
if (-not (Test-Path $binary)) { throw "Built, but there is no $binary" }

return (Resolve-Path $binary).Path
