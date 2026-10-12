#requires -Version 7
<#
.SYNOPSIS
    S8 acceptance: Nodearr as a Docker image, deployed twice, doing the whole job and failing the
    right way when it should.

.DESCRIPTION
    Builds the image and runs two complete deployments from acceptance/docker-compose.s8.yml:
    John's (nodearr-a, with Sonarr and Radarr holding a library) and Ryan's (nodearr-b, with a
    Sonarr and Radarr that have the same series and movie and no files). Nothing runs on the
    host. Each side shares one /downloads volume, and each Nodearr reads its side's libraries
    through read-only mounts.

    The script then checks the deployment itself, the full flow through Sonarr and Radarr, and
    five things that must fail or recover correctly:

      - the container is replaced in the middle of a transfer,
      - a file is asked for after it was hidden,
      - a file changes on the sender after its hash was fixed,
      - the direct route between the nodes goes away, leaving only a relay,
      - the sender blocks the receiver in the middle of a transfer.

    Needs Docker and ffmpeg. Everything lives in .run/s8 next to this script and in the
    nodearr-s8 compose project.

.PARAMETER SkipBuild
    Use the nodearr:s8 and nodearr-test-relay images that are already built.

.PARAMETER KeepRunning
    Leave the containers up afterwards. Node A is on port 6633 and node B on 6634.

.PARAMETER Down
    Remove the containers and their volumes, then exit.
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild,
    [switch] $KeepRunning,
    [switch] $Down
)

$ErrorActionPreference = 'Stop'
# Version 1 only: the APIs leave null properties out of their JSON, and reading one must give $null.
Set-StrictMode -Version 1

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '..')
$run = Join-Path $root '.run/s8'
$composeFile = Join-Path $root 'docker-compose.s8.yml'
$envFile = Join-Path $run '.env'
$media = Join-Path $run 'media'

. (Join-Path $root 'Library.ps1')

function New-Key { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }

function Invoke-Compose {
    docker compose -f $composeFile --env-file $envFile @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($args -join ' ') failed" }
}

New-Item -ItemType Directory -Force $run, $media | Out-Null

if ($Down) {
    if (Test-Path $envFile) { Invoke-Compose down -v 2>&1 | Out-Null }
    Write-Host 'Removed the containers and their volumes.'
    return
}

$keys = @{ A = New-Key; B = New-Key; C = New-Key; sonarrA = New-Key; radarrA = New-Key; sonarrB = New-Key; radarrB = New-Key }
$keys | ConvertTo-Json | Set-Content (Join-Path $run 'keys.json')
@("NODEARR_A_API_KEY=$($keys.A)", "NODEARR_B_API_KEY=$($keys.B)", "NODEARR_C_API_KEY=$($keys.C)",
  "SONARR_A_API_KEY=$($keys.sonarrA)", "RADARR_A_API_KEY=$($keys.radarrA)",
  "SONARR_B_API_KEY=$($keys.sonarrB)", "RADARR_B_API_KEY=$($keys.radarrB)") | Set-Content $envFile

# How each thing is reached from this machine, and what it is called inside the deployment.
$nodes = @{
    A = @{ Url = 'http://127.0.0.1:6633'; Key = $keys.A; Inside = 'http://nodearr-a:6633'; Container = 'nodearr-s8-nodearr-a-1' }
    B = @{ Url = 'http://127.0.0.1:6634'; Key = $keys.B; Inside = 'http://nodearr-b:6633'; Container = 'nodearr-s8-nodearr-b-1' }

    # Set up wrongly on purpose. See the compose file.
    C = @{ Url = 'http://127.0.0.1:6635'; Key = $keys.C; Inside = 'http://nodearr-c:6633'; Container = 'nodearr-s8-nodearr-c-1' }
}
$arr = @{
    sonarrA = @{ Url = 'http://127.0.0.1:18989'; Key = $keys.sonarrA; Inside = 'http://sonarr-a:8989'; Container = 'nodearr-s8-sonarr-a-1' }
    radarrA = @{ Url = 'http://127.0.0.1:17878'; Key = $keys.radarrA; Inside = 'http://radarr-a:7878'; Container = 'nodearr-s8-radarr-a-1' }
    sonarrB = @{ Url = 'http://127.0.0.1:28989'; Key = $keys.sonarrB; Inside = 'http://sonarr-b:8989'; Container = 'nodearr-s8-sonarr-b-1' }
    radarrB = @{ Url = 'http://127.0.0.1:27878'; Key = $keys.radarrB; Inside = 'http://radarr-b:7878'; Container = 'nodearr-s8-radarr-b-1' }
}
$directNetwork = 'nodearr-s8_wan'
$relayContainer = 'nodearr-s8-relay-1'

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

function Invoke-Node { param([string] $Name, [string] $Method, [string] $Path, $Body) Invoke-Http "$($nodes[$Name].Url)/api/v5" $nodes[$Name].Key $Method $Path $Body }
function Invoke-Arr { param([string] $App, [string] $Method, [string] $Path, $Body, [switch] $AllowError) Invoke-Http "$($arr[$App].Url)/api/v3" $arr[$App].Key $Method $Path $Body -AllowError:$AllowError }

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

function Wait-Node {
    param([string] $Name)
    Wait-Until -What "node $Name and its transport" -TimeoutSec 120 -Condition {
        try { $identity = Invoke-Node $Name GET '/nodes/identity'; if ($identity.transportRunning) { $identity } } catch { $null }
    }
}

function Add-Application {
    param([string] $Node, [string] $Implementation, [string] $App, [string] $SyncLevel = 'fullSync')

    $definition = Invoke-Node $Node GET '/applications/schema' | Where-Object implementation -eq $Implementation
    foreach ($pair in @{ nodearrUrl = $nodes[$Node].Inside; baseUrl = $arr[$App].Inside; apiKey = $arr[$App].Key }.GetEnumerator()) {
        ($definition.fields | Where-Object name -eq $pair.Key) | Add-Member -NotePropertyName value -NotePropertyValue $pair.Value -Force
    }
    $definition | Add-Member -Force -NotePropertyMembers @{ name = $Implementation; syncLevel = $SyncLevel }
    return (Invoke-Node $Node POST '/applications' $definition).id
}

# Node B's transfers. Empty, not an error, while B cannot be reached for a moment, which
# happens when its container is replaced or its networks change.
# The health messages a node shows on System > Status, keyed by the check they come from.
function Get-Health {
    param([string] $Name)
    $health = @{}
    foreach ($item in @(Invoke-Node $Name GET '/health')) { $health[$item.source] = $item.message }
    return $health
}

# Asks one node to check its connection to its first peer, and returns what it found.
function Test-Connection-To-Peer {
    param([string] $Name)
    $peer = Invoke-Node $Name GET '/nodes/peers' | Select-Object -First 1
    return Invoke-Node $Name POST "/nodes/peers/$($peer.id)/ping"
}

function Get-Transfers {
    try { return @(Invoke-Node B GET '/transfers') } catch { return @() }
}

function Wait-Transferring {
    param([long] $AtLeast = 1)
    return Wait-Until -What 'a transfer to be under way' -TimeoutSec 90 -EverySec 0.5 -Condition {
        Get-Transfers | Where-Object { $_.state -eq 'downloading' -and $_.bytesDone -ge $AtLeast } | Select-Object -First 1
    }
}

function Wait-Failed {
    param([string] $DownloadId, [int] $TimeoutSec = 60)
    return Wait-Until -What 'the transfer to fail' -TimeoutSec $TimeoutSec -EverySec 0.5 -Condition {
        Get-Transfers | Where-Object { $_.state -eq 'failed' -and (-not $DownloadId -or $_.downloadId -eq $DownloadId) } | Select-Object -First 1
    }
}

# What B's Sonarr recorded when a download failed.
function Wait-SonarrFailure {
    param([string] $DownloadId)
    return Wait-Until -What 'Sonarr to record the failure' -TimeoutSec 120 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $event = Invoke-Arr sonarrB GET "/history/series?seriesId=$($series.sonarrB.id)" | Where-Object { $_.eventType -eq 'downloadFailed' -and $_.downloadId -eq $DownloadId } | Select-Object -First 1
        if ($event) { $event.data.message }
    }
}

function Search-Episode { param([int] $Index) Invoke-Arr sonarrB POST '/command' @{ name = 'EpisodeSearch'; episodeIds = @($episodesB[$Index].id) } | Out-Null }

# Fetches the ticket for one of B's episodes, as B's Sonarr would when it grabs.
function Get-Ticket {
    param([int] $Index, [string] $Path)
    $release = @(Invoke-Arr sonarrB GET "/release?episodeId=$($episodesB[$Index].id)" | Where-Object indexer -eq 'Nodearr')[0]
    if (-not $release) { throw "B's Sonarr found nothing for episode $($Index + 1)" }
    $candidateId = $release.guid -replace '^nodearr:', ''
    Invoke-WebRequest "$($nodes.B.Url)/newznab/api?t=get&id=$candidateId&apikey=$($nodes.B.Key)" -OutFile $Path
}

# Hands a ticket to B's download client, as B's Sonarr would.
function Add-Ticket {
    param([string] $Path)
    return Invoke-RestMethod "$($nodes.B.Url)/sabnzbd/api?mode=addfile&cat=tv&priority=-100&output=json&apikey=$($nodes.B.Key)" -Method Post -Form @{ name = Get-Item $Path }
}

function Remove-Transfer { param([string] $DownloadId) Invoke-Node B DELETE "/transfers/$DownloadId" | Out-Null }

function Get-RelayBytes {
    $text = (docker exec $relayContainer curl -s http://localhost:9090/metrics) -join "`n"
    $value = { param($name) if ($text -match "(?m)^$name(?:\{[^}]*\})?\s+(\d+)") { [long]$Matches[1] } else { 0 } }
    return (& $value 'relayserver_bytes_sent_total') + (& $value 'relayserver_bytes_recv_total')
}

function Get-FilesUnder {
    param([string] $Container, [string] $Path)
    return @(docker exec $Container sh -c "find '$Path' -type f 2>/dev/null")
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

# --- Images and fresh state --------------------------------------------------------------------

if (-not $SkipBuild) {
    Write-Host 'Building the Nodearr image...'
    docker build -q -t nodearr:s8 $repo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The Nodearr image did not build' }

    Write-Host 'Building the test relay image...'
    Get-Content (Join-Path $root 'relay.Dockerfile') -Raw | docker build -q -t nodearr-test-relay - | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The test relay image did not build' }
}

$tvMedia = Join-Path $media 'episode-720p.mkv'
$movieMedia = Join-Path $media 'movie-720p.mkv'
New-Media $tvMedia 120 'testsrc2=size=1280x720:rate=24,noise=alls=30:allf=t' '-b:v 1400k -maxrate 1800k -bufsize 3600k'
New-Media $movieMedia 660 'smptebars=size=1280x720:rate=5' '-crf 30'
$tvHash = (Get-FileHash $tvMedia -Algorithm SHA256).Hash
$movieHash = (Get-FileHash $movieMedia -Algorithm SHA256).Hash
$tvSize = (Get-Item $tvMedia).Length

$episodeNames = 1..6 | ForEach-Object { 'Pingu.S01E{0:00}.720p.WEB-DL.AAC2.0.H.264-NODEARR' -f $_ }
$movieName = 'Night.of.the.Living.Dead.1968.720p.WEB-DL.AAC2.0.H.264-NODEARR'

Invoke-Compose down -v 2>&1 | Out-Null

try {
    Invoke-Compose up -d 2>&1 | Out-Null

    $identityA = Wait-Node A
    $identityB = Wait-Node B
    foreach ($app in $arr.Keys) {
        Wait-Until -What "$app API" -TimeoutSec 240 -Condition { try { Invoke-Arr $app GET '/system/status' } catch { $null } } | Out-Null
    }

    # --- 1. The deployment itself ----------------------------------------------------------------

    $owner = docker exec $nodes.A.Container stat -c '%u:%g %a' /config/node.key
    $runsAs = (docker exec $nodes.A.Container sh -c "ps -eo uid,comm | awk '`$2 ~ /^(dotnet|nodearr-transpo)/ {print `$1}' | sort -u") -join ','
    Assert-That 'The container runs Nodearr and its sidecar as the chosen user, not root, and keeps the key private' (
        "$owner" -eq '1000:1000 600' -and $runsAs -eq '1000' -and $identityA.name -eq 'John' -and $identityB.name -eq 'Ryan'
    ) @{ keyOwnerAndMode = "$owner"; processesRunAs = $runsAs; names = @($identityA.name, $identityB.name) }

    foreach ($app in 'sonarrA', 'sonarrB') { docker exec $arr[$app].Container chown abc:abc /tv | Out-Null }
    foreach ($app in 'radarrA', 'radarrB') { docker exec $arr[$app].Container chown abc:abc /movies | Out-Null }

    docker exec -u 1000:1000 $nodes.A.Container sh -c 'touch /tv/should-not-be-possible' 2>&1 | Out-Null
    $libraryWritable = $LASTEXITCODE -eq 0
    docker exec -u 1000:1000 $nodes.A.Container sh -c 'touch /downloads/.write-test && rm /downloads/.write-test' 2>&1 | Out-Null
    $downloadsWritable = $LASTEXITCODE -eq 0
    Assert-That 'Nodearr can write to /downloads and cannot write to the libraries' ($downloadsWritable -and -not $libraryWritable) @{ downloadsWritable = $downloadsWritable; libraryWritable = $libraryWritable }

    # --- Each node registers itself in its own Arr apps, by container name -----------------------

    Add-Application A Sonarr sonarrA | Out-Null
    Add-Application A Radarr radarrA | Out-Null
    Add-Application B Sonarr sonarrB | Out-Null
    Add-Application B Radarr radarrB | Out-Null
    foreach ($app in $arr.Keys) {
        Wait-Until -What "Nodearr's download client in $app" -Condition { Invoke-Arr $app GET '/downloadclient' | Where-Object name -eq 'Nodearr' | Select-Object -First 1 } | Out-Null
    }

    $series = @{}
    $movie = @{}
    foreach ($app in 'sonarrA', 'sonarrB') {
        Invoke-Arr $app POST '/rootfolder' @{ path = '/tv' } | Out-Null
        $found = (Invoke-Arr $app GET '/series/lookup?term=tvdb:75669')[0]
        $found | Add-Member -Force -NotePropertyMembers @{
            qualityProfileId = 1; rootFolderPath = '/tv'; monitored = $true; seasonFolder = $true
            addOptions = @{ monitor = 'all'; searchForMissingEpisodes = $false; searchForCutoffUnmetEpisodes = $false }
        }
        $series[$app] = Invoke-Arr $app POST '/series' $found
    }
    foreach ($app in 'radarrA', 'radarrB') {
        Invoke-Arr $app POST '/rootfolder' @{ path = '/movies' } | Out-Null
        $found = (Invoke-Arr $app GET '/movie/lookup?term=tmdb:10331')[0]
        $found | Add-Member -Force -NotePropertyMembers @{ qualityProfileId = 1; rootFolderPath = '/movies'; monitored = $true; minimumAvailability = 'released'; addOptions = @{ searchForMovie = $false } }
        $movie[$app] = Invoke-Arr $app POST '/movie' $found
    }

    $episodesB = Wait-Until -What "the episode list in B's Sonarr" -Condition {
        $all = @(Invoke-Arr sonarrB GET "/episode?seriesId=$($series.sonarrB.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -le 6 })
        if ($all.Count -eq 6) { $all | Sort-Object episodeNumber }
    }
    Wait-Until -What "the episode list in A's Sonarr" -Condition { @(Invoke-Arr sonarrA GET "/episode?seriesId=$($series.sonarrA.id)").Count -gt 6 } | Out-Null

    foreach ($name in $episodeNames) {
        Import-ArrDownload -ApiUrl "$($arr.sonarrA.Url)/api/v3" -ApiKey $arr.sonarrA.Key -App Sonarr -SourceFile $tvMedia -Name $name -Container $arr.sonarrA.Container
    }
    Import-ArrDownload -ApiUrl "$($arr.radarrA.Url)/api/v3" -ApiKey $arr.radarrA.Key -App Radarr -SourceFile $movieMedia -Name $movieName -Container $arr.radarrA.Container
    Wait-Until -What "A's Sonarr to hold six episode files" -TimeoutSec 90 -Condition { @(Invoke-Arr sonarrA GET "/episodefile?seriesId=$($series.sonarrA.id)").Count -eq 6 } | Out-Null
    Wait-Until -What "A's Radarr to hold the movie" -TimeoutSec 90 -Condition { (Invoke-Arr radarrA GET "/movie/$($movie.radarrA.id)").hasFile } | Out-Null

    Invoke-NodeCommand A @{ name = 'CatalogRefresh' }
    Invoke-NodeCommand B @{ name = 'CatalogRefresh' }

    $localFiles = @((Invoke-Node A GET '/catalog/local/episodefile?pageSize=50').records)
    $mappings = @(Invoke-Node A GET '/remotepathmapping')
    Assert-That 'With the libraries mounted at the same paths, Nodearr reads them with no path mappings' (
        $localFiles.Count -eq 6 -and @($localFiles | Where-Object readable).Count -eq 6 -and $mappings.Count -eq 0
    ) @{ files = $localFiles.Count; readable = @($localFiles | Where-Object readable).Count; pathMappings = $mappings.Count }

    $invite = Invoke-Node A POST '/nodes/invites' @{ validHours = 1; label = 's8'; shareAll = $true }
    Invoke-Node B POST '/nodes/invites/redeem' @{ code = $invite.code } | Out-Null
    Wait-Until -What "B to hold A's seven files" -TimeoutSec 60 -Condition { (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 7 } | Out-Null
    $bOnA = Invoke-Node A GET '/nodes/peers' | Select-Object -First 1
    $aOnB = Invoke-Node B GET '/nodes/peers' | Select-Object -First 1
    Assert-That 'The two deployments pair, and know each other by the names they were given' (
        $bOnA.state -eq 'trusted' -and $aOnB.state -eq 'trusted' -and $bOnA.name -eq 'Ryan' -and $aOnB.name -eq 'John'
    ) @{ aSees = "$($bOnA.name) $($bOnA.state)"; bSees = "$($aOnB.name) $($aOnB.state)" }

    $checked = Test-Connection-To-Peer B
    Assert-That 'A connection check says the two nodes reach each other directly' (
        $checked.connectionPath -eq 'direct' -and $checked.connectionAddress -and -not $checked.lastError
    ) @{ path = $checked.connectionPath; address = $checked.connectionAddress; error = $checked.lastError }

    # --- Settings in a container --------------------------------------------------------------------

    # What the image and the compose file give in the environment is fixed. The rest is set in
    # the UI, and changing it restarts the transport without costing the node its trust.
    $settingsB = Invoke-Node B GET '/settings/node'
    $settingsB.uploadLimitKbps = 8192
    $savedB = Invoke-Node B PUT '/settings/node' $settingsB
    $afterRestart = Wait-Until -What 'the nodes to reach each other after the restart' -TimeoutSec 60 -EverySec 3 -Condition {
        try { $check = Test-Connection-To-Peer B; if ($check.connectionPath -eq 'direct') { $check } } catch { $null }
    }
    $sidecarB = (docker exec $nodes.B.Container sh -c "ps -eo args | grep '[n]odearr-transport'") -join ' '
    Assert-That 'In a container the mounts and the port are fixed, the rest is set in the UI, and a change keeps the node trusted' (
        (@($settingsB.fixedByEnvironment) -join ',') -eq 'DirectTimeoutSeconds,DownloadFolder,NodeName,Port,PortMapping,Relay,RelayUrl' -and
        $settingsB.downloadFolder -eq '/downloads' -and $settingsB.port -eq 6633 -and
        $savedB.uploadLimitKbps -eq 8192 -and $savedB.transportRestarted -and $savedB.transportRunning -and
        $sidecarB -match '--upload-limit-kbps 8192' -and $sidecarB -match '--bind-port 6633' -and $afterRestart.state -eq 'trusted'
    ) @{ fixed = @($settingsB.fixedByEnvironment); restarted = $savedB.transportRestarted; running = $savedB.transportRunning; sidecar = ($sidecarB -replace '^\S+\s*', ''); path = $afterRestart.connectionPath }

    # --- A node set up wrongly is told so ----------------------------------------------------------

    Test-Connection-To-Peer A | Out-Null
    $goodHealth = Wait-Until -What "A's health checks to settle" -TimeoutSec 30 -Condition { Start-Sleep -Seconds 8; @{ Health = (Get-Health A) } }
    Wait-Node C | Out-Null
    Add-Application C Sonarr sonarrA 'addOnly' | Out-Null
    Invoke-NodeCommand C @{ name = 'CatalogRefresh' }
    $badHealth = Wait-Until -What 'the misconfigured node to be warned' -TimeoutSec 90 -EverySec 3 -Condition {
        $health = Get-Health C
        if ($health.LibraryReadableCheck -and $health.ApplicationDownloadFolderCheck) { $health }
    }
    Assert-That 'A node that cannot read the library, or whose Sonarr cannot see its downloads, is told so. A node set up properly shows no warnings at all.' (
        $goodHealth.Health.Count -eq 0 -and
        $badHealth.LibraryReadableCheck -match '/tv/' -and $badHealth.ApplicationDownloadFolderCheck -match '/downloads/complete'
    ) @{ goodNodeWarnings = @($goodHealth.Health.Values); library = $badHealth.LibraryReadableCheck; downloads = $badHealth.ApplicationDownloadFolderCheck }

    # --- 2. The full flow ------------------------------------------------------------------------

    Search-Episode 0
    $first = Wait-Transferring 1
    $imported = Wait-Until -What "B's Sonarr to import episode 1" -TimeoutSec 240 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $episode = Invoke-Arr sonarrB GET "/episode/$($episodesB[0].id)"
        if ($episode.hasFile) { $episode }
    }
    $fileB = Invoke-Arr sonarrB GET "/episodefile/$($imported.episodeFileId)"
    $landed = docker exec $arr.sonarrB.Container sha256sum $fileB.path
    $remoteMappings = @(Invoke-Arr sonarrB GET '/remotepathmapping')
    Assert-That "A grab in Ryan's Sonarr is fetched from John's node and imported, byte for byte, with no path mapping in Sonarr" (
        "$landed" -match $tvHash -and $fileB.size -eq $tvSize -and $remoteMappings.Count -eq 0 -and $first.peerName -eq 'John'
    ) @{ path = $fileB.path; hashMatches = ("$landed" -match $tvHash); sonarrPathMappings = $remoteMappings.Count; from = $first.peerName }

    Invoke-Arr radarrB POST '/command' @{ name = 'MoviesSearch'; movieIds = @($movie.radarrB.id) } | Out-Null
    $film = Wait-Until -What "B's Radarr to import the movie" -TimeoutSec 240 -EverySec 2 -Condition {
        Invoke-Arr radarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $current = Invoke-Arr radarrB GET "/movie/$($movie.radarrB.id)"
        if ($current.hasFile) { $current }
    }
    $filmLanded = docker exec $arr.radarrB.Container sha256sum $film.movieFile.path
    Assert-That "A grab in Ryan's Radarr ends the same way" ("$filmLanded" -match $movieHash) @{ path = $film.movieFile.path; hashMatches = ("$filmLanded" -match $movieHash) }

    Wait-Until -What 'the finished downloads to be cleared' -TimeoutSec 90 -Condition { @(Invoke-Node B GET '/transfers').Count -eq 0 } | Out-Null

    # --- 3. The container is replaced in the middle of a transfer --------------------------------

    Search-Episode 1
    $before = Wait-Transferring (4 * 1024 * 1024)
    Invoke-Compose up -d --force-recreate nodearr-b 2>&1 | Out-Null
    $identityAfter = Wait-Node B

    $samples = [System.Collections.Generic.List[long]]::new()
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
        $current = Get-Transfers | Where-Object downloadId -eq $before.downloadId | Select-Object -First 1
        if ($current) { $samples.Add([long]$current.bytesDone) }
        Start-Sleep -Milliseconds 250
    }
    $lowest = ($samples | Measure-Object -Minimum).Minimum
    $importedTwo = Wait-Until -What "B's Sonarr to import episode 2" -TimeoutSec 240 -EverySec 2 -Condition {
        Invoke-Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $episode = Invoke-Arr sonarrB GET "/episode/$($episodesB[1].id)"
        if ($episode.hasFile) { $episode }
    }
    $fileTwo = Invoke-Arr sonarrB GET "/episodefile/$($importedTwo.episodeFileId)"
    $landedTwo = docker exec $arr.sonarrB.Container sha256sum $fileTwo.path
    $stillTrusted = (Invoke-Node B GET '/nodes/peers' | Select-Object -First 1).state
    Assert-That 'A new container on the same volumes is the same node, still trusted, and carries the transfer on' (
        $identityAfter.nodeId -eq $identityB.nodeId -and $stillTrusted -eq 'trusted' -and $samples.Count -gt 5 -and
        $lowest -ge $before.bytesDone -and "$landedTwo" -match $tvHash
    ) @{ sameNodeId = ($identityAfter.nodeId -eq $identityB.nodeId); trust = $stillTrusted; knownBefore = $before.bytesDone; lowestSeenAfter = $lowest; hashMatches = ("$landedTwo" -match $tvHash) }

    Wait-Until -What 'the finished download to be cleared' -TimeoutSec 90 -Condition { @(Invoke-Node B GET '/transfers').Count -eq 0 } | Out-Null

    # --- 4. A file is asked for after it was hidden ----------------------------------------------

    # Ryan's node holds a ticket for episode 4. It is then frozen, so that it cannot hear that
    # John hid the series, and woken to use the ticket. Only John's node can stop this.
    $ticket = Join-Path $run 'ticket-hidden.nzb'
    Get-Ticket 3 $ticket
    $titles = Invoke-Node A GET '/sharing/titles'
    $pingu = $titles | Where-Object title -eq 'Pingu'
    $hidden = Invoke-Node A GET '/sharing/lists' | Where-Object isHidden

    docker pause $nodes.B.Container | Out-Null
    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @(@{ mediaKind = 'tv'; applicationId = $pingu.applicationId; arrId = $pingu.arrId }); peerIds = @() } | Out-Null
    Start-Sleep -Seconds 30
    docker unpause $nodes.B.Container | Out-Null

    $stillListed = (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords
    $added = Add-Ticket $ticket
    $denied = Wait-Failed $added.nzo_ids[0] 60
    $refusedBySender = @(docker logs --since 2m $nodes.A.Container 2>&1 | Select-String 'Refused .* a file it is not offered').Count
    Assert-That "A file hidden on John's node is refused by John's node, even to a node that still holds a ticket for it" (
        $added.status -and $stillListed -ge 6 -and $denied.failure -eq 'notAuthorized' -and $denied.bytesDone -eq 0 -and $refusedBySender -ge 1
    ) @{ ticketAccepted = $added.status; stillListedOnB = $stillListed; failure = $denied.failure; bytesDone = $denied.bytesDone; refusalsLoggedByA = $refusedBySender; message = $denied.failureMessage }

    Remove-Transfer $denied.downloadId
    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @(); peerIds = @() } | Out-Null
    Wait-Until -What "B to hold A's seven files again" -TimeoutSec 60 -Condition { (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 7 } | Out-Null

    # --- 5. A file changes on the sender after its hash was fixed --------------------------------

    # A first request makes John's node work out the hash of episode 3. It is cancelled, and one
    # byte of the file is then changed without changing its size or its time.
    $ticket = Join-Path $run 'ticket-corrupt.nzb'
    Get-Ticket 2 $ticket
    $warm = Add-Ticket $ticket
    Wait-Transferring 1 | Out-Null
    Remove-Transfer $warm.nzo_ids[0]

    $target = "/tv/Pingu/Season 1/$($episodeNames[2]).mkv"
    docker exec $arr.sonarrA.Container sh -c "touch -r '$target' /tmp/when && printf '\377' | dd of='$target' bs=1 seek=5000000 conv=notrunc 2>/dev/null && touch -r /tmp/when '$target'" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to change the file on node A' }

    Search-Episode 2
    $corrupt = Wait-Failed $null 120
    $corruptMessage = Wait-SonarrFailure $corrupt.downloadId
    $leftBehind = @(Get-FilesUnder $nodes.B.Container '/downloads/complete') + @(Get-FilesUnder $nodes.B.Container '/downloads/incomplete')
    Assert-That 'A file that does not match its hash is thrown away, never reaches Sonarr, and Sonarr is told why' (
        $corrupt.failure -eq 'verificationFailed' -and $corruptMessage -match 'VerificationFailed' -and $leftBehind.Count -eq 0 -and
        -not (Invoke-Arr sonarrB GET "/episode/$($episodesB[2].id)").hasFile
    ) @{ failure = $corrupt.failure; sonarrSays = $corruptMessage; filesLeft = $leftBehind.Count }
    Get-Transfers | ForEach-Object { Remove-Transfer $_.downloadId }

    # --- 6. The direct route goes away, and only the relay is left -------------------------------

    $relayBefore = Get-RelayBytes
    docker network disconnect $directNetwork $nodes.B.Container | Out-Null
    Start-Sleep -Seconds 3

    # Node B's published port drops for a moment when its networks change.
    $relayOnly = Wait-Until -What 'node B to answer after its network changed' -TimeoutSec 60 -EverySec 2 -Condition {
        try { Test-Connection-To-Peer B } catch { $null }
    }
    $relayWarning = Wait-Until -What 'node B to warn that it has no direct route' -TimeoutSec 30 -EverySec 2 -Condition {
        try { (Get-Health B).PeerNetworkCheck } catch { $null }
    }

    $askedAt = [datetime]$relayOnly.connectionChecked

    Search-Episode 4
    $unroutable = Wait-Failed $null 180

    # The check a failed attempt asks for runs on its own and takes up to ten seconds.
    $afterFailure = $null
    $giveUp = (Get-Date).AddSeconds(40)
    do {
        $afterFailure = Invoke-Node B GET '/nodes/peers' | Select-Object -First 1
        if (([datetime]$afterFailure.connectionChecked) -gt $askedAt) { break }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $giveUp)
    $relayAfter = Get-RelayBytes
    $unroutableMessage = Wait-SonarrFailure $unroutable.downloadId
    $leftBehind = @(Get-FilesUnder $nodes.B.Container '/downloads/complete') + @(Get-FilesUnder $nodes.B.Container '/downloads/incomplete')
    $relayed = $relayAfter - $relayBefore
    Assert-That 'With only a relay between the nodes the transfer fails as NoDirectRoute, and no file data goes through the relay' (
        $unroutable.failure -eq 'noDirectRoute' -and $unroutable.bytesDone -eq 0 -and $unroutable.attempts -eq 3 -and $leftBehind.Count -eq 0 -and
        $relayed -gt 0 -and $relayed -lt 1MB -and $unroutableMessage -match 'NoDirectRoute' -and
        $relayOnly.connectionPath -eq 'relay' -and -not $relayOnly.lastError
    ) @{ connectionCheckSaid = $relayOnly.connectionPath; failure = $unroutable.failure; attempts = $unroutable.attempts; bytesDone = $unroutable.bytesDone; fileSize = $tvSize; bytesThroughRelay = $relayed; sonarrSays = $unroutableMessage }

    Assert-That 'The failed transfer has the connection checked again unasked, and tells Sonarr what the check found and where to look' (
        ([datetime]$afterFailure.connectionChecked) -gt $askedAt -and $afterFailure.connectionPath -eq 'relay' -and
        $unroutableMessage -match 'reached John only through a relay' -and $unroutableMessage -match 'firewall'
    ) @{ checkedWhenAsked = $askedAt.ToString('o'); checkedAfterFailure = ([datetime]$afterFailure.connectionChecked).ToString('o'); pathAfterFailure = $afterFailure.connectionPath; sonarrSays = $unroutableMessage }

    Get-Transfers | ForEach-Object { Remove-Transfer $_.downloadId }
    docker network connect $directNetwork $nodes.B.Container | Out-Null
    Wait-Until -What 'the nodes to reach each other again' -TimeoutSec 60 -EverySec 3 -Condition {
        try { (Test-Connection-To-Peer B).connectionPath -eq 'direct' } catch { $false }
    } | Out-Null
    $warningGone = Wait-Until -What 'the warning to clear once the route is direct again' -TimeoutSec 30 -EverySec 2 -Condition {
        try { -not (Get-Health B).PeerNetworkCheck } catch { $false }
    }
    Assert-That 'While only a relay is left the node warns on System > Status, names the other node and the firewall, and stops warning once the route is direct' (
        $relayWarning -match 'No direct route to' -and $relayWarning -match 'firewall' -and $relayWarning -match 'NoDirectRoute' -and $warningGone
    ) @{ warning = $relayWarning; clearedAfterDirect = [bool]$warningGone }

    # --- 7. The sender blocks the receiver in the middle of a transfer ---------------------------

    Search-Episode 5
    $doomed = Wait-Transferring (2 * 1024 * 1024)
    Invoke-Node A POST "/nodes/peers/$($bOnA.id)/block" | Out-Null
    $blocked = Wait-Failed $doomed.downloadId 60
    $blockedMessage = Wait-SonarrFailure $doomed.downloadId
    Invoke-Node B POST '/command' @{ name = 'PeerCatalogSync' } | Out-Null
    $afterBlock = Wait-Until -What "B to lose A's catalog" -TimeoutSec 60 -Condition {
        if ((Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 0) { @{ Items = 0 } }
    }
    Assert-That 'When John blocks Ryan mid-transfer the transfer is cut, Sonarr is told, and Ryan is offered nothing more' (
        $blocked.failure -in 'revoked', 'blocked' -and $blocked.bytesDone -lt $blocked.size -and $blockedMessage -match 'Revoked|Blocked' -and $afterBlock.Items -eq 0
    ) @{ failure = $blocked.failure; bytesDone = $blocked.bytesDone; size = $blocked.size; sonarrSays = $blockedMessage; catalogItemsLeft = $afterBlock.Items }

    # --- 8. It contacts nothing it was not told to -----------------------------------------------

    # Node C has logged every request it made since it started, minutes ago. It was told about
    # one thing: John's Sonarr.
    $log = (docker exec $nodes.C.Container sh -c 'cat /config/logs/*.txt') -join "`n"
    $outbound = @([regex]::Matches($log, '(?m)\|HttpClient\|Req: \[\w+\] https?://([^/:\s]+)') | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() })
    $hosts = @($outbound | Sort-Object -Unique)
    $unexpected = @($hosts | Where-Object { $_ -ne 'sonarr-a' })
    Assert-That 'Left alone for minutes, a node makes no request to anything but the Sonarr it was given' (
        $outbound.Count -gt 0 -and $unexpected.Count -eq 0
    ) @{ requestsMade = $outbound.Count; hostsContacted = $hosts; unexpected = $unexpected }
}
catch {
    Write-Host "Stopped: $_" -ForegroundColor Red
    $results.Add([ordered]@{ check = 'Script ran to the end'; passed = $false; evidence = "$_" })
}
finally {
    $failedChecks = @($results | Where-Object { -not $_.passed })
    [ordered]@{ ranAt = (Get-Date).ToUniversalTime().ToString('o'); passed = ($failedChecks.Count -eq 0 -and $results.Count -gt 0); checks = $results } |
        ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'results.json')

    if ($failedChecks.Count -gt 0) {
        # Kept for looking into what went wrong.
        foreach ($name in 'A', 'B') { docker logs $nodes[$name].Container 2>&1 | Set-Content (Join-Path $run "node-$name.log") }
    }

    if (-not $KeepRunning) { Invoke-Compose down -v 2>&1 | Out-Null }
}

Write-Host "`n$($results.Count - $failedChecks.Count) of $($results.Count) checks passed. Details: $(Join-Path $run 'results.json')"
if ($failedChecks.Count -gt 0) { exit 1 }
