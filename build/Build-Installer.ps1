#requires -Version 7
<#
.SYNOPSIS
    Builds the Windows installer from a package Build-Release.ps1 has staged.

.DESCRIPTION
    Needs Inno Setup 6 (https://jrsoftware.org/isinfo.php). Run Build-Release.ps1 for the same
    runtime first: the installer is made from the folder it stages, so it holds exactly what the zip
    holds. Writes Nodearr.<version>.<runtime>-installer.exe beside the zip.

.PARAMETER Runtime
    win-x64 or win-x86.

.PARAMETER Version
    The release version, as given to Build-Release.ps1.

.PARAMETER RootDir
    Where Build-Release.ps1 staged the package. The default is the repository.

.PARAMETER OutputDir
    Where the installer goes. The default is _artifacts.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'win-x86')] [string] $Runtime,
    [Parameter(Mandatory)] [string] $Version,
    [string] $RootDir,
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$RootDir = $RootDir ? $RootDir : $repo
$OutputDir = $OutputDir ? $OutputDir : (Join-Path $repo '_artifacts')
$source = Join-Path $RootDir "_staging/$Runtime/Nodearr"

if (-not (Test-Path (Join-Path $source 'Nodearr.exe'))) {
    throw "No staged package at $source. Run build/Build-Release.ps1 -Runtime $Runtime first."
}

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue)?.Source,
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php' }

New-Item -ItemType Directory -Force $OutputDir | Out-Null

& $iscc /Q "/DVersion=$Version" "/DRuntime=$Runtime" "/DSourceDir=$((Resolve-Path $source).Path)" "/DOutputDir=$((Resolve-Path $OutputDir).Path)" (Join-Path $repo 'distribution/windows/setup/nodearr.iss') | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Inno Setup could not build the installer for $Runtime" }

$installer = Join-Path $OutputDir "Nodearr.$Version.$Runtime-installer.exe"
Write-Host "Built $installer"
return $installer
