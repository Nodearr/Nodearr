<#
.SYNOPSIS
    Sets up Nodearr, Sonarr and Radarr on Windows for testing Nodearr by hand, in its own pages.

.DESCRIPTION
    The script only sets the scene. It starts Nodearr from its release package, and Sonarr and
    Radarr from their own Windows packages, each with a small library of test videos. Everything
    you test in Nodearr, from signing in to sending a friend a series, you do in Nodearr's pages.
    The guide is docs/guides/ui-test.md.

        up -Solo           two complete set-ups on this machine, A and B, to play both friends
        up -Library A      one set-up, with library A. Your friend runs "up -Library B" on theirs
        status             where every page is, the API keys to connect the apps, what is running
        stop               stop everything, and keep it to start again with "up"
        down               stop everything and remove what this script made

    Library A is Night of the Living Dead and three episodes of Pingu. Library B is Plan 9 from
    Outer Space and three episodes of Bluey. The videos are test patterns made with ffmpeg, named
    as releases are, so Sonarr and Radarr import them as those titles.

    Sonarr, Radarr and ffmpeg are downloaded from their GitHub releases and checked against the
    SHA-256 each release publishes. Nothing is installed. Everything lives in ~\nodearr-ui-test,
    and every page is reachable from this machine only. The only thing another machine ever needs
    is Nodearr's UDP port.

    Sonarr and Radarr start with signing in turned off, on this machine only. Nodearr starts as a
    new install does, asking you to set up signing in, which is part of the test.

.PARAMETER Command
    up, status, stop or down.

.PARAMETER Solo
    With "up": both set-ups, A and B, on this machine.

.PARAMETER Library
    With "up": which set-up this machine gets when testing with a friend. Default A.

.PARAMETER NodearrZip
    The Nodearr package, Nodearr.<version>.win-x64.zip. By default the newest one in Downloads,
    beside this script, or in the test folder.

.PARAMETER OpenFirewall
    With "up": add a Windows Firewall rule that lets UDP in to Nodearr's transport. Needs
    PowerShell run as administrator. "down" removes it. Without it, allow nodearr-transport when
    Windows asks.

.PARAMETER Dir
    Where this script keeps everything. Default ~\nodearr-ui-test.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('up', 'status', 'stop', 'down')]
    [string] $Command,

    [switch] $Solo,

    [ValidateSet('A', 'B')]
    [string] $Library = 'A',

    [string] $NodearrZip,
    [switch] $OpenFirewall,
    [string] $Dir = (Join-Path $HOME 'nodearr-ui-test')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# What each set-up holds and where it listens. B's ports differ from A's, so both can run on one
# machine, and neither clashes with an installed Nodearr, Sonarr or Radarr on their usual ports.
$Sides = @{
    A = @{
        NodearrPort = 26633; SonarrPort = 28989; RadarrPort = 27878
        Movie       = @{ TmdbId = 10331; Title = 'Night of the Living Dead'; Release = 'Night.of.the.Living.Dead.1968.720p.WEB-DL.AAC2.0.H.264-NODEARR' }
        Series      = @{ TvdbId = 75669; Title = 'Pingu'; Release = 'Pingu.S01E0{0}.720p.WEB-DL.AAC2.0.H.264-NODEARR' }
    }
    B = @{
        NodearrPort = 26643; SonarrPort = 28990; RadarrPort = 27879
        Movie       = @{ TmdbId = 10513; Title = 'Plan 9 from Outer Space'; Release = 'Plan.9.from.Outer.Space.1957.720p.WEB-DL.AAC2.0.H.264-NODEARR' }
        Series      = @{ TvdbId = 353546; Title = 'Bluey'; Release = 'Bluey.2018.S01E0{0}.720p.WEB-DL.AAC2.0.H.264-NODEARR' }
    }
}

$SonarrPackage = @{
    Url    = 'https://github.com/Sonarr/Sonarr/releases/download/v4.0.20.3014/Sonarr.main.4.0.20.3014.win-x64.zip'
    Sha256 = '40ffa15710d791e71ab787e152610602e4e0d9ce4c2316e3b618c3c3d265d2af'
}
$RadarrPackage = @{
    Url    = 'https://github.com/Radarr/Radarr/releases/download/v6.4.4.10685/Radarr.master.6.4.4.10685.windows-core-x64.zip'
    Sha256 = '316c3c7e5edaab75f5497d919bc556da259d966f9497a465d484cd49df28afe9'
}
$FfmpegPackage = @{
    Url    = 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip'
    Sha256 = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
}

# A film shorter than this is taken for a sample and not imported. An episode needs less.
$MovieSeconds = 660
$EpisodeSeconds = 120

$ReleasesPage = 'https://github.com/Nodearr/Nodearr/releases'
$FirewallRule = 'Nodearr UI test'
$ThisScript = 'powershell -ExecutionPolicy Bypass -File ui-test.ps1'

# Written into each set-up's state, so that a folder made by anything else is never taken for one.
$MadeBy = 'tools/ui-test.ps1'

class StopTest : Exception {
    StopTest([string] $message) : base($message) {}
}

function Stop-Test([string] $Message) { throw [StopTest]::new($Message) }
function Say([string] $Text = '') { Write-Host $Text }
function Step([string] $Text) { Write-Host ''; Write-Host "== $Text" }

# --- State -----------------------------------------------------------------------------------

function Get-SideFolder([string] $Which) { Join-Path $Dir $Which.ToLowerInvariant() }

function Read-State([string] $Which) {
    $path = Join-Path (Get-SideFolder $Which) 'state.json'
    if (-not (Test-Path $path)) { return $null }

    $state = @{}
    (Get-Content $path -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $state[$_.Name] = $_.Value }

    if ($state.made_by -ne $MadeBy) {
        Stop-Test ("$(Get-SideFolder $Which) was made by something else, not by this script, and is left as it is.`n" +
            '  Choose another folder for this test: -Dir PATH')
    }

    return $state
}

function Save-State([hashtable] $State) {
    New-Item -ItemType Directory -Force $State.folder | Out-Null
    $State | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $State.folder 'state.json') -Encoding UTF8
}

function Get-SetUpSides { @('A', 'B' | Where-Object { Test-Path (Join-Path (Get-SideFolder $_) 'state.json') }) }

# --- Downloads -------------------------------------------------------------------------------

function Get-CheckedDownload([hashtable] $What) {
    $cache = Join-Path $Dir 'cache'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $file = Join-Path $cache ($What.Url -split '/')[-1]

    if ((Test-Path $file -PathType Leaf) -and (Get-FileHash $file -Algorithm SHA256).Hash -eq $What.Sha256) { return $file }

    Say "  Downloading $(Split-Path $file -Leaf)"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $What.Url -OutFile $file
    }
    catch {
        Stop-Test "Could not download $($What.Url): $($_.Exception.Message)"
    }

    $hash = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($hash -ne $What.Sha256) {
        Remove-Item $file -Force
        Stop-Test "$(Split-Path $file -Leaf) did not match the SHA-256 its release publishes, and was deleted. Got $hash."
    }

    return $file
}

function Expand-Package([string] $Zip, [string] $Destination) {
    # Extracted beside the destination and moved into place, so a half-extracted package is never
    # taken for a whole one.
    $temporary = "$Destination.partial"
    if (Test-Path $temporary) { Remove-Item $temporary -Recurse -Force }

    Unblock-File $Zip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($Zip, $temporary)

    if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
    Move-Item $temporary $Destination
}

function Find-NodearrZip {
    if ($NodearrZip) {
        if (-not (Test-Path $NodearrZip)) { Stop-Test "There is no file at $NodearrZip." }
        return (Resolve-Path $NodearrZip).Path
    }

    $places = @((Join-Path $HOME 'Downloads'), $PSScriptRoot, $Dir) | Where-Object { $_ -and (Test-Path $_) }
    $found = $places | ForEach-Object { Get-ChildItem $_ -File -Filter 'Nodearr.*.win-x64.zip' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $found) {
        Stop-Test ("There is no Nodearr package here. Download Nodearr.<version>.win-x64.zip from`n  $ReleasesPage`n" +
            "into your Downloads folder and run this again, or say where it is: -NodearrZip PATH")
    }

    return $found.FullName
}

function Test-NodearrZip([string] $Zip) {
    $sums = Join-Path (Split-Path $Zip) 'SHA256SUMS'
    if (-not (Test-Path $sums)) { return 'not checked (SHA256SUMS was not downloaded beside it)' }

    $line = Get-Content $sums | Where-Object { $_ -match ([regex]::Escape((Split-Path $Zip -Leaf)) + '$') } | Select-Object -First 1
    if (-not $line) { return 'not checked (SHA256SUMS does not list it)' }

    $expected = ($line -split '\s+')[0]
    $hash = (Get-FileHash $Zip -Algorithm SHA256).Hash
    if ($hash -ne $expected) { Stop-Test "$Zip does not match the release's SHA256SUMS. Download it again." }

    return 'matches the release''s SHA256SUMS'
}

# --- Processes -------------------------------------------------------------------------------

function Test-PortInUse([int] $Port) {
    $client = New-Object Net.Sockets.TcpClient
    try {
        $wait = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        return ($wait.AsyncWaitHandle.WaitOne(500) -and $client.Connected)
    }
    catch { return $false }
    finally { $client.Close() }
}

function Start-Hidden([string] $Program, [string[]] $Arguments, [hashtable] $Environment) {
    # A child takes the environment it is started with, and Start-Process in Windows PowerShell
    # has no way to give it one, so it is set here and taken away again.
    $saved = @{}
    foreach ($key in $Environment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }

    try {
        $quoted = $Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
        return (Start-Process -FilePath $Program -ArgumentList $quoted -WindowStyle Hidden -PassThru).Id
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
}

function Stop-Tree($ProcessId) {
    if (-not $ProcessId) { return }
    if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        # /T takes Nodearr's transport with it.
        & taskkill.exe /PID $ProcessId /T /F 2>&1 | Out-Null
    }
}

function Wait-Until([string] $What, [scriptblock] $Condition, [int] $Timeout = 120, [int] $Every = 2) {
    $deadline = (Get-Date).AddSeconds($Timeout)
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            $value = & $Condition
            if ($value) { return $value }
        }
        catch { $lastError = $_.Exception.Message }

        Start-Sleep -Seconds $Every
    }

    $message = "Gave up after ${Timeout}s waiting for $What."
    if ($lastError) { $message += " Last answer: $lastError" }
    Stop-Test $message
}

# --- Sonarr's and Radarr's APIs --------------------------------------------------------------

function Invoke-Arr([string] $Base, [string] $Key, [string] $Method, [string] $Path, $Body, [int] $Timeout = 60) {
    $request = @{
        Uri             = $Base + $Path
        Method          = $Method
        Headers         = @{ 'X-Api-Key' = $Key }
        TimeoutSec      = $Timeout
        UseBasicParsing = $true
    }

    if ($null -ne $Body) {
        # As bytes: Windows PowerShell would otherwise send the text as Latin-1.
        $request.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 30 -Compress))
        $request.ContentType = 'application/json; charset=utf-8'
    }

    try {
        # Windows PowerShell hands on a JSON list as one object. Through the pipeline it comes out
        # item by item, as in PowerShell 7.
        return Invoke-RestMethod @request | ForEach-Object { $_ }
    }
    catch [StopTest] { throw }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) { throw }

        $detail = "$($_.ErrorDetails.Message)"
        if ($detail.Length -gt 600) { $detail = $detail.Substring(0, 600) }
        Stop-Test "$Method $Base$Path answered $([int] $response.StatusCode): $detail"
    }
}

function Sonarr($State, [string] $Method, [string] $Path, $Body, [int] $Timeout = 60) {
    Invoke-Arr "http://127.0.0.1:$($State.sonarr_port)/api/v3" $State.sonarr_key $Method $Path $Body $Timeout
}

function Radarr($State, [string] $Method, [string] $Path, $Body, [int] $Timeout = 60) {
    Invoke-Arr "http://127.0.0.1:$($State.radarr_port)/api/v3" $State.radarr_key $Method $Path $Body $Timeout
}

function Invoke-ArrCommand([scriptblock] $App, $State, [hashtable] $Body, [int] $Timeout = 300) {
    $queued = & $App $State POST '/command' $Body

    Wait-Until "$($Body.name) to finish" {
        $now = & $App $State GET "/command/$($queued.id)"
        if ($now.status -eq 'failed') { Stop-Test "$($Body.name) failed: $($now.message)" }
        $now.status -eq 'completed'
    } -Timeout $Timeout -Every 1 | Out-Null
}

function Test-NodearrUp($State) {
    try {
        Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$($State.nodearr_port)/ping" -TimeoutSec 3 | Out-Null
        return $true
    }
    catch { return $false }
}

function Test-ArrUp([scriptblock] $App, $State) {
    try { & $App $State GET '/system/status' $null 3 | Out-Null; return $true } catch { return $false }
}

# --- Setting a side up -----------------------------------------------------------------------

function New-Key {
    $bytes = New-Object byte[] 16
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

function Get-SideState([string] $Which) {
    $state = Read-State $Which
    if ($null -ne $state) { return $state }

    $side = $Sides[$Which]
    $state = @{
        made_by      = $MadeBy
        side         = $Which
        folder       = Get-SideFolder $Which
        nodearr_port = $side.NodearrPort
        sonarr_port  = $side.SonarrPort
        radarr_port  = $side.RadarrPort
        sonarr_key   = New-Key
        radarr_key   = New-Key
        library_done = $false
    }
    Save-State $state
    return $state
}

function Install-Apps($State) {
    $folder = $State.folder
    Step "Set-up $($State.side): getting Nodearr, Sonarr and Radarr"

    $zip = Find-NodearrZip
    $check = Test-NodearrZip $zip

    if ($State.nodearr_zip -ne $zip -or -not (Test-Path (Join-Path $folder 'nodearr\Nodearr\Nodearr.Console.exe'))) {
        if (Test-NodearrUp $State) { Stop-Test "Nodearr $($State.side) is running. Run ""stop"" first to change its package." }
        Expand-Package $zip (Join-Path $folder 'nodearr')
        $State.nodearr_zip = $zip
        Save-State $State
    }
    Say "  Nodearr: $(Split-Path $zip -Leaf), $check"

    if (-not (Test-Path (Join-Path $folder 'sonarr\Sonarr'))) { Expand-Package (Get-CheckedDownload $SonarrPackage) (Join-Path $folder 'sonarr') }
    Say "  Sonarr:  $(($SonarrPackage.Url -split '/')[-1])"

    if (-not (Test-Path (Join-Path $folder 'radarr\Radarr'))) { Expand-Package (Get-CheckedDownload $RadarrPackage) (Join-Path $folder 'radarr') }
    Say "  Radarr:  $(($RadarrPackage.Url -split '/')[-1])"
}

function Start-Apps($State) {
    $folder = $State.folder
    foreach ($name in 'tv', 'movies', 'seed', 'sonarr-data', 'radarr-data', 'nodearr-data') {
        New-Item -ItemType Directory -Force (Join-Path $folder $name) | Out-Null
    }

    $sonarrUp = Test-ArrUp ${function:Sonarr} $State
    $radarrUp = Test-ArrUp ${function:Radarr} $State
    $nodearrUp = Test-NodearrUp $State

    $checks = @(
        @{ What = "Nodearr $($State.side)'s page"; Port = $State.nodearr_port; Up = $nodearrUp },
        @{ What = "Sonarr $($State.side)'s page"; Port = $State.sonarr_port; Up = $sonarrUp },
        @{ What = "Radarr $($State.side)'s page"; Port = $State.radarr_port; Up = $radarrUp }
    )
    foreach ($check in $checks) {
        if (-not $check.Up -and (Test-PortInUse $check.Port)) {
            Stop-Test "Port $($check.Port), for $($check.What), is already in use on this machine. Stop what is using it, and run this again."
        }
    }

    Step "Set-up $($State.side): starting Sonarr, Radarr and Nodearr"

    if (-not $sonarrUp) {
        $State.sonarr_pid = Start-Hidden (Join-Path $folder 'sonarr\Sonarr\Sonarr.Console.exe') @('-nobrowser', "-data=$(Join-Path $folder 'sonarr-data')") @{
            SONARR__AUTH__APIKEY          = $State.sonarr_key
            SONARR__AUTH__METHOD          = 'External'
            SONARR__SERVER__PORT          = "$($State.sonarr_port)"
            SONARR__SERVER__BINDADDRESS   = '127.0.0.1'
            SONARR__LOG__ANALYTICSENABLED = 'false'
        }
        Save-State $State
    }

    if (-not $radarrUp) {
        $State.radarr_pid = Start-Hidden (Join-Path $folder 'radarr\Radarr\Radarr.Console.exe') @('-nobrowser', "-data=$(Join-Path $folder 'radarr-data')") @{
            RADARR__AUTH__APIKEY          = $State.radarr_key
            RADARR__AUTH__METHOD          = 'External'
            RADARR__SERVER__PORT          = "$($State.radarr_port)"
            RADARR__SERVER__BINDADDRESS   = '127.0.0.1'
            RADARR__LOG__ANALYTICSENABLED = 'false'
        }
        Save-State $State
    }

    if (-not $nodearrUp) {
        # Before the transport first listens, so that Windows has nothing to ask.
        if ($OpenFirewall) { Add-FirewallRule $State }

        # As a new install starts: signing in is set up in its pages, and so is everything else.
        # Only the ports are fixed, so that two set-ups can share this machine.
        $State.nodearr_pid = Start-Hidden (Join-Path $folder 'nodearr\Nodearr\Nodearr.Console.exe') @('-nobrowser', "-data=$(Join-Path $folder 'nodearr-data')") @{
            Nodearr__Server__Port          = "$($State.nodearr_port)"
            Nodearr__Server__BindAddress   = '127.0.0.1'
            Nodearr__Transport__Port       = "$($State.nodearr_port)"
            Nodearr__Log__AnalyticsEnabled = 'false'
        }
        Save-State $State

        if (-not $OpenFirewall) {
            Say '  If Windows asks whether to let nodearr-transport through the firewall, allow it.'
            Say '  The question can open behind other windows. Cancelling it blocks the transport.'
        }
    }

    Wait-Until "Sonarr $($State.side) to start" { Test-ArrUp ${function:Sonarr} $State } -Timeout 300 | Out-Null
    Wait-Until "Radarr $($State.side) to start" { Test-ArrUp ${function:Radarr} $State } -Timeout 300 | Out-Null
    Wait-Until "Nodearr $($State.side) to start (its logs are in $(Join-Path $folder 'nodearr-data\logs'))" { Test-NodearrUp $State } -Timeout 180 | Out-Null

    foreach ($rule in Get-BlockingRules $State) {
        Say "  Warning: the firewall rule `"$rule`" blocks nodearr-transport. Remove it in Windows Defender Firewall, Inbound Rules."
    }
}

function Get-TransportPath($State) { Join-Path $State.folder 'nodearr\Nodearr\nodearr-transport.exe' }

function Add-FirewallRule($State) {
    $admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { Stop-Test '-OpenFirewall needs PowerShell run as administrator. Or leave it off and allow nodearr-transport when Windows asks.' }

    $name = "$FirewallRule ($($State.side))"
    if (-not (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $name -Direction Inbound -Program (Get-TransportPath $State) -Protocol UDP -Action Allow -Profile Any | Out-Null
    }

    $State.firewall_rule = $name
    Save-State $State
    Say "  Added the firewall rule `"$name`": UDP in to nodearr-transport."
}

function Get-BlockingRules($State) {
    # Cancelling Windows' question adds rules that block the program, and a block wins over an allow.
    try {
        return @(Get-NetFirewallApplicationFilter -Program (Get-TransportPath $State) -ErrorAction SilentlyContinue | Get-NetFirewallRule | Where-Object { $_.Action -eq 'Block' -and $_.Enabled -eq 'True' } | ForEach-Object { $_.DisplayName } | Sort-Object -Unique)
    }
    catch { return @() }
}

# --- The test library ------------------------------------------------------------------------

function Get-Ffmpeg {
    $folder = Join-Path $Dir 'ffmpeg'
    $program = Get-ChildItem $folder -Recurse -Filter 'ffmpeg.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($program) { return $program.FullName }

    Expand-Package (Get-CheckedDownload $FfmpegPackage) $folder
    return (Get-ChildItem $folder -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1).FullName
}

# A test pattern with a tone, named and imported as a release of the title. Made once, and copied
# for each title that needs one of its length.
function Get-TestVideo([string] $Kind) {
    $media = Join-Path $Dir 'media'
    New-Item -ItemType Directory -Force $media | Out-Null
    $file = Join-Path $media "$Kind.mkv"
    if (Test-Path $file) { return $file }

    $seconds = $(if ($Kind -eq 'movie') { $MovieSeconds } else { $EpisodeSeconds })
    $source = $(if ($Kind -eq 'movie') { 'smptebars=size=1280x720:rate=5' } else { 'testsrc2=size=1280x720:rate=5' })

    Say "  Making a $([int] ($seconds / 60))-minute test video"
    $ffmpeg = Get-Ffmpeg
    $arguments = @('-hide_banner', '-loglevel', 'error', '-y', '-f', 'lavfi', '-i', $source, '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000',
        '-t', "$seconds", '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p', '-crf', '30', '-c:a', 'aac', '-b:a', '64k', "$file.partial.mkv")
    & $ffmpeg @arguments
    if ($LASTEXITCODE -ne 0) { Stop-Test 'ffmpeg could not make the test video.' }
    Move-Item "$file.partial.mkv" $file -Force

    return $file
}

function Import-Release($State, [scriptblock] $App, [string] $ScanCommand, [string] $Video, [string] $Release) {
    $folder = Join-Path $State.folder "seed\$Release"
    New-Item -ItemType Directory -Force $folder | Out-Null
    Copy-Item $Video (Join-Path $folder "$Release.mkv") -Force
    Invoke-ArrCommand $App $State @{ name = $ScanCommand; path = $folder; importMode = 'copy' }
}

function Initialize-Library($State) {
    if ($State.library_done) { return }

    $side = $Sides[$State.side]
    $movies = Join-Path $State.folder 'movies'
    $tv = Join-Path $State.folder 'tv'

    Step "Set-up $($State.side): the test library, $($side.Movie.Title) and three episodes of $($side.Series.Title)"

    if (-not (@(Radarr $State GET '/rootfolder') | Where-Object { $_.path.TrimEnd('\') -eq $movies })) { Radarr $State POST '/rootfolder' @{ path = $movies } | Out-Null }
    if (-not (@(Sonarr $State GET '/rootfolder') | Where-Object { $_.path.TrimEnd('\') -eq $tv })) { Sonarr $State POST '/rootfolder' @{ path = $tv } | Out-Null }

    # The film, from TMDb by Radarr's own lookup.
    $movie = @(Radarr $State GET '/movie') | Where-Object { $_.tmdbId -eq $side.Movie.TmdbId } | Select-Object -First 1
    if (-not $movie) {
        $found = Wait-Until "Radarr $($State.side) to look the film up" { Radarr $State GET "/movie/lookup/tmdb?tmdbId=$($side.Movie.TmdbId)" } -Timeout 90 -Every 5
        $found | Add-Member -Force qualityProfileId 1
        $found | Add-Member -Force rootFolderPath $movies
        $found | Add-Member -Force monitored $true
        $found | Add-Member -Force minimumAvailability 'released'
        $found | Add-Member -Force addOptions @{ searchForMovie = $false }
        $movie = Radarr $State POST '/movie' $found
    }

    # The series, from TheTVDB by Sonarr's own lookup, every season monitored as Sonarr's default.
    $series = @(Sonarr $State GET '/series') | Where-Object { $_.tvdbId -eq $side.Series.TvdbId } | Select-Object -First 1
    if (-not $series) {
        $found = (Wait-Until "Sonarr $($State.side) to look the series up" { @(Sonarr $State GET "/series/lookup?term=tvdb:$($side.Series.TvdbId)") } -Timeout 90 -Every 5)[0]
        $found | Add-Member -Force qualityProfileId 1
        $found | Add-Member -Force rootFolderPath $tv
        $found | Add-Member -Force monitored $true
        $found | Add-Member -Force seasonFolder $true
        $found | Add-Member -Force addOptions @{ monitor = 'all'; searchForMissingEpisodes = $false; searchForCutoffUnmetEpisodes = $false }
        $series = Sonarr $State POST '/series' $found
    }

    Wait-Until "Sonarr $($State.side) to read the episodes" { @(Sonarr $State GET "/episode?seriesId=$($series.id)").Count -gt 3 } -Timeout 180 -Every 3 | Out-Null

    if (-not (Radarr $State GET "/movie/$($movie.id)").hasFile) {
        Import-Release $State ${function:Radarr} 'DownloadedMoviesScan' (Get-TestVideo 'movie') $side.Movie.Release
    }

    foreach ($episode in 1..3) {
        Import-Release $State ${function:Sonarr} 'DownloadedEpisodesScan' (Get-TestVideo 'episode') ($side.Series.Release -f $episode)
    }

    $films = @(Radarr $State GET '/movie' | Where-Object { $_.hasFile }).Count
    $episodes = @(Sonarr $State GET "/episodefile?seriesId=$($series.id)").Count
    if ($films -lt 1 -or $episodes -lt 3) {
        Stop-Test "The test library was not imported: $films film and $episodes episodes. Sonarr's and Radarr's logs are under $($State.folder)."
    }

    Remove-Item (Join-Path $State.folder 'seed') -Recurse -Force -ErrorAction SilentlyContinue
    $State.library_done = $true
    Save-State $State
    Say "  Radarr has $($side.Movie.Title), and Sonarr three episodes of $($side.Series.Title)."
}

# --- What to tell the person testing ---------------------------------------------------------

function Get-Card($State) {
    $side = $Sides[$State.side]
    $running = Test-NodearrUp $State

    # A browser keeps one sign-in per host name, whatever the port. B's Nodearr is opened by
    # another name for this machine, so that signing in to one does not sign out of the other.
    $address = $(if ($State.side -eq 'B') { '127.0.0.1' } else { 'localhost' })

    return @(
        "Set-up $($State.side)$(if (-not $running) { ' (stopped: run up to start it)' })",
        "  Nodearr  http://$($address):$($State.nodearr_port)",
        "  Sonarr   http://localhost:$($State.sonarr_port)   API key $($State.sonarr_key)",
        "  Radarr   http://localhost:$($State.radarr_port)   API key $($State.radarr_key)",
        "  Library  $($side.Movie.Title), and $($side.Series.Title) S01E01 to S01E03",
        "  Nodearr's UDP port, for another machine: $($State.nodearr_port)"
    )
}

function Show-Cards([string[]] $Which) {
    $lines = @()
    foreach ($w in $Which) {
        $state = Read-State $w
        if ($state) { $lines += Get-Card $state; $lines += '' }
    }

    Step 'Ready'
    $lines | ForEach-Object { Say $_ }
    Say 'In Nodearr, connect Sonarr and Radarr with the addresses and API keys above.'
    Say 'Everything else is in the guide: docs/guides/ui-test.md.'

    # Kept beside everything else, to look up later.
    $lines | Set-Content (Join-Path $Dir 'test-card.txt') -Encoding UTF8
}

# --- The commands ----------------------------------------------------------------------------

function Invoke-Up {
    $which = $(if ($Solo) { @('A', 'B') } else { @($Library) })

    # Testing with a friend is one set-up per machine. The other one, set up earlier, is left alone.
    foreach ($w in $which) {
        $state = Get-SideState $w
        Install-Apps $state
        Start-Apps $state
        Initialize-Library $state
    }

    Show-Cards (Get-SetUpSides)
}

function Invoke-Status {
    $which = Get-SetUpSides
    if (-not $which) { Say "Nothing is set up. Run: $ThisScript up -Solo, or up -Library A"; return }

    foreach ($w in $which) {
        $state = Read-State $w
        Get-Card $state | ForEach-Object { Say $_ }
        foreach ($rule in Get-BlockingRules $state) { Say "  Warning: the firewall rule `"$rule`" blocks nodearr-transport." }
        Say ''
    }
}

function Invoke-Stop {
    foreach ($w in Get-SetUpSides) {
        $state = Read-State $w
        Stop-Tree $state.nodearr_pid
        Stop-Tree $state.sonarr_pid
        Stop-Tree $state.radarr_pid
        Say "Stopped set-up $w. Run ""up"" to start it again as it was."
    }
}

function Invoke-Down {
    $which = Get-SetUpSides
    if (-not $which) { Say 'Nothing to remove.' }

    foreach ($w in $which) {
        $state = Read-State $w
        Stop-Tree $state.nodearr_pid
        Stop-Tree $state.sonarr_pid
        Stop-Tree $state.radarr_pid
        Start-Sleep -Seconds 2

        if ($state.firewall_rule) {
            try { Remove-NetFirewallRule -DisplayName $state.firewall_rule -ErrorAction Stop; Say "Removed the firewall rule `"$($state.firewall_rule)`"." }
            catch { Say "Could not remove the firewall rule `"$($state.firewall_rule)`": run this as administrator, or remove it in Windows Defender Firewall." }
        }

        # Only ever the folder this script made.
        Remove-Item $state.folder -Recurse -Force -ErrorAction SilentlyContinue
        Say "Stopped set-up $w and removed $($state.folder)."
    }

    if (-not (Get-SetUpSides)) {
        foreach ($name in 'cache', 'ffmpeg', 'media', 'test-card.txt') { Remove-Item (Join-Path $Dir $name) -Recurse -Force -ErrorAction SilentlyContinue }
        if ((Test-Path $Dir) -and -not (Get-ChildItem $Dir -Force)) { Remove-Item $Dir -Force }
    }
}

try {
    switch ($Command) {
        'up' { Invoke-Up }
        'status' { Invoke-Status }
        'stop' { Invoke-Stop }
        'down' { Invoke-Down }
    }
}
catch [StopTest] {
    Write-Host ''
    Write-Host "Stopped: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
