#requires -Version 7
<#
.SYNOPSIS
    S7 acceptance: a real Sonarr and Radarr grab from Nodearr, the file comes from a trusted node,
    is verified, and is imported.

.DESCRIPTION
    Node A has a library: its own Sonarr and Radarr hold three Pingu episodes and one movie.
    Node B has its own Sonarr and Radarr, which know the same series and movie and have no files.
    The nodes pair and A shares. RSS first skips that initial library, then fetches a fourth
    episode added afterwards. The script then presses "search" in B's Sonarr and Radarr and
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

.PARAMETER SonarrConsumerPort
    B's Sonarr port. Defaults to 28989.

.PARAMETER RadarrConsumerPort
    B's Radarr port. Defaults to 27878.

.PARAMETER OlderTransportBin
    A sidecar from an earlier release, for one node to run with -OlderNode. The transfer protocol
    is the sidecar's, so that node speaks to the other as a node on that release would. A sidecar
    from before 0.5.0 has no upload line: its upload settings are fixed at 0 in the environment,
    and its node gets the older checks. A later one gets the checks a current one does.

.PARAMETER OlderNode
    A or B: the node that runs -OlderTransportBin. A is then an older sender, B an older receiver.
#>
[CmdletBinding()]
param(
    [string] $TransportBin,
    [switch] $KeepRunning,
    [switch] $SetupOnly,
    [switch] $Down,
    [int] $UploadLimitKbps = 1024,
    [string] $NodearrDll,
    [int] $FirstPort = 6633,
    [int] $SonarrConsumerPort = 28989,
    [int] $RadarrConsumerPort = 27878,
    [string] $OlderTransportBin,
    [ValidateSet('', 'A', 'B')]
    [string] $OlderNode = ''
)

if ($OlderNode -and -not (Test-Path $OlderTransportBin)) { throw "-OlderNode $OlderNode needs -OlderTransportBin, the earlier release's sidecar" }
# Uploads wait their turn from 0.5.0 on, which brought --max-uploads and --total-upload-limit-kbps.
# An older sidecar with them is checked as a current one is; one from before has no line.
$olderQueues = $OlderNode -and ((& $OlderTransportBin --help) -join "`n") -match '--max-uploads'

$ErrorActionPreference = 'Stop'
# Version 1 only: the APIs leave null properties out of their JSON, and reading one must give $null.
Set-StrictMode -Version 1

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '..')
$run = Join-Path $root '.run/s7'
$dll = $NodearrDll ? $NodearrDll : (Join-Path $repo '_output/net10.0/Nodearr.Console.dll')
$composeFiles = @(
    '-f', (Join-Path $root 'docker-compose.arr.yml'),
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
    sonarrB = @{ Url = "http://127.0.0.1:$SonarrConsumerPort"; Key = New-Key }
    radarrB = @{ Url = "http://127.0.0.1:$RadarrConsumerPort"; Key = New-Key }
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
    Remove-Item Env:Nodearr__Transport__MaxUploads, Env:Nodearr__Transport__TotalUploadLimitKbps -ErrorAction SilentlyContinue
    if ($Name -eq $OlderNode) {
        $env:Nodearr__Transport__BinaryPath = $OlderTransportBin
        if (-not $olderQueues) {
            # Left at 0, Nodearr does not pass the earlier sidecar the arguments it does not know.
            $env:Nodearr__Transport__MaxUploads = 0
            $env:Nodearr__Transport__TotalUploadLimitKbps = 0
        }
    }
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
  "SONARR_B_PORT=$SonarrConsumerPort", "RADARR_B_PORT=$RadarrConsumerPort",
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
            "B's Sonarr:                http://localhost:$SonarrConsumerPort"
            "B's Radarr:                http://localhost:$RadarrConsumerPort"
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

    # --- RSS: pairing is quiet, only a later arrival is fetched ----------------------------------

    $rssIndexer = @(Invoke-Arr sonarrB GET '/indexer' | Where-Object name -eq 'Nodearr')
    if ($rssIndexer.Count -ne 1) { throw 'Expected exactly one Nodearr indexer in B''s Sonarr' }
    $rssIndexer = $rssIndexer[0]
    $bSonarrApp = @(Invoke-Node B GET '/applications' | Where-Object implementation -eq 'Sonarr')
    $feedUrl = ($rssIndexer.fields | Where-Object name -eq 'baseUrl').value
    Assert-That 'Sonarr is registered on its own application feed' (
        $bSonarrApp.Count -eq 1 -and $feedUrl -eq "http://host.docker.internal:$($nodes.B.Port)/newznab/$($bSonarrApp[0].id)"
    ) @{ feed = $feedUrl; applicationId = $bSonarrApp[0].id }
    Assert-That 'A new library app defaults to automatic fetching on, with upgrades off' (
        ($bSonarrApp[0].fields | Where-Object name -eq 'fetchNewFilesAutomatically').value -eq $true -and
        ($bSonarrApp[0].fields | Where-Object name -eq 'includeAutomaticUpgrades').value -eq $false -and $rssIndexer.enableRss
    ) @{ rss = $rssIndexer.enableRss }
    foreach ($enabled in @($false, $true)) {
        $appSettings = Invoke-Node B GET "/applications/$($bSonarrApp[0].id)"
        ($appSettings.fields | Where-Object name -eq 'fetchNewFilesAutomatically').value = $enabled
        Invoke-Node B PUT "/applications/$($appSettings.id)" $appSettings | Out-Null
        Invoke-NodeCommand B @{ name = 'ApplicationIndexerSync'; forceSync = $true }
        $indexers = @(Invoke-Arr sonarrB GET '/indexer' | Where-Object name -eq 'Nodearr')
        $saved = Invoke-Node B GET "/applications/$($appSettings.id)"
        Assert-That "Automatic fetching set to $enabled in Nodearr sets Sonarr RSS to $enabled" (
            $indexers.Count -eq 1 -and $indexers[0].enableRss -eq $enabled -and
            ($saved.fields | Where-Object name -eq 'fetchNewFilesAutomatically').value -eq $enabled
        ) @{ enabled = $enabled; rss = $indexers[0].enableRss; indexerCount = $indexers.Count }
    }
    $rss = Invoke-Arr sonarrB POST '/command' @{ name = 'RssSync' }
    Wait-Until -What 'Sonarr RSS sync after pairing' -Condition {
        $command = Invoke-Arr sonarrB GET "/command/$($rss.id)"
        if ($command.status -eq 'failed') { throw 'Sonarr RSS sync failed' }
        $command.status -eq 'completed'
    } | Out-Null
    Assert-That 'RSS does not grab the library received at pairing' (
        (Get-Transfers).Count -eq 0 -and
        @(Invoke-Arr sonarrB GET "/episode?seriesId=$($series.sonarrB.id)" | Where-Object hasFile).Count -eq 0 -and
        @(Get-History sonarrB "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object eventType -eq 'grabbed').Count -eq 0
    ) @{ transfers = (Get-Transfers).Count }

    $rssName = 'Pingu.S01E04.720p.WEB-DL.AAC2.0.H.264-NODEARR'
    Import-ArrDownload -ApiUrl "$($arr.sonarr.Url)/api/v3" -ApiKey $arr.sonarr.Key -App Sonarr -SourceFile $tvMedia -Name $rssName -DownloadsDir $downloads
    Invoke-NodeCommand A @{ name = 'CatalogRefresh' }
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    Wait-Until -What 'B to receive the new episode' -Condition { (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 5 } | Out-Null
    $rssEpisode = @(Invoke-Arr sonarrB GET "/episode?seriesId=$($series.sonarrB.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -eq 4 })[0]
    $aOnB = @(Invoke-Node B GET '/nodes/peers' | Where-Object state -eq 'trusted')[0]
    Assert-That 'A newly paired friend allows automatic fetching by default' ($aOnB.fetchNewFilesAutomatically -eq $true) @{ enabled = $aOnB.fetchNewFilesAutomatically }
    Invoke-Node B PUT "/nodes/peers/$($aOnB.id)/automaticfetch" @{ fetchNewFilesAutomatically = $false } | Out-Null
    $savedPeer = @(Invoke-Node B GET '/nodes/peers' | Where-Object id -eq $aOnB.id)[0]
    $rss = Invoke-Arr sonarrB POST '/command' @{ name = 'RssSync' }
    Wait-Until -What 'Sonarr RSS sync with the friend opted out' -Condition {
        $command = Invoke-Arr sonarrB GET "/command/$($rss.id)"
        if ($command.status -eq 'failed') { throw 'Sonarr RSS sync failed' }
        $command.status -eq 'completed'
    } | Out-Null
    $search = @(Invoke-Arr sonarrB GET "/release?episodeId=$($rssEpisode.id)")
    Assert-That 'Opting out a friend stops RSS but keeps manual episode search available' (
        $savedPeer.fetchNewFilesAutomatically -eq $false -and
        -not (Invoke-Arr sonarrB GET "/episode/$($rssEpisode.id)").hasFile -and
        @(Get-History sonarrB "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object eventType -eq 'grabbed').Count -eq 0 -and
        @($search | Where-Object title -eq $rssName).Count -gt 0
    ) @{ enabled = $savedPeer.fetchNewFilesAutomatically; searchResults = $search.Count }
    Invoke-Node B PUT "/nodes/peers/$($aOnB.id)/automaticfetch" @{ fetchNewFilesAutomatically = $true } | Out-Null
    $savedPeer = @(Invoke-Node B GET '/nodes/peers' | Where-Object id -eq $aOnB.id)[0]
    Assert-That 'Re-enabling automatic fetching is saved' ($savedPeer.fetchNewFilesAutomatically -eq $true) @{ enabled = $savedPeer.fetchNewFilesAutomatically }
    $rss = Invoke-Arr sonarrB POST '/command' @{ name = 'RssSync' }
    $rssImported = Wait-Until -What 'Sonarr to import the RSS arrival' -TimeoutSec 180 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $episode = Invoke-Arr sonarrB GET "/episode/$($rssEpisode.id)"
        if ($episode.hasFile) { $episode }
    }
    $rssFile = Invoke-Arr sonarrB GET "/episodefile/$($rssImported.episodeFileId)"
    $rssLanded = docker exec nodearr-s7-sonarr-b-1 sha256sum $rssFile.path
    $rssGrabs = @(Get-History sonarrB "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object eventType -eq 'grabbed')
    Assert-That 'RSS alone fetches and imports the later arrival, byte for byte, leaving the initial episodes missing' (
        "$rssLanded" -match $tvHash -and $rssGrabs.Count -eq 1 -and $rssGrabs[0].sourceTitle -eq $rssName -and
        @(Invoke-Arr sonarrB GET "/episode?seriesId=$($series.sonarrB.id)" | Where-Object hasFile).Count -eq 1
    ) @{ title = $rssGrabs[0].sourceTitle; imported = $rssFile.path; hashMatches = ("$rssLanded" -match $tvHash) }

    # Restore the original library for the remaining transfer scenarios.
    $appSettings = Invoke-Node B GET "/applications/$($bSonarrApp[0].id)"
    ($appSettings.fields | Where-Object name -eq 'fetchNewFilesAutomatically').value = $false
    Invoke-Node B PUT "/applications/$($appSettings.id)" $appSettings | Out-Null
    Invoke-NodeCommand B @{ name = 'ApplicationIndexerSync'; forceSync = $true }
    Wait-Until -What 'the RSS download to be cleared' -TimeoutSec 60 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null
    $senderEpisode = @(Invoke-Arr sonarr GET "/episode?seriesId=$($series.sonarr.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -eq 4 })
    if ($senderEpisode.Count -ne 1 -or -not $senderEpisode[0].episodeFileId) { throw 'Expected exactly one source episode for the RSS arrival' }
    Invoke-Arr sonarr DELETE "/episodefile/$($senderEpisode[0].episodeFileId)" | Out-Null
    Invoke-Arr sonarrB DELETE "/episodefile/$($rssFile.id)" | Out-Null
    Invoke-NodeCommand A @{ name = 'CatalogRefresh' }
    Invoke-NodeCommand B @{ name = 'CatalogRefresh' }
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    Wait-Until -What 'B to hold the original four offered files' -Condition { (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 4 } | Out-Null

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

    # --- 2c. The file changes on A without its size or modified time changing ---------------------

    # A's kept hash for it is now wrong, and nothing about the file says so. The first attempt
    # fails verification. A forgets the hash on hearing that, and the next attempt carries the
    # right one.
    $changedFile = Join-Path $library "tv/Pingu/Season 1/$($episodeNames[0]).mkv"
    $modified = (Get-Item $changedFile).LastWriteTimeUtc
    $stream = [IO.File]::Open($changedFile, 'Open', 'ReadWrite')
    $stream.Position = [long]($stream.Length / 2)
    $byte = $stream.ReadByte()
    $stream.Position--
    $stream.WriteByte(($byte + 1) % 256)
    $stream.Dispose()
    (Get-Item $changedFile).LastWriteTimeUtc = $modified
    $changedHash = (Get-FileHash $changedFile -Algorithm SHA256).Hash

    $byDayFileId = (Invoke-Arr sonarrB GET "/episode/$($episodesB[0].id)").episodeFileId
    Invoke-Arr sonarrB DELETE "/episodefile/$byDayFileId" | Out-Null
    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[0].id) } | Out-Null

    $sawMismatch = $false
    $lastSeen = $null
    $importedChanged = Wait-Until -What "B's Sonarr to import the changed episode" -TimeoutSec 240 -EverySec 2 -Condition {
        $transfer = Get-Transfers | Where-Object title -eq $episodeNames[0] | Select-Object -First 1
        if ($transfer) {
            $script:lastSeen = $transfer
            if ($transfer.failure -eq 'verificationFailed') { $script:sawMismatch = $true }
        }
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $episode = Invoke-Arr sonarrB GET "/episode/$($episodesB[0].id)"
        if ($episode.hasFile -and $episode.episodeFileId -ne $byDayFileId) { $episode }
    }
    $fileChanged = Invoke-Arr sonarrB GET "/episodefile/$($importedChanged.episodeFileId)"
    $landedChanged = docker exec nodearr-s7-sonarr-b-1 sha256sum $fileChanged.path
    $failedInSonarr = @(Get-History sonarrB "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object { $_.eventType -eq 'downloadFailed' -and $_.downloadId -eq $lastSeen.downloadId }).Count
    $mismatchUploads = @(Invoke-Node A GET '/transfers/uploads' | Where-Object { $_.title -eq "$($episodeNames[0]).mkv" -and $_.state -eq 'completed' -and $_.attempts -eq 2 -and $_.lastFailure -eq 'VerificationFailed' }).Count
    Assert-That 'A file that changed on the sending node without its size or time changing costs one attempt, not the release: the next attempt brings the changed file' (
        $sawMismatch -and $lastSeen.attempts -eq 2 -and "$landedChanged" -match $changedHash -and $changedHash -ne $tvHash -and
        $failedInSonarr -eq 0 -and $mismatchUploads -eq 1
    ) @{ sawMismatch = $sawMismatch; attempts = $lastSeen.attempts; hashMatchesChangedFile = ("$landedChanged" -match $changedHash); failedInSonarr = $failedInSonarr; mismatchUploadsOnA = $mismatchUploads }
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

    # --- 3b. A download limit on the receiving node ------------------------------------------------

    # A sends at up to its upload limit. B is set to take a quarter of that, and the transfer is
    # watched for ten seconds. Setting it restarts B's transport, as the upload limit does. The
    # ticket goes in under a category of its own: under Sonarr's, Sonarr would take the transfer
    # for one of its downloads, see it deleted, and turn the release down when it searches next.
    $downloadLimit = [math]::Max(64, [math]::Floor($UploadLimitKbps / 4))
    $settingsB = Invoke-Node B GET '/settings/node'
    $settingsB.downloadLimitKbps = $downloadLimit
    $limited = Invoke-Node B PUT "/settings/node/$($settingsB.id)" $settingsB
    Invoke-WebRequest "http://127.0.0.1:$($nodes.B.Port)/newznab/api?t=get&id=$candidateId&apikey=$($nodes.B.Key)" -OutFile $ticketFile
    $slow = Invoke-RestMethod ($addUrl -replace 'cat=tv', 'cat=limit-test') -Method Post -Form @{ name = Get-Item $ticketFile }
    $startedSlow = Wait-Transferring 1
    $from = [long]$startedSlow.bytesDone
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds 10
    $to = [long](Get-Transfers | Where-Object downloadId -eq $startedSlow.downloadId | Select-Object -First 1).bytesDone
    $rateKbps = [math]::Round(($to - $from) / 1024 / $watch.Elapsed.TotalSeconds)
    Assert-That 'A download limit on the receiving node holds a transfer to it, below what the sender would send' (
        $limited.downloadLimitKbps -eq $downloadLimit -and $limited.transportRestarted -and $limited.transportRunning -and
        $rateKbps -gt 0 -and $rateKbps -le ($downloadLimit * 1.25)
    ) @{ downloadLimitKbps = $downloadLimit; uploadLimitKbps = $UploadLimitKbps; measuredKbps = $rateKbps; restarted = $limited.transportRestarted }

    Invoke-Sab "mode=queue&name=delete&del_files=1&value=$($slow.nzo_ids[0])" | Out-Null
    Wait-Until -What 'the limited transfer to be removed' -TimeoutSec 30 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null
    $settingsB = Invoke-Node B GET '/settings/node'
    $settingsB.downloadLimitKbps = 0
    Invoke-Node B PUT "/settings/node/$($settingsB.id)" $settingsB | Out-Null

    # --- 3b2. A total upload limit on the sending node ---------------------------------------------

    # A is set to send at most half its per-upload limit in all, and B fetches two episodes from it
    # at once. Together they are held to the total, and each gets a fair part of it. A sender from
    # before 0.5.0 has no total limit to hold to.
    if ($OlderNode -eq 'A' -and -not $olderQueues) { Write-Host 'Skipped: a total upload limit, which the older sender on A does not have' -ForegroundColor Yellow }
    else {
    $totalLimit = [math]::Max(64, [math]::Floor($UploadLimitKbps / 2))
    $settingsA = Invoke-Node A GET '/settings/node'
    $settingsA.totalUploadLimitKbps = $totalLimit
    $totalLimited = Invoke-Node A PUT "/settings/node/$($settingsA.id)" $settingsA
    Wait-Until -What "A's transport to be reachable again" -TimeoutSec 60 -Condition { (Invoke-Node A GET '/settings/node').transportRunning } | Out-Null
    $pair = foreach ($episode in $episodesB[0], $episodesB[1]) {
        $offer = @(Invoke-Arr sonarrB GET "/release?episodeId=$($episode.id)" | Where-Object indexer -eq 'Nodearr')[0]
        Invoke-WebRequest "http://127.0.0.1:$($nodes.B.Port)/newznab/api?t=get&id=$($offer.guid -replace '^nodearr:', '')&apikey=$($nodes.B.Key)" -OutFile $ticketFile
        (Invoke-RestMethod ($addUrl -replace 'cat=tv', 'cat=limit-test') -Method Post -Form @{ name = Get-Item $ticketFile }).nzo_ids[0]
    }
    $both = Wait-Until -What 'both transfers to be under way' -TimeoutSec 60 -EverySec 0.5 -Condition {
        $moving = @(Get-Transfers | Where-Object { $_.state -eq 'downloading' -and $_.bytesDone -gt 0 })
        if ($moving.Count -ge 2) { $moving }
    }
    $fromEach = @{}
    $both | ForEach-Object { $fromEach[$_.downloadId] = [long]$_.bytesDone }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds 10
    $seconds = $watch.Elapsed.TotalSeconds
    $rates = @(Get-Transfers | Where-Object { $fromEach.ContainsKey($_.downloadId) } | ForEach-Object { [math]::Round(([long]$_.bytesDone - $fromEach[$_.downloadId]) / 1024 / $seconds) })
    $sumKbps = ($rates | Measure-Object -Sum).Sum
    Assert-That 'A total upload limit on the sending node holds two transfers from it together, each getting a fair part' (
        $totalLimited.totalUploadLimitKbps -eq $totalLimit -and $totalLimited.transportRestarted -and $totalLimited.transportRunning -and
        $rates.Count -eq 2 -and $sumKbps -le ($totalLimit * 1.25) -and @($rates | Where-Object { $_ -ge $totalLimit * 0.25 }).Count -eq 2
    ) @{ totalUploadLimitKbps = $totalLimit; uploadLimitKbps = $UploadLimitKbps; measuredKbps = $rates; togetherKbps = $sumKbps; restarted = $totalLimited.transportRestarted }

    foreach ($id in $pair) { Invoke-Sab "mode=queue&name=delete&del_files=1&value=$id" | Out-Null }
    Wait-Until -What 'the limited transfers to be removed' -TimeoutSec 30 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null
    $settingsA = Invoke-Node A GET '/settings/node'
    $settingsA.totalUploadLimitKbps = 0
    Invoke-Node A PUT "/settings/node/$($settingsA.id)" $settingsA | Out-Null
    }

    # --- 3c. A friend's PROPER replaces a copy that is not one -------------------------------------

    # A's Sonarr takes a PROPER of the first episode in place of its copy, as it would from any
    # indexer, and records its revision. B holds the first copy. It is shown A's PROPER as an
    # upgrade, and its Sonarr takes it from A as it takes any proper.
    $properName = 'Pingu.S01E01.PROPER.720p.WEB-DL.AAC2.0.H.264-NODEARR'
    Import-ArrDownload -ApiUrl "$($arr.sonarr.Url)/api/v3" -ApiKey $arr.sonarr.Key -App Sonarr -SourceFile $tvMedia -Name $properName -DownloadsDir $downloads
    Wait-Until -What "A's Sonarr to hold the PROPER" -TimeoutSec 60 -Condition {
        @(Invoke-Arr sonarr GET "/episodefile?seriesId=$($series.sonarr.id)") | Where-Object { $_.quality.revision.version -eq 2 } | Select-Object -First 1
    } | Out-Null
    Invoke-NodeCommand A @{ name = 'CatalogRefresh' }
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    $offeredProper = Wait-Until -What "B to be offered A's PROPER" -TimeoutSec 60 -EverySec 2 -Condition {
        @((Invoke-Node B GET '/catalog/peers/items?pageSize=50').records) | Where-Object { $_.revision -eq 'Proper' } | Select-Object -First 1
    }

    $heldBefore = (Invoke-Arr sonarrB GET "/episode/$($episodesB[0].id)").episodeFileId
    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[0].id) } | Out-Null
    $importedProper = Wait-Until -What "B's Sonarr to import the PROPER" -TimeoutSec 240 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $episode = Invoke-Arr sonarrB GET "/episode/$($episodesB[0].id)"
        if ($episode.hasFile -and $episode.episodeFileId -ne $heldBefore) { Invoke-Arr sonarrB GET "/episodefile/$($episode.episodeFileId)" }
    }
    $grabbedProper = Get-History sonarrB "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object { $_.eventType -eq 'grabbed' -and $_.sourceTitle -match 'PROPER' } | Select-Object -First 1
    Assert-That "A friend's PROPER is offered as one and shown as an upgrade, and Sonarr takes it in place of its copy as it takes any proper" (
        $offeredProper.localStatus -eq 'upgrade' -and $offeredProper.releaseTitle -match 'PROPER' -and $grabbedProper -and
        $importedProper.quality.revision.version -eq 2 -and $importedProper.quality.quality.name -eq 'WEBDL-720p'
    ) @{ offered = "$($offeredProper.releaseTitle) $($offeredProper.revision) $($offeredProper.localStatus)"; grabbed = $grabbedProper.sourceTitle; imported = "$($importedProper.quality.quality.name) v$($importedProper.quality.revision.version)" }
    Wait-Until -What 'the finished download to be cleared' -TimeoutSec 60 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null

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

    # --- 4a. One download at a time ---------------------------------------------------------------

    # B takes one download at a time. Its Sonarr loses the first two episodes and searches for
    # both: one arrives while the other waits, queued in Nodearr and in Sonarr's own queue, and
    # starts only once the first is done.
    Wait-Until -What 'the resumed download to be cleared' -TimeoutSec 60 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null
    $settingsB = Invoke-Node B GET '/settings/node'
    $settingsB.maxDownloads = 1
    $oneAtATime = Invoke-Node B PUT "/settings/node/$($settingsB.id)" $settingsB
    foreach ($episode in $episodesB[0], $episodesB[1]) {
        $fileId = (Invoke-Arr sonarrB GET "/episode/$($episode.id)").episodeFileId
        Invoke-Arr sonarrB DELETE "/episodefile/$fileId" | Out-Null
    }
    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[0].id, $episodesB[1].id) } | Out-Null
    $waiting = Wait-Until -What 'one download under way and one waiting' -TimeoutSec 90 -EverySec 0.5 -Condition {
        $all = @(Get-Transfers)
        $running = @($all | Where-Object state -eq 'downloading')
        $queued = @($all | Where-Object state -eq 'queued')
        if ($all.Count -eq 2 -and $running.Count -eq 1 -and $queued.Count -eq 1) { @{ Running = $running[0]; Queued = $queued[0] } }
    }
    $queuedSlot = (Invoke-Sab 'mode=queue&start=0&limit=0&category=tv').queue.slots | Where-Object nzo_id -eq $waiting.Queued.downloadId
    $inSonarr = Wait-Until -What "Sonarr's queue to show the waiting download" -TimeoutSec 30 -EverySec 1 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        @((Invoke-Arr sonarrB GET '/queue?pageSize=50').records) | Where-Object { $_.downloadId -eq $waiting.Queued.downloadId } | Select-Object -First 1
    }
    Assert-That 'With one download at a time, a second grab waits, queued in Nodearr and in Sonarr''s queue' (
        $oneAtATime.maxDownloads -eq 1 -and -not $oneAtATime.transportRestarted -and
        $queuedSlot.status -eq 'Queued' -and $inSonarr.status -eq 'queued' -and $inSonarr.trackedDownloadStatus -eq 'ok'
    ) @{ running = $waiting.Running.title; waiting = $waiting.Queued.title; sabStatus = $queuedSlot.status; sonarrStatus = $inSonarr.status; sonarrTracked = $inSonarr.trackedDownloadStatus; restarted = $oneAtATime.transportRestarted }

    # The waiting one starts once the first is no longer downloading, and both are imported.
    $turn = Wait-Until -What 'the waiting download to start' -TimeoutSec 180 -EverySec 0.5 -Condition {
        $all = @(Get-Transfers)
        $second = $all | Where-Object downloadId -eq $waiting.Queued.downloadId | Select-Object -First 1
        if ($second.state -eq 'downloading') { @{ FirstStillDownloading = @($all | Where-Object { $_.downloadId -eq $waiting.Running.downloadId -and $_.state -eq 'downloading' }).Count -gt 0 } }
    }
    $bothBack = Wait-Until -What "B's Sonarr to import both episodes" -TimeoutSec 240 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $back = @($episodesB[0], $episodesB[1] | ForEach-Object { Invoke-Arr sonarrB GET "/episode/$($_.id)" } | Where-Object hasFile)
        if ($back.Count -eq 2) { $back }
    }
    Assert-That 'The waiting download starts only once the first is done, and Sonarr imports both' (
        -not $turn.FirstStillDownloading -and $bothBack.Count -eq 2
    ) @{ firstStillDownloading = $turn.FirstStillDownloading; imported = @($bothBack | ForEach-Object { "S$($_.seasonNumber)E$($_.episodeNumber)" }) }

    Wait-Until -What 'the downloads to be cleared' -TimeoutSec 60 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null
    $settingsB = Invoke-Node B GET '/settings/node'
    $settingsB.maxDownloads = 3
    Invoke-Node B PUT "/settings/node/$($settingsB.id)" $settingsB | Out-Null

    # --- 4a2. One upload at a time ----------------------------------------------------------------

    # An older receiver cannot be told it waits, but it waits all the same: the limit is A's, and
    # leaving out the word that it understands queueing does not get round it. Its download simply
    # looks like one still connecting. An older sender has no line, and sends both at once. Both
    # mean a sidecar from before 0.5.0; a later one queues, and takes the checks a current one does.
    if ($OlderNode -and -not $olderQueues) {
        if ($OlderNode -eq 'B') {
            $settingsA = Invoke-Node A GET '/settings/node'
            $settingsA.maxUploads = 1
            Invoke-Node A PUT "/settings/node/$($settingsA.id)" $settingsA | Out-Null
            Wait-Until -What "A's transport to be running again" -TimeoutSec 60 -Condition { (Invoke-Node A GET '/settings/node').transportRunning } | Out-Null
        }
        foreach ($episode in $episodesB[0], $episodesB[1]) {
            $fileId = (Invoke-Arr sonarrB GET "/episode/$($episode.id)").episodeFileId
            Invoke-Arr sonarrB DELETE "/episodefile/$fileId" | Out-Null
        }
        $script:sawWaiting = $false
        Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[0].id, $episodesB[1].id) } | Out-Null
        # How many of the two are arriving at once: one for an older receiver behind A's limit,
        # both from an older sender.
        $expected = $OlderNode -eq 'B' ? 1 : 2
        $together = Wait-Until -What "$expected download(s) arriving and the rest started" -TimeoutSec 90 -EverySec 0.5 -Condition {
            $all = @(Get-Transfers)
            if (@($all | Where-Object { $null -ne $_.waitingPosition }).Count) { $script:sawWaiting = $true }
            $arriving = @($all | Where-Object { $_.state -eq 'downloading' -and $_.bytesDone -gt 0 })
            if ($all.Count -eq 2 -and @($all | Where-Object state -eq 'downloading').Count -eq 2 -and $arriving.Count -eq $expected) { $arriving }
        }
        $sendingNow = @(Invoke-Node A GET '/transfers/uploads' | Where-Object { $_.state -in 'preparing', 'sending', 'waiting' })
        $secondTurn = $null
        if ($OlderNode -eq 'B') {
            $first = $together[0].downloadId
            $secondTurn = Wait-Until -What 'the quiet waiting download to start arriving' -TimeoutSec 180 -EverySec 0.5 -Condition {
                $all = @(Get-Transfers)
                if (@($all | Where-Object { $null -ne $_.waitingPosition }).Count) { $script:sawWaiting = $true }
                $second = $all | Where-Object { $_.downloadId -ne $first -and $_.state -eq 'downloading' -and $_.bytesDone -gt 0 } | Select-Object -First 1
                if ($second) { @{ FirstStillArriving = @($all | Where-Object { $_.downloadId -eq $first -and $_.state -eq 'downloading' }).Count -gt 0 } }
            }
        }
        $bothMixed = Wait-Until -What "B's Sonarr to import both episodes again" -TimeoutSec 240 -EverySec 2 -Condition {
            Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
            $all = @(Get-Transfers)
            if (@($all | Where-Object { $null -ne $_.waitingPosition }).Count) { $script:sawWaiting = $true }
            $back = @($episodesB[0], $episodesB[1] | ForEach-Object { Invoke-Arr sonarrB GET "/episode/$($_.id)" } | Where-Object hasFile)
            if ($back.Count -eq 2) { $back }
        }
        $senderWaiting = @($sendingNow | Where-Object state -eq 'waiting')
        $senderSending = @($sendingNow | Where-Object state -eq 'sending')
        if ($OlderNode -eq 'B') {
            Assert-That 'An older receiver cannot skip the line: with one upload at a time on A, its second request waits untold and is sent once the first is done' (
                $together.Count -eq 1 -and $senderSending.Count -eq 1 -and $senderWaiting.Count -eq 1 -and $senderWaiting[0].queuePosition -eq 1 -and
                -not $script:sawWaiting -and -not $secondTurn.FirstStillArriving -and $bothMixed.Count -eq 2
            ) @{ arriving = @($together | ForEach-Object title); senderStates = @($sendingNow | ForEach-Object state); senderPosition = $senderWaiting[0].queuePosition; receiverToldOfWaiting = $script:sawWaiting; firstStillArriving = $secondTurn.FirstStillArriving; imported = @($bothMixed | ForEach-Object { "S$($_.seasonNumber)E$($_.episodeNumber)" }) }
        }
        else {
            Assert-That 'A new receiver fetches from an older sender as before: both requests are sent at once and imported' (
                $together.Count -eq 2 -and -not $script:sawWaiting -and $senderWaiting.Count -eq 0 -and $senderSending.Count -eq 2 -and $bothMixed.Count -eq 2
            ) @{ arriving = @($together | ForEach-Object title); sawWaiting = $script:sawWaiting; senderStates = @($sendingNow | ForEach-Object state); imported = @($bothMixed | ForEach-Object { "S$($_.seasonNumber)E$($_.episodeNumber)" }) }
        }

        Wait-Until -What 'the downloads to be cleared' -TimeoutSec 60 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null
        if ($OlderNode -eq 'B') {
            $settingsA = Invoke-Node A GET '/settings/node'
            $settingsA.maxUploads = 3
            Invoke-Node A PUT "/settings/node/$($settingsA.id)" $settingsA | Out-Null
            Wait-Until -What "A's transport to be running again" -TimeoutSec 60 -Condition { (Invoke-Node A GET '/settings/node').transportRunning } | Out-Null
        }
    }
    else {

    # Now A sends one file at a time and B takes several. B's Sonarr loses the same two episodes
    # and searches again. Both start on B, but A queues the second and tells B where it stands.
    $settingsA = Invoke-Node A GET '/settings/node'
    $settingsA.maxUploads = 1
    $oneUpload = Invoke-Node A PUT "/settings/node/$($settingsA.id)" $settingsA
    Wait-Until -What "A's transport to be running again" -TimeoutSec 60 -Condition { (Invoke-Node A GET '/settings/node').transportRunning } | Out-Null
    foreach ($episode in $episodesB[0], $episodesB[1]) {
        $fileId = (Invoke-Arr sonarrB GET "/episode/$($episode.id)").episodeFileId
        Invoke-Arr sonarrB DELETE "/episodefile/$fileId" | Out-Null
    }
    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[0].id, $episodesB[1].id) } | Out-Null
    $atPeer = Wait-Until -What 'one download arriving and one waiting at A' -TimeoutSec 90 -EverySec 0.5 -Condition {
        $all = @(Get-Transfers)
        $waitingAtA = @($all | Where-Object { $_.state -eq 'downloading' -and $null -ne $_.waitingPosition })
        $arriving = @($all | Where-Object { $_.state -eq 'downloading' -and $null -eq $_.waitingPosition -and $_.bytesDone -gt 0 })
        if ($all.Count -eq 2 -and $waitingAtA.Count -eq 1 -and $arriving.Count -eq 1) { @{ Waiting = $waitingAtA[0]; Arriving = $arriving[0] } }
    }
    $waitingUpload = Wait-Until -What 'A to list the upload waiting its turn' -TimeoutSec 15 -EverySec 0.5 -Condition {
        @(Invoke-Node A GET '/transfers/uploads') | Where-Object { $_.state -eq 'waiting' } | Select-Object -First 1
    }
    $atPeerSlot = (Invoke-Sab 'mode=queue&start=0&limit=0&category=tv').queue.slots | Where-Object nzo_id -eq $atPeer.Waiting.downloadId
    # For a moment after it starts, before A's word that it is queued arrives, the download is
    # reported as downloading. Sonarr's refresh runs in the background, so its queue can still show
    # that for a while: what counts is what it settles on.
    $atPeerInSonarr = $null
    $settleBy = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $settleBy -and $atPeerInSonarr.status -ne 'queued') {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        Start-Sleep -Seconds 1
        $atPeerInSonarr = @((Invoke-Arr sonarrB GET '/queue?pageSize=50').records) | Where-Object { $_.downloadId -eq $atPeer.Waiting.downloadId } | Select-Object -First 1
    }
    Assert-That 'With one upload at a time, the second request waits at the sender, which tells the receiver its place in line' (
        $oneUpload.maxUploads -eq 1 -and $oneUpload.transportRestarted -and $oneUpload.transportRunning -and
        $atPeer.Waiting.waitingPosition -eq 1 -and $waitingUpload.queuePosition -eq 1 -and $waitingUpload.peerName -and
        $atPeerSlot.status -eq 'Queued' -and $atPeerInSonarr.status -eq 'queued' -and $atPeerInSonarr.trackedDownloadStatus -eq 'ok'
    ) @{ arriving = $atPeer.Arriving.title; waiting = $atPeer.Waiting.title; position = $atPeer.Waiting.waitingPosition; upload = @{ state = $waitingUpload.state; position = $waitingUpload.queuePosition }; sabStatus = $atPeerSlot.status; sonarrStatus = $atPeerInSonarr.status; sonarrTracked = $atPeerInSonarr.trackedDownloadStatus }

    $sent = Wait-Until -What 'the waiting download to start arriving' -TimeoutSec 180 -EverySec 0.5 -Condition {
        $all = @(Get-Transfers)
        $second = $all | Where-Object downloadId -eq $atPeer.Waiting.downloadId | Select-Object -First 1
        if ($second.state -eq 'downloading' -and $null -eq $second.waitingPosition -and $second.bytesDone -gt 0) {
            @{ FirstStillArriving = @($all | Where-Object { $_.downloadId -eq $atPeer.Arriving.downloadId -and $_.state -eq 'downloading' }).Count -gt 0 }
        }
    }
    $bothSent = Wait-Until -What "B's Sonarr to import both episodes again" -TimeoutSec 240 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $back = @($episodesB[0], $episodesB[1] | ForEach-Object { Invoke-Arr sonarrB GET "/episode/$($_.id)" } | Where-Object hasFile)
        if ($back.Count -eq 2) { $back }
    }
    Assert-That 'The waiting request is sent once the first upload is done, and Sonarr imports both' (
        -not $sent.FirstStillArriving -and $bothSent.Count -eq 2
    ) @{ firstStillArriving = $sent.FirstStillArriving; imported = @($bothSent | ForEach-Object { "S$($_.seasonNumber)E$($_.episodeNumber)" }) }

    Wait-Until -What 'the downloads to be cleared' -TimeoutSec 60 -Condition { (Get-Transfers).Count -eq 0 } | Out-Null
    $settingsA = Invoke-Node A GET '/settings/node'
    $settingsA.maxUploads = 3
    Invoke-Node A PUT "/settings/node/$($settingsA.id)" $settingsA | Out-Null
    Wait-Until -What "A's transport to be running again" -TimeoutSec 60 -Condition { (Invoke-Node A GET '/settings/node').transportRunning } | Out-Null
    }

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
    # A reads its uploads from the transport once a second, so it can be a moment behind B.
    $cutUpload = Wait-Until -What 'A to list the upload it cut' -TimeoutSec 10 -EverySec 0.5 -Condition {
        @(Invoke-Node A GET '/transfers/uploads') | Where-Object { $_.title -eq "$($episodeNames[2]).mkv" -and $_.state -eq 'failed' } | Select-Object -First 1
    }
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
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
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
        if ($event) { @{ Message = $event.data.message; Blocklisted = 0; EpisodeHasFile = (Invoke-Arr sonarrB GET "/episode/$($episodesB[2].id)").hasFile } }
    }
    # Sonarr writes the history entry and the blocklist entry separately, in answer to the same
    # failure, so the blocklist can be a moment behind.
    $blocklistBy = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $blocklistBy -and $sonarrFailure.Blocklisted -lt 1) {
        $sonarrFailure.Blocklisted = @((Invoke-Arr sonarrB GET '/blocklist?pageSize=50').records).Count
        if ($sonarrFailure.Blocklisted -lt 1) { Start-Sleep -Seconds 1 }
    }
    Assert-That 'Sonarr records the failure with Nodearr''s reason, blocklists the release, and imports nothing' (
        $sonarrFailure.Message -match 'Revoked' -and $sonarrFailure.Blocklisted -ge 1 -and -not $sonarrFailure.EpisodeHasFile
    ) $sonarrFailure

    # --- 6. What the sending node saw of all that ---------------------------------------------------

    # The first episode was fetched by number, by day, and after it changed. The changed file took
    # two attempts, which should remain one completed upload row. Its PROPER, fetched one at a
    # time with the second episode, is a file of another name. The second episode was fetched
    # after the restart, one download at a time, and one upload at a time.
    $uploads = @(Invoke-Node A GET '/transfers/uploads')
    $ended = { param([string] $name, [string] $state, [string] $failure) @($uploads | Where-Object { $_.title -eq "$name.mkv" -and $_.state -eq $state -and (-not $failure -or $_.failure -eq $failure) }).Count }
    $completedAttempts = @($uploads | Where-Object { $_.title -eq "$($episodeNames[0]).mkv" -and $_.state -eq 'completed' -and $_.attempts -eq 2 -and $_.lastFailure -eq 'VerificationFailed' }).Count
    $resumedUpload = @($uploads | Where-Object { $_.title -eq "$($episodeNames[1]).mkv" -and $_.state -eq 'completed' -and $_.attempts -ge 2 }).Count

    # Episode 2 was cancelled by B twice and cut off by B stopping once. Which of the two words A
    # uses for B stopping depends on what it notices first: the connection closing, or the direct
    # path to B going with it.
    $wentAway = (& $ended $episodeNames[1] 'failed' 'Stopped') + (& $ended $episodeNames[1] 'failed' 'DirectPathLost')
    Assert-That 'Afterwards the sending node lists each upload and how it ended: sent, stopped by the other side, cut when unshared, or cut when trust ended' (
        (& $ended $episodeNames[0] 'completed') -eq 3 -and (& $ended $episodeNames[0] 'failed' 'VerificationFailed') -eq 0 -and $completedAttempts -eq 1 -and (& $ended $movieName 'completed') -eq 1 -and
        (& $ended $episodeNames[1] 'completed') -eq 3 -and $resumedUpload -eq 1 -and $wentAway -ge 2 -and
        (& $ended $episodeNames[2] 'failed' 'NotAuthorized') -eq 1 -and
        (& $ended $episodeNames[2] 'failed' 'Revoked') -eq 1 -and @($uploads | Where-Object { $_.state -in 'sending', 'preparing' }).Count -eq 0
    ) @{ uploads = @($uploads | ForEach-Object { "$($_.title -replace '\.720p.*', '') -> $($_.state) attempts=$($_.attempts) previous=$($_.lastFailure) failure=$($_.failure) $($_.bytesSent)/$($_.size)" }) }
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
