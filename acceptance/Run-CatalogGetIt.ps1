#requires -Version 7
<#
.SYNOPSIS
    Get a movie, episode, season and album from the peer catalog, ending in real Arr imports.
.DESCRIPTION
    Reuses S7's two nodes, Sonarr and Radarr, and adds two pinned Lidarr instances. All data and
    volumes belong to acceptance/.run/s7 and nodearr-s7. Starting over deletes that test data.
    The album is six silent FLACs with the real CD release's tags and track lengths.
    This is a same-machine direct-route test; it does not prove connectivity between networks.
.PARAMETER SkipSetup
    Reuse S7 after Run-S7Transfers.ps1 -SetupOnly. B's test movie and series are removed first.
.PARAMETER KeepRunning
    Keep the test nodes and containers for inspection. Otherwise remove their test volumes.
#>
[CmdletBinding()]
param(
    [string] $TransportBin,
    [string] $NodearrDll,
    [int] $FirstPort = 6633,
    [switch] $SkipSetup,
    [switch] $KeepRunning
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 1
$run = Join-Path $PSScriptRoot '.run/s7'
$compose = Join-Path $PSScriptRoot 'docker-compose.getit.yml'
$envFile = Join-Path $run '.env.getit'
$results = [System.Collections.Generic.List[object]]::new()

function Invoke-MusicCompose {
    docker compose -p nodearr-s7 -f $compose --env-file $envFile @args
    if ($LASTEXITCODE -ne 0) { throw "Music containers: docker compose $args failed" }
}

function Invoke-Api {
    param([string] $Base, [string] $Key, [string] $Method, [string] $Path, $Body)
    $request = @{ Uri = "$Base$Path"; Headers = @{ 'X-Api-Key' = $Key }; Method = $Method }
    if ($null -ne $Body) { $request.Body = $Body | ConvertTo-Json -Depth 40; $request.ContentType = 'application/json' }
    $response = Invoke-RestMethod @request
    return $response
}

function Node {
    param([string] $Side, [string] $Method, [string] $Path, $Body)
    Invoke-Api "http://127.0.0.1:$($FirstPort + ($Side -eq 'B' ? 1 : 0))/api/v5" $keys.$Side $Method $Path $Body
}

function Arr {
    param([string] $App, [string] $Method, [string] $Path, $Body)
    $target = $apps[$App]
    Invoke-Api $target.Base $target.Key $Method $Path $Body
}

function Wait-Until {
    param([string] $What, [scriptblock] $Condition, [int] $Seconds = 180)
    $until = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $until) {
        $value = & $Condition
        if ($value) { return $value }
        Start-Sleep -Seconds 2
    }
    throw "Timed out waiting for $What"
}

function Check {
    param([string] $Name, [bool] $Passed, $Evidence)
    $results.Add(@{ check = $Name; passed = $Passed; evidence = $Evidence })
    Write-Host "[$($Passed ? 'PASS' : 'FAIL')] $Name"
    if (-not $Passed) { throw "$Name failed: $($Evidence | ConvertTo-Json -Depth 8 -Compress)" }
}

function Command {
    param([string] $Side, [hashtable] $Body)
    $queued = Node $Side POST '/command' $Body
    Wait-Until "Node $Side $($Body.name)" {
        $state = Node $Side GET "/command/$($queued.id)"
        if ($state.status -eq 'failed') { throw $state.message }
        $state.status -eq 'completed'
    } | Out-Null
}

function Get-It {
    param($Item, [string] $Scope = 'file', [int] $Season = 0)
    $path = "/catalog/peers/items/$($Item.id)/getit"
    $plan = Node B GET "${path}?scope=$Scope&seasonNumber=$Season"
    $target = @($plan.plan.targets)[0]
    if ($plan.plan.problem -or $target.problem -or -not $target.defaults) { throw ($plan | ConvertTo-Json -Depth 12) }
    $queued = Node B POST $path @{ scope = $Scope; seasonNumber = $Season; options = $target.defaults }
    Wait-Until "catalog Get it command $($queued.id)" {
        $state = Node B GET "/command/$($queued.id)"
        if ($state.status -eq 'failed') { throw $state.message }
        $state.status -eq 'completed'
    } | Out-Null
    Write-Host "Requested $($Item.title), $Scope, through $($target.applicationName)"
}

try {
    if (-not $SkipSetup) {
        if (Test-Path $envFile) { Invoke-MusicCompose down -v 2>&1 | Out-Null }
        & (Join-Path $PSScriptRoot 'Run-S7Transfers.ps1') -SetupOnly -TransportBin $TransportBin -NodearrDll $NodearrDll -FirstPort $FirstPort -UploadLimitKbps 4096
        if ($LASTEXITCODE -ne 0) { throw 'S7 setup failed' }
    }
    $keys = Get-Content (Join-Path $run 'node-keys.json') | ConvertFrom-Json
    $musicKeysFile = Join-Path $run 'music-keys.json'
    if (-not (Test-Path $musicKeysFile)) {
        @{ A = [guid]::NewGuid().ToString('N'); B = [guid]::NewGuid().ToString('N') } | ConvertTo-Json | Set-Content $musicKeysFile
    }
    $musicKeys = Get-Content $musicKeysFile | ConvertFrom-Json
    Get-Content (Join-Path $run '.env') | Set-Content $envFile
    Add-Content $envFile "LIDARR_API_KEY=$($musicKeys.A)`nLIDARR_B_API_KEY=$($musicKeys.B)"
    New-Item -ItemType Directory -Force (Join-Path $run 'library/music') | Out-Null
    Invoke-MusicCompose up -d 2>&1 | Out-Null

    $apps = @{
        sonarrB = @{ Base = 'http://127.0.0.1:28989/api/v3'; Key = $keys.sonarrB }
        radarrB = @{ Base = 'http://127.0.0.1:27878/api/v3'; Key = $keys.radarrB }
        lidarr = @{ Base = 'http://127.0.0.1:18686/api/v1'; Key = $musicKeys.A }
        lidarrB = @{ Base = 'http://127.0.0.1:28686/api/v1'; Key = $musicKeys.B }
    }

    # Real additions, not pre-created titles in B's Arr library.
    foreach ($series in (Arr sonarrB GET '/series')) { Arr sonarrB DELETE "/series/$($series.id)?deleteFiles=true" | Out-Null }
    foreach ($movie in (Arr radarrB GET '/movie')) { Arr radarrB DELETE "/movie/$($movie.id)?deleteFiles=true" | Out-Null }
    Command B @{ name = 'CatalogRefresh' }
    $items = (Node B GET '/catalog/peers/items?pageSize=50').records
    $movie = $items | Where-Object mediaKind -eq 'movie' | Select-Object -First 1
    $episode = $items | Where-Object { $_.mediaKind -eq 'tv' -and $_.episodeNumbers -contains 1 } | Select-Object -First 1
    $tvHash = (Get-FileHash (Join-Path $run 'media/episode-720p.mkv')).Hash
    $movieHash = (Get-FileHash (Join-Path $run 'media/movie-720p.mkv')).Hash

    Get-It $movie
    $film = Wait-Until 'Radarr movie import' {
        Arr radarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        Arr radarrB GET '/movie' | Where-Object hasFile | Select-Object -First 1
    }
    $hash = docker exec nodearr-s7-radarr-b-1 sha256sum $film.movieFile.path
    Check 'Catalog movie adds to Radarr and imports the offered bytes' ("$hash" -match $movieHash) @{ path = $film.movieFile.path; hashMatches = ("$hash" -match $movieHash) }

    Get-It $episode
    $one = Wait-Until 'Sonarr episode import' {
        Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $series = @(Arr sonarrB GET '/series')[0]
        if ($series) { Arr sonarrB GET "/episode?seriesId=$($series.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -eq 1 -and $_.hasFile } }
    }
    $series = @(Arr sonarrB GET '/series')[0]
    $file = Arr sonarrB GET "/episodefile/$($one.episodeFileId)"
    $hash = docker exec nodearr-s7-sonarr-b-1 sha256sum $file.path
    $others = @(Arr sonarrB GET "/episode?seriesId=$($series.id)" | Where-Object { $_.seasonNumber -eq 1 -and $_.episodeNumber -in 2, 3 -and $_.monitored })
    Check 'Catalog episode adds to Sonarr, imports its bytes and leaves other episodes unmonitored' ("$hash" -match $tvHash -and $others.Count -eq 0) @{ hashMatches = ("$hash" -match $tvHash); otherMonitored = $others.Count }

    Get-It $episode 'season' 1
    $seasonFiles = Wait-Until 'Sonarr season import' {
        Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        $files = @(Arr sonarrB GET "/episodefile?seriesId=$($series.id)")
        if ($files.Count -eq 3) { $files }
    }
    $matches = @($seasonFiles | Where-Object { "$(docker exec nodearr-s7-sonarr-b-1 sha256sum $_.path)" -match $tvHash })
    $seasons = (Arr sonarrB GET "/series/$($series.id)").seasons
    Check 'Catalog season monitors the selected season and imports all three offered episode files' ($matches.Count -eq 3 -and @($seasons | Where-Object { $_.monitored -and $_.seasonNumber -ne 1 }).Count -eq 0) @{ imported = $matches.Count; monitoredSeasons = @($seasons | Where-Object monitored | ForEach-Object seasonNumber) }

    # A PROPER of an episode B already holds must still reach Sonarr's profile decisions.
    . (Join-Path $PSScriptRoot 'Library.ps1')
    $senderKey = ((Get-Content (Join-Path $run '.env') | Where-Object { $_ -like 'SONARR_API_KEY=*' }) -split '=', 2)[1]
    Import-ArrDownload -ApiUrl 'http://127.0.0.1:18989/api/v3' -ApiKey $senderKey -App Sonarr -SourceFile (Join-Path $run 'media/episode-720p.mkv') `
        -Name 'Pingu.S01E01.PROPER.720p.WEB-DL.AAC2.0.H.264-NODEARR' -DownloadsDir (Join-Path $run 'downloads')
    Command A @{ name = 'CatalogRefresh' }
    Command B @{ name = 'PeerCatalogSync' }
    $upgrade = Wait-Until 'PROPER offered as an upgrade' { (Node B GET '/catalog/peers/items?pageSize=50').records | Where-Object { $_.localStatus -eq 'upgrade' -and $_.episodeNumbers -contains 1 } | Select-Object -First 1 }
    Get-It $upgrade
    $proper = Wait-Until 'Sonarr to import the offered upgrade' {
        Arr sonarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        Arr sonarrB GET "/episodefile?seriesId=$($series.id)" | Where-Object { $_.quality.revision.version -eq 2 }
    }
    Check 'Catalog upgrade is searched and imported under Sonarr policy' ("$(docker exec nodearr-s7-sonarr-b-1 sha256sum $proper.path)" -match $tvHash) @{ revision = $proper.quality.revision.version; path = $proper.path }

    # Music registration uses exactly the application schema and remote path mappings as the UI.
    foreach ($pair in @(@('A', 'lidarr', 'nodearr-s7-lidarr-1'), @('B', 'lidarrB', 'nodearr-s7-lidarr-b-1'))) {
        $side, $app, $container = $pair
        Wait-Until "$app API" { try { Arr $app GET '/system/status' } catch { $null } } | Out-Null
        docker exec $container chown abc:abc /music | Out-Null
        if (@(Arr $app GET '/rootfolder').Count -eq 0) {
            Arr $app POST '/rootfolder' @{ name = 'Music'; path = '/music'; defaultQualityProfileId = 1; defaultMetadataProfileId = 1; defaultMonitorOption = 'none'; defaultNewItemMonitorOption = 'none' } | Out-Null
        }
        if (-not (Node $side GET '/applications' | Where-Object implementation -eq Lidarr)) {
            $definition = Node $side GET '/applications/schema' | Where-Object implementation -eq Lidarr
            foreach ($entry in @{ baseUrl = $apps[$app].Base.Replace('/api/v1', ''); apiKey = $apps[$app].Key; nodearrUrl = "http://host.docker.internal:$($FirstPort + ($side -eq 'B' ? 1 : 0))" }.GetEnumerator()) {
                ($definition.fields | Where-Object name -eq $entry.Key) | Add-Member -Force -NotePropertyName value -NotePropertyValue $entry.Value
            }
            $definition | Add-Member -Force -NotePropertyMembers @{ name = 'Lidarr'; syncLevel = 'fullSync' }
            Node $side POST '/applications' $definition | Out-Null
        }
    }
    Node A POST '/remotepathmapping' @{ host = '127.0.0.1'; remotePath = '/music/'; localPath = (Join-Path $run 'library/music') + [IO.Path]::DirectorySeparatorChar } | Out-Null
    Arr lidarrB POST '/remotepathmapping' @{ host = 'host.docker.internal'; remotePath = (Join-Path $run 'downloads-b/complete') + [IO.Path]::DirectorySeparatorChar; localPath = '/downloads/complete/' } | Out-Null

    $artist = Arr lidarr GET '/artist' | Where-Object artistName -eq Slint
    if (-not $artist) {
        $artist = @(Arr lidarr GET '/artist/lookup?term=lidarr:2869c510-2679-4a3f-bf15-e5e8b49f2f28')[0]
        $artist | Add-Member -Force -NotePropertyMembers @{ qualityProfileId = 1; metadataProfileId = 1; rootFolderPath = '/music'; monitored = $true; monitorNewItems = 'none'; addOptions = @{ monitor = 'none'; searchForMissingAlbums = $false } }
        $artist = Arr lidarr POST '/artist' $artist
    }
    $album = Wait-Until 'Spiderland metadata' { Arr lidarr GET "/album?artistId=$($artist.id)" | Where-Object title -eq Spiderland }
    $release = $album.releases | Where-Object foreignReleaseId -eq '2c8cba8b-a80a-4d7f-9a7f-22c922f1a42e'
    if (-not $release) { throw 'The six-track CD release is no longer in Lidarr metadata' }
    foreach ($edition in $album.releases) { $edition.monitored = $edition.id -eq $release.id }
    $album.monitored = $true
    $album.anyReleaseOk = $false
    Arr lidarr PUT "/album/$($album.id)" $album | Out-Null
    $tracks = @(Arr lidarr GET "/track?albumId=$($album.id)&albumReleaseId=$($release.id)" | Sort-Object absoluteTrackNumber)
    if ($tracks.Count -ne 6) { throw "Expected six tracks, found $($tracks.Count)" }
    $seed = Join-Path $run 'downloads/seed/Slint - Spiderland (1991) [FLAC]'
    New-Item -ItemType Directory -Force $seed | Out-Null
    foreach ($track in $tracks) {
        $path = Join-Path $seed ('{0:00}.flac' -f $track.absoluteTrackNumber)
        ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'anullsrc=r=44100:cl=stereo' -t ($track.duration / 1000.0).ToString([Globalization.CultureInfo]::InvariantCulture) -c:a flac `
            -metadata artist=Slint -metadata album=Spiderland -metadata "title=$($track.title)" -metadata "track=$($track.absoluteTrackNumber)/6" -metadata disc=1/1 -metadata date=1991 `
            -metadata "MUSICBRAINZ_ARTISTID=$($artist.foreignArtistId)" -metadata "MUSICBRAINZ_ALBUMARTISTID=$($artist.foreignArtistId)" `
            -metadata "MUSICBRAINZ_ALBUMID=$($release.foreignReleaseId)" -metadata "MUSICBRAINZ_RELEASEGROUPID=$($album.foreignAlbumId)" -metadata "MUSICBRAINZ_RELEASETRACKID=$($track.foreignTrackId)" $path
        if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed creating a test track' }
    }
    Arr lidarr POST '/command' @{ name = 'DownloadedAlbumsScan'; path = '/downloads/seed/Slint - Spiderland (1991) [FLAC]'; importMode = 'copy' } | Out-Null
    Wait-Until 'Lidarr A to import six test tracks' { (Arr lidarr GET "/album/$($album.id)").statistics.trackFileCount -eq 6 } | Out-Null
    # Compare the files actually offered. Lidarr may write tags when importing the seed.
    $sourceFiles = @(Arr lidarr GET "/trackfile?artistId=$($artist.id)")
    $sourceHashes = @($sourceFiles | ForEach-Object { ("$(docker exec nodearr-s7-lidarr-1 sha256sum $_.path)" -split ' ')[0].ToUpperInvariant() } | Sort-Object)
    Command A @{ name = 'CatalogRefresh' }
    Command B @{ name = 'PeerCatalogSync' }
    $offered = Wait-Until 'album in peer catalog' { (Node B GET '/catalog/peers/items?pageSize=50').records | Where-Object mediaKind -eq music | Select-Object -First 1 }
    Get-It $offered 'title'
    $albumB = Wait-Until 'Lidarr B to import six tracks' {
        Arr lidarrB POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        Arr lidarrB GET '/album' | Where-Object { $_.foreignAlbumId -eq $album.foreignAlbumId -and $_.statistics.trackFileCount -eq 6 }
    }
    $files = @(Arr lidarrB GET "/trackfile?artistId=$($albumB.artistId)")
    $landedHashes = @($files | ForEach-Object { ("$(docker exec nodearr-s7-lidarr-b-1 sha256sum $_.path)" -split ' ')[0].ToUpperInvariant() } | Sort-Object)
    $otherAlbums = @(Arr lidarrB GET "/album?artistId=$($albumB.artistId)" | Where-Object { $_.id -ne $albumB.id -and $_.monitored })
    Check 'Catalog album adds its artist to Lidarr, imports six identical tracks and monitors only that album' ($files.Count -eq 6 -and ($sourceHashes -join ',') -eq ($landedHashes -join ',') -and $otherAlbums.Count -eq 0) @{ imported = $files.Count; hashesMatch = (($sourceHashes -join ',') -eq ($landedHashes -join ',')); otherMonitored = $otherAlbums.Count }
    Check 'Catalog requests do not create received sends on Home' (@(Node B GET '/sharing/sends/received').Count -eq 0) @{}
}
catch {
    $results.Add(@{ check = 'Script ran to the end'; passed = $false; evidence = "$_" })
    Write-Host "Stopped: $_" -ForegroundColor Red
}
finally {
    $failed = @($results | Where-Object { -not $_.passed })
    @{ ranAt = (Get-Date).ToUniversalTime().ToString('o'); passed = ($failed.Count -eq 0 -and $results.Count -gt 0); checks = $results } | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'getit-results.json')
    if (-not $KeepRunning) {
        if (Test-Path $envFile) { Invoke-MusicCompose down -v 2>&1 | Out-Null }
        & (Join-Path $PSScriptRoot 'Run-S7Transfers.ps1') -Down
    }
}
if ($failed.Count -gt 0) { exit 1 }
