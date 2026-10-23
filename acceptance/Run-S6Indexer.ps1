#requires -Version 7
<#
.SYNOPSIS
    S6 acceptance: a real Sonarr and Radarr search Nodearr and find what a trusted node shares.

.DESCRIPTION
    Node A has a library: its own Sonarr and Radarr hold two Pingu episodes and one movie.
    Node B has its own Sonarr and Radarr, which know the same series and movie and have no files.
    The two nodes pair, A shares, and the script searches from B's Sonarr and Radarr, the way a
    person pressing "search" there would. It reads what those apps make of Nodearr's answers.

    Needs Docker and ffmpeg (as S3 does) and the transport sidecar (as S4 does).
    Everything lives in .run/s6 next to this script and in the nodearr-s6 compose project.

.PARAMETER TransportBin
    Path to the built nodearr-transport binary.

.PARAMETER KeepRunning
    Leave both instances and the containers running afterwards: A on port 6633, B on port 6634
    (or FirstPort and the one after), B's Sonarr on 28989 and B's Radarr on 27878.

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
    [string] $NodearrDll,
    [int] $FirstPort = 6633
)

$ErrorActionPreference = 'Stop'
# Version 1 only: the APIs leave null properties out of their JSON, and reading one must give $null.
Set-StrictMode -Version 1

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '..')
$run = Join-Path $root '.run/s6'
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

if (-not (Test-Path $dll)) { throw 'Build Nodearr first: dotnet build src/Nodearr.sln' }
if (-not $TransportBin) {
    $target = $env:CARGO_TARGET_DIR ? $env:CARGO_TARGET_DIR : (Join-Path $repo 'transport/target')
    $TransportBin = Join-Path $target ('release/nodearr-transport' + ($IsWindows ? '.exe' : ''))
}
if (-not (Test-Path $TransportBin)) { throw "Build the transport first: cargo build --release --manifest-path transport/Cargo.toml (looked for $TransportBin)" }

function New-Key { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }

$nodes = @{
    A = @{ Port = $FirstPort; Data = Join-Path $run 'a'; Key = New-Key }
    B = @{ Port = $FirstPort + 1; Data = Join-Path $run 'b'; Key = New-Key }
}
$arr = @{
    sonarr  = @{ Url = 'http://127.0.0.1:18989'; Key = New-Key }
    radarr  = @{ Url = 'http://127.0.0.1:17878'; Key = New-Key }
    sonarrB = @{ Url = 'http://127.0.0.1:28989'; Key = New-Key }
    radarrB = @{ Url = 'http://127.0.0.1:27878'; Key = New-Key }
}

function Invoke-Compose {
    docker compose -p nodearr-s6 @composeFiles --env-file $envFile @args
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

# Asks node B's indexer directly, as an Arr app would, and returns the parsed document.
function Invoke-Newznab {
    param([string] $Query, [string] $Key = $nodes.B.Key)
    $raw = (Invoke-WebRequest "http://127.0.0.1:$($nodes.B.Port)/newznab/api?$Query&apikey=$Key").Content
    if ($raw -is [byte[]]) { $raw = [Text.Encoding]::UTF8.GetString($raw) }
    return @{ Raw = $raw; Xml = [xml]$raw }
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

function Get-Fields { param($Provider) $fields = @{}; foreach ($field in $Provider.fields) { $fields[$field.name] = $field.value }; return $fields }

# What B currently holds of A's catalog, once it holds the expected number of files.
function Get-Seen {
    param([int] $ExpectedCount)

    return Wait-Until -What "B to hold $ExpectedCount of A's files" -TimeoutSec 45 -Condition {
        $items = @((Invoke-Node B GET '/catalog/peers/items?pageSize=200').records)
        # Wrapped, because an empty list on its own would read as "not yet".
        if ($items.Count -eq $ExpectedCount) { @{ Items = $items } }
    }
}

# The releases B's Sonarr or Radarr gets from Nodearr for one search, as that app describes them.
function Search-Arr {
    param([string] $App, [string] $Query)
    return @(Invoke-Arr $App GET "/release?$Query" | Where-Object indexer -eq 'Nodearr')
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

# Throwaway keys for this run, kept so the instances can be poked at afterwards with -KeepRunning.
@{ A = $nodes.A.Key; B = $nodes.B.Key; sonarrB = $arr.sonarrB.Key; radarrB = $arr.radarrB.Key } | ConvertTo-Json | Set-Content (Join-Path $run 'node-keys.json')

@("SONARR_API_KEY=$($arr.sonarr.Key)", "RADARR_API_KEY=$($arr.radarr.Key)", "PROWLARR_API_KEY=$(New-Key)",
  "SONARR_B_API_KEY=$($arr.sonarrB.Key)", "RADARR_B_API_KEY=$($arr.radarrB.Key)",
  "LIBRARY_DIR=$($library -replace '\\', '/')", "DOWNLOADS_DIR=$($downloads -replace '\\', '/')", "DOWNLOADS_B_DIR=$($downloadsB -replace '\\', '/')") | Set-Content $envFile

$tvMedia = Join-Path $media 'episode-720p.mkv'
$movieMedia = Join-Path $media 'movie-720p.mkv'
New-Media $tvMedia 120 'testsrc2=size=1280x720:rate=24,noise=alls=30:allf=t' '-b:v 1400k -maxrate 1800k -bufsize 3600k'
New-Media $movieMedia 660 'smptebars=size=1280x720:rate=5' '-crf 30'

Invoke-Compose down -v 2>&1 | Out-Null
foreach ($dir in $nodes.A.Data, $nodes.B.Data, $downloads, $downloadsB, $library) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $nodes.A.Data, $nodes.B.Data, (Join-Path $downloads 'complete'), (Join-Path $downloadsB 'complete'), (Join-Path $library 'tv'), (Join-Path $library 'movies') | Out-Null

$sceneEpisode = 'Pingu.S01E01.720p.WEB-DL.AAC2.0.H.264-NODEARR'
$plainEpisode = 'Pingu - S01E02 - Pingu Helps to Deliver the Mail WEBDL-720p'
$sceneMovie = 'Night.of.the.Living.Dead.1968.720p.WEB-DL.AAC2.0.H.264-NODEARR'

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

    $registered = @{}
    foreach ($app in 'sonarrB', 'radarrB') {
        $registered[$app] = Wait-Until -What "B's indexer in $app" -Condition { Invoke-Arr $app GET '/indexer' | Where-Object name -eq 'Nodearr' | Select-Object -First 1 }
    }
    $sonarrFields = Get-Fields $registered.sonarrB
    Assert-That "B's Sonarr and Radarr accept Nodearr as an indexer while B has no trusted nodes, with RSS off" (
        -not $registered.sonarrB.enableRss -and -not $registered.radarrB.enableRss -and
        $registered.sonarrB.enableAutomaticSearch -and $registered.radarrB.enableInteractiveSearch -and
        $registered.sonarrB.downloadClientId -gt 0 -and $registered.radarrB.downloadClientId -gt 0
    ) @{ sonarr = @{ enableRss = $registered.sonarrB.enableRss; downloadClientId = $registered.sonarrB.downloadClientId }; radarr = @{ enableRss = $registered.radarrB.enableRss; downloadClientId = $registered.radarrB.downloadClientId } }

    Assert-That "Sonarr is told to look for anime by season and episode too" (
        $sonarrFields.animeStandardFormatSearch -eq $true -and @($sonarrFields.animeCategories) -contains 5070
    ) @{ animeStandardFormatSearch = $sonarrFields.animeStandardFormatSearch; animeCategories = @($sonarrFields.animeCategories) }

    $caps = (Invoke-WebRequest "http://127.0.0.1:$($nodes.B.Port)/newznab/api?t=caps").Content
    if ($caps -is [byte[]]) { $caps = [Text.Encoding]::UTF8.GetString($caps) }
    $searching = ([xml]$caps).caps.searching
    $empty = Invoke-Newznab 't=tvsearch&cat=5030,5040&extended=1&offset=0&limit=100'
    Assert-That 'The indexer offers TV and movie searches by ID only, and answers a new node''s test with a stand-in' (
        $searching.'tv-search'.supportedParams -eq 'tvdbid,imdbid,tmdbid,season,ep' -and $searching.'movie-search'.supportedParams -eq 'imdbid,tmdbid' -and
        @($empty.Xml.rss.channel.item).Count -eq 1 -and $empty.Xml.rss.channel.item.title -eq 'Nodearr connection test, not a release'
    ) @{ tv = $searching.'tv-search'.supportedParams; movie = $searching.'movie-search'.supportedParams; emptyAnswer = @($empty.Xml.rss.channel.item | ForEach-Object title) }

    # --- A's library -----------------------------------------------------------------------------

    foreach ($pair in @{ 'nodearr-s6-sonarr-1' = '/tv'; 'nodearr-s6-radarr-1' = '/movies'; 'nodearr-s6-sonarr-b-1' = '/tv'; 'nodearr-s6-radarr-b-1' = '/movies' }.GetEnumerator()) {
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
        $all = @(Invoke-Arr sonarrB GET "/episode?seriesId=$($series.sonarrB.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -le 2 })
        if ($all.Count -eq 2) { $all | Sort-Object episodeNumber }
    }
    Wait-Until -What "the episode list in A's Sonarr" -Condition { @(Invoke-Arr sonarr GET "/episode?seriesId=$($series.sonarr.id)").Count -gt 2 } | Out-Null

    # One episode keeps the name it was released under. The other has only the name Sonarr's own
    # naming would give it, so its release title has to be built from what Sonarr knows.
    Import-ArrDownload -ApiUrl "$($arr.sonarr.Url)/api/v3" -ApiKey $arr.sonarr.Key -App Sonarr -SourceFile $tvMedia -Name $sceneEpisode -DownloadsDir $downloads
    Import-ArrDownload -ApiUrl "$($arr.sonarr.Url)/api/v3" -ApiKey $arr.sonarr.Key -App Sonarr -SourceFile $tvMedia -Name $plainEpisode -DownloadsDir $downloads
    Import-ArrDownload -ApiUrl "$($arr.radarr.Url)/api/v3" -ApiKey $arr.radarr.Key -App Radarr -SourceFile $movieMedia -Name $sceneMovie -DownloadsDir $downloads

    $filesA = Wait-Until -What "A's Sonarr to hold two episode files" -TimeoutSec 60 -Condition {
        $files = @(Invoke-Arr sonarr GET "/episodefile?seriesId=$($series.sonarr.id)")
        if ($files.Count -eq 2) { $files }
    }
    Wait-Until -What "A's Radarr to hold the movie" -TimeoutSec 60 -Condition { (Invoke-Arr radarr GET "/movie/$($movie.radarr.id)").hasFile } | Out-Null

    Invoke-NodeCommand A @{ name = 'CatalogRefresh' }
    Invoke-NodeCommand B @{ name = 'CatalogRefresh' }

    # --- Pair, and share everything --------------------------------------------------------------

    $invite = Invoke-Node A POST '/nodes/invites' @{ validHours = 1; label = 's6'; shareAll = $true }
    Invoke-Node B POST '/nodes/invites/redeem' @{ code = $invite.code } | Out-Null
    $seen = (Get-Seen 3).Items
    $bOnA = Invoke-Node A GET '/nodes/peers' | Select-Object -First 1
    $nodeIdA = (Invoke-Node A GET '/nodes/identity').nodeId

    # --- Sonarr searches -------------------------------------------------------------------------

    $forEpisodeOne = Search-Arr sonarrB "episodeId=$($episodesB[0].id)"
    $one = $forEpisodeOne | Select-Object -First 1
    Assert-That "B's Sonarr finds A's episode by ID, under the name it was released as, and would grab it" (
        $forEpisodeOne.Count -eq 1 -and $one.title -eq $sceneEpisode -and $one.quality.quality.name -eq 'WEBDL-720p' -and
        $one.approved -and $one.protocol -eq 'usenet' -and $one.size -eq (Get-Item $tvMedia).Length -and $one.releaseGroup -eq 'NODEARR'
    ) @{ count = $forEpisodeOne.Count; title = $one.title; quality = $one.quality.quality.name; approved = $one.approved; rejections = @($one.rejections); size = $one.size }

    $forEpisodeTwo = Search-Arr sonarrB "episodeId=$($episodesB[1].id)"
    $two = $forEpisodeTwo | Select-Object -First 1
    $twoEpisodes = @($two.mappedEpisodeNumbers) + @($two.episodeNumbers) | Where-Object { $null -ne $_ }
    Assert-That 'A file with no release name gets a title built from what Sonarr knows, and Sonarr reads it back correctly' (
        $forEpisodeTwo.Count -eq 1 -and $two.title -match '^Pingu\.S01E02\.720p\.WEB-DL\.' -and $two.title -notmatch 'Deliver|Mail|\s' -and
        $two.quality.quality.name -eq 'WEBDL-720p' -and $two.approved -and $twoEpisodes -contains 2
    ) @{ count = $forEpisodeTwo.Count; title = $two.title; quality = $two.quality.quality.name; approved = $two.approved; rejections = @($two.rejections); episodes = @($twoEpisodes) }

    $forSeason = Search-Arr sonarrB "seriesId=$($series.sonarrB.id)&seasonNumber=1"
    Assert-That 'A season search finds both episodes' (
        $forSeason.Count -eq 2 -and @($forSeason | Where-Object approved).Count -eq 2
    ) @{ titles = @($forSeason | ForEach-Object { "$($_.title) approved=$($_.approved)" }) }

    # --- Radarr searches -------------------------------------------------------------------------

    $forMovie = Search-Arr radarrB "movieId=$($movie.radarrB.id)"
    $film = $forMovie | Select-Object -First 1
    Assert-That "B's Radarr finds A's movie by ID and would grab it" (
        $forMovie.Count -eq 1 -and $film.title -eq $sceneMovie -and $film.quality.quality.name -eq 'WEBDL-720p' -and $film.approved -and
        $film.size -eq (Get-Item $movieMedia).Length
    ) @{ count = $forMovie.Count; title = $film.title; quality = $film.quality.quality.name; approved = $film.approved; rejections = @($film.rejections) }

    # --- What a result gives away, and whether it holds still ------------------------------------

    $feed = Invoke-Newznab 't=tvsearch&cat=5030,5040&extended=1&offset=0&limit=100&tvdbid=75669&season=1&ep=1'
    $whole = (Invoke-Newznab 't=search&extended=1&offset=0&limit=100').Raw
    Assert-That 'A result names neither the node it comes from nor that node''s file, and has no path in it' (
        $one.guid -match '^nodearr:[0-9a-f]{32}$' -and $whole -notmatch [regex]::Escape($nodeIdA) -and
        $whole -notmatch '/tv/|/movies/|Season 1|\.mkv' -and $whole -notmatch '"?[em]-\d+-\d+'
    ) @{ guid = $one.guid; bytes = $whole.Length }

    # Hiding and unhiding the movie makes B replace every row it holds for A, twice.
    $titles = Invoke-Node A GET '/sharing/titles'
    $pingu = $titles | Where-Object title -eq 'Pingu'
    $night = $titles | Where-Object title -eq 'Night of the Living Dead'
    $hidden = Invoke-Node A GET '/sharing/lists' | Where-Object isHidden
    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @(@{ mediaKind = 'movie'; applicationId = $night.applicationId; arrId = $night.arrId }); peerIds = @() } | Out-Null
    Get-Seen 2 | Out-Null
    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @(); peerIds = @() } | Out-Null
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    Get-Seen 3 | Out-Null
    $again = Search-Arr sonarrB "episodeId=$($episodesB[0].id)" | Select-Object -First 1
    Assert-That 'A file keeps its ID and its publish date when the catalog it came in is fetched again' (
        $again.guid -eq $one.guid -and $again.publishDate -eq $one.publishDate
    ) @{ before = @{ guid = $one.guid; publishDate = $one.publishDate }; after = @{ guid = $again.guid; publishDate = $again.publishDate } }

    $newest = @((Invoke-Newznab 't=search&extended=1&offset=0&limit=100').Xml.rss.channel.item)
    Assert-That 'A query with no IDs lists what is on offer, with no stand-in once there is something real' (
        $newest.Count -eq 3 -and -not ($newest | Where-Object title -eq 'Nodearr connection test, not a release')
    ) @{ titles = @($newest | ForEach-Object title) }

    # --- The ticket ------------------------------------------------------------------------------

    $candidateId = $one.guid -replace '^nodearr:', ''
    $ticket = Invoke-Newznab "t=get&id=$candidateId"
    $forged = $candidateId.Substring(0, 31) + ($candidateId[31] -eq '0' ? '1' : '0')
    $refused = Invoke-Newznab "t=get&id=$forged"
    $noKey = Invoke-Newznab "t=get&id=$candidateId" -Key 'wrong'
    Assert-That 'The link in a result returns a ticket for that file, and only for an ID Nodearr issued, with the right key' (
        $ticket.Xml.nzb.head.meta.Where({ $_.type -eq 'x-nodearr-candidate' }).'#text' -eq $candidateId -and @($ticket.Xml.nzb.file).Count -eq 1 -and
        $refused.Xml.error.code -eq '300' -and $noKey.Xml.error.code -eq '100'
    ) @{ ticketBytes = $ticket.Raw.Length; forged = $refused.Xml.error.description; wrongKey = $noKey.Xml.error.description }

    # A grab hands the ticket to Nodearr's download client, which starts a transfer. What becomes
    # of the transfer is S7's script. Here it is removed again.
    $grab = Invoke-Arr sonarrB POST '/release' @{ guid = $one.guid; indexerId = $one.indexerId } -AllowError
    $started = @(Invoke-Node B GET '/transfers')
    Assert-That 'A grab in Sonarr hands the ticket to Nodearr, which starts a transfer for that file' (
        -not $grab.Failed -and $started.Count -eq 1 -and $started[0].title -eq $sceneEpisode
    ) @{ grabFailed = [bool]$grab.Failed; body = $grab.Body; transfers = @($started | ForEach-Object title) }
    $started | ForEach-Object { Invoke-Node B DELETE "/transfers/$($_.downloadId)" | Out-Null }

    # --- Sharing rules reach the Arr apps --------------------------------------------------------

    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @(@{ mediaKind = 'tv'; applicationId = $pingu.applicationId; arrId = $pingu.arrId }); peerIds = @() } | Out-Null
    Get-Seen 1 | Out-Null
    $afterHide = Search-Arr sonarrB "episodeId=$($episodesB[0].id)"
    $staleTicket = Invoke-Newznab "t=get&id=$candidateId"
    $movieStill = Search-Arr radarrB "movieId=$($movie.radarrB.id)"
    Assert-That 'Hiding a series on A takes it out of B''s Sonarr searches and voids its old link, and leaves the movie' (
        $afterHide.Count -eq 0 -and $staleTicket.Xml.error.code -eq '300' -and $movieStill.Count -eq 1
    ) @{ sonarrResults = $afterHide.Count; oldLink = $staleTicket.Xml.error.description; radarrResults = $movieStill.Count }

    Invoke-Node A POST "/nodes/peers/$($bOnA.id)/revoke" | Out-Null
    Wait-Until -What "B to drop A's catalog" -TimeoutSec 30 -Condition { (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 0 } | Out-Null
    $afterRevoke = Search-Arr radarrB "movieId=$($movie.radarrB.id)"
    Assert-That 'When trust is revoked, B''s Radarr finds nothing' ($afterRevoke.Count -eq 0) @{ radarrResults = $afterRevoke.Count }
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
        'A', 'B' | ForEach-Object { Stop-Node $_ }
        Invoke-Compose down -v 2>&1 | Out-Null
    }
}

Write-Host "`n$($results.Count - $failed.Count) of $($results.Count) checks passed. Details: $(Join-Path $run 'results.json')"
if ($failed.Count -gt 0) { exit 1 }
