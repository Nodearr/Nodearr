#requires -Version 7
<#
.SYNOPSIS
    Acceptance for update checks (ADR 0024, #134): a node notices a release named by a signed
    manifest, refuses a tampered one, is never taken back to an older version, and asks nothing
    when its settings or the switch say not to.

.DESCRIPTION
    Runs one real Nodearr on this machine and a small web server that plays updates.nodearr.com.
    The manifest is written and signed by build/Publish-UpdateManifest.ps1, with an RSA key made
    for the run, and the node is told to trust that key and fetch from that server through the
    environment, as docs/guides/releases.md describes. Every request the server gets is recorded,
    so the checks can say exactly what the node sent.

    The node runs from the development build, which never replaces itself, so nothing is installed.
    The updater itself is tried on real installs with the UI test kit (docs/guides/ui-test.md).

    Needs ssh-keygen on the path. No Docker, no Arr app. Everything lives in .run/updates next to
    this script, the key included, and is deleted at the end unless -KeepRunning is given.

.PARAMETER KeepRunning
    Leave Nodearr and the manifest server running afterwards. The address of System > Updates is
    printed at the end.

.PARAMETER Down
    Stop everything this script started and delete .run/updates.
#>
[CmdletBinding()]
param(
    [switch] $KeepRunning,
    [switch] $Down
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 1

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '..')
$run = Join-Path $root '.run/updates'
$data = Join-Path $run 'a'
$site = Join-Path $run 'site'
$packages = Join-Path $site 'packages'
$keyFile = Join-Path $run 'key'
$pidFile = Join-Path $run 'A.pid'
$requestLog = Join-Path $run 'requests.log'
$dll = Join-Path $repo '_output/net10.0/Nodearr.Console.dll'
$portFile = Join-Path $run 'site.port'

function New-Key { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

# A port of its own, so the UI test kit or another node on 6633 is left alone.
$port = Get-FreeTcpPort

function Stop-Node {
    if (Test-Path $pidFile) {
        Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
        Remove-Item $pidFile -Force
    }
}

function Stop-Server {
    # The server waits on a request, so it is sent one that tells it to stop.
    if (Test-Path $portFile) {
        try { Invoke-WebRequest "http://127.0.0.1:$(Get-Content $portFile)/__stop" -TimeoutSec 5 | Out-Null } catch { }
    }
    Get-Job -Name 'nodearr-updates-site' -ErrorAction SilentlyContinue | Stop-Job -PassThru | Remove-Job -Force
}

if ($Down) {
    Stop-Node
    Stop-Server
    if (Test-Path $run) { Remove-Item $run -Recurse -Force }
    Write-Host 'Stopped Nodearr and the manifest server.'
    return
}

if (-not (Test-Path $dll)) { throw 'Build Nodearr first: dotnet build src/Nodearr.sln' }
$sshKeygen = (Get-Command ssh-keygen -ErrorAction SilentlyContinue)?.Source
if (-not $sshKeygen) { throw 'ssh-keygen is needed to sign the test manifest' }

$apiKey = New-Key

function Invoke-Node {
    param([string] $Method, [string] $Path, $Body)
    $request = @{ Uri = "http://127.0.0.1:$port/api/v5$Path"; Method = $Method; Headers = @{ 'X-Api-Key' = $apiKey }; TimeoutSec = 30 }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 10); $request.ContentType = 'application/json' }
    return Invoke-RestMethod @request
}

function Wait-Until {
    param([scriptblock] $Condition, [int] $TimeoutSec = 60, [string] $What)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $value = & $Condition
        if ($value) { return $value }
        Start-Sleep -Seconds 1
    }
    throw "Timed out after ${TimeoutSec}s waiting for $What"
}

# Whether a condition comes true within 30 seconds. Health is checked 5 seconds after what changed it.
function Test-Soon {
    param([scriptblock] $Condition)
    try { return [bool](Wait-Until -What 'it' -TimeoutSec 30 -Condition $Condition) } catch { return $false }
}

# Check Now on System > Updates. A refused signature is fetched once more after 30 seconds.
function Invoke-Check {
    $queued = Invoke-Node POST '/command' @{ name = 'ApplicationUpdateCheck' }
    Wait-Until -What 'the update check' -TimeoutSec 90 -Condition {
        $state = Invoke-Node GET "/command/$($queued.id)"
        if ($state.status -eq 'failed') { throw $state.message }
        $state.status -eq 'completed'
    } | Out-Null
    return Invoke-Node GET '/update'
}

function Get-Requests {
    if (-not (Test-Path $requestLog)) { return @() }
    return @(Get-Content $requestLog | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
}

function Clear-Requests { Set-Content $requestLog '' }

function Get-UpdateHealth { return @(Invoke-Node GET '/health' | Where-Object { $_.source -eq 'UpdateCheck' }) }

# Writes and signs a manifest naming $Version, as the maintainer would, and serves it.
function Publish-Manifest {
    param([string] $Version)

    Get-ChildItem $packages -File -ErrorAction SilentlyContinue | Remove-Item -Force
    New-Item -ItemType Directory -Force $packages | Out-Null

    $rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
    $extension = $IsWindows ? 'zip' : 'tar.gz'
    $bytes = [byte[]]::new(4096)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    [IO.File]::WriteAllBytes((Join-Path $packages "Nodearr.$Version.$rid.$extension"), $bytes)

    $out = Join-Path $run 'manifest'
    & (Join-Path $repo 'build/Publish-UpdateManifest.ps1') -Version $Version -PackagesFolder $packages `
        -BaseUrl "http://127.0.0.1:$sitePort/packages/" -SigningKeyFile $keyFile -OutputDir $out -WarningAction SilentlyContinue | Out-Null

    New-Item -ItemType Directory -Force (Join-Path $site 'v1') | Out-Null
    Copy-Item (Join-Path $out 'update-manifest.json') (Join-Path $site 'v1/main.json') -Force
    Copy-Item (Join-Path $out 'update-manifest.json.sig') (Join-Path $site 'v1/main.json.sig') -Force
}

$results = [System.Collections.Generic.List[object]]::new()

function Assert-That {
    param([string] $Name, [bool] $Passed, $Evidence)
    $results.Add([ordered]@{ check = $Name; passed = $Passed; evidence = $Evidence })
    Write-Host ("[{0}] {1}" -f ($Passed ? 'PASS' : 'FAIL'), $Name) -ForegroundColor ($Passed ? 'Green' : 'Red')
    if (-not $Passed) { Write-Host ("       " + ($Evidence | ConvertTo-Json -Depth 6 -Compress)) -ForegroundColor DarkGray }
}

function Start-Node {
    param([hashtable] $Environment = @{})

    foreach ($name in 'Nodearr__Network__ContactNodearr', 'Nodearr__Update__Check', 'Nodearr__Update__Automatically') { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    foreach ($entry in $Environment.GetEnumerator()) { Set-Item "Env:$($entry.Key)" $entry.Value }

    $process = Start-Process dotnet -ArgumentList "`"$dll`"", '-nobrowser', "-data=`"$data`"" -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $run 'A.out.log') -RedirectStandardError (Join-Path $run 'A.err.log')
    $process.Id | Set-Content $pidFile

    Wait-Until -What "Nodearr on port $port" -TimeoutSec 90 -Condition {
        try { (Invoke-RestMethod "http://127.0.0.1:$port/ping" -TimeoutSec 2).status -eq 'OK' } catch { $false }
    } | Out-Null
}

# --- Fresh state -------------------------------------------------------------------------------

Stop-Node
Stop-Server
if (Test-Path $run) { Remove-Item $run -Recurse -Force }
New-Item -ItemType Directory -Force $run, $data, $site | Out-Null

# A key for this run only. Nodearr is told to trust it through the environment, never otherwise.
& $sshKeygen -q -t rsa -b 3072 -N '' -C 'nodearr-acceptance' -f $keyFile | Out-Null
if (-not (Test-Path $keyFile)) { throw 'ssh-keygen did not make a key' }
$publicKey = (Get-Content "$keyFile.pub" -Raw).Trim()

# --- updates.nodearr.com, played by a folder on this machine --------------------------------------

$sitePort = Get-FreeTcpPort
$sitePort | Set-Content $portFile

Start-ThreadJob -Name 'nodearr-updates-site' -ArgumentList $site, $sitePort, $requestLog -ScriptBlock {
    param($folder, $listenPort, $log)
    $http = [System.Net.HttpListener]::new()
    $http.Prefixes.Add("http://127.0.0.1:$listenPort/")
    $http.Start()
    while ($http.IsListening) {
        $context = $http.GetContext()
        $request = $context.Request

        if ($request.Url.AbsolutePath -eq '/__stop') {
            $context.Response.Close()
            $http.Stop()
            break
        }

        [ordered]@{
            method = $request.HttpMethod; path = $request.Url.AbsolutePath; query = $request.Url.Query
            userAgent = $request.UserAgent; cookie = $request.Headers['Cookie']; length = $request.ContentLength64
        } | ConvertTo-Json -Compress | Add-Content $log

        $file = Join-Path $folder ($request.Url.AbsolutePath.TrimStart('/'))
        if ($request.HttpMethod -eq 'GET' -and (Test-Path $file -PathType Leaf)) {
            $bytes = [IO.File]::ReadAllBytes($file)
            $context.Response.ContentType = 'application/octet-stream'
            $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        else {
            $context.Response.StatusCode = 404
        }
        $context.Response.Close()
    }
} | Out-Null

Publish-Manifest '99.0.0'

@"
<Config>
  <Port>$port</Port>
  <ApiKey>$apiKey</ApiKey>
  <AuthenticationMethod>External</AuthenticationMethod>
</Config>
"@ | Set-Content (Join-Path $data 'config.xml')

$env:Nodearr__Update__ManifestUrl = "http://127.0.0.1:$sitePort/v1/"
$env:Nodearr__Update__PublicKey = $publicKey
$env:Nodearr__Transport__Relay = 'disabled'
$env:Nodearr__Transport__DisablePortMapping = 'true'

try {
    # --- 1. A node notices a release, soon after it starts ---------------------------------------

    Clear-Requests
    Start-Node
    $status = Wait-Until -What 'the check made after start' -TimeoutSec 60 -Condition {
        $s = Invoke-Node GET '/update'
        if ($s.outcome -ne 'notChecked') { $s }
    }

    Assert-That 'A node checks soon after it starts and notices a newer release named by a signed manifest' (
        $status.outcome -eq 'checked' -and $status.available.version -eq '99.0.0' -and $status.available.fileName -like 'Nodearr.99.0.0.*'
    ) @{ outcome = $status.outcome; available = $status.available.version; file = $status.available.fileName }

    Assert-That 'A development build says it never updates itself, and nothing is downloaded' (
        $status.installKind -eq 'development' -and -not (Get-Requests | Where-Object { $_.path -like '/packages/*' })
    ) @{ installKind = $status.installKind; requests = @(Get-Requests | ForEach-Object path) }

    $requests = Get-Requests
    Assert-That 'The check is two plain GETs of the manifest and its signature: no query, no cookie, no body' (
        $requests.Count -eq 2 -and
        (@($requests | ForEach-Object path | Sort-Object) -join ',') -eq '/v1/main.json,/v1/main.json.sig' -and
        -not ($requests | Where-Object { $_.method -ne 'GET' -or $_.query -or $_.cookie -or $_.length -gt 0 })
    ) @{ requests = $requests }

    Assert-That 'Its user agent names Nodearr and two numbers of its version, and nothing else' (
        -not ($requests | Where-Object { $_.userAgent -notmatch '^Nodearr/\d+\.\d+$' })
    ) @{ userAgents = @($requests | ForEach-Object userAgent | Sort-Object -Unique) }

    Assert-That 'A release found today raises no notice on System > Status' ((Get-UpdateHealth).Count -eq 0) @{ health = Get-UpdateHealth }

    # --- 2. A tampered manifest is refused ---------------------------------------------------------

    $manifestPath = Join-Path $site 'v1/main.json'
    $good = [IO.File]::ReadAllBytes($manifestPath)
    $tampered = [Text.Encoding]::UTF8.GetString($good).Replace('"rollout": 100', '"rollout": 99')
    [IO.File]::WriteAllText($manifestPath, $tampered, [Text.UTF8Encoding]::new($false))

    Clear-Requests
    $status = Invoke-Check
    Assert-That 'A manifest changed after signing is refused, and nothing in it is offered' (
        $status.outcome -eq 'badSignature' -and -not $status.available
    ) @{ outcome = $status.outcome; available = $status.available }

    Assert-That 'It was fetched a second time before being refused, in case the pair was caught mid-publish' (
        @(Get-Requests | Where-Object { $_.path -eq '/v1/main.json' }).Count -eq 2
    ) @{ requests = @(Get-Requests | ForEach-Object path) }

    $warned = Test-Soon { $h = Get-UpdateHealth; $h.Count -eq 1 -and $h[0].type -eq 'warning' }
    Assert-That 'System > Status warns about the refused manifest' $warned @{ health = Get-UpdateHealth }

    [IO.File]::WriteAllBytes($manifestPath, $good)
    $status = Invoke-Check
    Assert-That 'Signed again, the release is offered again and the warning goes' (
        $status.outcome -eq 'checked' -and $status.available.version -eq '99.0.0' -and (Test-Soon { (Get-UpdateHealth).Count -eq 0 })
    ) @{ outcome = $status.outcome; available = $status.available.version; health = Get-UpdateHealth }

    # --- 3. Never down -----------------------------------------------------------------------------

    Publish-Manifest '0.1.0'
    $status = Invoke-Check
    Assert-That 'A signed manifest for an older version is ignored, so a stale or replayed one cannot take a node back' (
        $status.outcome -eq 'checked' -and -not $status.available
    ) @{ outcome = $status.outcome; available = $status.available }

    Publish-Manifest '99.0.0'

    # --- 4. Check for Updates off ------------------------------------------------------------------

    $settings = Invoke-Node GET '/settings/general'
    $settings.updateCheck = $false
    Invoke-Node PUT '/settings/general' $settings | Out-Null

    Clear-Requests
    $status = Invoke-Check
    Assert-That 'With Check for Updates off, nothing is asked and nothing is offered' (
        $status.outcome -eq 'off' -and -not $status.updateCheck -and (Get-Requests).Count -eq 0 -and -not $status.available
    ) @{ outcome = $status.outcome; requests = @(Get-Requests) }

    $settings = Invoke-Node GET '/settings/general'
    $settings.updateCheck = $true
    Invoke-Node PUT '/settings/general' $settings | Out-Null

    # --- 5. The switch, in the environment, wins over the settings ---------------------------------

    Stop-Node
    Clear-Requests
    Start-Node @{ Nodearr__Network__ContactNodearr = 'false'; Nodearr__Update__Check = 'true' }
    Start-Sleep -Seconds 10
    $status = Invoke-Check
    $settings = Invoke-Node GET '/settings/general'

    Assert-That 'With Contact nodearr.com off in the environment, nothing is asked, whatever Check for Updates says' (
        $status.outcome -eq 'off' -and -not $status.contactNodearr -and $status.updateCheck -and (Get-Requests).Count -eq 0
    ) @{ outcome = $status.outcome; contactNodearr = $status.contactNodearr; updateCheck = $status.updateCheck; requests = @(Get-Requests) }

    Assert-That 'Settings > General shows both as fixed by the environment' (
        @($settings.fixedByEnvironment) -contains 'contactNodearr' -and @($settings.fixedByEnvironment) -contains 'updateCheck' -and -not $settings.contactNodearr
    ) @{ fixed = @($settings.fixedByEnvironment); contactNodearr = $settings.contactNodearr }
}
finally {
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $run 'results.json')

    if (-not $KeepRunning) {
        Stop-Node
        Stop-Server
        Remove-Item $keyFile, "$keyFile.pub" -Force -ErrorAction SilentlyContinue
    }
}

$failed = @($results | Where-Object { -not $_.passed })
Write-Host ''
Write-Host ("{0} of {1} checks passed" -f ($results.Count - $failed.Count), $results.Count) -ForegroundColor ($failed.Count ? 'Red' : 'Green')
if ($KeepRunning) { Write-Host "Nodearr: http://localhost:$port/system/updates" }
if ($failed.Count) { exit 1 }
