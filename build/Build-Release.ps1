#requires -Version 7
<#
.SYNOPSIS
    Builds Nodearr's release package for one platform, the way Sonarr's are built.

.DESCRIPTION
    Publishes the application self-contained for the runtime, so the machine it runs on needs no
    separate .NET installation. Puts it in a folder named Nodearr with the web UI, the transport sidecar, the licence,
    Nodearr's notices, the third-party notices (Build-Notices.ps1) and the .NET runtime's own
    licence and notices, and archives that folder: Nodearr.<version>.<runtime>.tar.gz, or .zip for
    Windows.

    Linux and macOS archives carry the executable bit for Nodearr and nodearr-transport. tar on
    Windows cannot set it, so build those archives on Linux or macOS, as the release workflow does.

.PARAMETER Runtime
    The .NET runtime identifier, such as linux-arm64. See Runtimes.ps1.

.PARAMETER Version
    The release version, such as 0.1.0 or 0.1.0-beta.1.

.PARAMETER TransportBin
    The sidecar built for this runtime (Build-Transport.ps1).

.PARAMETER UiFolder
    The built web UI. The default is _output/UI, from yarn build.

.PARAMETER OutputDir
    Where the archive goes. The default is _artifacts.

.PARAMETER RootDir
    Where the build writes _output and _temp. The default is the repository.

.PARAMETER SkipPublish
    Package what an earlier run published, without publishing again.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Runtime,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $TransportBin,
    [string] $UiFolder,
    [string] $OutputDir,
    [string] $RootDir,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Runtimes.ps1')
Get-RustTarget $Runtime | Out-Null

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$RootDir = $RootDir ? (New-Item -ItemType Directory -Force $RootDir).FullName : $repo
$UiFolder = $UiFolder ? $UiFolder : (Join-Path $repo '_output/UI')
$OutputDir = $OutputDir ? $OutputDir : (Join-Path $repo '_artifacts')
$framework = 'net10.0'
$forWindows = $Runtime -like 'win-*'

if (-not (Test-Path (Join-Path $UiFolder 'index.html'))) { throw "No web UI at $UiFolder. Build it first: yarn build --env production" }
if (-not (Test-Path $TransportBin)) { throw "No sidecar at $TransportBin. Build it first: build/Build-Transport.ps1 -Runtime $Runtime" }

# An assembly version is four numbers. A pre-release tag on the version is left out of it.
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-[0-9A-Za-z.-]+)?$') { throw "A version looks like 0.1.0 or 0.1.0-beta.1, not $Version" }
$assemblyVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"

$output = Join-Path $RootDir '_output'

if (-not $SkipPublish) {
    Write-Host "Publishing Nodearr $Version for $Runtime"

    # As Sonarr's build does it: every project, self-contained, for this one runtime.
    dotnet msbuild -restore (Join-Path $repo 'src/Nodearr.sln') `
        -p:SelfContained=true `
        -p:Configuration=Release `
        -p:Platform=$($forWindows ? 'Windows' : 'Posix') `
        -p:RuntimeIdentifiers=$Runtime `
        -p:EnableWindowsTargeting=true `
        -p:AssemblyVersion=$assemblyVersion `
        -p:NodearrRootDir=$($RootDir.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar) `
        -t:PublishAllRids `
        -v:minimal -nologo | Out-Host

    if ($LASTEXITCODE -ne 0) { throw "Publishing for $Runtime failed" }
}

$staging = Join-Path $RootDir "_staging/$Runtime"
$folder = Join-Path $staging 'Nodearr'

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $folder | Out-Null

Copy-Item (Join-Path $output "$framework/$Runtime/publish/*") $folder -Recurse

if ($forWindows) {
    # The tray application, Nodearr.exe, is built for Windows on its own.
    Copy-Item (Join-Path $output "$framework-windows/$Runtime/publish/*") $folder -Recurse -Force

    Get-ChildItem $folder -File | Where-Object { $_.Name -like 'Nodearr.Mono.*' -or $_.Name -like 'Mono.Posix.NETStandard.*' -or $_.Name -like 'libMonoPosixHelper.*' } | Remove-Item -Force
}
else {
    Get-ChildItem $folder -File | Where-Object { $_.Name -like 'ServiceInstall.*' -or $_.Name -like 'ServiceUninstall.*' -or $_.Name -like 'Nodearr.Windows.*' } | Remove-Item -Force
}

Copy-Item $UiFolder (Join-Path $folder 'UI') -Recurse
Copy-Item $TransportBin (Join-Path $folder "nodearr-transport$($forWindows ? '.exe' : '')")
Copy-Item (Join-Path $repo 'LICENSE') $folder
Copy-Item (Join-Path $repo 'NOTICE.md') $folder
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $folder

# The package carries the .NET runtime, and so its licence and notices, from the SDK that built it.
$dotnetRoot = $env:DOTNET_ROOT ? $env:DOTNET_ROOT : (Split-Path (Get-Command dotnet).Source)

foreach ($file in @{ 'LICENSE.txt' = 'dotnet-LICENSE.txt'; 'ThirdPartyNotices.txt' = 'dotnet-THIRD-PARTY-NOTICES.txt' }.GetEnumerator()) {
    $from = Join-Path $dotnetRoot $file.Key

    if (-not (Test-Path $from)) { throw "No $($file.Key) in $dotnetRoot, which the package needs for the .NET runtime it carries" }

    Copy-Item $from (Join-Path $folder $file.Value)
}

New-Item -ItemType Directory -Force $OutputDir | Out-Null
$archiveName = "Nodearr.$Version.$Runtime"

if ($forWindows) {
    $archive = Join-Path $OutputDir "$archiveName.zip"
    if (Test-Path $archive) { Remove-Item $archive -Force }
    Compress-Archive -Path $folder -DestinationPath $archive -CompressionLevel Optimal
}
else {
    if ($IsWindows) {
        Write-Warning 'tar on Windows cannot set the executable bit. This archive needs chmod +x after unpacking. Build it on Linux or macOS for a release.'
    }
    else {
        chmod a+x (Join-Path $folder 'Nodearr') (Join-Path $folder 'nodearr-transport')
        Get-ChildItem $folder -Recurse -File -Filter 'ffprobe' | ForEach-Object { chmod a+x $_.FullName }
    }

    $archive = Join-Path $OutputDir "$archiveName.tar.gz"
    tar -czf $archive -C $staging Nodearr
    if ($LASTEXITCODE -ne 0) { throw "tar failed for $archive" }
}

Write-Host "Packaged $archive"
return (Resolve-Path $archive).Path
