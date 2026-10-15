<#
.SYNOPSIS
    The two-machine test for Nodearr on Windows, on machines with nothing set up: no Docker, no
    Sonarr or Radarr, no Python.

.DESCRIPTION
    Run it once on each of two Windows machines that are on different networks:

        powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 send
            on the machine that will have the file

        powershell -ExecutionPolicy Bypass -File two-machine-test.ps1 receive CODE
            on the other, with the code "send" printed

    Each side runs Nodearr from its release package and Radarr from Radarr's own Windows
    package, both straight on the machine, in a folder of this script's (~\nodearr-test).
    Nothing is installed. Radarr and ffmpeg are downloaded from their GitHub releases and
    checked against the SHA-256 GitHub publishes for them. The Nodearr package is the one
    downloaded from Nodearr's Releases page; the script looks for it in Downloads.

    "send" adds Night of the Living Dead (a film in the public domain) to Radarr, makes a test
    video for it with ffmpeg, imports that, and prints an invite code. "receive" pairs with the
    code, checks how the two nodes are connected, has Radarr search and grab the film, and
    reports what happened.

        fetch     on the receiving side, to try the transfer again
        status    where the pages are, how this node is connected, and what Nodearr warns about
        down      stop everything this script started and remove its folder

    Nodearr and Radarr are started with signing in turned off and bound to this machine only, so
    their pages are reachable from this machine and from nowhere else. The only thing another
    machine ever needs is Nodearr's UDP port.

.PARAMETER Command
    send, receive, fetch, status or down.

.PARAMETER Code
    The invite code, for "receive".

.PARAMETER NodearrZip
    The Nodearr package, Nodearr.<version>.win-x64.zip. By default the newest one in Downloads,
    beside this script, or in the test folder.

.PARAMETER OpenFirewall
    Add a Windows Firewall rule that lets UDP in to Nodearr's transport. Needs PowerShell run as
    administrator. "down" removes the rule. Without it, allow nodearr-transport when Windows asks.

.PARAMETER Name
    What the other side sees this node called. Default: Sender or Receiver.

.PARAMETER SizeMB
    How big a test video "send" makes. Default 100.

.PARAMETER TimeoutSec
    How long "receive" and "fetch" wait for the transfer. Default 900.

.PARAMETER HttpPort
    Nodearr's web page, on this machine only. Default 6633.

.PARAMETER UdpPort
    The UDP port other nodes connect to. Default 6633.

.PARAMETER RadarrPort
    Radarr's web page, on this machine only. Default 7878.

.PARAMETER Dir
    Where this script keeps everything. Default ~\nodearr-test.

.PARAMETER Side
    Which side, for status, fetch and down, when both are set up on one machine.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('send', 'receive', 'fetch', 'status', 'down')]
    [string] $Command,

    [Parameter(Position = 1)]
    [string] $Code,

    [string] $NodearrZip,
    [switch] $OpenFirewall,
    [string] $Name,
    [int] $SizeMB = 100,
    [int] $TimeoutSec = 900,
    [int] $HttpPort = 6633,
    [int] $UdpPort = 6633,
    [int] $RadarrPort = 7878,
    [string] $Dir = (Join-Path $HOME 'nodearr-test'),

    [ValidateSet('send', 'receive')]
    [string] $Side
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$MovieTmdbId = 10331
$MovieTitle = 'Night of the Living Dead'
$MovieRelease = 'Night.of.the.Living.Dead.1968.720p.WEB-DL.AAC2.0.H.264-NODEARR'

# A film is not imported if it is short enough to be a sample.
$MovieSeconds = 660

$Radarr = @{
    Url    = 'https://github.com/Radarr/Radarr/releases/download/v6.4.4.10685/Radarr.master.6.4.4.10685.windows-core-x64.zip'
    Sha256 = '316c3c7e5edaab75f5497d919bc556da259d966f9497a465d484cd49df28afe9'
}
$Ffmpeg = @{
    Url    = 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip'
    Sha256 = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
}

$ReleasesPage = 'https://github.com/Nodearr/Nodearr/releases'
$FirewallRule = 'Nodearr two-machine test'
$ThisScript = 'powershell -ExecutionPolicy Bypass -File two-machine-test.ps1'

class StopTest : Exception {
    StopTest([string] $message) : base($message) {}
}

function Stop-Test([string] $Message) { throw [StopTest]::new($Message) }
function Say([string] $Text = '') { Write-Host $Text }
function Step([string] $Text) { Write-Host ''; Write-Host "== $Text" }

# --- State -----------------------------------------------------------------------------------

function Get-SideFolder([string] $Which) { Join-Path $Dir $Which }

function Read-State([string] $Folder) {
    $path = Join-Path $Folder 'state.json'
    if (-not (Test-Path $path)) { return $null }

    $state = @{}
    (Get-Content $path -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $state[$_.Name] = $_.Value }
    return $state
}

function Save-State([hashtable] $State) {
    New-Item -ItemType Directory -Force $State.folder | Out-Null
    $State | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $State.folder 'state.json') -Encoding UTF8
}

function Find-Side {
    $found = @('send', 'receive' | Where-Object { Test-Path (Join-Path (Get-SideFolder $_) 'state.json') })

    if ($Side) { return $Side }
    if ($found.Count -eq 1) { return $found[0] }
    if ($found.Count -eq 0) { Stop-Test 'Nothing has been set up on this machine yet. Run "send" or "receive CODE" first.' }
    Stop-Test 'Both sides are set up on this machine. Say which: -Side send or -Side receive.'
}

# --- Downloads -------------------------------------------------------------------------------

function Get-CheckedDownload([hashtable] $What) {
    $cache = Join-Path $Dir 'cache'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $file = Join-Path $cache ($What.Url -split '/')[-1]

    if ((Test-Path $file) -and (Get-FileHash $file -Algorithm SHA256).Hash -eq $What.Sha256) { return $file }

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
    # The release's SHA256SUMS, if it was downloaded too.
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

# --- The two APIs ----------------------------------------------------------------------------

function Invoke-Api([string] $Base, [string] $Key, [string] $Method, [string] $Path, $Body, [int] $Timeout = 60) {
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
        # Windows PowerShell hands on a JSON list as one object. Passed through the pipeline it
        # comes out item by item, as it does in PowerShell 7, and an empty list comes out as nothing.
        return Invoke-RestMethod @request | ForEach-Object { $_ }
    }
    catch [StopTest] { throw }
    catch {
        # An answer that is an error. No answer at all, such as nothing listening yet, is thrown on.
        $response = $_.Exception.Response
        if ($null -eq $response) { throw }

        $detail = "$($_.ErrorDetails.Message)"
        if ($detail.Length -gt 600) { $detail = $detail.Substring(0, 600) }
        Stop-Test "$Method $Base$Path answered $([int] $response.StatusCode): $detail"
    }
}

function Nodearr([string] $Method, [string] $Path, $Body, [int] $Timeout = 60) {
    Invoke-Api "http://127.0.0.1:$($script:state.http_port)/api/v5" $script:state.nodearr_key $Method $Path $Body $Timeout
}

function Radarr([string] $Method, [string] $Path, $Body, [int] $Timeout = 60) {
    Invoke-Api "http://127.0.0.1:$($script:state.radarr_port)/api/v3" $script:state.radarr_key $Method $Path $Body $Timeout
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

function Invoke-AppCommand([string] $App, [hashtable] $Body, [int] $Timeout = 300) {
    $queued = & $App POST '/command' $Body

    Wait-Until "$($Body.name) to finish" {
        $now = & $App GET "/command/$($queued.id)"
        if ($now.status -eq 'failed') { Stop-Test "$($Body.name) failed: $($now.message)" }
        $now.status -eq 'completed'
    } -Timeout $Timeout -Every 1 | Out-Null
}

function Get-Identity {
    try { return Nodearr GET '/nodes/identity' $null 3 } catch { return $null }
}

# --- Setting a side up -----------------------------------------------------------------------

function New-Key {
    $bytes = New-Object byte[] 16
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

function New-SideState([string] $Which) {
    return @{
        folder      = Get-SideFolder $Which
        side        = $Which
        name        = $(if ($Name) { $Name } elseif ($Which -eq 'send') { 'Sender' } else { 'Receiver' })
        http_port   = $HttpPort
        udp_port    = $UdpPort
        radarr_port = $RadarrPort
        nodearr_key = New-Key
        radarr_key  = New-Key
    }
}

function Get-SideState([string] $Which) {
    $state = Read-State (Get-SideFolder $Which)
    if ($null -eq $state) { $state = New-SideState $Which }
    Save-State $state
    return $state
}

function Install-Apps {
    $folder = $script:state.folder
    Step 'Getting Nodearr and Radarr'

    $zip = Find-NodearrZip
    $check = Test-NodearrZip $zip

    if ($script:state.nodearr_zip -ne $zip -or -not (Test-Path (Join-Path $folder 'nodearr\Nodearr\Nodearr.Console.exe'))) {
        if (Get-Identity) { Stop-Test 'Nodearr from an earlier run is still running. Run "down" first to change the package.' }
        Expand-Package $zip (Join-Path $folder 'nodearr')
        $script:state.nodearr_zip = $zip
        Save-State $script:state
    }
    Say "  Nodearr: $(Split-Path $zip -Leaf), $check"

    if (-not (Test-Path (Join-Path $folder 'radarr\Radarr'))) {
        Expand-Package (Get-CheckedDownload $Radarr) (Join-Path $folder 'radarr')
    }
    Say "  Radarr:  $(($Radarr.Url -split '/')[-1])"
}

function Start-Apps {
    $folder = $script:state.folder
    foreach ($name in 'movies', 'downloads\complete', 'nodearr-data', 'radarr-data') {
        New-Item -ItemType Directory -Force (Join-Path $folder $name) | Out-Null
    }

    $radarrUp = $false
    try { Radarr GET '/system/status' $null 3 | Out-Null; $radarrUp = $true } catch {}
    $nodearrUp = [bool] (Get-Identity)

    foreach ($check in @(@{ What = "Nodearr's page"; Port = $script:state.http_port; Up = $nodearrUp }, @{ What = "Radarr's page"; Port = $script:state.radarr_port; Up = $radarrUp })) {
        if (-not $check.Up -and (Test-PortInUse $check.Port)) {
            Stop-Test ("Port $($check.Port), for $($check.What), is already in use on this machine.`n" +
                '  Stop what is using it, or choose other ports: -HttpPort, -UdpPort, -RadarrPort.')
        }
    }

    Step 'Starting Radarr and Nodearr'

    if (-not $radarrUp) {
        $radarrFolder = Join-Path $folder 'radarr\Radarr'
        $program = @('Radarr.Console.exe', 'Radarr.exe') | ForEach-Object { Join-Path $radarrFolder $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $program) { Stop-Test "There is no Radarr program in $radarrFolder. Run ""down"" and start again." }
        $script:state.radarr_pid = Start-Hidden $program @('-nobrowser', "-data=$(Join-Path $folder 'radarr-data')") @{
            RADARR__AUTH__APIKEY           = $script:state.radarr_key
            RADARR__AUTH__METHOD           = 'External'
            RADARR__SERVER__PORT           = "$($script:state.radarr_port)"
            RADARR__SERVER__BINDADDRESS    = '127.0.0.1'
            RADARR__LOG__ANALYTICSENABLED  = 'false'
        }
        Save-State $script:state
    }

    if (-not $nodearrUp) {
        # Before the transport first listens, so that Windows has nothing to ask.
        if ($OpenFirewall) { Add-FirewallRule }

        $program = Join-Path $folder 'nodearr\Nodearr\Nodearr.Console.exe'
        $script:state.nodearr_pid = Start-Hidden $program @('-nobrowser', "-data=$(Join-Path $folder 'nodearr-data')") @{
            Nodearr__Server__Port                = "$($script:state.http_port)"
            Nodearr__Server__BindAddress         = '127.0.0.1'
            Nodearr__Auth__Method                = 'External'
            Nodearr__Auth__ApiKey                = $script:state.nodearr_key
            Nodearr__Log__AnalyticsEnabled       = 'false'
            Nodearr__Transport__NodeName         = $script:state.name
            Nodearr__Transport__Port             = "$($script:state.udp_port)"
            Nodearr__Transport__DownloadFolder   = (Join-Path $folder 'downloads')
        }
        Save-State $script:state

        if (-not $OpenFirewall) {
            Say '  If Windows asks whether to let nodearr-transport through the firewall, allow it.'
            Say '  The question can open behind other windows. Cancelling it blocks the transport.'
        }
    }

    Wait-Until 'Radarr to start' { Radarr GET '/system/status' $null 5 } -Timeout 300 | Out-Null
    $identity = Wait-Until "Nodearr and its transport to start (its logs are in $(Join-Path $folder 'nodearr-data\logs'))" {
        $found = Get-Identity
        if ($found -and $found.transportRunning) { $found }
    } -Timeout 180

    Say "  Nodearr is up as `"$($script:state.name)`"."
    foreach ($rule in Get-BlockingRules) { Say "  Warning: the firewall rule `"$rule`" blocks nodearr-transport. Remove it in Windows Defender Firewall, Inbound Rules." }
    return $identity
}

function Get-TransportPath { Join-Path $script:state.folder 'nodearr\Nodearr\nodearr-transport.exe' }

function Add-FirewallRule {
    $admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { Stop-Test '-OpenFirewall needs PowerShell run as administrator. Or leave it off and allow nodearr-transport when Windows asks.' }

    $name = "$FirewallRule ($($script:state.side))"
    if (-not (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $name -Direction Inbound -Program (Get-TransportPath) -Protocol UDP -Action Allow -Profile Any | Out-Null
    }

    $script:state.firewall_rule = $name
    Save-State $script:state
    Say "  Added the firewall rule `"$name`": UDP in to nodearr-transport."
}

function Get-BlockingRules {
    # Cancelling Windows' question adds rules that block the program, and a block wins over an allow.
    try {
        $program = Get-TransportPath
        return @(Get-NetFirewallApplicationFilter -Program $program -ErrorAction SilentlyContinue | Get-NetFirewallRule | Where-Object { $_.Action -eq 'Block' -and $_.Enabled -eq 'True' } | ForEach-Object { $_.DisplayName })
    }
    catch { return @() }
}

function Initialize-Library {
    $folder = $script:state.folder
    $movies = Join-Path $folder 'movies'

    Step "Adding $MovieTitle to Radarr"
    if (-not (@(Radarr GET '/rootfolder') | Where-Object { $_.path.TrimEnd('\') -eq $movies })) {
        Radarr POST '/rootfolder' @{ path = $movies } | Out-Null
    }

    $movie = @(Radarr GET '/movie') | Where-Object { $_.tmdbId -eq $MovieTmdbId } | Select-Object -First 1
    if (-not $movie) {
        $found = (Wait-Until 'Radarr to look the film up' { @(Radarr GET "/movie/lookup?term=tmdb:$MovieTmdbId") } -Timeout 90 -Every 5)[0]
        $found | Add-Member -Force qualityProfileId 1
        $found | Add-Member -Force rootFolderPath $movies
        $found | Add-Member -Force monitored $true
        $found | Add-Member -Force minimumAvailability 'released'
        $found | Add-Member -Force addOptions @{ searchForMovie = $false }
        $movie = Radarr POST '/movie' $found
    }

    Step 'Telling Nodearr about Radarr'
    if (-not (@(Nodearr GET '/applications') | Where-Object { $_.implementation -eq 'Radarr' })) {
        # Both run on this machine and see the same folders, so neither needs a path mapping.
        $addresses = @{
            nodearrUrl = "http://127.0.0.1:$($script:state.http_port)"
            baseUrl    = "http://127.0.0.1:$($script:state.radarr_port)"
            apiKey     = $script:state.radarr_key
        }
        $definition = @(Nodearr GET '/applications/schema') | Where-Object { $_.implementation -eq 'Radarr' } | Select-Object -First 1
        foreach ($field in $definition.fields) {
            if ($addresses.ContainsKey($field.name)) { $field | Add-Member -Force value $addresses[$field.name] }
        }
        $definition | Add-Member -Force name 'Radarr'
        $definition | Add-Member -Force syncLevel 'fullSync'
        Nodearr POST '/applications' $definition | Out-Null
    }

    Wait-Until 'Nodearr to add itself to Radarr as an indexer and a download client' {
        (@(Radarr GET '/indexer') | Where-Object { $_.name -eq 'Nodearr' }) -and (@(Radarr GET '/downloadclient') | Where-Object { $_.name -eq 'Nodearr' })
    } -Timeout 90 | Out-Null
    Say '  Radarr has Nodearr as an indexer and as a download client.'

    return $movie
}

function New-Film($Movie) {
    if ((Radarr GET "/movie/$($Movie.id)").hasFile) {
        Say '  Radarr already has a file for it.'
        return
    }

    Step "Making a test video of about $SizeMB MB (colour bars and noise, $([int] ($MovieSeconds / 60)) minutes long)"
    $ffmpegFolder = Join-Path $Dir 'cache\ffmpeg'
    $ffmpeg = Get-ChildItem $ffmpegFolder -Recurse -Filter 'ffmpeg.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $ffmpeg) {
        Expand-Package (Get-CheckedDownload $Ffmpeg) $ffmpegFolder
        $ffmpeg = Get-ChildItem $ffmpegFolder -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    }

    $seed = Join-Path $script:state.folder "downloads\seed\$MovieRelease"
    New-Item -ItemType Directory -Force $seed | Out-Null
    $video = Join-Path $seed "$MovieRelease.mkv"
    $kilobits = [Math]::Max(200, [int] ($SizeMB * 8192 / $MovieSeconds) - 64)

    $arguments = @(
        '-hide_banner', '-loglevel', 'error', '-y',
        '-f', 'lavfi', '-i', 'testsrc2=size=1280x720:rate=12,noise=alls=40:allf=t',
        '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000',
        '-t', "$MovieSeconds",
        '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p',
        '-b:v', "${kilobits}k", '-minrate', "${kilobits}k", '-maxrate', "${kilobits}k", '-bufsize', "$($kilobits * 2)k",
        '-c:a', 'aac', '-b:a', '64k',
        "`"$video`"")
    $run = Start-Process -FilePath $ffmpeg.FullName -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    if ($run.ExitCode -ne 0) { Stop-Test "ffmpeg could not make the test video (exit code $($run.ExitCode))." }

    Step 'Importing it into Radarr'
    Invoke-AppCommand Radarr @{ name = 'DownloadedMoviesScan'; path = $seed; importMode = 'move' }
    Wait-Until 'Radarr to hold the file' { (Radarr GET "/movie/$($Movie.id)").hasFile } -Timeout 120 | Out-Null
}

function Get-FileSummary($Movie) {
    $file = (Radarr GET "/movie/$($Movie.id)").movieFile
    if (-not $file -or -not (Test-Path -LiteralPath $file.path)) { return $null }

    return @{ path = $file.path; size = [long] $file.size; sha256 = (Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash.ToLower() }
}

function Get-NetworkSummary($Identity) {
    if ($Identity.udp -eq $false) { return 'could not confirm that UDP gets out' }
    if ($Identity.mappingVaries) { return 'a different public port for every destination (direct connections usually need a forwarded port)' }
    if ($Identity.udp) { return 'UDP works and the public port stays the same' }
    return 'not known yet'
}

function Get-Warnings { @(Nodearr GET '/health') | Where-Object { $_ } | ForEach-Object { $_.message } }

function Show-Pages {
    Say "  Nodearr:  http://localhost:$($script:state.http_port)"
    Say "  Radarr:   http://localhost:$($script:state.radarr_port)"
}

# --- The commands ----------------------------------------------------------------------------

function Invoke-Send {
    $script:state = Get-SideState 'send'
    Install-Apps
    Start-Apps | Out-Null
    $movie = Initialize-Library
    New-Film $movie

    Step 'Reading the library into Nodearr'
    Invoke-AppCommand Nodearr @{ name = 'CatalogRefresh' }
    $files = Wait-Until 'Nodearr to list the file as readable' { @((Nodearr GET '/catalog/local/moviefile?pageSize=50').records | Where-Object { $_.readable }) } -Timeout 60
    $file = Get-FileSummary $movie
    Say "  $(@($files).Count) file, $([int] ($file.size / 1MB)) MB, SHA-256 $($file.sha256)"

    Step 'Creating an invite that shares everything'
    $invite = Nodearr POST '/nodes/invites' @{ validHours = 24; label = 'two-machine test'; shareAll = $true }
    $identity = Nodearr GET '/nodes/identity'
    $script:state.sha256 = $file.sha256
    Save-State $script:state

    Say ''
    Say ('=' * 78)
    Say 'This side is ready. Leave this machine on, with Nodearr and Radarr running.'
    Show-Pages
    Say "  This network: $(Get-NetworkSummary $identity)"
    foreach ($message in Get-Warnings) { Say "  Nodearr warns: $message" }
    Say ''
    Say "Other nodes reach this one on UDP port $($script:state.udp_port). Nothing else needs to be open."
    Say ''
    Say 'On the other machine, run this. The code works once and for 24 hours:'
    Say ''
    Say "  $ThisScript receive '$($invite.code)'"
    Say ''
    Say 'The file it should end up with has this SHA-256:'
    Say "  $($file.sha256)"
    Say ('=' * 78)
}

function Invoke-Receive {
    $script:state = Get-SideState 'receive'
    $code = "$Code".Trim()

    if (-not $code -and -not $script:state.code) { Stop-Test '"receive" needs the invite code that "send" printed on the other machine.' }

    Install-Apps
    Start-Apps | Out-Null
    $movie = Initialize-Library

    if ($code -and $code -ne $script:state.code) {
        Step 'Pairing with the other node'
        $peer = Nodearr POST '/nodes/invites/redeem' @{ code = $code }
        Say "  Paired with $($peer.name)."

        # A new code is a new sending side. A node left over from an earlier run would offer the
        # same film and no longer be there.
        foreach ($other in @(Nodearr GET '/nodes/peers')) {
            if ($other.id -ne $peer.id) {
                if ($other.state -eq 'trusted') { Nodearr POST "/nodes/peers/$($other.id)/revoke" @{} | Out-Null }
                if ($other.state -ne 'blocked') { Nodearr DELETE "/nodes/peers/$($other.id)" | Out-Null }
            }
        }

        $script:state.code = $code
        $script:state.peer_node_id = $peer.nodeId
        Save-State $script:state
    }
    else {
        Say ''
        Say '  Already paired. The code is not needed again.'
    }

    Step 'Waiting for what the other node shares'
    $count = Wait-Until 'the other node to send its catalog' { (Nodearr GET '/catalog/peers/items?pageSize=50').totalRecords } -Timeout 120
    Say "  It offers $count file(s)."

    Invoke-Fetch $movie
}

function Invoke-Fetch($Movie) {
    if (-not $Movie) {
        if ((Find-Side) -ne 'receive') { Stop-Test '"fetch" is for the receiving side. On the sending side there is nothing to fetch.' }
        $script:state = Read-State (Get-SideFolder 'receive')
        Start-Apps | Out-Null
        $Movie = @(Radarr GET '/movie') | Where-Object { $_.tmdbId -eq $MovieTmdbId } | Select-Object -First 1
    }

    $result = [ordered] @{ when = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz') }

    Step 'Checking how the two nodes are connected (up to 30 seconds)'
    $trusted = @(Nodearr GET '/nodes/peers') | Where-Object { $_.state -eq 'trusted' }
    $peer = $trusted | Where-Object { $_.nodeId -eq $script:state.peer_node_id } | Select-Object -First 1
    if (-not $peer) { $peer = $trusted | Select-Object -First 1 }
    if (-not $peer) { Stop-Test 'This node trusts no other node. Run "receive CODE" with a new code from the sending side.' }

    $checked = Nodearr POST "/nodes/peers/$($peer.id)/ping" @{} 90
    $result.connection = switch ($checked.connectionPath) { 'direct' { 'Direct' } 'relay' { 'Relay only' } default { 'Could not reach the other node' } }
    $result.address = $checked.connectionAddress
    $result.checkError = $checked.lastError
    $line = "  $($result.connection)"
    if ($result.address) { $line += " ($($result.address))" }
    if ($result.checkError) { $line += ": $($result.checkError)" }
    Say $line

    if ($checked.connectionPath -eq 'relay') {
        Say '  The nodes can talk, but a file needs a direct connection. The transfer is tried anyway, to see it reported.'
    }

    $result.thisNetwork = Get-NetworkSummary (Nodearr GET '/nodes/identity')

    # A run before this one may have left the film in place, a failed download, or the release on
    # Radarr's blocklist. Any of those would stop Radarr grabbing it again.
    $current = Radarr GET "/movie/$($Movie.id)"
    if ($current.hasFile) {
        Radarr DELETE "/moviefile/$($current.movieFile.id)" | Out-Null
        Wait-Until 'Radarr to notice the film is gone' { -not (Radarr GET "/movie/$($Movie.id)").hasFile } -Timeout 60 -Every 1 | Out-Null
    }

    foreach ($transfer in @(Nodearr GET '/transfers') | Where-Object { $_ }) { Nodearr DELETE "/transfers/$($transfer.downloadId)" | Out-Null }

    $blocked = @((Radarr GET '/blocklist?pageSize=200').records | Where-Object { $_ } | ForEach-Object { $_.id })
    if ($blocked.Count) { Radarr DELETE '/blocklist/bulk' @{ ids = $blocked } | Out-Null }

    # Radarr keeps a download it has just imported in its queue until it next looks at its
    # download clients, and turns down a release it thinks is still in the queue.
    Wait-Until 'Radarr to finish with the last download' {
        $records = @((Radarr GET '/queue?pageSize=50').records | Where-Object { $_ })
        if ($records.Count) { Radarr POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null }
        $records.Count -eq 0
    } -Timeout 120 -Every 3 | Out-Null

    Step 'Having Radarr search for the film and grab it'
    $started = Get-Date
    Radarr POST '/command' @{ name = 'MoviesSearch'; movieIds = @($Movie.id) } | Out-Null

    $seen = $null
    $lastShown = [datetime]::MinValue
    $lastRefresh = Get-Date

    while (((Get-Date) - $started).TotalSeconds -lt $TimeoutSec) {
        Start-Sleep -Seconds 2
        $transfers = @(Nodearr GET '/transfers') | Where-Object { $_ }

        if ($transfers) {
            $seen = $transfers | Select-Object -First 1
            if ($seen.state -eq 'failed') { break }

            if (((Get-Date) - $lastShown).TotalSeconds -ge 10 -and $seen.size) {
                $lastShown = Get-Date
                Say "  $($seen.state): $([int] ($seen.bytesDone / 1MB)) of $([int] ($seen.size / 1MB)) MB"
            }
        }

        if ((Radarr GET "/movie/$($Movie.id)").hasFile) { break }
        if (-not $seen -and ((Get-Date) - $started).TotalSeconds -gt 120) { break }

        if (((Get-Date) - $lastRefresh).TotalSeconds -ge 10) {
            $lastRefresh = Get-Date
            Radarr POST '/command' @{ name = 'RefreshMonitoredDownloads' } | Out-Null
        }
    }

    $elapsed = ((Get-Date) - $started).TotalSeconds
    $imported = Get-FileSummary $Movie

    if ($imported) {
        $result.transfer = 'Completed and imported'
        $result.size = $imported.size
        $result.sha256 = $imported.sha256
        $result.seconds = [int] $elapsed
    }
    elseif ($seen -and $seen.state -eq 'failed') {
        $result.transfer = "Failed: $($seen.failure)"
        $result.message = $seen.failureMessage
    }
    elseif (-not $seen) {
        $releases = @(Radarr GET "/release?movieId=$($Movie.id)" $null 120) | Where-Object { $_.indexer -eq 'Nodearr' }
        $result.transfer = 'Radarr did not grab anything'
        if ($releases) {
            $result.message = 'Radarr found it and turned it down: ' + ((@($releases | ForEach-Object { $_.rejections }) | Sort-Object -Unique) -join '; ')
        }
        else {
            $result.message = 'The Nodearr indexer returned nothing for the film.'
        }
    }
    else {
        $result.transfer = "Not finished after ${TimeoutSec}s"
        $result.message = "$($seen.state), $($seen.bytesDone) of $($seen.size) bytes"
    }

    $result.warnings = @(Get-Warnings)
    $resultFile = Join-Path $script:state.folder 'result.json'
    $result | ConvertTo-Json -Depth 5 | Set-Content $resultFile -Encoding UTF8

    Say ''
    Say ('=' * 78)
    Say 'What to write down'
    Say "  When:                 $($result.when)"
    Say "  This side's network:  $($result.thisNetwork)"
    $line = "  `"Check connection`":   $($result.connection)"
    if ($result.address) { $line += " ($($result.address))" }
    Say $line
    Say "  The transfer:         $($result.transfer)"

    if ($imported) {
        Say "  Size and time:        $([int] ($imported.size / 1MB)) MB in $($result.seconds)s, search and import included"
        Say "  SHA-256 here:         $($imported.sha256)"
        Say '                        It should be the one the sending side printed.'
    }

    if ($result.message) { Say "  Said why:             $($result.message)" }
    foreach ($message in $result.warnings) { Say "  Nodearr warns:        $message" }
    foreach ($rule in Get-BlockingRules) { Say "  Firewall:             the rule `"$rule`" blocks nodearr-transport" }

    Say ''
    Say "Saved to $resultFile. To try again after changing something:"
    Say "  $ThisScript fetch"
    Say ('=' * 78)
}

function Invoke-Status {
    $which = Find-Side
    $script:state = Read-State (Get-SideFolder $which)

    Say "Set up as the $(if ($which -eq 'send') { 'sending' } else { 'receiving' }) side, `"$($script:state.name)`"."
    $identity = Get-Identity
    if (-not $identity) {
        Say '  Nodearr is not running. Run "send" or "receive" again to start it.'
        return
    }

    Show-Pages
    Say "  Other nodes reach this one on UDP port $($script:state.udp_port)."
    Say "  This network: $(Get-NetworkSummary $identity)"
    foreach ($peer in @(Nodearr GET '/nodes/peers') | Where-Object { $_ }) {
        $path = if ($peer.connectionPath) { $peer.connectionPath } else { 'unknown' }
        Say "  Node $($peer.name): $($peer.state), last connection check: $path"
    }
    foreach ($message in Get-Warnings) { Say "  Nodearr warns: $message" }
    foreach ($rule in Get-BlockingRules) { Say "  Firewall: the rule `"$rule`" blocks nodearr-transport" }
}

function Invoke-Down {
    $sides = if ($Side) { @($Side) } else { @('send', 'receive' | Where-Object { Test-Path (Join-Path (Get-SideFolder $_) 'state.json') }) }
    if (-not $sides) { Say 'Nothing to remove.' }

    foreach ($which in $sides) {
        $state = Read-State (Get-SideFolder $which)
        if ($null -eq $state) { continue }

        Stop-Tree $state.nodearr_pid
        Stop-Tree $state.radarr_pid
        Start-Sleep -Seconds 2

        if ($state.firewall_rule) {
            try { Remove-NetFirewallRule -DisplayName $state.firewall_rule -ErrorAction Stop; Say "Removed the firewall rule `"$($state.firewall_rule)`"." }
            catch { Say "Could not remove the firewall rule `"$($state.firewall_rule)`": run this as administrator, or remove it in Windows Defender Firewall." }
        }

        # Only ever the folder this script made.
        Remove-Item $state.folder -Recurse -Force -ErrorAction SilentlyContinue
        Say "Stopped the $which side and removed $($state.folder)."
    }

    if (-not @('send', 'receive' | Where-Object { Test-Path (Get-SideFolder $_) })) {
        Remove-Item (Join-Path $Dir 'cache') -Recurse -Force -ErrorAction SilentlyContinue
        if ((Test-Path $Dir) -and -not (Get-ChildItem $Dir -Force)) { Remove-Item $Dir -Force }
    }
}

try {
    switch ($Command) {
        'send' { Invoke-Send }
        'receive' { Invoke-Receive }
        'fetch' { Invoke-Fetch $null }
        'status' { Invoke-Status }
        'down' { Invoke-Down }
    }
}
catch [StopTest] {
    Say ''
    Say "Stopped: $($_.Exception.Message)"
    exit 1
}
catch {
    Say ''
    Say "Stopped by a fault in this script, at line $($_.InvocationInfo.ScriptLineNumber): $($_.Exception.Message)"
    Say 'Running the same command again carries on from where it stopped. If it stops here again, send this message on.'
    exit 1
}
