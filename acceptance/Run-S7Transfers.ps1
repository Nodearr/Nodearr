#requires -Version 7
<#
.SYNOPSIS
    S7 acceptance: a real Sonarr and Radarr grab from Nodearr, the file comes from a trusted node,
    is verified, and is imported.

.DESCRIPTION
    Node A has a library: its own Sonarr and Radarr hold three Pingu episodes and one movie.
    Node B has its own Sonarr and Radarr, which know the same series and movie and have no files.
    The nodes pair and A shares. The script then presses "search" in B's Sonarr and Radarr and
    follows each grab to the file landing in B's library. It checks that A hashed what it shares
    before anything was asked for. It also cancels a transfer, restarts
    node B in the middle of one, has A unshare the series in the middle of one, and has A end
    trust in the middle of one.

    Both nodes run on this machine with relays off, so their only route to each other is direct.
    A's uploads are slowed so that there is a middle to act in.

    Needs Docker and ffmpeg (as S3 does) and the transport sidecar (as S4 does).
    Everything lives in .run/s7 next to this script and in the nodearr-s7 compose project.

.PARAMETER TransportBin
    Path to the built nodearr-transport binary.

.PARAMETER KeepRunning
    Leave both instances and the containers running afterwards: A on port 6633, B on port 6634
    (or FirstPort and the one after),
    B's Sonarr on 28989 and B's Radarr on 27878.

.PARAMETER SetupOnly
    Build the whole environment, pair the nodes, share everything, and stop there with it all
    running and nothing grabbed yet. For trying it by hand: press search in B's Sonarr or Radarr.
    A file named ready.txt appears in .run/s7 when it is ready.

.PARAMETER Down
    Stop both instances and remove the containers, then exit.

.PARAMETER UploadLimitKbps
    How fast node A may send, in kilobytes per second. The checks need it slow enough to act in
    the middle of a transfer. An episode of about 21 MB takes about 20 seconds at the default.

.PARAMETER NodearrDll
    Path to a built Nodearr.Console.dll, when it is not the one in _output. For running beside a
    node that is already running out of _output.

.PARAMETER FirstPort
    Node A's port. Node B takes the next one.
#>
[CmdletBinding()]
param(
    [string] $TransportBin,
    [switch] $KeepRunning,
    [switch] $SetupOnly,
    [switch] $Down,
    [int] $UploadLimitKbps = 1024,
    [string] $NodearrDll,
    [int] $FirstPort = 6633
)

$ErrorActionPreference = 'Stop'
# Version 1 only: the APIs leave null properties out of their JSON, and reading one must give $null.
Set-StrictMode -Version 1

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '..')
$run = Join-Path $root '.run/s7'
$dll = $NodearrDll ? $NodearrDll : (Join-Path $repo '_output/net10.0/Nodearr.Console.dll')
$composeFiles = @(
    '-f', (Join-Path $repo 'spikes/arr-contract/docker-compose.yml'),
    '-f', (Join-Path $root 'docker-compose.library.yml'),
    '-f', (Join-Path $root 'docker-compose.consumer.yml'))
$envFile = Join-Path $run '.env'
$downloads = Join-Path $run 'downloads'
$downloadsB = Join-Path $run 'downloads-b'
$library = Join-Path $run 'library'
$media = Join-Path $run 'media'

$readyFile = Join-Path $run 'ready.txt'

. (Join-Path $root 'Library.ps1')

# Stopping needs neither the build nor the sidecar.
if (-not $Down) {
if (-not (Test-Path $dll)) { throw 'Build Nodearr first: dotnet build src/Nodearr.sln' }
if (-not $TransportBin) {
    $target = $env:CARGO_TARGET_DIR ? $env:CARGO_TARGET_DIR : (Join-Path $repo 'transport/target')
    $TransportBin = Join-Path $target ('release/nodearr-transport' + ($IsWindows ? '.exe' : ''))
}
if (-not (Test-Path $TransportBin)) { throw "Build the transport first: cargo build --release --manifest-path transport/Cargo.toml (looked for $TransportBin)" }
}

function New-Key { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }

$nodes = @{
    A = @{ Port = $FirstPort; Data = Join-Path $run 'a'; Key = New-Key; Downloads = $downloads; UploadLimit = $UploadLimitKbps }
    B = @{ Port = $FirstPort + 1; Data = Join-Path $run 'b'; Key = New-Key; Downloads = $downloadsB; UploadLimit = 0 }
}
$arr = @{
    sonarr  = @{ Url = 'http://127.0.0.1:18989'; Key = New-Key }
    radarr  = @{ Url = 'http://127.0.0.1:17878'; Key = New-Key }
    sonarrB = @{ Url = 'http://127.0.0.1:28989'; Key = New-Key }
    radarrB = @{ Url = 'http://127.0.0.1:27878'; Key = New-Key }
}

function Invoke-Compose {
    docker compose -p nodearr-s7 @composeFiles --env-file $envFile @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($args -join ' ') failed" }
}

function Stop-Node {
    param([string] $Name)
    $pidFile = Join-Path $run "$Name.pid"
    if (Test-Path $pidFile) {
        Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
        Remove-Item $pidFile -Force
    }
}

function Invoke-Http {
    param([string] $BaseUrl, [string] $Key, [string] $Method, [string] $Path, $Body, [switch] $AllowError)

    $request = @{ Uri = "$BaseUrl$Path"; Method = $Method; Headers = @{ 'X-Api-Key' = $Key }; SkipHttpErrorCheck = $true; StatusCodeVariable = 'status' }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 30); $request.ContentType = 'application/json' }

    $response = Invoke-RestMethod @request
    if ($status -ge 400) {
        if ($AllowError) { return @{ Failed = $true; Status = $status; Body = ($response | ConvertTo-Json -Depth 6 -Compress) } }
        throw "$Method $BaseUrl$Path -> $status $($response | ConvertTo-Json -Depth 6 -Compress)"
    }
    return $response
}

function Invoke-Node { param([string] $Name, [string] $Method, [string] $Path, $Body) Invoke-Http "http://127.0.0.1:$($nodes[$Name].Port)/api/v5" $nodes[$Name].Key $Method $Path $Body }
function Invoke-Arr { param([string] $App, [string] $Method, [string] $Path, $Body, [switch] $AllowError) Invoke-Http "$($arr[$App].Url)/api/v3" $arr[$App].Key $Method $Path $Body -AllowError:$AllowError }

# Node B's download client API, called the way an Arr app calls it.
function Invoke-Sab {
    param([string] $Query)
    return Invoke-RestMethod "http://127.0.0.1:$($nodes.B.Port)/sabnzbd/api?$Query&output=json&apikey=$($nodes.B.Key)"
}

function Wait-Until {
    param([scriptblock] $Condition, [int] $TimeoutSec = 60, [string] $What, [double] $EverySec = 1)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $value = & $Condition
        if ($value) { return $value }
        Start-Sleep -Seconds $EverySec
    }
    throw "Timed out after ${TimeoutSec}s waiting for $What"
}

function Invoke-NodeCommand {
    param([string] $Name, [hashtable] $Command)
    $queued = Invoke-Node $Name POST '/command' $Command
    Wait-Until -What "$Name command $($Command.name)" -TimeoutSec 180 -Condition {
        $state = Invoke-Node $Name GET "/command/$($queued.id)"
        if ($state.status -eq 'failed') { throw "$Name command $($Command.name) failed: $($state.message)" }
        $state.status -eq 'completed'
    } | Out-Null
}

function Start-Node {
    param([string] $Name)

    $node = $nodes[$Name]
    $env:Nodearr__Server__Port = $node.Port
    $env:Nodearr__Auth__ApiKey = $node.Key
    $env:Nodearr__Auth__Method = 'External'
    $env:Nodearr__Log__AnalyticsEnabled = 'false'
    $env:Nodearr__Transport__BinaryPath = $TransportBin
    $env:Nodearr__Transport__Relay = 'disabled'
    $env:Nodearr__Transport__DisablePortMapping = 'true'
    $env:Nodearr__Transport__DownloadFolder = $node.Downloads
    $env:Nodearr__Transport__UploadLimitKbps = $node.UploadLimit

    $process = Start-Process dotnet -ArgumentList "`"$dll`"", '-nobrowser', "-data=`"$($node.Data)`"" -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $run "$Name.out.log") -RedirectStandardError (Join-Path $run "$Name.err.log")
    $process.Id | Set-Content (Join-Path $run "$Name.pid")

    Wait-Until -What "node $Name and its transport" -TimeoutSec 90 -Condition {
        try { (Invoke-Node $Name GET '/nodes/identity').transportRunning } catch { $false }
    } | Out-Null
}

function Add-Application {
    param([string] $Node, [string] $Implementation, [string] $App)

    $definition = Invoke-Node $Node GET '/applications/schema' | Where-Object implementation -eq $Implementation
    foreach ($pair in @{ nodearrUrl = "http://host.docker.internal:$($nodes[$Node].Port)"; baseUrl = $arr[$App].Url; apiKey = $arr[$App].Key }.GetEnumerator()) {
        ($definition.fields | Where-Object name -eq $pair.Key) | Add-Member -NotePropertyName value -NotePropertyValue $pair.Value -Force
    }
    $definition | Add-Member -Force -NotePropertyMembers @{ name = $Implementation; syncLevel = 'fullSync' }
    return (Invoke-Node $Node POST '/applications' $definition).id
}

function Get-Transfers { return @(Invoke-Node B GET '/transfers') }

# Waits for B to hold one transfer that is under way and has moved some bytes, and returns it.
function Wait-Transferring {
    param([long] $AtLeast = 1)
    return Wait-Until -What 'a transfer to be under way' -TimeoutSec 60 -EverySec 0.5 -Condition {
        Get-Transfers | Where-Object { $_.state -eq 'downloading' -and $_.bytesDone -ge $AtLeast } | Select-Object -First 1
    }
}

function Get-History {
    param([string] $App, [string] $Path)
    return @(Invoke-Arr $App GET $Path)
}

$results = [System.Collections.Generic.List[object]]::new()

function Assert-That {
    param([string] $Name, [bool] $Passed, $Evidence)
    $results.Add([ordered]@{ check = $Name; passed = $Passed; evidence = $Evidence })
    Write-Host ("[{0}] {1}" -f ($Passed ? 'PASS' : 'FAIL'), $Name) -ForegroundColor ($Passed ? 'Green' : 'Red')
    if (-not $Passed) { Write-Host ("       " + ($Evidence | ConvertTo-Json -Depth 6 -Compress)) -ForegroundColor DarkGray }
}

function New-Media {
    param([string] $Path, [int] $Seconds, [string] $Source, [string] $VideoArgs)
    if (Test-Path $Path) { return }
    $encode = $VideoArgs -split ' '
    ffmpeg -hide_banner -loglevel error -y -f lavfi -i $Source -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t $Seconds `
        -c:v libx264 -preset ultrafast -pix_fmt yuv420p @encode -c:a aac -b:a 64k $Path
    if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }
}

# --- Fresh state -------------------------------------------------------------------------------

New-Item -ItemType Directory -Force $run, $media | Out-Null
'A', 'B' | ForEach-Object { Stop-Node $_ }
if (Test-Path $readyFile) { Remove-Item $readyFile -Force }

if ($Down) {
    if (Test-Path $envFile) { Invoke-Compose down -v 2>&1 | Out-Null }
    Write-Host 'Stopped both nodes and removed the containers.'
    return
}

# Throwaway keys for this run, kept so the instances can be poked at afterwards with -KeepRunning.
@{ A = $nodes.A.Key; B = $nodes.B.Key; sonarrB = $arr.sonarrB.Key; radarrB = $arr.radarrB.Key } | ConvertTo-Json | Set-Content (Join-Path $run 'node-keys.json')

@("SONARR_API_KEY=$($arr.sonarr.Key)", "RADARR_API_KEY=$($arr.radarr.Key)", "PROWLARR_API_KEY=$(New-Key)",
  "SONARR_B_API_KEY=$($arr.sonarrB.Key)", "RADARR_B_API_KEY=$($arr.radarrB.Key)",
  "LIBRARY_DIR=$($library -replace '\\', '/')", "DOWNLOADS_DIR=$($downloads -replace '\\', '/')", "DOWNLOADS_B_DIR=$($downloadsB -replace '\\', '/')") | Set-Content $envFile

$tvMedia = Join-Path $media 'episode-720p.mkv'
$movieMedia = Join-Path $media 'movie-720p.mkv'
New-Media $tvMedia 120 'testsrc2=size=1280x720:rate=24,noise=alls=30:allf=t' '-b:v 1400k -maxrate 1800k -bufsize 3600k'
New-Media $movieMedia 660 'smptebars=size=1280x720:rate=5' '-crf 30'
$tvHash = (Get-FileHash $tvMedia -Algorithm SHA256).Hash
$movieHash = (Get-FileHash $movieMedia -Algorithm SHA256).Hash

Invoke-Compose down -v 2>&1 | Out-Null
foreach ($dir in $nodes.A.Data, $nodes.B.Data, $downloads, $downloadsB, $library) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $nodes.A.Data, $nodes.B.Data, (Join-Path $downloads 'complete'), (Join-Path $downloadsB 'complete'), (Join-Path $library 'tv'), (Join-Path $library 'movies') | Out-Null

$episodeNames = @(
    'Pingu.S01E01.720p.WEB-DL.AAC2.0.H.264-NODEARR'
    'Pingu.S01E02.720p.WEB-DL.AAC2.0.H.264-NODEARR'
    'Pingu.S01E03.720p.WEB-DL.AAC2.0.H.264-NODEARR'
)
$movieName = 'Night.of.the.Living.Dead.1968.720p.WEB-DL.AAC2.0.H.264-NODEARR'

try {
    Start-Node A
    Start-Node B
    Invoke-Compose up -d sonarr radarr sonarr-b radarr-b 2>&1 | Out-Null
    foreach ($app in $arr.Keys) {
        Wait-Until -What "$app API" -TimeoutSec 240 -Condition { try { Invoke-Arr $app GET '/system/status' } catch { $null } } | Out-Null
    }

    # --- Each node registers itself in its own Arr apps ------------------------------------------

    Add-Application A Sonarr sonarr | Out-Null
    Add-Application A Radarr radarr | Out-Null
    Add-Application B Sonarr sonarrB | Out-Null
    Add-Application B Radarr radarrB | Out-Null
    foreach ($app in 'sonarrB', 'radarrB') {
        Wait-Until -What "B's download client in $app" -Condition { Invoke-Arr $app GET '/downloadclient' | Where-Object name -eq 'Nodearr' | Select-Object -First 1 } | Out-Null
    }

    # A reads its library through path mappings, because its Arr apps see /tv and /movies.
    Invoke-Node A POST '/remotepathmapping' @{ host = '127.0.0.1'; remotePath = '/tv/'; localPath = (Join-Path $library 'tv') + [IO.Path]::DirectorySeparatorChar } | Out-Null
    Invoke-Node A POST '/remotepathmapping' @{ host = '127.0.0.1'; remotePath = '/movies/'; localPath = (Join-Path $library 'movies') + [IO.Path]::DirectorySeparatorChar } | Out-Null

    # B's Arr apps see B's download folder at /downloads, and are told so the way they are for
    # any download client that runs on another machine.
    $completeB = (Join-Path $downloadsB 'complete') + [IO.Path]::DirectorySeparatorChar
    foreach ($app in 'sonarrB', 'radarrB') {
        Invoke-Arr $app POST '/remotepathmapping' @{ host = 'host.docker.internal'; remotePath = $completeB; localPath = '/downloads/complete/' } | Out-Null
    }

    foreach ($pair in @{ 'nodearr-s7-sonarr-1' = '/tv'; 'nodearr-s7-radarr-1' = '/movies'; 'nodearr-s7-sonarr-b-1' = '/tv'; 'nodearr-s7-radarr-b-1' = '/movies' }.GetEnumerator()) {
        docker exec $pair.Key chown abc:abc $pair.Value | Out-Null
    }

    $series = @{}
    $movie = @{}
    foreach ($app in 'sonarr', 'sonarrB') {
        Invoke-Arr $app POST '/rootfolder' @{ path = '/tv' } | Out-Null
        $found = (Invoke-Arr $app GET '/series/lookup?term=tvdb:75669')[0]
        $found | Add-Member -Force -NotePropertyMembers @{
            qualityProfileId = 1; rootFolderPath = '/tv'; monitored = $true; seasonFolder = $true
            addOptions = @{ monitor = 'all'; searchForMissingEpisodes = $false; searchForCutoffUnmetEpisodes = $false }
        }
        $series[$app] = Invoke-Arr $app POST '/series' $found
    }
    foreach ($app in 'radarr', 'radarrB') {
        Invoke-Arr $app POST '/rootfolder' @{ path = '/movies' } | Out-Null
        $found = (Invoke-Arr $app GET '/movie/lookup?term=tmdb:10331')[0]
        $found | Add-Member -Force -NotePropertyMembers @{ qualityProfileId = 1; rootFolderPath = '/movies'; monitored = $true; minimumAvailability = 'released'; addOptions = @{ searchForMovie = $false } }
        $movie[$app] = Invoke-Arr $app POST '/movie' $found
    }

    $episodesB = Wait-Until -What "the episode list in B's Sonarr" -Condition {
        $all = @(Invoke-Arr sonarrB GET "/episode?seriesId=$($series.sonarrB.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -le 3 })
        if ($all.Count -eq 3) { $all | Sort-Object episodeNumber }
    }
    Wait-Until -What "the episode list in A's Sonarr" -Condition { @(Invoke-Arr sonarr GET "/episode?seriesId=$($series.sonarr.id)").Count -gt 3 } | Out-Null

    foreach ($name in $episodeNames) {
        Import-ArrDownload -ApiUrl "$($arr.sonarr.Url)/api/v3" -ApiKey $arr.sonarr.Key -App Sonarr -SourceFile $tvMedia -Name $name -DownloadsDir $downloads
    }
    Import-ArrDownload -ApiUrl "$($arr.radarr.Url)/api/v3" -ApiKey $arr.radarr.Key -App Radarr -SourceFile $movieMedia -Name $movieName -DownloadsDir $downloads
    Wait-Until -What "A's Sonarr to hold three episode files" -TimeoutSec 60 -Condition { @(Invoke-Arr sonarr GET "/episodefile?seriesId=$($series.sonarr.id)").Count -eq 3 } | Out-Null
    Wait-Until -What "A's Radarr to hold the movie" -TimeoutSec 60 -Condition { (Invoke-Arr radarr GET "/movie/$($movie.radarr.id)").hasFile } | Out-Null

    Invoke-NodeCommand A @{ name = 'CatalogRefresh' }
    Invoke-NodeCommand B @{ name = 'CatalogRefresh' }

    $invite = Invoke-Node A POST '/nodes/invites' @{ validHours = 1; label = 's7'; shareAll = $true }
    Invoke-Node B POST '/nodes/invites/redeem' @{ code = $invite.code } | Out-Null
    Wait-Until -What "B to hold A's four files" -TimeoutSec 45 -Condition { (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 4 } | Out-Null
    $bOnA = Invoke-Node A GET '/nodes/peers' | Select-Object -First 1

    if ($SetupOnly) {
        @(
            "Ready $((Get-Date).ToString('u'))"
            "Node A (has the library):  http://localhost:$($nodes.A.Port)"
            "Node B (wants the files):  http://localhost:$($nodes.B.Port)"
            "B's Sonarr:                http://localhost:28989"
            "B's Radarr:                http://localhost:27878"
            "A's Sonarr:                http://localhost:18989"
            "A's Radarr:                http://localhost:17878"
        ) | Set-Content $readyFile
        Get-Content $readyFile | Write-Host
        return
    }

    # --- 0. Hashing ahead of time ------------------------------------------------------------------

    $hashing = Wait-Until -What 'A to hash what it shares' -TimeoutSec 60 -Condition {
        $status = Invoke-Node A GET '/sharing/hashing'
        if ($status.shared -eq 4 -and $status.hashed -eq 4 -and $status.lastPass) { $status }
    }
    Assert-That 'Before anything is asked for, the sending node has hashed every file it shares' ($null -ne $hashing) $hashing

    # --- 1. Sonarr: search, grab, transfer, import -----------------------------------------------

    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[0].id) } | Out-Null
    $moving = Wait-Transferring 1
    $slot = (Invoke-Sab 'mode=queue&start=0&limit=0&category=tv').queue.slots | Select-Object -First 1
    Assert-That 'A grab in Sonarr starts a transfer from the other node, and Sonarr sees it in its download client' (
        $moving.title -eq $episodeNames[0] -and $moving.size -eq (Get-Item $tvMedia).Length -and $moving.peerName -and
        $slot.nzo_id -eq $moving.downloadId -and $slot.status -eq 'Downloading'
    ) @{ transfer = @{ title = $moving.title; size = $moving.size; bytesDone = $moving.bytesDone; peer = $moving.peerName }; slot = $slot }

    $sending = Wait-Until -What 'A to show the upload' -TimeoutSec 30 -EverySec 0.5 -Condition {
        @(Invoke-Node A GET '/transfers/uploads') | Where-Object { $_.state -eq 'sending' -and $_.bytesSent -gt 0 } | Select-Object -First 1
    }
    Assert-That 'The node sending the file shows it leaving: what, to whom and how far along' (
        $sending.title -eq "$($episodeNames[0]).mkv" -and $sending.peerName -and $sending.size -eq (Get-Item $tvMedia).Length -and $sending.bytesSent -lt $sending.size
    ) @{ title = $sending.title; to = $sending.peerName; bytesSent = $sending.bytesSent; size = $sending.size }

    $incomplete = Join-Path $downloadsB "incomplete/$($moving.downloadId)"
    $appearedEarly = Test-Path (Join-Path $downloadsB "complete/tv/$($episodeNames[0])")
    Assert-That 'While it arrives the file is under incomplete, and nothing is under complete' (
        (Test-Path $incomplete) -and -not $appearedEarly
    ) @{ incomplete = (Test-Path $incomplete); completeEarly = $appearedEarly }

    $imported = Wait-Until -What "B's Sonarr to import episode 1" -TimeoutSec 180 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $episode = Invoke-Arr sonarrB GET "/episode/$($episodesB[0].id)"
        if ($episode.hasFile) { $episode }
    }
    $fileB = Invoke-Arr sonarrB GET "/episodefile/$($imported.episodeFileId)"
    $landed = docker exec nodearr-s7-sonarr-b-1 sha256sum $fileB.path
    $history = Get-History sonarrB "/history/series?seriesId=$($series.sonarrB.id)"
    $grabbed = $history | Where-Object eventType -eq 'grabbed' | Select-Object -First 1
    $importedEvent = $history | Where-Object eventType -eq 'downloadFolderImported' | Select-Object -First 1
    Assert-That 'The file Sonarr imports is the other node''s file, byte for byte, tied to the download Nodearr issued' (
        "$landed" -match $tvHash -and $fileB.size -eq (Get-Item $tvMedia).Length -and
        $grabbed.downloadId -eq $moving.downloadId -and $importedEvent.downloadId -eq $moving.downloadId
    ) @{ path = $fileB.path; hashMatches = ("$landed" -match $tvHash); grabbed = $grabbed.downloadId; imported = $importedEvent.downloadId; transfer = $moving.downloadId }

    $cleared = Wait-Until -What 'Sonarr to clear the finished download' -TimeoutSec 60 -Condition {
        if ((Get-Transfers).Count -eq 0) { @{ Left = @(Get-ChildItem (Join-Path $downloadsB 'complete') -Recurse -File -ErrorAction SilentlyContinue).Count; Incomplete = (Test-Path $incomplete) } }
    }
    Assert-That 'After the import Sonarr removes the download, and Nodearr keeps no copy' ($cleared.Left -eq 0 -and -not $cleared.Incomplete) $cleared

    # --- 2. Radarr -------------------------------------------------------------------------------

    Invoke-Arr radarrB POST '/command' @{ name = 'MoviesSearch'; movieIds = @($movie.radarrB.id) } | Out-Null
    $film = Wait-Until -What "B's Radarr to import the movie" -TimeoutSec 180 -EverySec 2 -Condition {
        Invoke-Arr radarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $current = Invoke-Arr radarrB GET "/movie/$($movie.radarrB.id)"
        if ($current.hasFile) { $current }
    }
    $filmLanded = docker exec nodearr-s7-radarr-b-1 sha256sum $film.movieFile.path
    Assert-That 'A grab in Radarr ends the same way: the movie is imported, byte for byte' (
        "$filmLanded" -match $movieHash -and $film.movieFile.size -eq (Get-Item $movieMedia).Length
    ) @{ path = $film.movieFile.path; hashMatches = ("$filmLanded" -match $movieHash) }

    # --- 2b. The first episode again, asked for by the day it aired --------------------------------

    # B's Sonarr is told the series is a daily one, which makes it search by day and not by
    # number, and turn down any result that is not titled by day. A's Sonarr still calls it
    # standard. The episode's file is deleted on B first, so there is something to fetch.
    $airDate = (Invoke-Arr sonarrB GET "/episode/$($episodesB[0].id)").airDate
    $firstFileId = (Invoke-Arr sonarrB GET "/episode/$($episodesB[0].id)").episodeFileId
    Invoke-Arr sonarrB DELETE "/episodefile/$firstFileId" | Out-Null
    $asDaily = Invoke-Arr sonarrB GET "/series/$($series.sonarrB.id)"
    $asDaily.seriesType = 'daily'
    Invoke-Arr sonarrB PUT "/series/$($series.sonarrB.id)" $asDaily | Out-Null

    $offeredByDay = @(Invoke-Arr sonarrB GET "/release?episodeId=$($episodesB[0].id)" | Where-Object indexer -eq 'Nodearr')[0]
    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[0].id) } | Out-Null
    $byDay = Wait-Transferring 1
    $importedByDay = Wait-Until -What "B's Sonarr to import the episode it asked for by day" -TimeoutSec 180 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $episode = Invoke-Arr sonarrB GET "/episode/$($episodesB[0].id)"
        if ($episode.hasFile -and $episode.episodeFileId -ne $firstFileId) { $episode }
    }
    $fileByDay = Invoke-Arr sonarrB GET "/episodefile/$($importedByDay.episodeFileId)"
    $landedByDay = docker exec nodearr-s7-sonarr-b-1 sha256sum $fileByDay.path
    $grabbedByDay = Get-History sonarrB "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object { $_.eventType -eq 'grabbed' -and $_.downloadId -eq $byDay.downloadId } | Select-Object -First 1
    $dayInTitle = 'Pingu.' + ($airDate -replace '-', '.') + '.'
    Assert-That 'A Sonarr that calls the series daily asks by the day the episode aired, is answered with a title by day, and imports the file' (
        $airDate -and $offeredByDay.approved -and $offeredByDay.title.StartsWith($dayInTitle) -and $byDay.title -eq $offeredByDay.title -and
        $grabbedByDay.sourceTitle -eq $offeredByDay.title -and "$landedByDay" -match $tvHash
    ) @{ airDate = $airDate; offered = $offeredByDay.title; approved = $offeredByDay.approved; rejections = @($offeredByDay.rejections); transfer = $byDay.title; grabbed = $grabbedByDay.sourceTitle; hashMatches = ("$landedByDay" -match $tvHash) }

    $asStandard = Invoke-Arr sonarrB GET "/series/$($series.sonarrB.id)"
    $asStandard.seriesType = 'standard'
    Invoke-Arr sonarrB PUT "/series/$($series.sonarrB.id)" $asStandard | Out-Null
    Wait-Until -What 'the finished download to be cleared' -TimeoutSec 60 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null

    # --- 3. The same ticket twice, and cancelling --------------------------------------------------

    $release = @(Invoke-Arr sonarrB GET "/release?episodeId=$($episodesB[1].id)" | Where-Object indexer -eq 'Nodearr')[0]
    $candidateId = $release.guid -replace '^nodearr:', ''
    $ticketFile = Join-Path $run 'ticket.nzb'
    Invoke-WebRequest "http://127.0.0.1:$($nodes.B.Port)/newznab/api?t=get&id=$candidateId&apikey=$($nodes.B.Key)" -OutFile $ticketFile
    $addUrl = "http://127.0.0.1:$($nodes.B.Port)/sabnzbd/api?mode=addfile&cat=tv&priority=-100&output=json&apikey=$($nodes.B.Key)"
    $first = Invoke-RestMethod $addUrl -Method Post -Form @{ name = Get-Item $ticketFile }
    $second = Invoke-RestMethod $addUrl -Method Post -Form @{ name = Get-Item $ticketFile }
    $moving = Wait-Transferring 1
    Assert-That 'Handing over the same ticket twice gives one transfer' (
        $first.status -and $second.status -and $first.nzo_ids[0] -eq $second.nzo_ids[0] -and (Get-Transfers).Count -eq 1
    ) @{ first = $first.nzo_ids; second = $second.nzo_ids; transfers = (Get-Transfers).Count }

    $deleted = Invoke-Sab "mode=queue&name=delete&del_files=1&value=$($first.nzo_ids[0])"
    Start-Sleep -Seconds 2
    $afterCancel = @{
        Transfers = (Get-Transfers).Count
        Incomplete = Test-Path (Join-Path $downloadsB "incomplete/$($first.nzo_ids[0])")
        Complete = @(Get-ChildItem (Join-Path $downloadsB 'complete') -Recurse -File -ErrorAction SilentlyContinue).Count
    }
    Assert-That 'Deleting it from the queue stops the transfer and leaves nothing behind' (
        $deleted.status -and $afterCancel.Transfers -eq 0 -and -not $afterCancel.Incomplete -and $afterCancel.Complete -eq 0
    ) $afterCancel

    # --- 4. Node B restarts in the middle ----------------------------------------------------------

    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[1].id) } | Out-Null
    $before = Wait-Transferring (4 * 1024 * 1024)
    Stop-Node B
    Start-Sleep -Seconds 3

    # Opened, not listed: a directory listing can lag behind a file that was just being written.
    $partial = @(Get-ChildItem (Join-Path $downloadsB "incomplete/$($before.downloadId)") -File)[0]
    $stream = [IO.File]::OpenRead($partial.FullName)
    $heldAtStop = $stream.Length
    $stream.Dispose()

    Start-Node B

    # A transfer that started over would be seen, at this speed, somewhere below what was held.
    # One that carried on is never seen below it.
    $samples = [System.Collections.Generic.List[long]]::new()
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
        $current = Get-Transfers | Where-Object downloadId -eq $before.downloadId | Select-Object -First 1
        if ($current) { $samples.Add([long]$current.bytesDone) }
        Start-Sleep -Milliseconds 250
    }
    $lowest = ($samples | Measure-Object -Minimum).Minimum
    $highest = ($samples | Measure-Object -Maximum).Maximum
    Assert-That 'After a restart the same transfer carries on from what had already arrived' (
        $heldAtStop -ge (4 * 1024 * 1024) -and $samples.Count -gt 10 -and $lowest -ge $before.bytesDone -and $highest -gt $heldAtStop
    ) @{ heldAtStop = $heldAtStop; knownBeforeStop = $before.bytesDone; lowestSeenAfter = $lowest; highestSeenAfter = $highest; samples = $samples.Count }

    $importedTwo = Wait-Until -What "B's Sonarr to import episode 2" -TimeoutSec 180 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $episode = Invoke-Arr sonarrB GET "/episode/$($episodesB[1].id)"
        if ($episode.hasFile) { $episode }
    }
    $fileTwo = Invoke-Arr sonarrB GET "/episodefile/$($importedTwo.episodeFileId)"
    $landedTwo = docker exec nodearr-s7-sonarr-b-1 sha256sum $fileTwo.path
    Assert-That 'The file that was resumed is still whole and correct when Sonarr imports it' ("$landedTwo" -match $tvHash) @{ path = $fileTwo.path; hashMatches = ("$landedTwo" -match $tvHash) }

    # --- 4b. The series is unshared in the middle ------------------------------------------------

    # A puts the series on its Hidden list while episode 3 is on its way. Trust stays.
    $hiddenList = Invoke-Node A GET '/sharing/lists' | Where-Object isHidden | Select-Object -First 1
    $pinguOnA = Invoke-Node A GET '/sharing/titles' | Where-Object title -match 'Pingu' | Select-Object -First 1
    $pinguKey = @{ mediaKind = $pinguOnA.mediaKind; applicationId = $pinguOnA.applicationId; arrId = $pinguOnA.arrId }

    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[2].id) } | Out-Null
    $unshared = Wait-Transferring (2 * 1024 * 1024)
    Invoke-Node A PUT "/sharing/lists/$($hiddenList.id)" @{ name = $hiddenList.name; items = @($pinguKey); peerIds = @() } | Out-Null
    $cut = Wait-Until -What 'the transfer to stop once the series is unshared' -TimeoutSec 15 -EverySec 0.5 -Condition {
        Get-Transfers | Where-Object { $_.downloadId -eq $unshared.downloadId -and $_.state -eq 'failed' } | Select-Object -First 1
    }
    $cutUpload = @(Invoke-Node A GET '/transfers/uploads') | Where-Object { $_.title -eq "$($episodeNames[2]).mkv" -and $_.state -eq 'failed' } | Select-Object -First 1
    $cutSlot = (Invoke-Sab 'mode=history&start=0&limit=60&category=tv').history.slots | Where-Object nzo_id -eq $unshared.downloadId
    Assert-That 'When the sending node unshares a series mid-transfer, the transfer stops there, says why, and is not tried again' (
        $cut.failure -eq 'notAuthorized' -and $cut.bytesDone -lt $cut.size -and $cut.attempts -eq 1 -and
        $cutSlot.status -eq 'Failed' -and $cutSlot.fail_message -match 'NotAuthorized' -and
        $cutUpload.failure -eq 'NotAuthorized' -and $cutUpload.bytesSent -lt $cutUpload.size -and
        -not (Test-Path (Join-Path $downloadsB "incomplete/$($unshared.downloadId)"))
    ) @{ failure = $cut.failure; bytesDone = $cut.bytesDone; size = $cut.size; attempts = $cut.attempts; message = $cutSlot.fail_message; upload = @{ failure = $cutUpload.failure; message = $cutUpload.failureMessage; bytesSent = $cutUpload.bytesSent } }

    # Share it again, and let Sonarr record the failure and then forget it, so the release can be
    # grabbed again below.
    Wait-Until -What 'Sonarr to record the failure' -TimeoutSec 90 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        Get-History sonarrB "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object { $_.eventType -eq 'downloadFailed' -and $_.downloadId -eq $unshared.downloadId } | Select-Object -First 1
    } | Out-Null
    foreach ($entry in @((Invoke-Arr sonarrB GET '/blocklist?pageSize=50').records)) { Invoke-Arr sonarrB DELETE "/blocklist/$($entry.id)" | Out-Null }
    Invoke-Node A PUT "/sharing/lists/$($hiddenList.id)" @{ name = $hiddenList.name; items = @(); peerIds = @() } | Out-Null
    Wait-Until -What "B to hold A's four files again" -TimeoutSec 45 -Condition { (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 4 } | Out-Null
    Wait-Until -What 'the failed download to be cleared' -TimeoutSec 60 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null

    # --- 5. Trust ends in the middle ---------------------------------------------------------------

    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[2].id) } | Out-Null
    $doomed = Wait-Transferring (2 * 1024 * 1024)
    Invoke-Node A POST "/nodes/peers/$($bOnA.id)/revoke" | Out-Null
    $failed = Wait-Until -What 'the transfer to fail' -TimeoutSec 30 -EverySec 0.5 -Condition {
        Get-Transfers | Where-Object { $_.downloadId -eq $doomed.downloadId -and $_.state -eq 'failed' } | Select-Object -First 1
    }
    $failedSlot = (Invoke-Sab 'mode=history&start=0&limit=60&category=tv').history.slots | Where-Object nzo_id -eq $doomed.downloadId
    Assert-That 'When the other node ends trust mid-transfer, the transfer stops and says why' (
        $failed.failure -eq 'revoked' -and $failed.bytesDone -lt $failed.size -and $failedSlot.status -eq 'Failed' -and
        $failedSlot.fail_message -match 'Revoked' -and -not (Test-Path (Join-Path $downloadsB "incomplete/$($doomed.downloadId)"))
    ) @{ failure = $failed.failure; bytesDone = $failed.bytesDone; size = $failed.size; message = $failedSlot.fail_message }

    $sonarrFailure = Wait-Until -What 'Sonarr to record the failure' -TimeoutSec 90 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $event = Get-History sonarrB "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object { $_.eventType -eq 'downloadFailed' -and $_.downloadId -eq $doomed.downloadId } | Select-Object -First 1
        if ($event) { @{ Message = $event.data.message; Blocklisted = @((Invoke-Arr sonarrB GET '/blocklist?pageSize=50').records).Count; EpisodeHasFile = (Invoke-Arr sonarrB GET "/episode/$($episodesB[2].id)").hasFile } }
    }
    Assert-That 'Sonarr records the failure with Nodearr''s reason, blocklists the release, and imports nothing' (
        $sonarrFailure.Message -match 'Revoked' -and $sonarrFailure.Blocklisted -ge 1 -and -not $sonarrFailure.EpisodeHasFile
    ) $sonarrFailure

    # --- 6. What the sending node saw of all that ---------------------------------------------------

    # The first episode was sent twice: once asked for by number, once by day.
    $uploads = @(Invoke-Node A GET '/transfers/uploads')
    $ended = { param([string] $name, [string] $state, [string] $failure) @($uploads | Where-Object { $_.title -eq "$name.mkv" -and $_.state -eq $state -and (-not $failure -or $_.failure -eq $failure) }).Count }

    # Episode 2 was cancelled by B once and cut off by B stopping once. Which of the two words A
    # uses for B stopping depends on what it notices first: the connection closing, or the direct
    # path to B going with it.
    $wentAway = (& $ended $episodeNames[1] 'failed' 'Stopped') + (& $ended $episodeNames[1] 'failed' 'DirectPathLost')
    Assert-That 'Afterwards the sending node lists each upload and how it ended: sent, stopped by the other side, cut when unshared, or cut when trust ended' (
        (& $ended $episodeNames[0] 'completed') -eq 2 -and (& $ended $movieName 'completed') -eq 1 -and
        (& $ended $episodeNames[1] 'completed') -eq 1 -and $wentAway -ge 2 -and
        (& $ended $episodeNames[2] 'failed' 'NotAuthorized') -eq 1 -and
        (& $ended $episodeNames[2] 'failed' 'Revoked') -eq 1 -and @($uploads | Where-Object { $_.state -in 'sending', 'preparing' }).Count -eq 0
    ) @{ uploads = @($uploads | ForEach-Object { "$($_.title -replace '\.720p.*', '') -> $($_.state) $($_.failure) $($_.bytesSent)/$($_.size)" }) }
}
catch {
    Write-Host "Stopped: $_" -ForegroundColor Red
    $results.Add([ordered]@{ check = 'Script ran to the end'; passed = $false; evidence = "$_" })
}
finally {
    $failed = @($results | Where-Object { -not $_.passed })
    [ordered]@{ ranAt = (Get-Date).ToUniversalTime().ToString('o'); passed = ($failed.Count -eq 0 -and $results.Count -gt 0); checks = $results } |
        ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'results.json')

    if (-not $KeepRunning -and -not $SetupOnly) {
        'A', 'B' | ForEach-Object { Stop-Node $_ }
        Invoke-Compose down -v 2>&1 | Out-Null
    }
}

if ($SetupOnly -and $failed.Count -eq 0) { return }

Write-Host "`n$($results.Count - $failed.Count) of $($results.Count) checks passed. Details: $(Join-Path $run 'results.json')"
if ($failed.Count -gt 0) { exit 1 }
