#requires -Version 7
<#
.SYNOPSIS
    S2 spike: runs the direct-only transfer scenarios on one machine against a self-hosted iroh relay.

.DESCRIPTION
    Starts `iroh-relay --dev` (plain HTTP on localhost:3340, metrics on localhost:9090), then runs the
    spike binary as provider and fetcher for each scenario. Relayed bytes are measured twice: by iroh's
    own per-path counters on both endpoints, and independently by the relay server's metrics.

    This covers same-machine paths only. The cross-NAT run needs a second machine: see README.md.

.PARAMETER Bin
    Path to the built nodearr-iroh-spike binary.

.PARAMETER RelayBin
    Path to an iroh-relay 1.3.0 binary built with the `server` feature.
#>
[CmdletBinding()]
param(
    [string] $Bin,
    [string] $RelayBin = 'iroh-relay',
    # Also run against n0's public relays. Only handshake and coordination traffic reaches them.
    [switch] $IncludeN0,
    [int] $FileMegabytes = 16,
    [int] $DirectTimeout = 12
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$run = Join-Path $root '.run'
New-Item -ItemType Directory -Force $run | Out-Null

if (-not $Bin) {
    $target = $env:CARGO_TARGET_DIR ? $env:CARGO_TARGET_DIR : (Join-Path $root 'target')
    $Bin = Join-Path $target ('release/nodearr-iroh-spike' + ($IsWindows ? '.exe' : ''))
}
if (-not (Test-Path $Bin)) { throw "Build the spike first: cargo build --release (looked for $Bin)" }

$relayUrl = 'http://localhost:3340'
$source = Join-Path $run 'source.bin'
if (-not (Test-Path $source) -or (Get-Item $source).Length -ne $FileMegabytes * 1MB) {
    $bytes = [byte[]]::new($FileMegabytes * 1MB)
    [System.Random]::new(42).NextBytes($bytes)
    [System.IO.File]::WriteAllBytes($source, $bytes)
}

function Get-RelayBytes {
    $text = (Invoke-WebRequest 'http://localhost:9090/metrics' -TimeoutSec 5).Content
    $value = { param($name) [long](($text -split "`n" | Where-Object { $_ -match "^$name " }) -replace '^\S+ ', '') }
    return @{ Sent = (& $value 'relayserver_bytes_sent_total'); Received = (& $value 'relayserver_bytes_recv_total') }
}

function Get-Result {
    param([string] $File)
    return Get-Content $File | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object event -eq 'result' | Select-Object -Last 1
}

$results = [System.Collections.Generic.List[object]]::new()

function Invoke-Scenario {
    param(
        [string] $Name, [string[]] $ProvideArgs, [string[]] $FetchArgs,
        [switch] $RelayOnlyTicket, [string] $Expect, [bool] $ExpectRelayedMedia = $false
    )

    $id = ($Name -replace '[^a-z0-9]+', '-').Trim('-')
    $provideOut = Join-Path $run "$id.provide.jsonl"
    $fetchOut = Join-Path $run "$id.fetch.jsonl"
    $target = Join-Path $run "$id.out"
    Remove-Item $provideOut, $fetchOut, $target -Force -ErrorAction SilentlyContinue

    $before = Get-RelayBytes
    $provider = Start-Process $Bin -PassThru -WindowStyle Hidden -RedirectStandardOutput $provideOut `
        -RedirectStandardError (Join-Path $run "$id.provide.err") `
        -ArgumentList (@('provide', '--file', "`"$source`"", '--once', '--direct-timeout', $DirectTimeout) + $ProvideArgs)

    try {
        $bound = $null
        foreach ($attempt in 1..60) {
            $bound = Get-Content $provideOut -ErrorAction SilentlyContinue | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object event -eq 'bound' | Select-Object -First 1
            if ($bound) { break }
            Start-Sleep -Milliseconds 250
        }
        if (-not $bound) { throw "provider for '$Name' never bound" }

        # A relay-only ticket withholds the provider's IP addresses, so the connection has to start on
        # the relay, the way it does when the peers are behind different NATs.
        $ticket = $RelayOnlyTicket ? "$($bound.endpoint_id);$($bound.home_relay);" : $bound.ticket

        & $Bin fetch --ticket $ticket --out $target --direct-timeout $DirectTimeout @FetchArgs 2> (Join-Path $run "$id.fetch.err") | Set-Content $fetchOut
        $provider.WaitForExit(($DirectTimeout + 30) * 1000) | Out-Null
    }
    finally {
        if (-not $provider.HasExited) { $provider.Kill() }
    }

    Start-Sleep -Milliseconds 500
    $after = Get-RelayBytes
    $fetch = Get-Result $fetchOut
    $provide = Get-Result $provideOut
    $intact = (Test-Path $target) -and ((Get-FileHash $target -Algorithm SHA256).Hash -eq (Get-FileHash $source -Algorithm SHA256).Hash)
    $relayServerBytes = ($after.Sent - $before.Sent)

    # "Relayed media" means file data crossed the relay. The relay always carries some bytes for the
    # handshake and for hole-punching coordination, so compare against the file size, not against zero.
    $relayedMedia = ($provide.media_bytes_moved_while_relay_selected -gt 0) -or ($fetch.media_bytes_moved_while_relay_selected -gt 0)
    $passed = ($fetch.outcome -eq $Expect) -and ($relayedMedia -eq $ExpectRelayedMedia) -and (($Expect -eq 'Completed') -eq $intact)
    # Both the relay server's count and each endpoint's own relay-path count must stay at handshake scale.
    $relayPathBytes = [Math]::Max([long]$provide.relay_paths.tx_bytes, [long]$fetch.relay_paths.tx_bytes)
    if (-not $ExpectRelayedMedia) { $passed = $passed -and ($relayServerBytes -lt 64KB) -and ($relayPathBytes -lt 64KB) }

    $results.Add([ordered]@{
        scenario = $Name; passed = $passed; expected = $Expect
        fetchOutcome = $fetch.outcome; provideOutcome = $provide.outcome; detail = $fetch.detail
        fileBytes = (Get-Item $source).Length; fileArrivedIntact = $intact
        providerMediaBytesSent = $provide.media_bytes; fetcherMediaBytesReceived = $fetch.media_bytes
        mediaBytesMovedWhileRelaySelected = [Math]::Max([long]$provide.media_bytes_moved_while_relay_selected, [long]$fetch.media_bytes_moved_while_relay_selected)
        relayServerBytesForwarded = $relayServerBytes
        providerRelayPaths = $provide.relay_paths; providerDirectPaths = $provide.direct_paths
        fetcherRelayPaths = $fetch.relay_paths; fetcherDirectPaths = $fetch.direct_paths
        seconds = [Math]::Round($fetch.seconds, 2)
        selectedPathHistory = @(Get-Content $fetchOut | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object event -eq 'path_selected' | ForEach-Object remote)
    })
    Remove-Item $target -Force -ErrorAction SilentlyContinue
    Write-Host ("[{0}] {1}: {2}, relay forwarded {3:N0} bytes" -f ($passed ? 'PASS' : 'FAIL'), $Name, $fetch.outcome, $relayServerBytes) -ForegroundColor ($passed ? 'Green' : 'Red')
}

# --- Relay -------------------------------------------------------------------------------------

$relay = Start-Process $RelayBin -ArgumentList '--dev' -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $run 'relay.out.log') -RedirectStandardError (Join-Path $run 'relay.err.log')

try {
    foreach ($attempt in 1..40) {
        try { Get-RelayBytes | Out-Null; break } catch { Start-Sleep -Milliseconds 250 }
    }

    $custom = @('--relay', 'custom', '--relay-url', $relayUrl)

    Invoke-Scenario 'strict: relay disabled, direct addresses in the ticket' -Expect 'Completed' `
        -ProvideArgs @('--relay', 'disabled') -FetchArgs @('--relay', 'disabled')

    Invoke-Scenario 'coordinated: starts on the relay, file data waits for the direct path' -Expect 'Completed' -RelayOnlyTicket `
        -ProvideArgs $custom -FetchArgs $custom

    Invoke-Scenario 'no direct route: provider reachable only through the relay' -Expect 'NoDirectRoute' -RelayOnlyTicket `
        -ProvideArgs ($custom + '--no-ip') -FetchArgs $custom

    Invoke-Scenario 'control: same relay-only peers with the direct-only rule switched off' -Expect 'Completed' -RelayOnlyTicket -ExpectRelayedMedia $true `
        -ProvideArgs ($custom + @('--no-ip', '--media', 'allow-relay')) -FetchArgs ($custom + @('--media', 'allow-relay'))

    # Any well-formed endpoint ID that is not the fetcher's will do for the allow list.
    $stranger = (Get-Content (Join-Path $run 'strict-relay-disabled-direct-addresses-in-the-ticket.provide.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object event -eq 'bound').endpoint_id
    Invoke-Scenario 'authorization: fetcher is not on the provider allow list' -Expect 'NotAuthorized' `
        -ProvideArgs ($custom + @('--allow', $stranger)) -FetchArgs $custom

    if ($IncludeN0) {
        # The local relay's counter does not see this traffic. The endpoints' own relay-path counters do.
        $n0 = @('--relay', 'n0')
        Invoke-Scenario 'n0 relays, coordinated: starts on a public relay, file data waits for the direct path' -Expect 'Completed' -RelayOnlyTicket `
            -ProvideArgs $n0 -FetchArgs $n0
        Invoke-Scenario 'n0 relays, no direct route: provider reachable only through a public relay' -Expect 'NoDirectRoute' -RelayOnlyTicket `
            -ProvideArgs ($n0 + '--no-ip') -FetchArgs $n0
    }
}
finally {
    if (-not $relay.HasExited) { $relay.Kill() }
}

$failed = @($results | Where-Object { -not $_.passed })
[ordered]@{ ranAt = (Get-Date).ToUniversalTime().ToString('o'); iroh = '1.3.0'; passed = ($failed.Count -eq 0); scenarios = $results } |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $run 'results.json')
Write-Host "`n$($results.Count - $failed.Count) of $($results.Count) scenarios passed. Details: $(Join-Path $run 'results.json')"
if ($failed.Count -gt 0) { exit 1 }
