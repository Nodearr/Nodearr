#requires -Version 7
<#
.SYNOPSIS
    Identical copies: the same file on two trusted nodes is one result in Sonarr, and a transfer
    carries on from the second node when the first stops part way.

.DESCRIPTION
    Nodes A and C read the same Sonarr library, so they hold the very same episode file and
    offer it with the same BLAKE3 hash. Node B has its own Sonarr, which knows the series and has
    no file. B trusts both A and C.

    The script checks that B sees two copies of one file, that B's Sonarr is shown one result for
    it, and that when the node B is fetching from stops in the middle, the transfer carries on
    from the other node, from where it had got to, and Sonarr imports the right file.

    All three nodes run on this machine with relays off. A's and C's uploads are slowed so that
    there is a middle to act in. Needs Docker, ffmpeg and the transport sidecar, as S7 does.
    Everything lives in .run/copies next to this script and in the nodearr-copies compose project.

.PARAMETER TransportBin
    Path to the built nodearr-transport binary.

.PARAMETER KeepRunning
    Leave the nodes and the containers running afterwards.

.PARAMETER Down
    Stop the nodes and remove the containers, then exit.

.PARAMETER UploadLimitKbps
    How fast A and C may send, in kilobytes per second.

.PARAMETER NodearrDll
    Path to a built Nodearr.Console.dll, when it is not the one in _output.

.PARAMETER FirstPort
    Node A's port. B takes the next one, and C the one after.
#>
[CmdletBinding()]
param(
    [string] $TransportBin,
    [switch] $KeepRunning,
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
$run = Join-Path $root '.run/copies'
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

. (Join-Path $root 'Library.ps1')

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
    A = @{ Port = $FirstPort; Data = Join-Path $run 'a'; Key = New-Key; Downloads = Join-Path $run 'downloads-a-node'; UploadLimit = $UploadLimitKbps }
    B = @{ Port = $FirstPort + 1; Data = Join-Path $run 'b'; Key = New-Key; Downloads = $downloadsB; UploadLimit = 0 }
    C = @{ Port = $FirstPort + 2; Data = Join-Path $run 'c'; Key = New-Key; Downloads = Join-Path $run 'downloads-c-node'; UploadLimit = $UploadLimitKbps }
}
$arr = @{
    sonarr  = @{ Url = 'http://127.0.0.1:18989'; Key = New-Key }
    sonarrB = @{ Url = 'http://127.0.0.1:28989'; Key = New-Key }
}

function Invoke-Compose {
    docker compose -p nodearr-copies @composeFiles --env-file $envFile @args
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
    param([string] $BaseUrl, [string] $Key, [string] $Method, [string] $Path, $Body)

    $request = @{ Uri = "$BaseUrl$Path"; Method = $Method; Headers = @{ 'X-Api-Key' = $Key }; SkipHttpErrorCheck = $true; StatusCodeVariable = 'status' }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 30); $request.ContentType = 'application/json' }

    $response = Invoke-RestMethod @request
    if ($status -ge 400) { throw "$Method $BaseUrl$Path -> $status $($response | ConvertTo-Json -Depth 6 -Compress)" }
    return $response
}

function Invoke-Node { param([string] $Name, [string] $Method, [string] $Path, $Body) Invoke-Http "http://127.0.0.1:$($nodes[$Name].Port)/api/v5" $nodes[$Name].Key $Method $Path $Body }
function Invoke-Arr { param([string] $App, [string] $Method, [string] $Path, $Body) Invoke-Http "$($arr[$App].Url)/api/v3" $arr[$App].Key $Method $Path $Body }

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

    # On one machine they would all go by its name. Here each is called what the script calls it.
    $env:Nodearr__Transport__NodeName = $Name

    $process = Start-Process dotnet -ArgumentList "`"$dll`"", '-nobrowser', "-data=`"$($node.Data)`"" -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $run "$Name.out.log") -RedirectStandardError (Join-Path $run "$Name.err.log")
    $process.Id | Set-Content (Join-Path $run "$Name.pid")

    Wait-Until -What "node $Name and its transport" -TimeoutSec 90 -Condition {
        try { (Invoke-Node $Name GET '/nodes/identity').transportRunning } catch { $false }
    } | Out-Null
}

function Add-Application {
    param([string] $Node, [string] $App, [string] $SyncLevel)

    $definition = Invoke-Node $Node GET '/applications/schema' | Where-Object implementation -eq 'Sonarr'
    foreach ($pair in @{ nodearrUrl = "http://host.docker.internal:$($nodes[$Node].Port)"; baseUrl = $arr[$App].Url; apiKey = $arr[$App].Key }.GetEnumerator()) {
        ($definition.fields | Where-Object name -eq $pair.Key) | Add-Member -NotePropertyName value -NotePropertyValue $pair.Value -Force
    }
    $definition | Add-Member -Force -NotePropertyMembers @{ name = 'Sonarr'; syncLevel = $SyncLevel }
    return (Invoke-Node $Node POST '/applications' $definition).id
}

function Get-Transfers { return @(Invoke-Node B GET '/transfers') }

$results = [System.Collections.Generic.List[object]]::new()

function Assert-That {
    param([string] $Name, [bool] $Passed, $Evidence)
    $results.Add([ordered]@{ check = $Name; passed = $Passed; evidence = $Evidence })
    Write-Host ("[{0}] {1}" -f ($Passed ? 'PASS' : 'FAIL'), $Name) -ForegroundColor ($Passed ? 'Green' : 'Red')
    if (-not $Passed) { Write-Host ("       " + ($Evidence | ConvertTo-Json -Depth 6 -Compress)) -ForegroundColor DarkGray }
}

# --- Fresh state -------------------------------------------------------------------------------

New-Item -ItemType Directory -Force $run, $media | Out-Null
'A', 'B', 'C' | ForEach-Object { Stop-Node $_ }

if ($Down) {
    if (Test-Path $envFile) { Invoke-Compose down -v 2>&1 | Out-Null }
    Write-Host 'Stopped the nodes and removed the containers.'
    return
}

@("SONARR_API_KEY=$($arr.sonarr.Key)", "RADARR_API_KEY=$(New-Key)", "PROWLARR_API_KEY=$(New-Key)",
  "SONARR_B_API_KEY=$($arr.sonarrB.Key)", "RADARR_B_API_KEY=$(New-Key)",
  "LIBRARY_DIR=$($library -replace '\\', '/')", "DOWNLOADS_DIR=$($downloads -replace '\\', '/')", "DOWNLOADS_B_DIR=$($downloadsB -replace '\\', '/')") | Set-Content $envFile

$tvMedia = Join-Path $media 'episode-720p.mkv'
if (-not (Test-Path $tvMedia)) {
    ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=1280x720:rate=24,noise=alls=30:allf=t' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 120 `
        -c:v libx264 -preset ultrafast -pix_fmt yuv420p -b:v 1400k -maxrate 1800k -bufsize 3600k -c:a aac -b:a 64k $tvMedia
    if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }
}
$tvHash = (Get-FileHash $tvMedia -Algorithm SHA256).Hash

Invoke-Compose down -v 2>&1 | Out-Null
foreach ($dir in $nodes.A.Data, $nodes.B.Data, $nodes.C.Data, $downloads, $downloadsB, $library) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $nodes.A.Data, $nodes.B.Data, $nodes.C.Data, (Join-Path $downloads 'complete'), (Join-Path $downloadsB 'complete'), (Join-Path $library 'tv') | Out-Null

$episodeName = 'Pingu.S01E01.720p.WEB-DL.AAC2.0.H.264-NODEARR'

try {
    'A', 'B', 'C' | ForEach-Object { Start-Node $_ }
    Invoke-Compose up -d sonarr sonarr-b 2>&1 | Out-Null
    foreach ($app in $arr.Keys) {
        Wait-Until -What "$app API" -TimeoutSec 240 -Condition { try { Invoke-Arr $app GET '/system/status' } catch { $null } } | Out-Null
    }

    # A keeps Nodearr registered in the shared Sonarr. C only reads it.
    Add-Application A sonarr 'fullSync' | Out-Null
    Add-Application C sonarr 'addOnly' | Out-Null
    Add-Application B sonarrB 'fullSync' | Out-Null
    Wait-Until -What "B's download client in its Sonarr" -Condition { Invoke-Arr sonarrB GET '/downloadclient' | Where-Object name -eq 'Nodearr' | Select-Object -First 1 } | Out-Null

    foreach ($name in 'A', 'C') {
        Invoke-Node $name POST '/remotepathmapping' @{ host = '127.0.0.1'; remotePath = '/tv/'; localPath = (Join-Path $library 'tv') + [IO.Path]::DirectorySeparatorChar } | Out-Null
    }
    $completeB = (Join-Path $downloadsB 'complete') + [IO.Path]::DirectorySeparatorChar
    Invoke-Arr sonarrB POST '/remotepathmapping' @{ host = 'host.docker.internal'; remotePath = $completeB; localPath = '/downloads/complete/' } | Out-Null

    foreach ($container in 'nodearr-copies-sonarr-1', 'nodearr-copies-sonarr-b-1') { docker exec $container chown abc:abc /tv | Out-Null }

    $series = @{}
    foreach ($app in 'sonarr', 'sonarrB') {
        Invoke-Arr $app POST '/rootfolder' @{ path = '/tv' } | Out-Null
        $found = (Invoke-Arr $app GET '/series/lookup?term=tvdb:75669')[0]
        $found | Add-Member -Force -NotePropertyMembers @{
            qualityProfileId = 1; rootFolderPath = '/tv'; monitored = $true; seasonFolder = $true
            addOptions = @{ monitor = 'all'; searchForMissingEpisodes = $false; searchForCutoffUnmetEpisodes = $false }
        }
        $series[$app] = Invoke-Arr $app POST '/series' $found
    }
    $episodeB = Wait-Until -What "the episode list in B's Sonarr" -Condition {
        Invoke-Arr sonarrB GET "/episode?seriesId=$($series.sonarrB.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -eq 1 } | Select-Object -First 1
    }
    Wait-Until -What "the episode list in A's Sonarr" -Condition { @(Invoke-Arr sonarr GET "/episode?seriesId=$($series.sonarr.id)").Count -gt 1 } | Out-Null

    Import-ArrDownload -ApiUrl "$($arr.sonarr.Url)/api/v3" -ApiKey $arr.sonarr.Key -App Sonarr -SourceFile $tvMedia -Name $episodeName -DownloadsDir $downloads
    Wait-Until -What "A's Sonarr to hold the episode" -TimeoutSec 60 -Condition { @(Invoke-Arr sonarr GET "/episodefile?seriesId=$($series.sonarr.id)").Count -eq 1 } | Out-Null

    'A', 'C', 'B' | ForEach-Object { Invoke-NodeCommand $_ @{ name = 'CatalogRefresh' } }

    # B trusts both, and both share everything with it.
    foreach ($name in 'A', 'C') {
        $invite = Invoke-Node $name POST '/nodes/invites' @{ validHours = 1; label = 'copies'; shareAll = $true }
        Invoke-Node B POST '/nodes/invites/redeem' @{ code = $invite.code } | Out-Null
    }

    # --- 1. The same file, twice --------------------------------------------------------------------

    foreach ($name in 'A', 'C') {
        Wait-Until -What "$name to hash what it shares" -TimeoutSec 90 -Condition { (Invoke-Node $name GET '/sharing/hashing').hashed -eq 1 } | Out-Null
    }
    $copies = Wait-Until -What 'B to hold both copies with their hash' -TimeoutSec 60 -Condition {
        Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
        $items = @((Invoke-Node B GET '/catalog/peers/items?pageSize=50').records)
        if ($items.Count -eq 2 -and @($items | Where-Object blake3).Count -eq 2) { $items }
    }
    Assert-That 'Two nodes holding the same file offer it with the same hash' (
        @($copies.blake3 | Select-Object -Unique).Count -eq 1 -and @($copies.peerName | Select-Object -Unique).Count -eq 2
    ) @{ copies = @($copies | ForEach-Object { "$($_.peerName): $($_.blake3)" }) }

    # --- 2. One result in Sonarr ------------------------------------------------------------------------

    $offered = @(Invoke-Arr sonarrB GET "/release?episodeId=$($episodeB.id)" | Where-Object indexer -eq 'Nodearr')
    Assert-That 'Sonarr is shown the file once, not once for each node that has it' (
        $offered.Count -eq 1 -and $offered[0].approved
    ) @{ results = @($offered | ForEach-Object title); approved = $offered[0].approved; rejections = @($offered[0].rejections) }

    # --- 3. The node it comes from stops part way -------------------------------------------------------

    Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodeB.id) } | Out-Null
    $before = Wait-Until -What 'the transfer to be under way' -TimeoutSec 60 -EverySec 0.5 -Condition {
        Get-Transfers | Where-Object { $_.state -eq 'downloading' -and $_.bytesDone -ge 4MB } | Select-Object -First 1
    }
    $firstNode = $before.peerName
    $stopped = $firstNode
    Stop-Node $stopped
    $heldAtStop = (Get-Transfers | Where-Object downloadId -eq $before.downloadId).bytesDone

    $samples = [System.Collections.Generic.List[long]]::new()
    $moved = Wait-Until -What 'the transfer to carry on from the other node' -TimeoutSec 180 -EverySec 0.5 -Condition {
        $now = Get-Transfers | Where-Object downloadId -eq $before.downloadId | Select-Object -First 1
        if ($now -and $now.peerId -ne $before.peerId) { $samples.Add([long]$now.bytesDone) }
        if ($now -and $now.peerId -ne $before.peerId -and $now.state -eq 'downloading' -and $now.bytesDone -gt $heldAtStop) { $now }
    }

    $imported = Wait-Until -What "B's Sonarr to import the episode" -TimeoutSec 240 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $episode = Invoke-Arr sonarrB GET "/episode/$($episodeB.id)"
        if ($episode.hasFile) { $episode }
    }
    $fileB = Invoke-Arr sonarrB GET "/episodefile/$($imported.episodeFileId)"
    $landed = docker exec nodearr-copies-sonarr-b-1 sha256sum $fileB.path
    $failedInSonarr = @(Invoke-Arr sonarrB GET "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object eventType -eq 'downloadFailed').Count
    $lowest = ($samples | Measure-Object -Minimum).Minimum
    Assert-That 'When the node it comes from stops, the transfer carries on from the other node from where it was, and Sonarr imports the right file' (
        $firstNode -in 'A', 'C' -and $moved.peerId -ne $before.peerId -and $moved.peerName -in 'A', 'C' -and $moved.failureMessage -match 'Carrying on from' -and
        $lowest -ge $heldAtStop -and "$landed" -match $tvHash -and $failedInSonarr -eq 0
    ) @{ from = $firstNode; stopped = $stopped; to = $moved.peerName; message = $moved.failureMessage; heldAtStop = $heldAtStop; lowestAfter = $lowest; hashMatches = ("$landed" -match $tvHash); failedInSonarr = $failedInSonarr }
}
catch {
    Write-Host "Stopped: $_" -ForegroundColor Red
    $results.Add([ordered]@{ check = 'Script ran to the end'; passed = $false; evidence = "$_" })
}
finally {
    $failed = @($results | Where-Object { -not $_.passed })
    [ordered]@{ ranAt = (Get-Date).ToUniversalTime().ToString('o'); passed = ($failed.Count -eq 0 -and $results.Count -gt 0); checks = $results } |
        ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'results.json')

    if (-not $KeepRunning) {
        'A', 'B', 'C' | ForEach-Object { Stop-Node $_ }
        Invoke-Compose down -v 2>&1 | Out-Null
    }
}

Write-Host "`n$($results.Count - $failed.Count) of $($results.Count) checks passed. Details: $(Join-Path $run 'results.json')"
if ($failed.Count -gt 0) { exit 1 }
