#requires -Version 7
<#
.SYNOPSIS
    Acceptance for Settings > General: the address this page is served at, signing in, the API
    key and logging, changed through the API the way the page changes them.

.DESCRIPTION
    Runs one real Nodearr with its transport sidecar on this machine, and a pinned Sonarr in a
    container for the one check that needs an Arr app: a new API key has to reach it.

    Unlike the other scripts the node is not given its port, its key or its way of signing in
    through the environment, because a setting given there cannot be changed and those are the
    ones under test. They start in config.xml instead, as on an ordinary install.

    Needs Docker and the transport sidecar. Everything lives in .run/general next to this script.

.PARAMETER TransportBin
    Path to the built nodearr-transport binary.

.PARAMETER KeepRunning
    Leave Nodearr and Sonarr running afterwards. Open http://localhost:6633/settings/general.
    Signing in is by form by then, and the script does not print the password it chose.

.PARAMETER SetupOnly
    Start Nodearr and Sonarr, run no checks, and leave them running, so the page can be looked
    at as a fresh install shows it: http://localhost:6633/settings/general. Nothing asks for a
    password, and the API key can be reset from the page. Writes .run/general/ready.txt when up.

.PARAMETER Down
    Stop everything this script started and delete .run/general.
#>
[CmdletBinding()]
param(
    [string] $TransportBin,
    [switch] $KeepRunning,
    [switch] $SetupOnly,
    [switch] $Down
)

$ErrorActionPreference = 'Stop'
# Version 1 only: the API leaves null properties out of its JSON, and reading one must give $null.
Set-StrictMode -Version 1

$root = $PSScriptRoot
$repo = Resolve-Path (Join-Path $root '..')
$run = Join-Path $root '.run/general'
$data = Join-Path $run 'a'
$pidFile = Join-Path $run 'A.pid'
$envFile = Join-Path $run '.env'
$configFile = Join-Path $data 'config.xml'
$dll = Join-Path $repo '_output/net10.0/Nodearr.Console.dll'
$composeFile = Join-Path $repo 'spikes/arr-contract/docker-compose.yml'
$sonarrUrl = 'http://127.0.0.1:18989'

function New-Key { -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }) }

function Stop-Node {
    if (Test-Path $pidFile) {
        Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
        Remove-Item $pidFile -Force
    }
}

function Invoke-Compose {
    docker compose -p nodearr-general -f $composeFile --env-file $envFile @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($args -join ' ') failed" }
}

if ($Down) {
    Stop-Node
    if (Test-Path $envFile) { Invoke-Compose down -v 2>&1 | Out-Null }
    if (Test-Path $run) { Remove-Item $run -Recurse -Force }
    Write-Host 'Stopped Nodearr and removed the container.'
    return
}

if (-not (Test-Path $dll)) { throw 'Build Nodearr first: dotnet build src/Nodearr.sln' }
if (-not $TransportBin) {
    $target = $env:CARGO_TARGET_DIR ? $env:CARGO_TARGET_DIR : (Join-Path $repo 'transport/target')
    $TransportBin = Join-Path $target ('release/nodearr-transport' + ($IsWindows ? '.exe' : ''))
}
if (-not (Test-Path $TransportBin)) { throw "Build the transport first: cargo build --release --manifest-path transport/Cargo.toml (looked for $TransportBin)" }

# Where the node answers, and with which key. Both change during the run.
$node = @{ Port = 6633; Key = New-Key }

function Invoke-Node {
    param([string] $Method, [string] $Path, $Body, [switch] $AllowError, [string] $Key = $node.Key, [int] $Port = $node.Port)

    $request = @{
        Uri = "http://127.0.0.1:$Port/api/v5$Path"; Method = $Method; Headers = @{ 'X-Api-Key' = $Key }
        SkipHttpErrorCheck = $true; StatusCodeVariable = 'status'; TimeoutSec = 30
    }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 10); $request.ContentType = 'application/json' }

    $response = Invoke-RestMethod @request
    if ($status -ge 400) {
        if ($AllowError) { return @{ Failed = $true; Status = $status; Errors = @($response) } }
        throw "$Method $Path -> $status $($response | ConvertTo-Json -Depth 6 -Compress)"
    }
    return $response
}

function Invoke-Sonarr {
    param([string] $Method, [string] $Path, $Body, [switch] $AllowError)

    $request = @{
        Uri = "$sonarrUrl/api/v3$Path"; Method = $Method; Headers = @{ 'X-Api-Key' = $sonarrKey }
        SkipHttpErrorCheck = $true; StatusCodeVariable = 'status'; TimeoutSec = 60
    }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 30); $request.ContentType = 'application/json' }

    $response = Invoke-RestMethod @request
    if ($status -ge 400) {
        if ($AllowError) { return @{ Failed = $true; Status = $status; Body = ($response | ConvertTo-Json -Depth 6 -Compress) } }
        throw "Sonarr $Method $Path -> $status $($response | ConvertTo-Json -Depth 6 -Compress)"
    }
    return $response
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

# Whether anything answers Nodearr's ping on a port.
function Test-Answers {
    param([int] $Port)
    try { return (Invoke-RestMethod "http://127.0.0.1:$Port/ping" -TimeoutSec 2).status -eq 'OK' } catch { return $false }
}

function Wait-Node {
    return Wait-Until -What "Nodearr and its transport on port $($node.Port)" -TimeoutSec 90 -Condition {
        try { $identity = Invoke-Node GET '/nodes/identity'; if ($identity.transportRunning) { $identity } } catch { $null }
    }
}

function Get-Settings { Invoke-Node GET '/settings/general' }

# Sends the settings back with some changed, the way the page does.
function Set-Settings {
    param([hashtable] $Changes, [switch] $AllowError)
    $settings = Get-Settings
    foreach ($change in $Changes.GetEnumerator()) { $settings | Add-Member -Force -NotePropertyName $change.Key -NotePropertyValue $change.Value }
    return Invoke-Node PUT '/settings/general' $settings -AllowError:$AllowError
}

function Get-Refused { param($Response) return @($Response.Errors | ForEach-Object propertyName | Sort-Object) -join ',' }

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

# The sidecars running for this node's data folder. There must only ever be one.
function Get-Sidecars {
    return @(Get-CimInstance Win32_Process -Filter "Name like 'nodearr-transport%'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like "*$data*" })
}

# A value straight from config.xml, which is where the settings on this page are kept.
function Get-Saved { param([string] $Name) return ([xml](Get-Content $configFile -Raw)).Config.$Name }

# Signs in the way the sign-in page does, and says where Nodearr sent the browser next.
function Get-SignInResult {
    param([string] $Username, [string] $Password)
    $response = Invoke-WebRequest "http://127.0.0.1:$($node.Port)/login" -Method Post -Body @{ username = $Username; password = $Password } `
        -MaximumRedirection 0 -SkipHttpErrorCheck -ErrorAction SilentlyContinue
    $location = "$($response.Headers.Location)"
    return @{ Status = [int]$response.StatusCode; SignedIn = ($response.StatusCode -eq 302 -and $location -notmatch 'loginFailed'); Cookie = [bool]$response.Headers['Set-Cookie'] }
}

$results = [System.Collections.Generic.List[object]]::new()

function Assert-That {
    param([string] $Name, [bool] $Passed, $Evidence)
    $results.Add([ordered]@{ check = $Name; passed = $Passed; evidence = $Evidence })
    Write-Host ("[{0}] {1}" -f ($Passed ? 'PASS' : 'FAIL'), $Name) -ForegroundColor ($Passed ? 'Green' : 'Red')
    if (-not $Passed) { Write-Host ("       " + ($Evidence | ConvertTo-Json -Depth 6 -Compress)) -ForegroundColor DarkGray }
}

# --- Fresh state -------------------------------------------------------------------------------

New-Item -ItemType Directory -Force $run | Out-Null
Stop-Node
if (Test-Path $envFile) { Invoke-Compose down -v 2>&1 | Out-Null }
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force $data | Out-Null

$sonarrKey = New-Key
"SONARR_API_KEY=$sonarrKey", "RADARR_API_KEY=$(New-Key)", "PROWLARR_API_KEY=$(New-Key)", "NODEARR_API_KEY=$($node.Key)" | Set-Content $envFile

# An ordinary install keeps these in config.xml, where the settings page can change them.
@"
<Config>
  <Port>$($node.Port)</Port>
  <ApiKey>$($node.Key)</ApiKey>
  <AuthenticationMethod>External</AuthenticationMethod>
  <LogLevel>debug</LogLevel>
</Config>
"@ | Set-Content $configFile

try {
    foreach ($name in 'Nodearr__Server__Port', 'Nodearr__Auth__ApiKey', 'Nodearr__Auth__Method') { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    $env:Nodearr__Log__AnalyticsEnabled = 'false'
    $env:Nodearr__Transport__BinaryPath = $TransportBin
    $env:Nodearr__Transport__Relay = 'disabled'
    $env:Nodearr__Transport__DisablePortMapping = 'true'

    # One general setting given in the environment, to see that it is shown as fixed.
    $env:Nodearr__App__LaunchBrowser = 'false'

    $process = Start-Process dotnet -ArgumentList "`"$dll`"", '-nobrowser', "-data=`"$data`"" -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $run 'A.out.log') -RedirectStandardError (Join-Path $run 'A.err.log')
    $process.Id | Set-Content $pidFile

    Invoke-Compose up -d sonarr 2>&1 | Out-Null
    $identity = Wait-Node

    if ($SetupOnly) {
        $KeepRunning = $true
        "Ready $((Get-Date).ToString('u'))", 'Nodearr: http://localhost:6633/settings/general', "Sonarr:  $sonarrUrl" | Set-Content (Join-Path $run 'ready.txt')
        Get-Content (Join-Path $run 'ready.txt') | Write-Host
        return
    }

    # --- 1. What the page shows -----------------------------------------------------------------

    $settings = Get-Settings
    Assert-That 'The page shows where Nodearr listens, its key and how signing in works, and which settings the environment fixes' (
        $settings.port -eq 6633 -and $settings.apiKey -eq $node.Key -and $settings.authenticationMethod -eq 'external' -and
        $settings.logLevel -eq 'debug' -and (@($settings.fixedByEnvironment) -join ',') -eq 'launchBrowser'
    ) @{ port = $settings.port; keyShown = ($settings.apiKey -eq $node.Key); signIn = $settings.authenticationMethod; logLevel = $settings.logLevel; fixed = @($settings.fixedByEnvironment) }

    # --- 2. Refusals, and what the environment fixes ----------------------------------------------

    $bad = Set-Settings @{ port = 99999; logSizeLimit = 50 } -AllowError
    $after = Get-Settings
    Assert-That 'Settings that cannot work are refused, each named, and nothing is changed' (
        $bad.Failed -and $bad.Status -eq 400 -and (Get-Refused $bad) -eq 'LogSizeLimit,Port' -and $after.port -eq 6633 -and (Get-Saved 'Port') -eq '6633'
    ) @{ status = $bad.Status; refused = (Get-Refused $bad); portAfter = $after.port }

    Set-Settings @{ launchBrowser = $true } | Out-Null
    $stillFixed = Get-Settings
    Assert-That 'A setting the environment fixes stays as it is, whatever is sent, and is not written to the file' (
        $stillFixed.launchBrowser -eq $false -and (Get-Saved 'LaunchBrowser') -ne 'True'
    ) @{ launchBrowser = $stillFixed.launchBrowser; inFile = (Get-Saved 'LaunchBrowser') }

    # --- 3. Logging takes effect at once ----------------------------------------------------------

    $traceLog = Join-Path $data 'logs/nodearr.trace.txt'
    $traceBefore = Test-Path $traceLog
    Set-Settings @{ logLevel = 'trace' } | Out-Null
    $traceAfter = Wait-Until -What 'the trace log to be written' -TimeoutSec 30 -Condition {
        Invoke-Node GET '/system/status' | Out-Null
        (Test-Path $traceLog) -and (Get-Item $traceLog).Length -gt 0
    }
    Set-Settings @{ logLevel = 'info' } | Out-Null
    Assert-That 'A new log level is saved and used straight away, with no restart' (
        -not $traceBefore -and $traceAfter -and (Get-Settings).logLevel -eq 'info' -and (Get-Saved 'LogLevel') -eq 'info'
    ) @{ traceLogBefore = $traceBefore; traceLogAfter = [bool]$traceAfter; levelNow = (Get-Settings).logLevel; inFile = (Get-Saved 'LogLevel') }

    # --- 4. A new port, applied by a restart from the page ----------------------------------------

    $newPort = Get-FreeTcpPort
    Set-Settings @{ port = $newPort } | Out-Null
    $notYet = (Test-Answers 6633) -and -not (Test-Answers $newPort)

    Invoke-Node POST '/system/restart' | Out-Null
    $node.Port = $newPort
    $moved = Wait-Node
    $sidecars = Get-Sidecars
    Assert-That 'A new port is saved, waits for a restart, and after the restart Nodearr answers there and nowhere else' (
        $notYet -and (Get-Settings).port -eq $newPort -and -not (Test-Answers 6633)
    ) @{ waitedForRestart = $notYet; portNow = (Get-Settings).port; oldPortStillAnswers = (Test-Answers 6633) }

    Assert-That 'The restart is the same process, the same node and one sidecar: nothing is left behind' (
        (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) -and $moved.nodeId -eq $identity.nodeId -and $moved.transportRunning -and $sidecars.Count -eq 1
    ) @{ sameProcess = [bool](Get-Process -Id $process.Id -ErrorAction SilentlyContinue); sameNodeId = ($moved.nodeId -eq $identity.nodeId); sidecars = $sidecars.Count }

    # Back to where Sonarr will be told to find it.
    Set-Settings @{ port = 6633 } | Out-Null
    Invoke-Node POST '/system/restart' | Out-Null
    $node.Port = 6633
    $back = Wait-Node
    Assert-That 'A second restart works as well as the first' (
        $back.nodeId -eq $identity.nodeId -and (Get-Sidecars).Count -eq 1 -and -not (Test-Answers $newPort)
    ) @{ sameNodeId = ($back.nodeId -eq $identity.nodeId); sidecars = (Get-Sidecars).Count; otherPortStillAnswers = (Test-Answers $newPort) }

    # --- 5. Signing in ----------------------------------------------------------------------------

    $first = New-Key
    $second = New-Key
    $mismatch = Set-Settings @{ authenticationMethod = 'forms'; authenticationRequired = 'enabled'; username = 'owner'; password = $first; passwordConfirmation = 'something else' } -AllowError
    Set-Settings @{ authenticationMethod = 'forms'; authenticationRequired = 'enabled'; username = 'owner'; password = $first; passwordConfirmation = $first } | Out-Null

    $page = Invoke-WebRequest "http://127.0.0.1:6633/" -MaximumRedirection 0 -SkipHttpErrorCheck -ErrorAction SilentlyContinue
    $right = Get-SignInResult 'owner' $first
    $wrong = Get-SignInResult 'owner' 'not the password'
    Assert-That 'Signing in by form can be turned on: the page then asks for it, takes the right password and turns the wrong one away' (
        $mismatch.Failed -and (Get-Refused $mismatch) -eq 'PasswordConfirmation' -and
        $page.StatusCode -eq 302 -and "$($page.Headers.Location)" -match '/login' -and
        $right.SignedIn -and $right.Cookie -and -not $wrong.SignedIn -and (Get-Settings).authenticationMethod -eq 'forms'
    ) @{ mismatchRefused = (Get-Refused $mismatch); pageStatus = [int]$page.StatusCode; pageSentTo = "$($page.Headers.Location)"; rightPassword = $right; wrongPassword = $wrong }

    # What a browser is sent to. Checking that signing in works is not checking that there is a
    # page to sign in on: for a while there was none, and a fresh install led straight to a 404.
    $signInPage = Invoke-WebRequest 'http://127.0.0.1:6633/login?returnUrl=%2F' -SkipHttpErrorCheck
    $logo = Invoke-WebRequest 'http://127.0.0.1:6633/Content/Images/logo.svg' -SkipHttpErrorCheck
    $followed = Invoke-WebRequest 'http://127.0.0.1:6633/' -SkipHttpErrorCheck
    $withReturn = Invoke-WebRequest 'http://127.0.0.1:6633/login?returnUrl=%2Fsettings%2Fgeneral' -Method Post -Body @{ username = 'owner'; password = $first } `
        -MaximumRedirection 0 -SkipHttpErrorCheck -ErrorAction SilentlyContinue
    Assert-That 'The page a browser is sent to exists: a sign-in form that needs nothing from behind the sign-in, and goes back to where the browser was headed' (
        $signInPage.StatusCode -eq 200 -and "$($signInPage.Headers['Content-Type'])" -match 'text/html' -and
        $signInPage.Content -match 'method="POST"' -and $signInPage.Content -match 'name="username"' -and $signInPage.Content -match 'name="password"' -and
        $logo.StatusCode -eq 200 -and $followed.StatusCode -eq 200 -and $followed.Content -match 'name="password"' -and
        $withReturn.StatusCode -eq 302 -and "$($withReturn.Headers.Location)" -eq '/settings/general'
    ) @{ pageStatus = [int]$signInPage.StatusCode; isForm = ($signInPage.Content -match 'name="password"'); logoStatus = [int]$logo.StatusCode; rootEndsAt = [int]$followed.StatusCode; afterSigningIn = "$($withReturn.Headers.Location)" }

    Set-Settings @{ password = $second; passwordConfirmation = $second } | Out-Null
    $old = Get-SignInResult 'owner' $first
    $new = Get-SignInResult 'owner' $second
    Assert-That 'A changed password is the only one that works afterwards, and the API key still does' (
        -not $old.SignedIn -and $new.SignedIn -and (Get-Settings).username -eq 'owner'
    ) @{ oldPassword = $old; newPassword = $new }

    # --- 6. A new API key reaches Sonarr ------------------------------------------------------------

    Wait-Until -What 'the Sonarr API' -TimeoutSec 240 -EverySec 2 -Condition { try { Invoke-Sonarr GET '/system/status' } catch { $null } } | Out-Null

    $definition = Invoke-Node GET '/applications/schema' | Where-Object implementation -eq 'Sonarr'
    foreach ($pair in @{ nodearrUrl = 'http://host.docker.internal:6633'; baseUrl = $sonarrUrl; apiKey = $sonarrKey }.GetEnumerator()) {
        ($definition.fields | Where-Object name -eq $pair.Key) | Add-Member -NotePropertyName value -NotePropertyValue $pair.Value -Force
    }
    $definition | Add-Member -Force -NotePropertyMembers @{ name = 'Sonarr'; syncLevel = 'fullSync' }
    Invoke-Node POST '/applications' $definition | Out-Null

    $indexer = Wait-Until -What "Nodearr's indexer in Sonarr" -TimeoutSec 60 -Condition { Invoke-Sonarr GET '/indexer' | Where-Object name -eq 'Nodearr' | Select-Object -First 1 }
    $client = Wait-Until -What "Nodearr's download client in Sonarr" -TimeoutSec 60 -Condition { Invoke-Sonarr GET '/downloadclient' | Where-Object name -eq 'Nodearr' | Select-Object -First 1 }
    $beforeReset = Invoke-Sonarr POST '/indexer/test' $indexer -AllowError

    $oldKey = $node.Key
    Invoke-Node POST '/command' @{ name = 'ResetApiKey' } | Out-Null

    # The script is in the page's position now: the key it holds stops working. The new one is
    # read from where it is kept.
    $newKey = Wait-Until -What 'a new key in config.xml' -TimeoutSec 30 -Condition { $saved = Get-Saved 'ApiKey'; if ($saved -and $saved -ne $oldKey) { $saved } }
    $node.Key = $newKey
    Wait-Until -What 'the reset to finish' -TimeoutSec 90 -Condition {
        $commands = @(Invoke-Node GET '/command' | Where-Object name -eq 'ResetApiKey')
        $commands.Count -gt 0 -and @($commands | Where-Object { $_.status -notin 'completed', 'failed' }).Count -eq 0
    } | Out-Null

    $withOld = Invoke-Node GET '/system/status' -Key $oldKey -AllowError
    $feedWithOld = Invoke-WebRequest "http://127.0.0.1:6633/newznab/api?t=tvsearch&apikey=$oldKey" -SkipHttpErrorCheck
    $shown = (Get-Settings).apiKey -eq $newKey
    Assert-That 'Resetting the API key replaces it at once: the old key is refused everywhere, with no restart' (
        $newKey -match '^[0-9a-f]{32}$' -and $withOld.Failed -and $withOld.Status -eq 401 -and $feedWithOld.Content -match 'code="100"' -and $shown
    ) @{ newKeyLooksRight = ($newKey -match '^[0-9a-f]{32}$'); oldKeyStatus = $withOld.Status; feedRefusesOldKey = ($feedWithOld.Content -match 'code="100"'); pageShowsNewKey = $shown }

    # Sonarr tests an indexer and a download client with the key it holds for them.
    $indexerNow = Invoke-Sonarr GET '/indexer' | Where-Object name -eq 'Nodearr' | Select-Object -First 1
    $clientNow = Invoke-Sonarr GET '/downloadclient' | Where-Object name -eq 'Nodearr' | Select-Object -First 1
    $indexerTest = Invoke-Sonarr POST '/indexer/test' $indexerNow -AllowError
    $clientTest = Invoke-Sonarr POST '/downloadclient/test' $clientNow -AllowError
    Assert-That 'Sonarr was given the new key: its own tests of the Nodearr indexer and download client pass with it' (
        -not $beforeReset.Failed -and -not $indexerTest.Failed -and -not $clientTest.Failed -and $indexerNow.id -eq $indexer.id -and $clientNow.id -eq $client.id
    ) @{ indexerTestBefore = (-not $beforeReset.Failed); indexerTestAfter = ($indexerTest.Failed ? $indexerTest.Body : 'passed'); downloadClientTestAfter = ($clientTest.Failed ? $clientTest.Body : 'passed') }
}
catch {
    Write-Host "Stopped: $_" -ForegroundColor Red
    $results.Add([ordered]@{ check = 'Script ran to the end'; passed = $false; evidence = "$_" })
}
finally {
    $failed = @($results | Where-Object { -not $_.passed })

    if (-not $SetupOnly) {
        [ordered]@{ ranAt = (Get-Date).ToUniversalTime().ToString('o'); passed = ($failed.Count -eq 0 -and $results.Count -gt 0); checks = $results } |
            ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'results.json')
    }

    if (-not $KeepRunning) {
        Stop-Node
        Invoke-Compose down -v 2>&1 | Out-Null
    }
}

if ($SetupOnly) { return }

Write-Host "`n$($results.Count - $failed.Count) of $($results.Count) checks passed. Details: $(Join-Path $run 'results.json')"
if ($failed.Count -gt 0) { exit 1 }
