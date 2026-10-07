<#
.SYNOPSIS
    Creates the portable ZIP: publish output plus a portable.txt marker.

.DESCRIPTION
    A portable copy keeps its data in a Data folder beside the executable and
    does not touch %LOCALAPPDATA% or the registry. The ZIP is the distribution
    format for the portable edition; it is produced by scripts/publish.ps1's
    output and this script, and nothing in it depends on the build machine.
#>
[CmdletBinding()]
param(
    [string]$PublishDir = '',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($PublishDir)) { $PublishDir = Join-Path $PSScriptRoot '..\artifacts\publish' }
if ([string]::IsNullOrEmpty($Output)) { $Output = Join-Path $PSScriptRoot '..\artifacts\TaskbarGroups-2.0.0-portable-win-x64.zip' }

if (-not (Test-Path $PublishDir)) {
    throw "Publish output not found at $PublishDir. Run scripts/publish.ps1 first."
}

# Stage into a clean temp folder so the ZIP has a single top-level folder.
$staging = Join-Path $env:TEMP ('tg-portable-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
try {
    $appDir = Join-Path $staging 'TaskbarGroups'
    Copy-Item $PublishDir $appDir -Recurse -Force

    # The marker is what makes DataPathResolver choose the Data folder beside
    # the executable on first run.
    Set-Content -Path (Join-Path $appDir 'portable.txt') -Value @"
Taskbar Groups portable mode.
Delete this file and the Data folder to return to installed mode.
"@ -Encoding UTF8

    New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
    if (Test-Path $Output) { Remove-Item $Output -Force }

    Compress-Archive -Path $appDir -DestinationPath $Output -CompressionLevel Optimal

    Get-Item $Output | ForEach-Object { 'Portable ZIP: {0} ({1:N1} MB)' -f $_.FullName, ($_.Length / 1MB) }
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
