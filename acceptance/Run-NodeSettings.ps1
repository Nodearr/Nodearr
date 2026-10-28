#requires -Version 7
<#
.SYNOPSIS
    Acceptance for the node settings page: settings changed through the API take effect on a
    running node, bad ones are refused, and ones fixed by the environment stay fixed.

.DESCRIPTION
    Runs two real Nodearr instances with real transport sidecars on this machine, as the S4
    script does. Both are started with the relay and port mapping switched off in the
    environment, which keeps the test off the network and gives it two settings that are fixed.

    Needs the transport sidecar. Everything lives in .run/settings next to this script.

.PARAMETER TransportBin
    Path to the built nodearr-transport binary.

.PARAMETER KeepRunning
    Leave both instances running afterwards: A on port 6633, B on port 6634. Open
    http://localhost:6633/settings/node to see the page.
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
$run = Join-Path $root '.run/settings'
$dll = Join-Path $repo '_output/net10.0/Nodearr.Console.dll'

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

function Stop-Node {
    param([string] $Name)
    $pidFile = Join-Path $run "$Name.pid"
    if (Test-Path $pidFile) {
        Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
        Remove-Item $pidFile -Force
    }
}

function Invoke-Node {
    param([string] $Name, [string] $Method, [string] $Path, $Body, [switch] $AllowError)

    $request = @{
        Uri = "http://127.0.0.1:$($nodes[$Name].Port)/api/v5$Path"; Method = $Method; Headers = @{ 'X-Api-Key' = $nodes[$Name].Key }
        SkipHttpErrorCheck = $true; StatusCodeVariable = 'status'
    }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 10); $request.ContentType = 'application/json' }

    $response = Invoke-RestMethod @request
    if ($status -ge 400) {
        if ($AllowError) { return @{ Failed = $true; Status = $status; Errors = @($response) } }
        throw "$Method $Path -> $status $($response | ConvertTo-Json -Depth 6 -Compress)"
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

function Start-Node {
    param([string] $Name)

    $node = $nodes[$Name]
    $env:Nodearr__Server__Port = $node.Port
    $env:Nodearr__Auth__ApiKey = $node.Key
    $env:Nodearr__Auth__Method = 'External'
    $env:Nodearr__Log__AnalyticsEnabled = 'false'
    $env:Nodearr__Transport__BinaryPath = $TransportBin

    # Fixed by the environment, as a container would have them.
    $env:Nodearr__Transport__Relay = 'disabled'
    $env:Nodearr__Transport__DisablePortMapping = 'true'

    $process = Start-Process dotnet -ArgumentList "`"$dll`"", '-nobrowser', "-data=`"$($node.Data)`"" -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $run "$Name.out.log") -RedirectStandardError (Join-Path $run "$Name.err.log")
    $process.Id | Set-Content (Join-Path $run "$Name.pid")

    return Wait-Until -What "node $Name and its transport" -TimeoutSec 90 -Condition {
        try { $identity = Invoke-Node $Name GET '/nodes/identity'; if ($identity.transportRunning) { $identity } } catch { $null }
    }
}

function Get-Settings { param([string] $Name) Invoke-Node $Name GET '/settings/node' }

# Sends the node's settings back with some changed, the way the page does.
function Set-Settings {
    param([string] $Name, [hashtable] $Changes, [switch] $AllowError)
    $settings = Get-Settings $Name
    foreach ($change in $Changes.GetEnumerator()) { $settings | Add-Member -Force -NotePropertyName $change.Key -NotePropertyValue $change.Value }
    return Invoke-Node $Name PUT '/settings/node' $settings -AllowError:$AllowError
}

# The settings a refused save pointed at.
function Get-Refused { param($Response) return @($Response.Errors | ForEach-Object propertyName | Sort-Object) -join ',' }

function Get-Ports { param($Identity) return @($Identity.ipAddresses | ForEach-Object { ($_ -split ':')[-1] } | Sort-Object -Unique) }

function Get-FreeUdpPort {
    $socket = [System.Net.Sockets.Socket]::new('InterNetwork', 'Dgram', 'Udp')
    try { $socket.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)); return $socket.LocalEndPoint.Port } finally { $socket.Dispose() }
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
'A', 'B' | ForEach-Object { Stop-Node $_ }
foreach ($dir in $nodes.A.Data, $nodes.B.Data, (Join-Path $run 'moved-downloads')) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $nodes.A.Data, $nodes.B.Data | Out-Null
@{ A = $nodes.A.Key; B = $nodes.B.Key } | ConvertTo-Json | Set-Content (Join-Path $run 'node-keys.json')

try {
    $identityA = Start-Node A
    $identityB = Start-Node B

    # --- What a new node starts with -------------------------------------------------------------

    $initial = Get-Settings A
    Assert-That 'A new node shows its settings, and which of them the environment fixes' (
        $initial.relay -eq 'disabled' -and -not $initial.portMapping -and $initial.port -eq 0 -and $initial.directTimeoutSeconds -eq 20 -and
        $initial.uploadLimitKbps -eq 0 -and $initial.totalUploadLimitKbps -eq 0 -and $initial.downloadLimitKbps -eq 0 -and $initial.nodeName -eq '' -and $initial.downloadFolder -eq '' -and
        (@($initial.fixedByEnvironment) -join ',') -eq 'PortMapping,Relay' -and $initial.defaultNodeName -and
        $initial.defaultDownloadFolder -eq (Join-Path $nodes.A.Data 'downloads') -and $initial.transportRunning
    ) @{ settings = $initial }

    # --- Bad settings are refused, and say which --------------------------------------------------

    $bad = Set-Settings A @{ port = 80; directTimeoutSeconds = 1; uploadLimitKbps = 5; totalUploadLimitKbps = 5; downloadLimitKbps = 5; downloadFolder = 'not/a/whole/path'; nodeName = ('x' * 80) } -AllowError
    $afterBad = Get-Settings A
    Assert-That 'Settings that cannot work are refused together, each named, and nothing is changed' (
        $bad.Failed -and $bad.Status -eq 400 -and (Get-Refused $bad) -eq 'DirectTimeoutSeconds,DownloadFolder,DownloadLimitKbps,NodeName,Port,TotalUploadLimitKbps,UploadLimitKbps' -and
        $afterBad.port -eq 0 -and $afterBad.nodeName -eq '' -and $afterBad.directTimeoutSeconds -eq 20
    ) @{ status = $bad.Status; refused = (Get-Refused $bad); messages = @($bad.Errors | ForEach-Object errorMessage) }

    $takenPort = [int]@(Get-Ports $identityB)[0]
    $taken = Set-Settings A @{ port = $takenPort } -AllowError
    Assert-That 'A port another program is using is refused' (
        $taken.Failed -and (Get-Refused $taken) -eq 'Port' -and "$($taken.Errors[0].errorMessage)" -match 'already in use'
    ) @{ port = $takenPort; message = "$($taken.Errors[0].errorMessage)" }

    $fixed = Set-Settings A @{ relay = 'n0'; portMapping = $true }
    Assert-That 'A setting the environment fixes stays as it is, whatever is sent' (
        $fixed.relay -eq 'disabled' -and -not $fixed.portMapping -and -not $fixed.transportRestarted
    ) @{ relay = $fixed.relay; portMapping = $fixed.portMapping; restarted = $fixed.transportRestarted }

    # --- Pair, then change things under a node that is trusted ------------------------------------

    $invite = Invoke-Node A POST '/nodes/invites' @{ validHours = 1; label = 'settings' }
    Invoke-Node B POST '/nodes/invites/redeem' @{ code = $invite.code } | Out-Null
    $aOnB = Invoke-Node B GET '/nodes/peers' | Select-Object -First 1

    $named = Set-Settings A @{ nodeName = 'John' }
    $seenByB = Invoke-Node B POST "/nodes/peers/$($aOnB.id)/ping"
    Assert-That 'A new name is used at once, with no restart, and a trusted node sees it' (
        $named.nodeName -eq 'John' -and -not $named.transportRestarted -and (Invoke-Node A GET '/nodes/identity').name -eq 'John' -and
        $seenByB.name -eq 'John' -and -not $seenByB.lastError
    ) @{ saved = $named.nodeName; restarted = $named.transportRestarted; bSees = $seenByB.name; error = $seenByB.lastError }

    $port = Get-FreeUdpPort
    $moved = Set-Settings A @{ port = $port; uploadLimitKbps = 2048; totalUploadLimitKbps = 3072; downloadLimitKbps = 4096; directTimeoutSeconds = 30 }
    $identityAfter = Invoke-Node A GET '/nodes/identity'
    $command = (Get-CimInstance Win32_Process -Filter "Name = 'nodearr-transport.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -match [regex]::Escape($nodes.A.Data) }).CommandLine
    Assert-That 'A new port, limits and wait restart the transport, which comes back as the same node on that port' (
        $moved.transportRestarted -and $moved.transportRunning -and $identityAfter.nodeId -eq $identityA.nodeId -and
        (Get-Ports $identityAfter) -contains "$port" -and "$command" -match "--bind-port $port" -and "$command" -match '--upload-limit-kbps 2048' -and "$command" -match '--total-upload-limit-kbps 3072' -and "$command" -match '--download-limit-kbps 4096' -and
        "$command" -match '--direct-timeout-secs 30' -and "$command" -match '--relay disabled' -and "$command" -match '--no-port-mapping'
    ) @{ port = $port; restarted = $moved.transportRestarted; running = $moved.transportRunning; sameNode = ($identityAfter.nodeId -eq $identityA.nodeId); ports = @(Get-Ports $identityAfter); sidecar = ("$command" -replace '^.*nodearr-transport(\.exe)?"?\s*', '') }

    # A tells the nodes it trusts where it is now. B then reaches it on the new port.
    $reached = Wait-Until -What 'B to reach A at its new port' -TimeoutSec 60 -EverySec 3 -Condition {
        $check = Invoke-Node B POST "/nodes/peers/$($aOnB.id)/ping"
        if (-not $check.lastError -and $check.connectionPath -eq 'direct' -and "$($check.connectionAddress)" -match ":$port$") { $check }
    }
    Assert-That 'A trusted node finds it again at the new port, directly, without pairing again' (
        $reached.state -eq 'trusted' -and $reached.connectionPath -eq 'direct'
    ) @{ state = $reached.state; path = $reached.connectionPath; address = $reached.connectionAddress }

    # --- The download folder ----------------------------------------------------------------------

    $newFolder = Join-Path $run 'moved-downloads'
    $folderSaved = Set-Settings A @{ downloadFolder = $newFolder }
    # Nodearr makes complete/ and its category folders at once, so the Arr apps find the folder they
    # are told about. The check for writing leaves no file behind.
    $reported = (Invoke-RestMethod "http://127.0.0.1:$($nodes.A.Port)/sabnzbd/api?mode=get_config&output=json&apikey=$($nodes.A.Key)").config.misc.complete_dir
    Assert-That 'A new download folder is made, checked for writing, and is what Sonarr and Radarr are told' (
        $folderSaved.downloadFolder -eq $newFolder -and -not $folderSaved.transportRestarted -and (Test-Path $newFolder) -and
        $reported -eq (Join-Path $newFolder 'complete') -and @(Get-ChildItem $newFolder -Recurse -File -Force).Count -eq 0
    ) @{ saved = $folderSaved.downloadFolder; exists = (Test-Path $newFolder); toldToArr = $reported }

    # --- It all survives a restart of Nodearr -----------------------------------------------------

    Stop-Node A
    $identityRestarted = Start-Node A
    $kept = Get-Settings A
    Assert-That 'After Nodearr restarts, the settings are as they were saved and the node is still itself' (
        $kept.nodeName -eq 'John' -and $kept.port -eq $port -and $kept.uploadLimitKbps -eq 2048 -and $kept.totalUploadLimitKbps -eq 3072 -and $kept.downloadLimitKbps -eq 4096 -and $kept.directTimeoutSeconds -eq 30 -and
        $kept.downloadFolder -eq $newFolder -and $kept.relay -eq 'disabled' -and
        $identityRestarted.nodeId -eq $identityA.nodeId -and $identityRestarted.name -eq 'John' -and (Get-Ports $identityRestarted) -contains "$port"
    ) @{ settings = @{ nodeName = $kept.nodeName; port = $kept.port; uploadLimitKbps = $kept.uploadLimitKbps; downloadFolder = $kept.downloadFolder }; sameNode = ($identityRestarted.nodeId -eq $identityA.nodeId) }

    $cleared = Set-Settings A @{ nodeName = ''; port = 0; uploadLimitKbps = 0; totalUploadLimitKbps = 0; downloadLimitKbps = 0; downloadFolder = '' }
    $identityCleared = Invoke-Node A GET '/nodes/identity'
    Assert-That 'Clearing a setting goes back to its default' (
        $cleared.nodeName -eq '' -and $cleared.port -eq 0 -and $cleared.downloadLimitKbps -eq 0 -and $cleared.totalUploadLimitKbps -eq 0 -and $cleared.downloadFolder -eq '' -and $cleared.transportRestarted -and $cleared.transportRunning -and
        $identityCleared.name -eq $initial.defaultNodeName -and $identityCleared.nodeId -eq $identityA.nodeId
    ) @{ name = $identityCleared.name; restarted = $cleared.transportRestarted; running = $cleared.transportRunning }
}
catch {
    Write-Host "Stopped: $_" -ForegroundColor Red
    $results.Add([ordered]@{ check = 'Script ran to the end'; passed = $false; evidence = "$_" })
}
finally {
    $failed = @($results | Where-Object { -not $_.passed })
    [ordered]@{ ranAt = (Get-Date).ToUniversalTime().ToString('o'); passed = ($failed.Count -eq 0 -and $results.Count -gt 0); checks = $results } |
        ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'results.json')

    if (-not $KeepRunning) { 'A', 'B' | ForEach-Object { Stop-Node $_ } }
}

Write-Host "`n$($results.Count - $failed.Count) of $($results.Count) checks passed. Details: $(Join-Path $run 'results.json')"
if ($failed.Count -gt 0) { exit 1 }
