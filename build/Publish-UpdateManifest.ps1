#requires -Version 7
<#
.SYNOPSIS
    Writes and signs the update manifest for a release, and attaches it to the release (ADR 0024).

.DESCRIPTION
    Run it after the release workflow has drafted the release, and before publishing it. It reads the
    draft's packages and SHA256SUMS, and the version's section of CHANGELOG.md, and writes
    update-manifest.json: the version, the date, the changes, a link to the changelog on nodearr.com,
    and each platform's package with its address, size and SHA-256.

    It signs the file with `ssh-keygen -Y sign` in the namespace nodearr-update, using the key git
    signs tags with (user.signingkey and gpg.ssh.program, so 1Password signs it as it signs a tag),
    checks the signature against build/update-signers, and attaches update-manifest.json and
    update-manifest.json.sig to the release. Publishing the release copies them to
    updates.nodearr.com (.github/workflows/website.yml).

    With -PackagesFolder and -BaseUrl it describes packages in a local folder instead, served from
    that address, and does not touch GitHub. That is for testing an update against a local manifest,
    signed with a key of its own (-SigningKeyFile).

.PARAMETER Version
    The release version, such as 0.10.0, without the v.

.PARAMETER Branch
    The update branch the manifest is for. Nodearr fetches <branch>.json. The default is main.

.PARAMETER Rollout
    The share of nodes, 1 to 100, that take the release. The default is 100.

.PARAMETER Repository
    The repository the release is in. The default is Nodearr/Nodearr.

.PARAMETER PackagesFolder
    A folder of packages to describe instead of the release's: Nodearr.<version>.<runtime>.zip or
    .tar.gz files. SHA256SUMS in it is used if present, and worked out if not.

.PARAMETER BaseUrl
    With -PackagesFolder: the address the folder is served from.

.PARAMETER SigningKeyFile
    A private key file to sign with, for testing. The default is git's signing key.

.PARAMETER OutputDir
    Where the two files are written. The default is _artifacts/update-manifest.

.PARAMETER NoUpload
    Write and check the files without attaching them to the release.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Branch = 'main',
    [ValidateRange(1, 100)] [int] $Rollout = 100,
    [string] $Repository = 'Nodearr/Nodearr',
    [string] $PackagesFolder,
    [string] $BaseUrl,
    [string] $SigningKeyFile,
    [string] $OutputDir,
    [switch] $NoUpload
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutputDir = $OutputDir ? $OutputDir : (Join-Path $repo '_artifacts/update-manifest')
$namespace = 'nodearr-update'
$tag = "v$Version"
$local = [bool]$PackagesFolder

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "A version looks like 0.10.0 or 0.10.0-beta.1, not $Version" }
if ($local -and -not $BaseUrl) { throw '-PackagesFolder needs -BaseUrl, the address the folder is served from' }

New-Item -ItemType Directory -Force $OutputDir | Out-Null
$work = Join-Path $OutputDir 'work'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null

# --- The packages -------------------------------------------------------------------------------

$sums = @{}
$sizes = @{}

if ($local) {
    $folder = (Resolve-Path $PackagesFolder).Path
    $sumsFile = Join-Path $folder 'SHA256SUMS'

    foreach ($file in Get-ChildItem $folder -File | Where-Object { $_.Name -like "Nodearr.$Version.*" }) {
        $sizes[$file.Name] = $file.Length
    }

    if (Test-Path $sumsFile) {
        Get-Content $sumsFile | ForEach-Object { if ($_ -match '^([0-9a-fA-F]{64})\s+\*?(\S+)$') { $sums[$Matches[2]] = $Matches[1].ToLowerInvariant() } }
    }
    else {
        foreach ($name in $sizes.Keys) { $sums[$name] = (Get-FileHash (Join-Path $folder $name) -Algorithm SHA256).Hash.ToLowerInvariant() }
    }

    $urlFor = { param($name) $BaseUrl.TrimEnd('/') + '/' + [Uri]::EscapeDataString($name) }
}
else {
    $release = gh release view $tag -R $Repository --json assets,isDraft | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "No release $tag in $Repository. Push the tag and wait for the release workflow to draft it." }

    foreach ($asset in $release.assets) { $sizes[$asset.name] = [long]$asset.size }

    gh release download $tag -R $Repository -p SHA256SUMS -D $work --clobber
    if ($LASTEXITCODE -ne 0) { throw "The release $tag has no SHA256SUMS" }

    Get-Content (Join-Path $work 'SHA256SUMS') | ForEach-Object { if ($_ -match '^([0-9a-fA-F]{64})\s+\*?(\S+)$') { $sums[$Matches[2]] = $Matches[1].ToLowerInvariant() } }

    $urlFor = { param($name) "https://github.com/$Repository/releases/download/$tag/$([Uri]::EscapeDataString($name))" }
}

# Nodearr.<version>.<runtime>.zip or .tar.gz: the packages the updater installs. The installer is not
# one of them: an installed Nodearr replaces its folder from the zip, which holds the same files.
$packages = [ordered]@{}
$pattern = '^Nodearr\.' + [regex]::Escape($Version) + '\.(?<runtime>[a-z0-9-]+)\.(zip|tar\.gz)$'

foreach ($name in $sums.Keys | Sort-Object) {
    if ($name -notmatch $pattern) { continue }
    if (-not $sizes.ContainsKey($name)) { throw "SHA256SUMS lists $name, which is not among the packages" }

    $packages[$Matches['runtime']] = [ordered]@{
        file   = $name
        url    = & $urlFor $name
        size   = $sizes[$name]
        sha256 = $sums[$name]
    }
}

if ($packages.Count -eq 0) { throw "No packages for $Version were found" }

# --- What changed -------------------------------------------------------------------------------

# The version's section of the changelog: its heading, the paragraph under it, and each ### list.
$heading = $null
$summary = @()
$changes = @()
$section = $null
$inVersion = $false

foreach ($line in Get-Content (Join-Path $repo 'CHANGELOG.md')) {
    if ($line -match '^## (.+)$') {
        if ($inVersion) { break }

        $title = $Matches[1].Trim()
        $inVersion = $title -eq $Version -or $title.StartsWith("$Version ")
        if ($inVersion) { $heading = $title }
        continue
    }

    if (-not $inVersion) { continue }

    if ($line -match '^### (.+)$') {
        $section = [ordered]@{ heading = $Matches[1].Trim(); items = [System.Collections.Generic.List[string]]::new() }
        $changes += $section
    }
    elseif ($line -match '^- (.+)$' -and $section) {
        $section.items.Add($Matches[1].Trim())
    }
    elseif ($line -match '^\s+\S' -and $section -and $section.items.Count -gt 0) {
        $section.items[$section.items.Count - 1] += ' ' + $line.Trim()
    }
    elseif ($line.Trim() -and -not $section) {
        $summary += $line.Trim()
    }
}

if (-not $heading) { Write-Warning "CHANGELOG.md has no section for $Version, so the manifest lists no changes" }

# Bold is shown as asterisks in the app, and links into the repository need its address.
function Format-Change([string] $text) {
    $text = $text -replace '\*\*([^*]+)\*\*', '$1'
    return [regex]::Replace($text, '\]\((?!https?:|#)([^)]+)\)', { param($m) "](https://github.com/$Repository/blob/$tag/$($m.Groups[1].Value))" })
}

# The anchor nodearr.com's changelog gives the heading: lower case, without punctuation, spaces as hyphens.
$anchor = $heading ? (($heading.ToLowerInvariant() -replace '[^a-z0-9 -]', '') -replace ' ', '-') : $null

$manifest = [ordered]@{
    format    = 1
    branch    = $Branch
    version   = $Version
    released  = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    changelog = $anchor ? "https://nodearr.com/changelog/#$anchor" : 'https://nodearr.com/changelog/'
    summary   = ($summary | ForEach-Object { Format-Change $_ }) -join ' '
    changes   = @($changes | ForEach-Object { [ordered]@{ heading = $_.heading; items = @($_.items | ForEach-Object { Format-Change $_ }) } })
    image     = "ghcr.io/$($Repository.Split('/')[0].ToLowerInvariant())/nodearr:$Version"
    rollout   = $Rollout
    packages  = $packages
}

$manifestPath = Join-Path $OutputDir 'update-manifest.json'
$signaturePath = "$manifestPath.sig"

# Signed byte for byte as written: UTF-8 without a byte order mark, and LF line endings.
$json = ($manifest | ConvertTo-Json -Depth 10).Replace("`r`n", "`n") + "`n"
[IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))
if (Test-Path $signaturePath) { Remove-Item $signaturePath -Force }

# --- The signature ------------------------------------------------------------------------------

$sshKeygen = (Get-Command ssh-keygen -ErrorAction Stop).Source

if ($SigningKeyFile) {
    $signer = $sshKeygen
    $keyFile = (Resolve-Path $SigningKeyFile).Path
    $publicKey = (& $sshKeygen -y -f $keyFile).Trim()
    $allowedSigners = Join-Path $work 'allowed_signers'
    "nodearr-release namespaces=`"$namespace`" $publicKey" | Set-Content $allowedSigners -NoNewline
}
else {
    $signingKey = (git config --get user.signingkey).Trim()
    if (-not $signingKey) { throw 'git has no user.signingkey. Pass -SigningKeyFile, or set up tag signing.' }

    $program = (git config --get gpg.ssh.program)
    $signer = $program ? $program.Trim() : $sshKeygen

    # A key given as the public key itself, as 1Password sets it up, is written to a file for -f.
    if ($signingKey -match '^(ssh-|ecdsa-|sk-)') {
        $keyFile = Join-Path $work 'signing-key.pub'
        $signingKey | Set-Content $keyFile -NoNewline
    }
    else {
        $keyFile = $signingKey
    }

    $allowedSigners = Join-Path $repo 'build/update-signers'
}

# A copy without an extension is signed, as git signs a file. ssh-keygen writes <file>.sig beside it,
# but 1Password's op-ssh-sign replaces the extension, so update-manifest.json would get
# update-manifest.sig.
$toSign = Join-Path $work 'update-manifest'
Copy-Item $manifestPath $toSign

Write-Host "Signing $manifestPath with $signer"
& $signer -Y sign -n $namespace -f $keyFile $toSign | Out-Host
if ($LASTEXITCODE -ne 0 -or -not (Test-Path "$toSign.sig")) { throw 'Signing failed' }
Move-Item "$toSign.sig" $signaturePath -Force

# Checked as Nodearr will check it, against the key it trusts.
$verify = Start-Process $sshKeygen -ArgumentList '-Y', 'verify', '-f', "`"$allowedSigners`"", '-I', 'nodearr-release', '-n', $namespace, '-s', "`"$signaturePath`"" `
    -RedirectStandardInput $manifestPath -NoNewWindow -Wait -PassThru
if ($verify.ExitCode -ne 0) { throw "The signature does not verify against $allowedSigners" }

Remove-Item $work -Recurse -Force

Write-Host "Nodearr $Version, $($packages.Count) packages, rollout $Rollout%"

if ($local -or $NoUpload) {
    return $manifestPath
}

gh release upload $tag $manifestPath $signaturePath -R $Repository --clobber
if ($LASTEXITCODE -ne 0) { throw "Could not attach the manifest to $tag" }

Write-Host "Attached update-manifest.json and its signature to $tag. Publishing the release puts them on updates.nodearr.com."
return $manifestPath
