#requires -Version 7
<#
.SYNOPSIS
    S1 spike: proves the Arr contract end to end against pinned Sonarr, Radarr and Prowlarr containers.

.DESCRIPTION
    Starts a fresh Nodearr (the stub Newznab and SABnzbd endpoints in src/Nodearr.Http/ArrContractSpike)
    on the host and fresh Arr containers, configures everything through the Arr APIs, then runs six
    scenarios: search, grab and real import for one episode and one movie directly, the same through
    Prowlarr, and a failed transfer for each that Arr must blocklist.

    Everything it creates lives in .run/ next to this script and in the nodearr-s1 compose project.

.PARAMETER Down
    Stop Nodearr, remove the containers and their volumes, and delete .run/ (API keys and media included).

.PARAMETER KeepRunning
    Leave Nodearr and the containers running afterwards so they can be inspected.
#>
[CmdletBinding()]
param(
    [switch] $Down,
    [switch] $KeepRunning,
    [int] $NodearrPort = 8789
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '../..')
$run = Join-Path $root '.run'
$compose = Join-Path $root 'docker-compose.yml'
$envFile = Join-Path $run '.env'
$pidFile = Join-Path $run 'nodearr.pid'
$nodearrData = Join-Path $run 'nodearr-data'
$downloads = Join-Path $run 'downloads'
$peerLibrary = Join-Path $run 'peer-library'
$tvMedia = Join-Path $peerLibrary 'episode-720p.mkv'
$movieMedia = Join-Path $peerLibrary 'movie-720p.mkv'
$transcript = Join-Path $nodearrData 'arr-contract-transcript.jsonl'

# A series with 5 minute episodes keeps the test media small. See New-Media below.
$tvdbId = 75669
$seriesSlug = 'Pingu'
$movies = @(
    @{ Key = 'direct'; TmdbId = 10331; Title = 'Night.of.the.Living.Dead.1968' },
    @{ Key = 'prowlarr'; TmdbId = 10513; Title = 'Plan.9.from.Outer.Space.1957' },
    @{ Key = 'fail'; TmdbId = 3085; Title = 'His.Girl.Friday.1940' }
)

function Stop-Nodearr {
    if (Test-Path $pidFile) {
        $nodearrPid = [int](Get-Content $pidFile)
        Get-Process -Id $nodearrPid -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
        Remove-Item $pidFile -Force
    }
}

function Invoke-Compose {
    docker compose -f $compose --env-file $envFile @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($args -join ' ') failed" }
}

if ($Down) {
    Stop-Nodearr
    if (Test-Path $envFile) { Invoke-Compose down -v }
    if (Test-Path $run) { Remove-Item $run -Recurse -Force }
    return
}

# --- Fresh state -------------------------------------------------------------------------------

New-Item -ItemType Directory -Force $run, $peerLibrary | Out-Null

if (-not (Test-Path $envFile)) {
    $newKey = { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }
    @('SONARR', 'RADARR', 'PROWLARR', 'NODEARR') | ForEach-Object { "$($_)_API_KEY=$(& $newKey)" } | Set-Content $envFile
}

$keys = @{}
Get-Content $envFile | Where-Object { $_ -match '=' } | ForEach-Object { $k, $v = $_ -split '=', 2; $keys[$k.Trim()] = $v.Trim() }

function New-Media {
    # Synthetic 720p test pattern. Arr imports only real video: it probes the file and rejects samples.
    param([string] $Path, [int] $Seconds, [string] $Source, [string] $VideoArgs)

    if (Test-Path $Path) { return }
    Write-Host "Generating $(Split-Path $Path -Leaf) with ffmpeg..."
    $encode = $VideoArgs -split ' '
    ffmpeg -hide_banner -loglevel error -y `
        -f lavfi -i $Source `
        -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t $Seconds `
        -c:v libx264 -preset ultrafast -pix_fmt yuv420p @encode -c:a aac -b:a 64k $Path
    if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }
}

# Episode: Sonarr rejects a WEBDL-720p release under 3 MB per minute of episode runtime (15 MB for a
# 5 minute episode) and treats a file under 90 seconds as a sample. Two minutes at about 1.4 Mbit/s.
# The noise filter stops the encoder compressing the pattern away to nothing.
New-Media $tvMedia 120 'testsrc2=size=1280x720:rate=24,noise=alls=30:allf=t' '-b:v 1400k -maxrate 1800k -bufsize 3600k'
# Movie: Radarr has no minimum size by default, but a feature film under 10 minutes is a sample.
New-Media $movieMedia 660 'smptebars=size=1280x720:rate=5' '-crf 30'

Stop-Nodearr
Invoke-Compose down -v 2>&1 | Out-Null
foreach ($dir in $nodearrData, $downloads) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $nodearrData, (Join-Path $downloads 'complete'), (Join-Path $downloads 'incomplete') | Out-Null

# --- Helpers -----------------------------------------------------------------------------------

$apps = @{
    sonarr   = @{ Url = 'http://127.0.0.1:18989/api/v3'; Key = $keys.SONARR_API_KEY; Container = 'nodearr-s1-sonarr-1' }
    radarr   = @{ Url = 'http://127.0.0.1:17878/api/v3'; Key = $keys.RADARR_API_KEY; Container = 'nodearr-s1-radarr-1' }
    prowlarr = @{ Url = 'http://127.0.0.1:19696/api/v1'; Key = $keys.PROWLARR_API_KEY; Container = 'nodearr-s1-prowlarr-1' }
}

function Invoke-Arr {
    param([string] $App, [string] $Method, [string] $Path, $Body)

    $request = @{
        Uri = "$($apps[$App].Url)$Path"; Method = $Method; Headers = @{ 'X-Api-Key' = $apps[$App].Key }
        SkipHttpErrorCheck = $true; StatusCodeVariable = 'status'
    }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 30); $request.ContentType = 'application/json' }

    $response = Invoke-RestMethod @request
    if ($status -ge 400) { throw "$App $Method $Path -> $status $($response | ConvertTo-Json -Depth 6 -Compress)" }

    return $response
}

function Wait-Until {
    param([scriptblock] $Condition, [int] $TimeoutSec = 120, [string] $What, [double] $EverySec = 2)

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $value = & $Condition
        if ($value) { return $value }
        Start-Sleep -Seconds $EverySec
    }
    throw "Timed out after ${TimeoutSec}s waiting for $What"
}

function Invoke-ArrCommand {
    param([string] $App, [hashtable] $Command, [int] $TimeoutSec = 120)

    $queued = Invoke-Arr $App POST '/command' $Command
    Wait-Until -What "$App command $($Command.name)" -TimeoutSec $TimeoutSec -EverySec 1 -Condition {
        $state = Invoke-Arr $App GET "/command/$($queued.id)"
        if ($state.status -eq 'failed') { throw "$App command $($Command.name) failed: $($state.message)" }
        $state.status -eq 'completed'
    } | Out-Null
}

function Set-Fields {
    param($Resource, [hashtable] $Values)

    foreach ($name in $Values.Keys) {
        $field = $Resource.fields | Where-Object name -eq $name
        if (-not $field) { throw "Schema for $($Resource.implementation) has no field '$name'" }
        $field | Add-Member -NotePropertyName value -NotePropertyValue $Values[$name] -Force
    }
}

function Get-Transcript {
    if (-not (Test-Path $transcript)) { return @() }
    return @(Get-Content $transcript | ForEach-Object { $_ | ConvertFrom-Json })
}

$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param([string] $Scenario, [bool] $Passed, [hashtable] $Evidence, [int] $TranscriptFrom)

    $calls = @(Get-Transcript | Select-Object -Skip $TranscriptFrom | ForEach-Object {
        $query = ($_.query.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join '&'
        "$(($_.client -split '/')[0]) $($_.method) $($_.path)?$query => $($_.summary)"
    })
    $results.Add([ordered]@{ scenario = $Scenario; passed = $Passed; evidence = $Evidence; nodearrCalls = $calls })
    Write-Host ("[{0}] {1}" -f ($Passed ? 'PASS' : 'FAIL'), $Scenario) -ForegroundColor ($Passed ? 'Green' : 'Red')
}

# --- Start Nodearr and the containers ----------------------------------------------------------

$dll = Join-Path $repo '_output/net10.0/Nodearr.Console.dll'
if (-not (Test-Path $dll)) { throw "Build Nodearr first: dotnet build src/Nodearr.sln" }

$env:Nodearr__Server__Port = $NodearrPort
$env:Nodearr__Auth__ApiKey = $keys.NODEARR_API_KEY
$env:Nodearr__Auth__Method = 'External'
$env:Nodearr__Log__AnalyticsEnabled = 'false'

$nodearr = Start-Process dotnet -ArgumentList "`"$dll`"", '-nobrowser', "-data=`"$nodearrData`"" -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $run 'nodearr.out.log') -RedirectStandardError (Join-Path $run 'nodearr.err.log')
$nodearr.Id | Set-Content $pidFile

Invoke-Compose up -d 2>&1 | Out-Null

$nodearrUrl = "http://127.0.0.1:$NodearrPort"
Wait-Until -What 'Nodearr /ping' -Condition { try { (Invoke-RestMethod "$nodearrUrl/ping" -TimeoutSec 2).status -eq 'OK' } catch { $false } } | Out-Null
foreach ($app in $apps.Keys) {
    Wait-Until -What "$app API" -TimeoutSec 180 -Condition { try { Invoke-Arr $app GET '/system/status' } catch { $null } } | Out-Null
}

$versions = [ordered]@{
    nodearr  = (Invoke-RestMethod "$nodearrUrl/api/v5/system/status" -Headers @{ 'X-Api-Key' = $keys.NODEARR_API_KEY }).version
    sonarr   = (Invoke-Arr sonarr GET '/system/status').version
    radarr   = (Invoke-Arr radarr GET '/system/status').version
    prowlarr = (Invoke-Arr prowlarr GET '/system/status').version
}
Write-Host "Versions: $($versions | ConvertTo-Json -Compress)"

# --- Library items in Sonarr and Radarr --------------------------------------------------------

# Named volumes start out owned by root. The linuxserver images run the app as user abc.
docker exec $apps.sonarr.Container chown abc:abc /tv
docker exec $apps.radarr.Container chown abc:abc /movies

Invoke-Arr sonarr POST '/rootfolder' @{ path = '/tv' } | Out-Null
$series = (Invoke-Arr sonarr GET "/series/lookup?term=tvdb:$tvdbId")[0]
$series | Add-Member -Force -NotePropertyMembers @{
    qualityProfileId = 1; rootFolderPath = '/tv'; monitored = $true; seasonFolder = $true
    addOptions = @{ monitor = 'all'; searchForMissingEpisodes = $false; searchForCutoffUnmetEpisodes = $false }
}
$series = Invoke-Arr sonarr POST '/series' $series
$episodes = Wait-Until -What 'Sonarr episode list' -Condition {
    $all = @(Invoke-Arr sonarr GET "/episode?seriesId=$($series.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -le 3 })
    if ($all.Count -eq 3) { $all | Sort-Object episodeNumber }
}

Invoke-Arr radarr POST '/rootfolder' @{ path = '/movies' } | Out-Null
foreach ($movie in $movies) {
    $lookup = (Invoke-Arr radarr GET "/movie/lookup?term=tmdb:$($movie.TmdbId)")[0]
    $lookup | Add-Member -Force -NotePropertyMembers @{
        qualityProfileId = 1; rootFolderPath = '/movies'; monitored = $true; minimumAvailability = 'released'
        addOptions = @{ searchForMovie = $false }
    }
    $movie.Radarr = Invoke-Arr radarr POST '/movie' $lookup
}

# --- Candidates Nodearr will offer -------------------------------------------------------------

$published = [datetime]::UtcNow.Date.AddDays(-2)
$candidates = @()
foreach ($episode in $episodes) {
    $n = $episode.episodeNumber
    $candidates += [ordered]@{
        id = "tv-e$n"; kind = 'tv'; category = 5040; tvdbId = $tvdbId; season = 1; episode = $n
        title = "$seriesSlug.S01E0$n.720p.WEB-DL.AAC2.0.H.264-NODEARR"
        sourcePath = $tvMedia; publishedAt = $published.AddMinutes($n).ToString('o')
        fail = ($n -eq 3 ? 'Nodearr transfer failed: NoDirectRoute (no direct path to peer, nothing was relayed)' : $null)
    }
}
foreach ($movie in $movies) {
    $candidates += [ordered]@{
        id = "movie-$($movie.Key)"; kind = 'movie'; category = 2040; tmdbId = $movie.TmdbId; imdbId = $movie.Radarr.imdbId
        title = "$($movie.Title).720p.WEB-DL.AAC2.0.H.264-NODEARR"
        sourcePath = $movieMedia; publishedAt = $published.AddHours(1).AddMinutes($movie.TmdbId % 50).ToString('o')
        fail = ($movie.Key -eq 'fail' ? 'Nodearr transfer failed: VerificationFailed (BLAKE3 mismatch)' : $null)
    }
}
[ordered]@{
    completeDirLocal = (Join-Path $downloads 'complete'); completeDirReported = '/downloads/complete'
    transferSeconds = 8; candidates = $candidates
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $nodearrData 'arr-contract-spike.json')

# --- Register Nodearr as download client and indexer -------------------------------------------

function Register-Nodearr {
    param([string] $App, [string] $CategoryField, [string] $Category, [int[]] $IndexerCategories)

    $mark = @(Get-Transcript).Count

    $client = Invoke-Arr $App GET '/downloadclient/schema' | Where-Object implementation -eq 'Sabnzbd'
    Set-Fields $client @{ host = 'host.docker.internal'; port = $NodearrPort; urlBase = 'sabnzbd'; apiKey = $keys.NODEARR_API_KEY; $CategoryField = $Category }
    $client | Add-Member -Force -NotePropertyMembers @{ name = 'Nodearr'; enable = $true; removeCompletedDownloads = $true; removeFailedDownloads = $true }
    $client = Invoke-Arr $App POST '/downloadclient' $client

    $indexer = Invoke-Arr $App GET '/indexer/schema' | Where-Object implementation -eq 'Newznab' | Select-Object -First 1
    Set-Fields $indexer @{ baseUrl = "http://host.docker.internal:$NodearrPort/newznab"; apiPath = '/api'; apiKey = $keys.NODEARR_API_KEY; categories = $IndexerCategories }
    $indexer | Add-Member -Force -NotePropertyMembers @{
        name = 'Nodearr'; enableRss = $false; enableAutomaticSearch = $true; enableInteractiveSearch = $true; downloadClientId = $client.id
    }
    $indexer = Invoke-Arr $App POST '/indexer' $indexer

    Add-Result "$App accepts Nodearr as SABnzbd download client and Newznab indexer (connection tests run on save)" $true @{
        downloadClientId = $client.id; indexerId = $indexer.id; indexerPinnedToDownloadClient = ($indexer.downloadClientId -eq $client.id)
        indexerEnableRss = $indexer.enableRss
    } $mark

    return @{ Client = $client; Indexer = $indexer }
}

$sonarrReg = Register-Nodearr sonarr 'tvCategory' 'tv' @(5030, 5040)
$radarrReg = Register-Nodearr radarr 'movieCategory' 'movies' @(2030, 2040)

# --- Scenarios ---------------------------------------------------------------------------------

function Get-Item-Config {
    param([string] $App)

    if ($App -eq 'sonarr') {
        return @{ IdParam = 'episodeId'; Search = { param($id) @{ name = 'EpisodeSearch'; episodeIds = @($id) } }
                  HasFile = { param($id) $e = Invoke-Arr sonarr GET "/episode/$id"; if ($e.hasFile) { (Invoke-Arr sonarr GET "/episodefile/$($e.episodeFileId)").path } } }
    }
    return @{ IdParam = 'movieId'; Search = { param($id) @{ name = 'MoviesSearch'; movieIds = @($id) } }
              HasFile = { param($id) $m = Invoke-Arr radarr GET "/movie/$id"; if ($m.hasFile) { $m.movieFile.path } } }
}

function Get-History {
    param([string] $App, [string] $IdParam, [int] $Id)

    # Radarr's paged /history ignores movieId. Its per-movie endpoint returns a plain array.
    if ($App -eq 'radarr') { return @(Invoke-Arr radarr GET "/history/movie?movieId=$Id" | Sort-Object date) }
    return @((Invoke-Arr $App GET "/history?$IdParam=$Id&pageSize=50&sortKey=date&sortDirection=ascending").records)
}

function Watch-Queue {
    # Polls the Arr queue, nudging Arr to refresh its download client state instead of waiting a minute.
    param([string] $App, [scriptblock] $Done, [int] $TimeoutSec = 180, [string] $What)

    $seen = [System.Collections.Generic.List[string]]::new()
    $value = Wait-Until -What $What -TimeoutSec $TimeoutSec -EverySec 3 -Condition {
        Invoke-Arr $App POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        foreach ($record in (Invoke-Arr $App GET '/queue?pageSize=50&includeUnknownSeriesItems=true&includeUnknownMovieItems=true').records) {
            $state = "$($record.status)/$($record.trackedDownloadState)"
            if ($seen.Count -eq 0 -or $seen[-1] -ne $state) { $seen.Add($state) }
        }
        & $Done
    }
    return @{ Value = $value; QueueStates = @($seen) }
}

function Test-Import {
    param([string] $App, [string] $Via, [int] $Id, [string] $CandidateTitle, [bool] $Interactive)

    $cfg = Get-Item-Config $App
    $mark = @(Get-Transcript).Count
    $evidence = @{}

    if ($Interactive) {
        $release = Invoke-Arr $App GET "/release?$($cfg.IdParam)=$Id" | Where-Object title -eq $CandidateTitle | Select-Object -First 1
        if (-not $release) { throw "$App interactive search did not return $CandidateTitle" }
        $evidence.release = @{
            guid = $release.guid; indexer = $release.indexer; protocol = $release.protocol; quality = $release.quality.quality.name
            size = $release.size; approved = $release.approved; rejections = @($release.rejections)
        }
        Invoke-Arr $App POST '/release' @{ guid = $release.guid; indexerId = $release.indexerId } | Out-Null
    }
    else {
        Invoke-ArrCommand $App (& $cfg.Search $Id)
    }

    $watch = Watch-Queue $App -What "$App import of $CandidateTitle" -Done { & $cfg.HasFile $Id }
    $path = $watch.Value
    $listing = docker exec $apps[$App].Container ls -l $path
    $events = @(Get-History $App $cfg.IdParam $Id | ForEach-Object { $_.eventType })
    $grabbed = Get-History $App $cfg.IdParam $Id | Where-Object eventType -eq 'grabbed' | Select-Object -First 1

    $evidence += @{
        via = $Via; grab = ($Interactive ? 'interactive search then manual grab' : 'automatic search command')
        queueStatesSeen = $watch.QueueStates; historyEvents = $events
        downloadId = $grabbed.downloadId; downloadClient = $grabbed.data.downloadClient; grabbedFromIndexer = $grabbed.data.indexer
        importedPath = $path; fileInLibrary = "$listing"
    }

    $passed = ($LASTEXITCODE -eq 0) -and ($events -contains 'grabbed') -and ($events -contains 'downloadFolderImported') -and ($grabbed.downloadId -like 'SABnzbd_nzo_*')
    Add-Result "$App via ${Via}: search, grab, transfer and real import of $CandidateTitle" $passed $evidence $mark
}

function Test-Failure {
    param([string] $App, [int] $Id, [string] $CandidateTitle)

    $cfg = Get-Item-Config $App
    $mark = @(Get-Transcript).Count

    Invoke-ArrCommand $App (& $cfg.Search $Id)
    $watch = Watch-Queue $App -What "$App failed download of $CandidateTitle" -Done {
        Get-History $App $cfg.IdParam $Id | Where-Object eventType -eq 'downloadFailed' | Select-Object -First 1
    }
    $failed = $watch.Value

    $blocklist = Wait-Until -What "$App blocklist entry" -TimeoutSec 30 -Condition {
        (Invoke-Arr $App GET '/blocklist?pageSize=50').records | Where-Object sourceTitle -eq $CandidateTitle | Select-Object -First 1
    }

    # Arr searches again after a failure. The same release must now be rejected instead of grabbed again.
    Start-Sleep -Seconds 5
    $release = Invoke-Arr $App GET "/release?$($cfg.IdParam)=$Id" | Where-Object title -eq $CandidateTitle | Select-Object -First 1
    $grabs = @(Get-History $App $cfg.IdParam $Id | Where-Object eventType -eq 'grabbed').Count
    $stillTracked = @((Invoke-Arr $App GET '/queue?pageSize=50').records).Count

    $evidence = @{
        queueStatesSeen = $watch.QueueStates; failedMessage = $failed.data.message; blocklistEntry = @{
            sourceTitle = $blocklist.sourceTitle; protocol = $blocklist.protocol; indexer = $blocklist.indexer; message = $blocklist.message
        }
        releaseAfterFailure = @{ rejected = $release.rejected; rejections = @($release.rejections) }
        grabCount = $grabs; hasFile = [bool](& $cfg.HasFile $Id); arrQueueLengthAfter = $stillTracked
    }
    $passed = $release.rejected -and (@($release.rejections) -join ' ' -match 'lock') -and ($grabs -eq 1) -and (-not $evidence.hasFile)
    Add-Result "$App failure path: failed transfer of $CandidateTitle is reported, blocklisted and not grabbed again" $passed $evidence $mark
}

$movieId = @{}; foreach ($movie in $movies) { $movieId[$movie.Key] = $movie.Radarr.id }
$title = @{}; foreach ($c in $candidates) { $title[$c.id] = $c.title }

Test-Import sonarr 'direct Newznab' $episodes[0].id $title['tv-e1'] $false
Test-Import radarr 'direct Newznab' $movieId['direct'] $title['movie-direct'] $false
Test-Failure sonarr $episodes[2].id $title['tv-e3']
Test-Failure radarr $movieId['fail'] $title['movie-fail']

# --- The same through Prowlarr -----------------------------------------------------------------

Invoke-Arr sonarr DELETE "/indexer/$($sonarrReg.Indexer.id)" | Out-Null
Invoke-Arr radarr DELETE "/indexer/$($radarrReg.Indexer.id)" | Out-Null

$mark = @(Get-Transcript).Count
$prowlarrIndexer = Invoke-Arr prowlarr GET '/indexer/schema' | Where-Object { $_.implementation -eq 'Newznab' -and $_.name -eq 'Generic Newznab' } | Select-Object -First 1
Set-Fields $prowlarrIndexer @{ baseUrl = "http://host.docker.internal:$NodearrPort/newznab"; apiPath = '/api'; apiKey = $keys.NODEARR_API_KEY }
$prowlarrIndexer | Add-Member -Force -NotePropertyMembers @{ name = 'Nodearr'; enable = $true; appProfileId = 1 }
$prowlarrIndexer = Invoke-Arr prowlarr POST '/indexer' $prowlarrIndexer

foreach ($app in 'sonarr', 'radarr') {
    $application = Invoke-Arr prowlarr GET '/applications/schema' | Where-Object implementation -eq $app
    $port = ($app -eq 'sonarr' ? 8989 : 7878)
    Set-Fields $application @{ prowlarrUrl = 'http://prowlarr:9696'; baseUrl = "http://${app}:$port"; apiKey = $apps[$app].Key }
    $application | Add-Member -Force -NotePropertyMembers @{ name = $app; syncLevel = 'fullSync' }
    Invoke-Arr prowlarr POST '/applications' $application | Out-Null
}
Invoke-ArrCommand prowlarr @{ name = 'ApplicationIndexerSync'; forceSync = $true }

$synced = @{}
foreach ($app in 'sonarr', 'radarr') {
    $synced[$app] = Wait-Until -What "Prowlarr to sync its Nodearr indexer into $app" -Condition {
        Invoke-Arr $app GET '/indexer' | Where-Object { $_.name -like '*Prowlarr*' } | Select-Object -First 1
    }
}
Add-Result 'Prowlarr accepts Nodearr as a Generic Newznab indexer and syncs it to Sonarr and Radarr' $true @{
    prowlarrIndexerId = $prowlarrIndexer.id; prowlarrCapabilities = @($prowlarrIndexer.capabilities.categories | ForEach-Object { "$($_.id) $($_.name)" })
    sonarrIndexer = @{ name = $synced.sonarr.name; baseUrl = ($synced.sonarr.fields | Where-Object name -eq baseUrl).value; downloadClientId = $synced.sonarr.downloadClientId }
    radarrIndexer = @{ name = $synced.radarr.name; baseUrl = ($synced.radarr.fields | Where-Object name -eq baseUrl).value; downloadClientId = $synced.radarr.downloadClientId }
} $mark

Test-Import sonarr 'Prowlarr' $episodes[1].id $title['tv-e2'] $true
Test-Import radarr 'Prowlarr' $movieId['prowlarr'] $title['movie-prowlarr'] $true

# --- Report ------------------------------------------------------------------------------------

$failed = @($results | Where-Object { -not $_.passed })
[ordered]@{
    ranAt = (Get-Date).ToUniversalTime().ToString('o'); versions = $versions
    passed = ($failed.Count -eq 0); scenarios = $results
} | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'results.json')
Copy-Item $transcript (Join-Path $run 'transcript.jsonl') -Force

Write-Host "`n$($results.Count - $failed.Count) of $($results.Count) scenarios passed. Details: $(Join-Path $run 'results.json')"

if (-not $KeepRunning) {
    Stop-Nodearr
    Invoke-Compose down -v 2>&1 | Out-Null
}

if ($failed.Count -gt 0) { exit 1 }
