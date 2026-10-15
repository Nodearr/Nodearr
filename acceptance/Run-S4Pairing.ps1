#requires -Version 7
<#
.SYNOPSIS
    S4 acceptance: identity, invites, trust, blocking and revoking between two real Nodearr instances.

.DESCRIPTION
    Starts two Nodearr instances on this machine, each with its own data folder and its own
    nodearr-transport sidecar, and drives them through their HTTP APIs.

    The sidecars run with relays and port mapping switched off and reach each other on local
    addresses, so nothing leaves the machine.

    Everything lives in .run/s4 next to this script.

.PARAMETER TransportBin
    Path to the built nodearr-transport binary.

.PARAMETER KeepRunning
    Leave both instances running afterwards: A on port 6633 and B on port 6634.
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
$run = Join-Path $root '.run/s4'
$dll = Join-Path $repo '_output/net10.0/Nodearr.Console.dll'
if (-not (Test-Path $dll)) { throw 'Build Nodearr first: dotnet build src/Nodearr.sln' }

if (-not $TransportBin) {
    $target = $env:CARGO_TARGET_DIR ? $env:CARGO_TARGET_DIR : (Join-Path $repo 'transport/target')
    $TransportBin = Join-Path $target ('release/nodearr-transport' + ($IsWindows ? '.exe' : ''))
}
if (-not (Test-Path $TransportBin)) { throw "Build the transport first: cargo build --release --manifest-path transport/Cargo.toml (looked for $TransportBin)" }

$nodes = @{
    A = @{ Port = 6633; Data = Join-Path $run 'a' }
    B = @{ Port = 6634; Data = Join-Path $run 'b' }
}

function Stop-Node {
    param([string] $Name)

    $pidFile = Join-Path $run "$Name.pid"
    if (Test-Path $pidFile) {
        Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
        Remove-Item $pidFile -Force
    }
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
        try { (Invoke-Api $Name GET '/nodes/identity').transportRunning } catch { $false }
    } | Out-Null
}

function Invoke-Api {
    param([string] $Name, [string] $Method, [string] $Path, $Body, [switch] $AllowError)

    $node = $nodes[$Name]
    $request = @{
        Uri = "http://127.0.0.1:$($node.Port)/api/v5$Path"; Method = $Method; Headers = @{ 'X-Api-Key' = $node.Key }
        SkipHttpErrorCheck = $true; StatusCodeVariable = 'status'
    }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 10); $request.ContentType = 'application/json' }

    $response = Invoke-RestMethod @request
    if ($status -ge 400) {
        if ($AllowError) { return @{ Failed = $true; Status = $status; Message = $response.message } }
        throw "$Name $Method $Path -> $status $($response | ConvertTo-Json -Depth 6 -Compress)"
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

$results = [System.Collections.Generic.List[object]]::new()

function Assert-That {
    param([string] $Name, [bool] $Passed, $Evidence)

    $results.Add([ordered]@{ check = $Name; passed = $Passed; evidence = $Evidence })
    Write-Host ("[{0}] {1}" -f ($Passed ? 'PASS' : 'FAIL'), $Name) -ForegroundColor ($Passed ? 'Green' : 'Red')
    if (-not $Passed) { Write-Host ("       " + ($Evidence | ConvertTo-Json -Depth 6 -Compress)) -ForegroundColor DarkGray }
}

function Get-Peer {
    param([string] $On, [string] $NodeId)
    return Invoke-Api $On GET '/nodes/peers' | Where-Object nodeId -eq $NodeId | Select-Object -First 1
}

function Invoke-Pairing {
    $invite = Invoke-Api A POST '/nodes/invites' @{ validHours = 1; label = 'acceptance' }
    Invoke-Api B POST '/nodes/invites/redeem' @{ code = $invite.code } | Out-Null
    return $invite
}

# --- Fresh state -------------------------------------------------------------------------------

New-Item -ItemType Directory -Force $run | Out-Null
foreach ($name in $nodes.Keys) {
    Stop-Node $name
    if (Test-Path $nodes[$name].Data) { Remove-Item $nodes[$name].Data -Recurse -Force }
    New-Item -ItemType Directory -Force $nodes[$name].Data | Out-Null
    $nodes[$name].Key = -join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
}

try {
    Start-Node A
    Start-Node B

    # --- 1. Identity -----------------------------------------------------------------------------

    $idA = Invoke-Api A GET '/nodes/identity'
    $idB = Invoke-Api B GET '/nodes/identity'
    Assert-That 'Each node has its own identity, backed by a key file in its data folder' (
        $idA.nodeId -match '^[0-9a-f]{64}$' -and $idB.nodeId -match '^[0-9a-f]{64}$' -and $idA.nodeId -ne $idB.nodeId -and
        (Test-Path (Join-Path $nodes.A.Data 'node.key')) -and (Test-Path (Join-Path $nodes.B.Data 'node.key'))
    ) @{ a = $idA.nodeId; b = $idB.nodeId }

    # --- 2. Pairing ------------------------------------------------------------------------------

    $invite = Invoke-Pairing
    $bOnA = Get-Peer A $idB.nodeId
    $aOnB = Get-Peer B $idA.nodeId
    Assert-That 'Redeeming an invite makes both nodes trust each other' (
        $bOnA.state -eq 'trusted' -and $aOnB.state -eq 'trusted' -and $bOnA.name -and $aOnB.name
    ) @{ bOnA = $bOnA; aOnB = $aOnB }

    $again = Invoke-Api B POST '/nodes/invites/redeem' @{ code = $invite.code } -AllowError
    $stored = Invoke-Api A GET '/nodes/invites' | Where-Object id -eq $invite.id
    Assert-That 'An invite works once, and the code is not kept after it is shown' (
        $again.Failed -and $again.Status -eq 400 -and $stored.redeemedByNodeId -eq $idB.nodeId -and -not $stored.code -and $invite.code -like 'nodearr-invite:*'
    ) @{ secondRedeem = $again; stored = $stored }

    # Nobody has asked for a connection check yet.
    $unasked = Wait-Until -What 'both nodes to check their connection after pairing' -TimeoutSec 40 -Condition {
        $a = Get-Peer A $idB.nodeId
        $b = Get-Peer B $idA.nodeId
        if ($a.connectionChecked -and $b.connectionChecked) { @{ A = $a; B = $b } }
    }
    Assert-That 'After pairing each node checks the connection without being asked, and finds it direct' (
        $unasked.A.connectionPath -eq 'direct' -and $unasked.B.connectionPath -eq 'direct' -and -not $unasked.A.lastError -and -not $unasked.B.lastError
    ) @{ aFound = $unasked.A.connectionPath; bFound = $unasked.B.connectionPath; aError = $unasked.A.lastError; bError = $unasked.B.lastError }

    $pingB = Invoke-Api B POST "/nodes/peers/$($aOnB.id)/ping"
    $pingA = Invoke-Api A POST "/nodes/peers/$($bOnA.id)/ping"
    Assert-That 'Trusted nodes reach each other in both directions' (
        $pingA.lastSeen -and -not $pingA.lastError -and $pingB.lastSeen -and -not $pingB.lastError
    ) @{ aToB = $pingA; bToA = $pingB }

    # --- 3. Blocking -----------------------------------------------------------------------------

    $blocked = Invoke-Api A POST "/nodes/peers/$($bOnA.id)/block"
    $refused = Invoke-Api B POST "/nodes/peers/$($aOnB.id)/ping"
    Assert-That 'A blocked node is refused when it connects' (
        $blocked.state -eq 'blocked' -and $refused.lastError -like 'Blocked*'
    ) @{ state = $blocked.state; pingFromBlockedNode = $refused.lastError }

    $newInvite = Invoke-Api A POST '/nodes/invites' @{ validHours = 1; label = 'for a blocked node' }
    $repair = Invoke-Api B POST '/nodes/invites/redeem' @{ code = $newInvite.code } -AllowError
    $deleteBlocked = Invoke-Api A DELETE "/nodes/peers/$($bOnA.id)" -AllowError
    Assert-That 'A blocked node cannot pair again with a new invite, and the block cannot be deleted away' (
        $repair.Failed -and (Get-Peer A $idB.nodeId).state -eq 'blocked' -and $deleteBlocked.Failed
    ) @{ redeem = $repair; delete = $deleteBlocked }

    $unblocked = Invoke-Api A POST "/nodes/peers/$($bOnA.id)/unblock"
    $stillRefused = Invoke-Api B POST "/nodes/peers/$($aOnB.id)/ping" -AllowError
    Assert-That 'Unblocking does not restore trust' (
        $unblocked.state -eq 'revoked' -and ($stillRefused.Failed -or $stillRefused.lastError -like 'NotAuthorized*')
    ) @{ state = $unblocked.state; ping = $stillRefused }

    Invoke-Pairing | Out-Null
    $peersOnA = @(Invoke-Api A GET '/nodes/peers')
    Assert-That 'Pairing again with a new invite restores trust without duplicating the node' (
        $peersOnA.Count -eq 1 -and $peersOnA[0].state -eq 'trusted' -and (Get-Peer B $idA.nodeId).state -eq 'trusted'
    ) @{ peersOnA = $peersOnA.Count; state = $peersOnA[0].state }

    # --- 4. Restart ------------------------------------------------------------------------------

    Stop-Node A
    Start-Sleep -Seconds 2
    $orphans = @(Get-CimInstance Win32_Process -Filter "Name like 'nodearr-transport%'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like "*$($nodes.A.Data)*" })
    Start-Node A

    $idA2 = Invoke-Api A GET '/nodes/identity'
    # A's addresses changed with the restart. It tells its trusted peers when its transport comes up.
    $reachable = Wait-Until -What 'B to reach A after the restart' -TimeoutSec 30 -Condition {
        $ping = Invoke-Api B POST "/nodes/peers/$((Get-Peer B $idA.nodeId).id)/ping"
        if (-not $ping.lastError) { $ping }
    }
    $health = @(Invoke-Api A GET '/health' | Where-Object source -eq 'PeerTransportCheck')
    Assert-That 'A restart keeps the identity and the trust, stops the old sidecar, and peers find the node again' (
        $idA2.nodeId -eq $idA.nodeId -and (Get-Peer A $idB.nodeId).state -eq 'trusted' -and $orphans.Count -eq 0 -and $reachable.lastSeen -and $health.Count -eq 0
    ) @{ sameNodeId = ($idA2.nodeId -eq $idA.nodeId); orphanSidecars = $orphans.Count; transportHealthErrors = $health.Count }

    # --- 5. Revoking -----------------------------------------------------------------------------

    $revoked = Invoke-Api A POST "/nodes/peers/$((Get-Peer A $idB.nodeId).id)/revoke"
    $seenByB = Wait-Until -What 'B to learn it was revoked' -TimeoutSec 15 -Condition {
        $peer = Get-Peer B $idA.nodeId
        if ($peer.state -eq 'revoked') { $peer }
    }
    $afterRevoke = Invoke-Api B POST "/nodes/peers/$($seenByB.id)/ping" -AllowError
    Assert-That 'Revoking ends trust on both sides' (
        $revoked.state -eq 'revoked' -and $seenByB.state -eq 'revoked' -and $afterRevoke.Failed
    ) @{ onA = $revoked.stateReason; onB = $seenByB.stateReason; pingAfter = $afterRevoke }

    $badCode = Invoke-Api B POST '/nodes/invites/redeem' @{ code = 'nodearr-invite:not-a-real-code' } -AllowError
    Assert-That 'A malformed invite code is refused with a reason' ($badCode.Failed -and $badCode.Status -eq 400 -and $badCode.Message) $badCode
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
        Stop-Node A
        Stop-Node B
    }
}

Write-Host "`n$($results.Count - $failed.Count) of $($results.Count) checks passed. Details: $(Join-Path $run 'results.json')"
if ($failed.Count -gt 0) { exit 1 }
