#requires -Version 7
<#
.SYNOPSIS
    S5 acceptance: sharing rules and catalog exchange between two real Nodearr instances.

.DESCRIPTION
    Node A imports a real catalog from Sonarr and Radarr containers. Node B imports from the same
    Sonarr only. The two pair, and the script checks from B's side exactly what A's sharing rules let
    B see, and how B compares it with its own catalog.

    Needs Docker and ffmpeg (as S3 does) and the transport sidecar (as S4 does).
    Everything lives in .run/s5 next to this script and in the nodearr-s5 compose project.

.PARAMETER TransportBin
    Path to the built nodearr-transport binary.

.PARAMETER KeepRunning
    Leave both instances and the containers running afterwards: A on port 6633, B on port 6634.
#>
[CmdletBinding()]
param(
    [string] $TransportBin,
    [switch] $KeepRunning
)

$ErrorActionPreference = 'Stop'
# Version 1 only: the API leaves null properties out of its JSON, and reading one must give $null.
Set-StrictMode -Version 1

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '..')
$run = Join-Path $root '.run/s5'
$dll = Join-Path $repo '_output/net10.0/Nodearr.Console.dll'
$composeFiles = @('-f', (Join-Path $root 'docker-compose.arr.yml'), '-f', (Join-Path $root 'docker-compose.library.yml'))
$envFile = Join-Path $run '.env'
$downloads = Join-Path $run 'downloads'
$library = Join-Path $run 'library'
$peerLibrary = Join-Path $run 'peer-library'

. (Join-Path $root 'Library.ps1')

if (-not (Test-Path $dll)) { throw 'Build Nodearr first: dotnet build src/Nodearr.sln' }
if (-not $TransportBin) {
    $target = $env:CARGO_TARGET_DIR ? $env:CARGO_TARGET_DIR : (Join-Path $repo 'transport/target')
    $TransportBin = Join-Path $target ('release/nodearr-transport' + ($IsWindows ? '.exe' : ''))
}
if (-not (Test-Path $TransportBin)) { throw "Build the transport first: cargo build --release --manifest-path transport/Cargo.toml (looked for $TransportBin)" }

function New-Key { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }

$nodes = @{
    A = @{ Port = 6633; Data = Join-Path $run 'a'; Key = New-Key }
    B = @{ Port = 6634; Data = Join-Path $run 'b'; Key = New-Key }
}
$arr = @{
    sonarr = @{ Url = 'http://127.0.0.1:18989/api/v3'; Key = New-Key }
    radarr = @{ Url = 'http://127.0.0.1:17878/api/v3'; Key = New-Key }
}

function Invoke-Compose {
    docker compose -p nodearr-s5 @composeFiles --env-file $envFile @args
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
        if ($AllowError) { return @{ Failed = $true; Status = $status } }
        throw "$Method $BaseUrl$Path -> $status $($response | ConvertTo-Json -Depth 6 -Compress)"
    }
    return $response
}

function Invoke-Node { param([string] $Name, [string] $Method, [string] $Path, $Body) Invoke-Http "http://127.0.0.1:$($nodes[$Name].Port)/api/v5" $nodes[$Name].Key $Method $Path $Body }
function Invoke-Arr { param([string] $App, [string] $Method, [string] $Path, $Body) Invoke-Http $arr[$App].Url $arr[$App].Key $Method $Path $Body }

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

function Invoke-ArrCommand {
    param([string] $App, [hashtable] $Command)
    $queued = Invoke-Arr $App POST '/command' $Command
    Wait-Until -What "$App command $($Command.name)" -TimeoutSec 180 -Condition { (Invoke-Arr $App GET "/command/$($queued.id)").status -eq 'completed' } | Out-Null
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
    param([string] $Node, [string] $Implementation, [string] $Url, [string] $ApiKey, [string] $SyncLevel)

    $definition = Invoke-Node $Node GET '/applications/schema' | Where-Object implementation -eq $Implementation
    foreach ($pair in @{ nodearrUrl = "http://host.docker.internal:$($nodes[$Node].Port)"; baseUrl = $Url; apiKey = $ApiKey }.GetEnumerator()) {
        ($definition.fields | Where-Object name -eq $pair.Key) | Add-Member -NotePropertyName value -NotePropertyValue $pair.Value -Force
    }
    $definition | Add-Member -Force -NotePropertyMembers @{ name = $Implementation; syncLevel = $SyncLevel }
    return (Invoke-Node $Node POST '/applications' $definition).id
}

# What B currently holds of A's catalog, once B has fetched the version A now offers.
function Get-Seen {
    param([int] $ExpectedCount)

    return Wait-Until -What "B to hold $ExpectedCount of A's files" -TimeoutSec 45 -Condition {
        $items = @((Invoke-Node B GET '/catalog/peers/items?pageSize=200').records)
        # Wrapped, because an empty list on its own would read as "not yet".
        if ($items.Count -eq $ExpectedCount) { @{ Items = $items } }
    }
}

# Waits for B to hold exactly these of A's files: "e1" for an episode, "movie" for the movie.
function Get-SeenNames {
    param([string[]] $Expected)

    $want = ($Expected | Sort-Object) -join ' '
    return Wait-Until -What "B to hold exactly $want" -TimeoutSec 45 -Condition {
        $items = @((Invoke-Node B GET '/catalog/peers/items?pageSize=200').records)
        $names = @($items | ForEach-Object { $_.mediaKind -eq 'movie' ? 'movie' : "e$(@($_.episodeNumbers) -join ',')" } | Sort-Object)
        if (($names -join ' ') -eq $want) { @{ Names = $names } }
    }
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

New-Item -ItemType Directory -Force $run, $peerLibrary | Out-Null
'A', 'B' | ForEach-Object { Stop-Node $_ }

# Throwaway keys for this run, kept so the instances can be poked at afterwards with -KeepRunning.
@{ A = $nodes.A.Key; B = $nodes.B.Key } | ConvertTo-Json | Set-Content (Join-Path $run 'node-keys.json')

@("SONARR_API_KEY=$($arr.sonarr.Key)", "RADARR_API_KEY=$($arr.radarr.Key)", "PROWLARR_API_KEY=$(New-Key)",
  "LIBRARY_DIR=$($library -replace '\\', '/')", "DOWNLOADS_DIR=$($downloads -replace '\\', '/')") | Set-Content $envFile

$tvMedia = Join-Path $peerLibrary 'episode-720p.mkv'
$movieMedia = Join-Path $peerLibrary 'movie-720p.mkv'
New-Media $tvMedia 120 'testsrc2=size=1280x720:rate=24,noise=alls=30:allf=t' '-b:v 1400k -maxrate 1800k -bufsize 3600k'
New-Media $movieMedia 660 'smptebars=size=1280x720:rate=5' '-crf 30'

Invoke-Compose down -v 2>&1 | Out-Null
foreach ($dir in $nodes.A.Data, $nodes.B.Data, $downloads, $library) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $nodes.A.Data, $nodes.B.Data, (Join-Path $downloads 'complete'), (Join-Path $library 'tv'), (Join-Path $library 'movies') | Out-Null

try {
    Start-Node A
    Start-Node B
    Invoke-Compose up -d sonarr radarr 2>&1 | Out-Null
    foreach ($app in 'sonarr', 'radarr') {
        Wait-Until -What "$app API" -TimeoutSec 180 -Condition { try { Invoke-Arr $app GET '/system/status' } catch { $null } } | Out-Null
    }

    # --- Catalogs --------------------------------------------------------------------------------

    # A registers itself in both apps. B only reads Sonarr and leaves A's registration alone.
    $sonarrOnA = Add-Application A Sonarr 'http://127.0.0.1:18989' $arr.sonarr.Key 'fullSync'
    Add-Application A Radarr 'http://127.0.0.1:17878' $arr.radarr.Key 'fullSync' | Out-Null
    foreach ($app in 'sonarr', 'radarr') {
        Wait-Until -What "A's indexer in $app" -Condition { Invoke-Arr $app GET '/indexer' | Where-Object name -eq 'Nodearr' } | Out-Null
    }

    docker exec nodearr-s5-sonarr-1 chown abc:abc /tv | Out-Null
    docker exec nodearr-s5-radarr-1 chown abc:abc /movies | Out-Null

    Invoke-Arr sonarr POST '/rootfolder' @{ path = '/tv' } | Out-Null
    $series = (Invoke-Arr sonarr GET '/series/lookup?term=tvdb:75669')[0]
    $series | Add-Member -Force -NotePropertyMembers @{
        qualityProfileId = 1; rootFolderPath = '/tv'; monitored = $true; seasonFolder = $true
        addOptions = @{ monitor = 'all'; searchForMissingEpisodes = $false; searchForCutoffUnmetEpisodes = $false }
    }
    $series = Invoke-Arr sonarr POST '/series' $series
    $episodes = Wait-Until -What 'Sonarr episode list' -Condition {
        $all = @(Invoke-Arr sonarr GET "/episode?seriesId=$($series.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -le 2 })
        if ($all.Count -eq 2) { $all | Sort-Object episodeNumber }
    }

    Invoke-Arr radarr POST '/rootfolder' @{ path = '/movies' } | Out-Null
    $movie = (Invoke-Arr radarr GET '/movie/lookup?term=tmdb:10331')[0]
    $movie | Add-Member -Force -NotePropertyMembers @{ qualityProfileId = 1; rootFolderPath = '/movies'; monitored = $true; minimumAvailability = 'released'; addOptions = @{ searchForMovie = $false } }
    $movie = Invoke-Arr radarr POST '/movie' $movie

    # The files arrive the way finished downloads do: each app imports them from /downloads.
    Import-ArrDownload -ApiUrl $arr.sonarr.Url -ApiKey $arr.sonarr.Key -App Sonarr -SourceFile $tvMedia -Name 'Pingu.S01E01.720p.WEB-DL.AAC2.0.H.264-NODEARR' -DownloadsDir $downloads
    Import-ArrDownload -ApiUrl $arr.sonarr.Url -ApiKey $arr.sonarr.Key -App Sonarr -SourceFile $tvMedia -Name 'Pingu.S01E02.720p.WEB-DL.AAC2.0.H.264-NODEARR' -DownloadsDir $downloads
    Import-ArrDownload -ApiUrl $arr.radarr.Url -ApiKey $arr.radarr.Key -App Radarr -SourceFile $movieMedia -Name 'Night.of.the.Living.Dead.1968.720p.WEB-DL.AAC2.0.H.264-NODEARR' -DownloadsDir $downloads
    Wait-Until -What 'Sonarr to import two episodes' -TimeoutSec 60 -Condition {
        @(Invoke-Arr sonarr GET "/episode?seriesId=$($series.id)" | Where-Object hasFile).Count -eq 2
    } | Out-Null
    Wait-Until -What 'Radarr to import the movie' -TimeoutSec 60 -Condition { (Invoke-Arr radarr GET "/movie/$($movie.id)").hasFile } | Out-Null

    # A's catalog: two Pingu episodes and one movie.
    Invoke-NodeCommand A @{ name = 'CatalogRefresh' }

    # Then episode 2 is deleted in Sonarr, and only B refreshes. A still offers it. B no longer has it.
    # A misses the deletion, as it would if it were down when Sonarr called: its webhook is taken
    # out of Sonarr first. It is put back, and tested, further down.
    foreach ($hook in @(Invoke-Arr sonarr GET '/notification' | Where-Object name -eq 'Nodearr')) { Invoke-Arr sonarr DELETE "/notification/$($hook.id)" | Out-Null }
    $episodeTwoFile = Invoke-Arr sonarr GET "/episodefile?seriesId=$($series.id)" | Where-Object { $_.relativePath -like '*S01E02*' } | Select-Object -First 1
    Invoke-Arr sonarr DELETE "/episodefile/$($episodeTwoFile.id)" | Out-Null
    Add-Application B Sonarr 'http://127.0.0.1:18989' $arr.sonarr.Key 'addOnly' | Out-Null
    Invoke-NodeCommand B @{ name = 'CatalogRefresh' }

    $titles = Invoke-Node A GET '/sharing/titles'
    $pingu = $titles | Where-Object title -eq 'Pingu'
    $night = $titles | Where-Object title -eq 'Night of the Living Dead'
    Assert-That 'A lists the series and movie it can share' ($pingu.files -eq 2 -and $night.files -eq 1 -and @($titles).Count -eq 2) @{ titles = @($titles | ForEach-Object { "$($_.title): $($_.files)" }) }

    # --- Pairing with sharing everything turned off: nothing is shared ---------------------------

    $invite = Invoke-Node A POST '/nodes/invites' @{ validHours = 1; label = 's5'; shareAll = $false }
    Invoke-Node B POST '/nodes/invites/redeem' @{ code = $invite.code } | Out-Null
    $bOnA = Invoke-Node A GET '/nodes/peers' | Select-Object -First 1
    $aOnB = Invoke-Node B GET '/nodes/peers' | Select-Object -First 1

    $synced = Wait-Until -What "B's first sync with A" -TimeoutSec 45 -Condition {
        $summary = Invoke-Node B GET '/catalog/peers' | Where-Object peerId -eq $aOnB.id
        if ($summary.catalogSyncedAt) { $summary }
    }
    Assert-That 'A peer paired with sharing everything turned off is offered nothing' (
        -not $bOnA.shareAll -and ($synced.notInLibrary + $synced.missing + $synced.upgrade + $synced.have) -eq 0 -and -not $synced.catalogError -and
        (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 0
    ) @{ summary = $synced }

    # --- Share everything except Hidden ----------------------------------------------------------

    # Something newly shared waits for B's next Catalog Sync, as a new release waits for Sonarr's
    # next RSS Sync. B is asked to sync now, as a user can.
    Invoke-Node A PUT "/nodes/peers/$($bOnA.id)/shareall" @{ shareAll = $true } | Out-Null
    Start-Sleep -Seconds 8
    $beforeSync = (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    $seen = (Get-Seen 3).Items
    $syncTask = Invoke-Node B GET '/system/task' | Where-Object { $_.taskName -eq 'PeerCatalogSync' }
    Assert-That 'Something newly shared reaches the peer at its next Catalog Sync, every 15 minutes, and not before' (
        $beforeSync -eq 0 -and $syncTask.interval -eq 15
    ) @{ heldBeforeSync = $beforeSync; interval = $syncTask.interval }
    $summary = Invoke-Node B GET '/catalog/peers' | Where-Object peerId -eq $aOnB.id
    $byTitle = @{}
    foreach ($item in $seen) { $byTitle[$item.releaseTitle] = $item }
    Assert-That 'With "share everything", the peer is told about every file, promptly and without asking again' (
        $seen.Count -eq 3 -and $summary.have -eq 1 -and $summary.missing -eq 1 -and $summary.notInLibrary -eq 1
    ) @{ summary = $summary; items = @($seen | ForEach-Object { "$($_.releaseTitle) -> $($_.localStatus)" }) }

    $episodeOne = $seen | Where-Object { $_.mediaKind -eq 'tv' -and (@($_.episodeNumbers) -join ',') -eq '1' }
    $episodeTwo = $seen | Where-Object { $_.mediaKind -eq 'tv' -and (@($_.episodeNumbers) -join ',') -eq '2' }
    $theMovie = $seen | Where-Object mediaKind -eq 'movie'
    Assert-That 'The peer compares what it is offered with its own catalog' (
        $episodeOne.localStatus -eq 'have' -and $episodeTwo.localStatus -eq 'missing' -and $theMovie.localStatus -eq 'notInLibrary' -and
        $episodeOne.title -eq 'Pingu' -and $episodeOne.tvdbId -eq 75669 -and $theMovie.tmdbId -eq 10331 -and $theMovie.quality -eq 'WEBDL-720p'
    ) @{ episode1 = $episodeOne.localStatus; episode2 = $episodeTwo.localStatus; movie = $theMovie.localStatus }

    $raw = (Invoke-WebRequest "http://127.0.0.1:$($nodes.B.Port)/api/v5/catalog/peers/items?pageSize=200" -Headers @{ 'X-Api-Key' = $nodes.B.Key }).Content
    Assert-That 'What the peer receives says nothing about where the files are' (
        $raw -notmatch '/tv/' -and $raw -notmatch '/movies/' -and $raw -notmatch 'Season 1' -and $raw -notmatch '"path"'
    ) @{ bytes = $raw.Length }

    # --- Hidden ----------------------------------------------------------------------------------

    $hidden = Invoke-Node A GET '/sharing/lists' | Where-Object isHidden
    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @(@{ mediaKind = 'tv'; applicationId = $pingu.applicationId; arrId = $pingu.arrId }); peerIds = @() } | Out-Null
    $seen = (Get-Seen 1).Items
    Assert-That 'Hiding a series removes it from what a "share everything" peer is offered' ($seen[0].mediaKind -eq 'movie') @{ items = @($seen | ForEach-Object releaseTitle) }

    # B already held A's catalog, so the hiding reached it as two removals and nothing else.
    $changeLine = Wait-Until -What "B to log the change it took" -TimeoutSec 15 -Condition {
        Select-String -Path (Join-Path $nodes.B.Data 'logs/nodearr.debug.txt') -Pattern 'is now version \w+: 0 files added or changed, 2 removed' -ErrorAction SilentlyContinue | Select-Object -Last 1
    }
    Assert-That 'A change reaches a peer that holds the catalog as only what changed, not the whole catalog again' ($null -ne $changeLine) @{ line = "$($changeLine.Line)" }

    # --- Share lists -----------------------------------------------------------------------------

    Invoke-Node A PUT "/nodes/peers/$($bOnA.id)/shareall" @{ shareAll = $false } | Out-Null
    Get-Seen 0 | Out-Null
    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @(); peerIds = @() } | Out-Null

    $list = Invoke-Node A POST '/sharing/lists' @{ name = 'Kids'; items = @(@{ mediaKind = 'tv'; applicationId = $pingu.applicationId; arrId = $pingu.arrId }); peerIds = @() }
    Start-Sleep -Seconds 3
    $beforeAssign = (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords
    Invoke-Node A PUT "/sharing/lists/$($list.id)" @{ name = 'Kids'; items = $list.items; peerIds = @($bOnA.id) } | Out-Null
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    $seen = (Get-Seen 2).Items
    Assert-That 'A share list offers only its titles, and only once the peer is put on it' (
        $beforeAssign -eq 0 -and @($seen | Where-Object mediaKind -eq 'tv').Count -eq 2
    ) @{ beforeAssign = $beforeAssign; items = @($seen | ForEach-Object releaseTitle) }

    # --- Seasons and single episodes, the way Sonarr monitors them --------------------------------

    $seasonOne = @(Invoke-Node A GET "/sharing/titles/$($pingu.applicationId)/$($pingu.arrId)/seasons") | Where-Object seasonNumber -eq 1
    $e1 = ($seasonOne.episodes | Where-Object episodeNumber -eq 1).episodeArrId
    $e2 = ($seasonOne.episodes | Where-Object episodeNumber -eq 2).episodeArrId
    $pinguKey = @{ mediaKind = 'tv'; applicationId = $pingu.applicationId; arrId = $pingu.arrId }
    $movieKey = @{ mediaKind = 'movie'; applicationId = $night.applicationId; arrId = $night.arrId }

    Invoke-Node A PUT "/sharing/lists/$($list.id)" @{ name = 'Kids'; items = @($pinguKey + @{ episodeArrId = $e2 }); peerIds = @($bOnA.id) } | Out-Null
    $oneEpisode = (Get-SeenNames 'e2').Names
    Invoke-Node A PUT "/sharing/lists/$($list.id)" @{ name = 'Kids'; items = @($pinguKey + @{ seasonNumber = 1 }); peerIds = @($bOnA.id) } | Out-Null
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    $oneSeason = (Get-SeenNames 'e1', 'e2').Names
    Assert-That 'A list can hold one episode or one season of a series, and the peer is offered just that' (
        $e1 -and $e2 -and ($oneEpisode -join ' ') -eq 'e2' -and ($oneSeason -join ' ') -eq 'e1 e2'
    ) @{ oneEpisode = $oneEpisode; oneSeason = $oneSeason; seasons = $seasonOne.episodes.Count }

    Invoke-Node A PUT "/sharing/lists/$($list.id)" @{ name = 'Kids'; items = @($pinguKey, $movieKey); peerIds = @($bOnA.id) } | Out-Null
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    Get-SeenNames 'e1', 'e2', 'movie' | Out-Null
    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @($pinguKey + @{ episodeArrId = $e1 }); peerIds = @() } | Out-Null
    $hidOne = (Get-SeenNames 'e2', 'movie').Names
    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @($pinguKey + @{ seasonNumber = 1 }); peerIds = @() } | Out-Null
    $hidSeason = (Get-SeenNames 'movie').Names
    Assert-That 'Hiding one episode or one season keeps back just that, even from a list that shares the whole series' (
        ($hidOne -join ' ') -eq 'e2 movie' -and ($hidSeason -join ' ') -eq 'movie'
    ) @{ hidOneEpisode = $hidOne; hidSeason = $hidSeason }

    Invoke-Node A PUT "/sharing/lists/$($hidden.id)" @{ items = @(); peerIds = @() } | Out-Null
    Invoke-Node A DELETE "/sharing/lists/$($list.id)" | Out-Null
    Get-Seen 0 | Out-Null
    Assert-That 'Deleting the list takes its titles away from the peer' $true @{ }

    # --- Sonarr tells A, and A tells the peer ------------------------------------------------------

    # A's webhook goes back into Sonarr the way it got there: A keeping its registration in step.
    Invoke-NodeCommand A @{ name = 'ApplicationIndexerSync' }
    $hook = Wait-Until -What "A's webhook in Sonarr" -TimeoutSec 30 -Condition {
        Invoke-Arr sonarr GET '/notification' | Where-Object { $_.name -eq 'Nodearr' -and $_.implementation -eq 'Webhook' } | Select-Object -First 1
    }
    $hookUrl = ($hook.fields | Where-Object name -eq 'url').value
    $hookHeader = @(($hook.fields | Where-Object name -eq 'headers').value) | Select-Object -First 1
    Assert-That 'A puts its own webhook into Sonarr: to itself, with its key, for files coming and going and nothing else' (
        $hookUrl -eq "http://host.docker.internal:$($nodes.A.Port)/api/v5/arr/webhook/$sonarrOnA" -and $hookHeader.key -eq 'X-Api-Key' -and
        $hook.onDownload -and $hook.onUpgrade -and $hook.onEpisodeFileDelete -and $hook.onSeriesDelete -and -not $hook.onGrab -and -not $hook.onHealthIssue
    ) @{ url = $hookUrl; header = $hookHeader.key; onDownload = $hook.onDownload; onEpisodeFileDelete = $hook.onEpisodeFileDelete; onGrab = $hook.onGrab }

    # A catches up on the deletion it missed, and B holds what A has: episode 1 and the movie.
    Invoke-NodeCommand A @{ name = 'CatalogRefresh' }
    Invoke-Node A PUT "/nodes/peers/$($bOnA.id)/shareall" @{ shareAll = $true } | Out-Null
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    Get-SeenNames 'e1', 'movie' | Out-Null

    # Episode 1 is deleted in Sonarr. Nobody asks anyone to sync.
    $episodeOneFile = Invoke-Arr sonarr GET "/episodefile?seriesId=$($series.id)" | Where-Object { $_.relativePath -like '*S01E01*' } | Select-Object -First 1
    $deletedAt = Get-Date
    Invoke-Arr sonarr DELETE "/episodefile/$($episodeOneFile.id)" | Out-Null
    $gone = (Get-SeenNames 'movie').Names
    $tookToGo = ((Get-Date) - $deletedAt).TotalSeconds
    Assert-That 'A file deleted in Sonarr leaves the peer within seconds with nobody asking: Sonarr tells A, and A tells the peer' (
        ($gone -join ' ') -eq 'movie' -and $tookToGo -lt 30
    ) @{ held = $gone; seconds = [math]::Round($tookToGo, 1) }

    # Sonarr imports it again. A has it at once. The peer gets it at its next sync, as from any indexer.
    $importedAt = Get-Date
    Import-ArrDownload -ApiUrl $arr.sonarr.Url -ApiKey $arr.sonarr.Key -App Sonarr -SourceFile $tvMedia -Name 'Pingu.S01E01.720p.WEB-DL.AAC2.0.H.264-NODEARR' -DownloadsDir $downloads
    $back = Wait-Until -What 'A to hold episode 1 again' -TimeoutSec 30 -EverySec 0.5 -Condition {
        $now = Invoke-Node A GET '/sharing/titles' | Where-Object title -eq 'Pingu'
        if ($now.files -eq 1) { $now }
    }
    $tookToCome = ((Get-Date) - $importedAt).TotalSeconds
    Invoke-NodeCommand B @{ name = 'PeerCatalogSync' }
    $returned = (Get-SeenNames 'e1', 'movie').Names
    Assert-That 'A file Sonarr imports is in A''s catalog within seconds, without waiting for a full read' (
        $back.files -eq 1 -and $tookToCome -lt 30 -and ($returned -join ' ') -eq 'e1 movie'
    ) @{ seconds = [math]::Round($tookToCome, 1); held = $returned }

    # --- Trust ends ------------------------------------------------------------------------------

    Get-Seen 2 | Out-Null
    Invoke-Node A POST "/nodes/peers/$($bOnA.id)/revoke" | Out-Null
    $after = Wait-Until -What "B to drop A's catalog" -TimeoutSec 30 -Condition {
        $left = Invoke-Node B GET '/catalog/peers/items?pageSize=50'
        if ($left.totalRecords -eq 0) { @{ Items = 0; Summaries = @(Invoke-Node B GET '/catalog/peers').Count } }
    }
    $direct = Invoke-Node B POST '/command' @{ name = 'PeerCatalogSync' }
    Start-Sleep -Seconds 3
    Assert-That 'When trust is revoked the peer loses the catalog and cannot fetch it again' (
        $after.Items -eq 0 -and $after.Summaries -eq 0 -and (Invoke-Node B GET '/catalog/peers/items?pageSize=50').totalRecords -eq 0
    ) $after

    $tasks = @((Invoke-Node A GET '/system/task').taskName)
    Assert-That 'The peer catalog sync is scheduled' ($tasks -contains 'PeerCatalogSync') @{ tasks = $tasks }
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
