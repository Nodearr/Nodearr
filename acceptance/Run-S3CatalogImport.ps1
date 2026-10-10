#requires -Version 7
<#
.SYNOPSIS
    S3 acceptance: Arr app registration and catalog import against real Sonarr and Radarr containers.

.DESCRIPTION
    Starts a fresh Nodearr on the host and fresh pinned Sonarr and Radarr containers, then checks:

    - Adding an application in Nodearr registers one Nodearr download client and one pinned Nodearr
      indexer in the Arr app, while Nodearr's catalog is still empty.
    - Files the Arr apps import (through the S1 stub transfer) appear in the local catalog with their
      media identity, including a file that holds two episodes.
    - Path mappings turn the Arr app's paths into paths Nodearr can read.
    - A file removed in the Arr app is marked missing on the next refresh and purged on request.
    - Syncing again changes nothing, and deleting the application removes its catalog.

    Everything lives in .run/ next to this script and in the nodearr-s3 compose project.

.PARAMETER KeepRunning
    Leave Nodearr and the containers running afterwards.

.PARAMETER Down
    Stop everything and delete .run/.
#>
[CmdletBinding()]
param(
    [switch] $KeepRunning,
    [switch] $Down,
    [int] $NodearrPort = 6633
)

$ErrorActionPreference = 'Stop'
# Version 1 only: the API leaves null properties out of its JSON, and reading one must give $null.
Set-StrictMode -Version 1

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '..')
$run = Join-Path $root '.run'
$composeFiles = @('-f', (Join-Path $repo 'spikes/arr-contract/docker-compose.yml'), '-f', (Join-Path $root 'docker-compose.library.yml'))
$envFile = Join-Path $run '.env'
$pidFile = Join-Path $run 'nodearr.pid'
$nodearrData = Join-Path $run 'nodearr-data'
$downloads = Join-Path $run 'downloads'
$library = Join-Path $run 'library'
$peerLibrary = Join-Path $run 'peer-library'

function Stop-Nodearr {
    if (Test-Path $pidFile) {
        Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
        Remove-Item $pidFile -Force
    }
}

function Invoke-Compose {
    docker compose -p nodearr-s3 @composeFiles --env-file $envFile @args
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
Stop-Nodearr

$keys = @{}
if (Test-Path $envFile) {
    Get-Content $envFile | Where-Object { $_ -match '_API_KEY=' } | ForEach-Object { $k, $v = $_ -split '=', 2; $keys[$k.Trim()] = $v.Trim() }
}
foreach ($name in 'SONARR', 'RADARR', 'PROWLARR', 'NODEARR') {
    if (-not $keys["${name}_API_KEY"]) { $keys["${name}_API_KEY"] = -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }
}
@($keys.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) + "LIBRARY_DIR=$($library -replace '\\', '/')", "DOWNLOADS_DIR=$($downloads -replace '\\', '/')" | Set-Content $envFile

function New-Media {
    param([string] $Path, [int] $Seconds, [string] $Source, [string] $VideoArgs)

    if (Test-Path $Path) { return }
    $encode = $VideoArgs -split ' '
    ffmpeg -hide_banner -loglevel error -y -f lavfi -i $Source -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t $Seconds `
        -c:v libx264 -preset ultrafast -pix_fmt yuv420p @encode -c:a aac -b:a 64k $Path
    if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }
}

# Sized to pass Sonarr's minimum size and both apps' sample detection. See spikes/arr-contract.
$tvMedia = Join-Path $peerLibrary 'episode-720p.mkv'
$doubleMedia = Join-Path $peerLibrary 'double-episode-720p.mkv'
$movieMedia = Join-Path $peerLibrary 'movie-720p.mkv'
New-Media $tvMedia 120 'testsrc2=size=1280x720:rate=24,noise=alls=30:allf=t' '-b:v 1400k -maxrate 1800k -bufsize 3600k'
New-Media $doubleMedia 240 'testsrc2=size=1280x720:rate=24,noise=alls=30:allf=t' '-b:v 1400k -maxrate 1800k -bufsize 3600k'
New-Media $movieMedia 660 'smptebars=size=1280x720:rate=5' '-crf 30'

Invoke-Compose down -v 2>&1 | Out-Null
foreach ($dir in $nodearrData, $downloads, $library) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $nodearrData, (Join-Path $downloads 'complete'), (Join-Path $library 'tv'), (Join-Path $library 'movies') | Out-Null

# --- Helpers -----------------------------------------------------------------------------------

$nodearrUrl = "http://127.0.0.1:$NodearrPort"
$apps = @{
    sonarr  = @{ Url = 'http://127.0.0.1:18989/api/v3'; Key = $keys.SONARR_API_KEY }
    radarr  = @{ Url = 'http://127.0.0.1:17878/api/v3'; Key = $keys.RADARR_API_KEY }
    nodearr = @{ Url = "$nodearrUrl/api/v5"; Key = $keys.NODEARR_API_KEY }
}

function Invoke-Api {
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

function Invoke-Command-Api {
    param([string] $App, [hashtable] $Command, [int] $TimeoutSec = 180)

    $queued = Invoke-Api $App POST '/command' $Command
    Wait-Until -What "$App command $($Command.name)" -TimeoutSec $TimeoutSec -EverySec 1 -Condition {
        $state = Invoke-Api $App GET "/command/$($queued.id)"
        if ($state.status -eq 'failed') { throw "$App command $($Command.name) failed: $($state.message)" }
        $state.status -eq 'completed'
    } | Out-Null
}

$results = [System.Collections.Generic.List[object]]::new()

function Assert-That {
    param([string] $Name, [bool] $Passed, $Evidence)

    $results.Add([ordered]@{ check = $Name; passed = $Passed; evidence = $Evidence })
    Write-Host ("[{0}] {1}" -f ($Passed ? 'PASS' : 'FAIL'), $Name) -ForegroundColor ($Passed ? 'Green' : 'Red')
    if (-not $Passed) { Write-Host ("       " + ($Evidence | ConvertTo-Json -Depth 6 -Compress)) -ForegroundColor DarkGray }
}

function Get-Fields {
    param($Provider)
    $map = @{}
    foreach ($field in $Provider.fields) { $map[$field.name] = ($field.PSObject.Properties.Name -contains 'value' ? $field.value : $null) }
    return $map
}

# --- Start -------------------------------------------------------------------------------------

$dll = Join-Path $repo '_output/net10.0/Nodearr.Console.dll'
if (-not (Test-Path $dll)) { throw 'Build Nodearr first: dotnet build src/Nodearr.sln' }

$env:Nodearr__Server__Port = $NodearrPort
$env:Nodearr__Auth__ApiKey = $keys.NODEARR_API_KEY
$env:Nodearr__Auth__Method = 'External'
$env:Nodearr__Log__AnalyticsEnabled = 'false'

$nodearr = Start-Process dotnet -ArgumentList "`"$dll`"", '-nobrowser', "-data=`"$nodearrData`"" -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $run 'nodearr.out.log') -RedirectStandardError (Join-Path $run 'nodearr.err.log')
$nodearr.Id | Set-Content $pidFile

Invoke-Compose up -d sonarr radarr 2>&1 | Out-Null

Wait-Until -What 'Nodearr' -Condition { try { (Invoke-RestMethod "$nodearrUrl/ping" -TimeoutSec 2).status -eq 'OK' } catch { $false } } | Out-Null
foreach ($app in 'sonarr', 'radarr') {
    Wait-Until -What "$app API" -TimeoutSec 180 -Condition { try { Invoke-Api $app GET '/system/status' } catch { $null } } | Out-Null
}

# --- 1. Registration with an empty catalog -----------------------------------------------------

$definitions = @{
    sonarr = @{ Implementation = 'Sonarr'; Url = 'http://127.0.0.1:18989'; Category = 'tv'; CategoryField = 'tvCategory' }
    radarr = @{ Implementation = 'Radarr'; Url = 'http://127.0.0.1:17878'; Category = 'movies'; CategoryField = 'movieCategory' }
}
$schemas = Invoke-Api nodearr GET '/applications/schema'
Assert-That 'Nodearr offers Sonarr and Radarr as applications' (@($schemas.implementation | Sort-Object) -join ',' -eq 'Radarr,Sonarr') @{ implementations = @($schemas.implementation) }

$applicationIds = @{}
foreach ($app in 'sonarr', 'radarr') {
    $definition = $schemas | Where-Object implementation -eq $definitions[$app].Implementation
    foreach ($pair in @{ nodearrUrl = "http://host.docker.internal:$NodearrPort"; baseUrl = $definitions[$app].Url; apiKey = $apps[$app].Key }.GetEnumerator()) {
        ($definition.fields | Where-Object name -eq $pair.Key) | Add-Member -NotePropertyName value -NotePropertyValue $pair.Value -Force
    }
    $definition | Add-Member -Force -NotePropertyMembers @{ name = $definitions[$app].Implementation; syncLevel = 'fullSync' }
    $applicationIds[$app] = (Invoke-Api nodearr POST '/applications' $definition).id

    $registered = Wait-Until -What "Nodearr's indexer in $app" -TimeoutSec 60 -Condition {
        $indexer = Invoke-Api $app GET '/indexer' | Where-Object name -eq 'Nodearr' | Select-Object -First 1
        $client = Invoke-Api $app GET '/downloadclient' | Where-Object name -eq 'Nodearr' | Select-Object -First 1
        if ($indexer -and $client) { @{ Indexer = $indexer; Client = $client } }
    }
    $indexerFields = Get-Fields $registered.Indexer
    $clientFields = Get-Fields $registered.Client

    Assert-That "$app got one Nodearr download client and one pinned Nodearr indexer while the catalog was empty" (
        $registered.Indexer.implementation -eq 'Newznab' -and $registered.Client.implementation -eq 'Sabnzbd' -and
        $registered.Indexer.downloadClientId -eq $registered.Client.id -and -not $registered.Indexer.enableRss -and
        $indexerFields.baseUrl -eq "http://host.docker.internal:$NodearrPort/newznab" -and
        $clientFields.host -eq 'host.docker.internal' -and $clientFields.port -eq $NodearrPort -and $clientFields.urlBase -eq 'sabnzbd' -and
        $clientFields[$definitions[$app].CategoryField] -eq $definitions[$app].Category
    ) @{
        indexer = @{ id = $registered.Indexer.id; downloadClientId = $registered.Indexer.downloadClientId; enableRss = $registered.Indexer.enableRss; baseUrl = $indexerFields.baseUrl; categories = $indexerFields.categories }
        downloadClient = @{ id = $registered.Client.id; host = $clientFields.host; port = $clientFields.port; urlBase = $clientFields.urlBase; category = $clientFields[$definitions[$app].CategoryField] }
    }
}

# --- 2. Put real files in the Arr libraries ----------------------------------------------------

docker exec nodearr-s3-sonarr-1 chown abc:abc /tv | Out-Null
docker exec nodearr-s3-radarr-1 chown abc:abc /movies | Out-Null

Invoke-Api sonarr POST '/rootfolder' @{ path = '/tv' } | Out-Null
$series = (Invoke-Api sonarr GET '/series/lookup?term=tvdb:75669')[0]
$series | Add-Member -Force -NotePropertyMembers @{
    qualityProfileId = 1; rootFolderPath = '/tv'; monitored = $true; seasonFolder = $true
    addOptions = @{ monitor = 'all'; searchForMissingEpisodes = $false; searchForCutoffUnmetEpisodes = $false }
}
$series = Invoke-Api sonarr POST '/series' $series
$episodes = Wait-Until -What 'Sonarr episode list' -Condition {
    $all = @(Invoke-Api sonarr GET "/episode?seriesId=$($series.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -le 5 })
    if ($all.Count -eq 5) { $all | Sort-Object episodeNumber }
}

Invoke-Api radarr POST '/rootfolder' @{ path = '/movies' } | Out-Null
$movie = (Invoke-Api radarr GET '/movie/lookup?term=tmdb:10331')[0]
$movie | Add-Member -Force -NotePropertyMembers @{ qualityProfileId = 1; rootFolderPath = '/movies'; monitored = $true; minimumAvailability = 'released'; addOptions = @{ searchForMovie = $false } }
$movie = Invoke-Api radarr POST '/movie' $movie

# The S1 stub stands in for peers: these are the releases the Arr apps will find and "download".
$published = [datetime]::UtcNow.Date.AddDays(-2)
[ordered]@{
    completeDirLocal = (Join-Path $downloads 'complete'); completeDirReported = '/downloads/complete'; transferSeconds = 2
    candidates = @(
        [ordered]@{ id = 'tv-e1'; kind = 'tv'; category = 5040; tvdbId = 75669; season = 1; episode = 1; title = 'Pingu.S01E01.720p.WEB-DL.AAC2.0.H.264-NODEARR'; sourcePath = $tvMedia; publishedAt = $published.AddMinutes(1).ToString('o') }
        [ordered]@{ id = 'tv-e2'; kind = 'tv'; category = 5040; tvdbId = 75669; season = 1; episode = 2; title = 'Pingu.S01E02.720p.WEB-DL.AAC2.0.H.264-NODEARR'; sourcePath = $tvMedia; publishedAt = $published.AddMinutes(2).ToString('o') }
        [ordered]@{ id = 'tv-e4e5'; kind = 'tv'; category = 5040; tvdbId = 75669; season = 1; episode = 4; title = 'Pingu.S01E04E05.720p.WEB-DL.AAC2.0.H.264-NODEARR'; sourcePath = $doubleMedia; publishedAt = $published.AddMinutes(4).ToString('o') }
        [ordered]@{ id = 'movie-1'; kind = 'movie'; category = 2040; tmdbId = 10331; imdbId = $movie.imdbId; title = 'Night.of.the.Living.Dead.1968.720p.WEB-DL.AAC2.0.H.264-NODEARR'; sourcePath = $movieMedia; publishedAt = $published.AddHours(1).ToString('o') }
    )
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $nodearrData 'arr-contract-spike.json')

Invoke-Command-Api sonarr @{ name = 'EpisodeSearch'; episodeIds = @($episodes[0].id) }
Invoke-Command-Api sonarr @{ name = 'EpisodeSearch'; episodeIds = @($episodes[1].id) }
Invoke-Command-Api sonarr @{ name = 'EpisodeSearch'; episodeIds = @($episodes[3].id) }
Invoke-Command-Api radarr @{ name = 'MoviesSearch'; movieIds = @($movie.id) }

Wait-Until -What 'Sonarr to import three files covering four episodes' -TimeoutSec 240 -EverySec 3 -Condition {
    Invoke-Api sonarr POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
    @(Invoke-Api sonarr GET "/episode?seriesId=$($series.id)" | Where-Object hasFile).Count -eq 4
} | Out-Null
Wait-Until -What 'Radarr to import the movie' -TimeoutSec 240 -EverySec 3 -Condition {
    Invoke-Api radarr POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
    (Invoke-Api radarr GET "/movie/$($movie.id)").hasFile
} | Out-Null

# --- 3. Catalog import -------------------------------------------------------------------------

Invoke-Command-Api nodearr @{ name = 'CatalogRefresh' }

$summary = Invoke-Api nodearr GET '/catalog/local'
$sonarrSummary = $summary | Where-Object implementation -eq 'Sonarr'
$radarrSummary = $summary | Where-Object implementation -eq 'Radarr'
$sonarrEpisodeCount = @(Invoke-Api sonarr GET "/episode?seriesId=$($series.id)").Count

Assert-That 'Catalog summary matches what Sonarr and Radarr hold' (
    $sonarrSummary.series -eq 1 -and $sonarrSummary.episodes -eq $sonarrEpisodeCount -and $sonarrSummary.episodeFiles -eq 3 -and
    $radarrSummary.movies -eq 1 -and $radarrSummary.movieFiles -eq 1 -and $sonarrSummary.missingFiles -eq 0 -and
    $sonarrSummary.lastSyncCompleted -and -not $sonarrSummary.lastSyncError
) @{ sonarr = $sonarrSummary; radarr = $radarrSummary; sonarrEpisodes = $sonarrEpisodeCount }

$episodeFiles = (Invoke-Api nodearr GET '/catalog/local/episodefile?pageSize=50').records
$double = $episodeFiles | Where-Object { $_.relativePath -like '*E04*' } | Select-Object -First 1
$single = $episodeFiles | Where-Object { $_.relativePath -like '*S01E01*' } | Select-Object -First 1

Assert-That 'Episode files carry series identity, quality and size' (
    $single.title -eq 'Pingu' -and $single.tvdbId -eq 75669 -and $single.seasonNumber -eq 1 -and (@($single.episodeNumbers) -join ',') -eq '1' -and
    $single.quality -eq 'WEBDL-720p' -and $single.size -eq (Get-Item $tvMedia).Length -and $single.applicationName -eq 'Sonarr'
) @{ file = $single }

Assert-That 'A multi-episode file is one file listing both episodes' (
    $double -and (@($double.episodeNumbers) -join ',') -eq '4,5' -and @($double.episodeTitles).Count -eq 2
) @{ relativePath = $double.relativePath; episodeNumbers = @($double.episodeNumbers); episodeTitles = @($double.episodeTitles) }

$movieFiles = (Invoke-Api nodearr GET '/catalog/local/moviefile?pageSize=50').records
Assert-That 'Movie files carry movie identity' (
    @($movieFiles).Count -eq 1 -and $movieFiles[0].title -eq 'Night of the Living Dead' -and $movieFiles[0].tmdbId -eq 10331 -and
    $movieFiles[0].imdbId -eq $movie.imdbId -and $movieFiles[0].year -eq 1968 -and $movieFiles[0].quality -eq 'WEBDL-720p'
) @{ file = $movieFiles[0] }

# --- 4. Path mappings --------------------------------------------------------------------------

Assert-That 'Without a path mapping the Arr path is not readable from the host' (-not $single.readable -and $single.localPath -eq $single.path) @{ path = $single.path; localPath = $single.localPath }

Invoke-Api nodearr POST '/remotepathmapping' @{ host = '127.0.0.1'; remotePath = '/tv/'; localPath = (Join-Path $library 'tv') + [IO.Path]::DirectorySeparatorChar } | Out-Null
Invoke-Api nodearr POST '/remotepathmapping' @{ host = '127.0.0.1'; remotePath = '/movies/'; localPath = (Join-Path $library 'movies') + [IO.Path]::DirectorySeparatorChar } | Out-Null

$mappedEpisode = (Invoke-Api nodearr GET '/catalog/local/episodefile?pageSize=50').records | Where-Object id -eq $single.id
$mappedMovie = (Invoke-Api nodearr GET '/catalog/local/moviefile?pageSize=50').records[0]
Assert-That 'With path mappings Nodearr can read the files the Arr apps report' (
    $mappedEpisode.readable -and $mappedMovie.readable -and (Test-Path -LiteralPath $mappedEpisode.localPath) -and (Get-Item -LiteralPath $mappedEpisode.localPath).Length -eq $mappedEpisode.size
) @{ episode = @{ path = $mappedEpisode.path; localPath = $mappedEpisode.localPath; readable = $mappedEpisode.readable }; movie = @{ path = $mappedMovie.path; localPath = $mappedMovie.localPath; readable = $mappedMovie.readable } }

# --- 5. Missing and purge ----------------------------------------------------------------------

$removed = Invoke-Api sonarr GET "/episodefile?seriesId=$($series.id)" | Where-Object { $_.relativePath -like '*S01E02*' } | Select-Object -First 1
Invoke-Api sonarr DELETE "/episodefile/$($removed.id)" | Out-Null
Invoke-Command-Api nodearr @{ name = 'CatalogRefresh'; applicationId = $applicationIds.sonarr }

$afterDelete = (Invoke-Api nodearr GET '/catalog/local/episodefile?pageSize=50').records
$missing = $afterDelete | Where-Object { $_.relativePath -like '*S01E02*' } | Select-Object -First 1
$summaryAfterDelete = Invoke-Api nodearr GET '/catalog/local' | Where-Object implementation -eq 'Sonarr'
Assert-That 'A file removed in Sonarr is marked missing, not deleted, on the next refresh' (
    $missing -and $missing.missingSince -and $summaryAfterDelete.missingFiles -eq 1 -and @($afterDelete).Count -eq 3 -and
    @($afterDelete | Where-Object { -not $_.missingSince }).Count -eq 2
) @{ missingSince = $missing.missingSince; missingFiles = $summaryAfterDelete.missingFiles; files = @($afterDelete).Count }

Invoke-Command-Api nodearr @{ name = 'CatalogRefresh'; applicationId = $applicationIds.sonarr; purgeMissing = $true }
$afterPurge = (Invoke-Api nodearr GET '/catalog/local/episodefile?pageSize=50').records
Assert-That 'Purging removes the missing file from the catalog' (@($afterPurge).Count -eq 2 -and -not ($afterPurge | Where-Object { $_.relativePath -like '*S01E02*' })) @{ files = @($afterPurge | ForEach-Object relativePath) }

# --- 6. Repeatable sync, scheduling, and removal -----------------------------------------------

Invoke-Command-Api nodearr @{ name = 'ApplicationIndexerSync'; forceSync = $true }
$counts = @{}
foreach ($app in 'sonarr', 'radarr') {
    $counts[$app] = @{
        indexers = @(Invoke-Api $app GET '/indexer' | Where-Object name -eq 'Nodearr').Count
        clients = @(Invoke-Api $app GET '/downloadclient' | Where-Object name -eq 'Nodearr').Count
        pinned = (Invoke-Api $app GET '/indexer' | Where-Object name -eq 'Nodearr').downloadClientId -eq (Invoke-Api $app GET '/downloadclient' | Where-Object name -eq 'Nodearr').id
    }
}
Assert-That 'Syncing again leaves exactly one Nodearr indexer and one download client in each app' (
    $counts.sonarr.indexers -eq 1 -and $counts.sonarr.clients -eq 1 -and $counts.sonarr.pinned -and
    $counts.radarr.indexers -eq 1 -and $counts.radarr.clients -eq 1 -and $counts.radarr.pinned
) $counts

$tasks = @((Invoke-Api nodearr GET '/system/task').taskName)
$unwanted = @($tasks | Where-Object { $_ -in 'RssSync', 'RefreshSeries', 'ImportListSync', 'UpdateSceneMapping', 'RefreshMonitoredDownloads', 'ApplicationUpdateCheck' })
Assert-That 'The catalog refresh and application sync are scheduled, and the inherited TV jobs are not' (
    ($tasks -contains 'CatalogRefresh') -and ($tasks -contains 'ApplicationIndexerSync') -and $unwanted.Count -eq 0
) @{ tasks = $tasks }

Invoke-Api nodearr DELETE "/applications/$($applicationIds.radarr)" | Out-Null
$afterRemoval = Wait-Until -What 'Radarr catalog removal' -TimeoutSec 30 -Condition {
    $left = Invoke-Api nodearr GET '/catalog/local'
    if (-not ($left | Where-Object implementation -eq 'Radarr')) { @{ MovieFiles = (Invoke-Api nodearr GET '/catalog/local/moviefile?pageSize=50').totalRecords } }
}
Assert-That 'Deleting an application removes its catalog' ($afterRemoval.MovieFiles -eq 0) $afterRemoval

# --- Report ------------------------------------------------------------------------------------

$failed = @($results | Where-Object { -not $_.passed })
[ordered]@{ ranAt = (Get-Date).ToUniversalTime().ToString('o'); passed = ($failed.Count -eq 0); checks = $results } |
    ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'results.json')
Write-Host "`n$($results.Count - $failed.Count) of $($results.Count) checks passed. Details: $(Join-Path $run 'results.json')"

if (-not $KeepRunning) {
    Stop-Nodearr
    Invoke-Compose down -v 2>&1 | Out-Null
}

if ($failed.Count -gt 0) { exit 1 }
