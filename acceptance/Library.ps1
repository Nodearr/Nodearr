# Dot-sourced by the acceptance scripts.

<#
.SYNOPSIS
    Puts a media file into a Sonarr or Radarr library the way a finished download gets there:
    the app is pointed at a folder under /downloads and imports what it finds.

.DESCRIPTION
    The app works out the series or movie, the episode and the quality from the name, so the name
    decides what the file becomes. A release-style name is also kept by the app as the scene name.
    The caller checks afterwards that the app has the file, because an import the app turns down
    still ends as a completed command.
#>
function Import-ArrDownload {
    param(
        [Parameter(Mandatory)] [string] $ApiUrl,
        [Parameter(Mandatory)] [string] $ApiKey,
        [Parameter(Mandatory)] [ValidateSet('Sonarr', 'Radarr')] [string] $App,
        [Parameter(Mandatory)] [string] $SourceFile,
        [Parameter(Mandatory)] [string] $Name,

        # The folder on this machine that the app has mounted at /downloads.
        [Parameter(Mandatory)] [string] $DownloadsDir
    )

    $folder = Join-Path $DownloadsDir "seed/$Name"
    New-Item -ItemType Directory -Force $folder | Out-Null
    Copy-Item -LiteralPath $SourceFile (Join-Path $folder ($Name + [IO.Path]::GetExtension($SourceFile))) -Force

    $headers = @{ 'X-Api-Key' = $ApiKey }
    $command = @{ name = ($App -eq 'Sonarr' ? 'DownloadedEpisodesScan' : 'DownloadedMoviesScan'); path = "/downloads/seed/$Name"; importMode = 'copy' }
    $queued = Invoke-RestMethod "$ApiUrl/command" -Method Post -Headers $headers -ContentType 'application/json' -Body ($command | ConvertTo-Json)

    $deadline = (Get-Date).AddSeconds(180)
    do {
        Start-Sleep -Seconds 1
        $state = Invoke-RestMethod "$ApiUrl/command/$($queued.id)" -Headers $headers
    } while ($state.status -notin 'completed', 'failed' -and (Get-Date) -lt $deadline)

    if ($state.status -ne 'completed') { throw "$App did not finish importing ${Name}: $($state.status) $($state.message)" }
}
