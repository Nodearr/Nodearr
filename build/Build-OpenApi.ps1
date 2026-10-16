#requires -Version 7
<#
.SYNOPSIS
    Writes the OpenAPI documents, src/Nodearr.Api.V5/openapi.json and src/Nodearr.Api.V3/openapi.json,
    from the API as it is built.

.DESCRIPTION
    A debug build serves its OpenAPI documents at /docs/v5/openapi.json and /docs/v3/openapi.json,
    generated from the controllers. This builds Nodearr in Debug, starts it on a free port with a
    data folder of its own, fetches both, writes them into the repository, and stops it again.

    Run it whenever the API changes, and commit the result.

.PARAMETER RootDir
    Where the build writes _output and _temp. The default is a folder in the system's temp folder,
    so a build in the repository is left alone.
#>
[CmdletBinding()]
param(
    [string] $RootDir
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$RootDir = $RootDir ? $RootDir : (Join-Path ([IO.Path]::GetTempPath()) 'nodearr-openapi')
$RootDir = (New-Item -ItemType Directory -Force $RootDir).FullName
$data = Join-Path ([IO.Path]::GetTempPath()) "nodearr-openapi-data-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"

Write-Host 'Building Nodearr in Debug'
dotnet build (Join-Path $repo 'src/Nodearr.sln') -c Debug -p:NodearrRootDir=$($RootDir.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar) -v:minimal -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'The build failed' }

$exe = Join-Path $RootDir "_output/net10.0/Nodearr.Console$($IsWindows ? '.exe' : '')"
if (-not (Test-Path $exe)) { throw "No Nodearr at $exe" }

# A port nothing else is using.
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()

$apiKey = [Guid]::NewGuid().ToString('N')
$environment = @{
    Nodearr__Server__Port = "$port"
    Nodearr__Server__BindAddress = '127.0.0.1'
    Nodearr__Auth__ApiKey = $apiKey
    Nodearr__Auth__Method = 'External'
    Nodearr__Log__AnalyticsEnabled = 'false'
}

foreach ($name in $environment.Keys) { Set-Item "env:$name" $environment[$name] }
$process = Start-Process $exe -ArgumentList @('-nobrowser', "-data=$data") -PassThru -WindowStyle Hidden
foreach ($name in $environment.Keys) { Remove-Item "env:$name" }

try {
    $base = "http://127.0.0.1:$port"
    $ready = $false

    foreach ($attempt in 1..90) {
        try {
            Invoke-RestMethod "$base/api/v5/system/status" -Headers @{ 'X-Api-Key' = $apiKey } | Out-Null
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    if (-not $ready) { throw "Nodearr did not start on port $port" }

    foreach ($version in 'v5', 'v3') {
        $target = Join-Path $repo "src/Nodearr.Api.$($version.ToUpperInvariant())/openapi.json"
        $json = (Invoke-WebRequest "$base/docs/$version/openapi.json" -Headers @{ 'X-Api-Key' = $apiKey }).Content

        # As served, with Unix line endings and a final newline.
        Set-Content $target (($json -replace "`r`n", "`n").TrimEnd() + "`n") -NoNewline -Encoding utf8NoBOM

        $paths = ($json | ConvertFrom-Json -Depth 100).paths.PSObject.Properties.Name.Count
        Write-Host "Wrote $target, $paths paths"
    }
}
finally {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
}
