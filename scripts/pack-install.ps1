<#
.SYNOPSIS
    Creates the installed ZIP: publish output without a portable marker.

.DESCRIPTION
    Identical to the portable edition except that without portable.txt the app
    keeps its data in %LOCALAPPDATA%\TaskbarGroups, which is what an installer
    would produce. Installers that need a proper MSI can wrap this folder; the
    app itself is MSI-agnostic.
#>
[CmdletBinding()]
param(
    [string]$PublishDir = '',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($PublishDir)) { $PublishDir = Join-Path $PSScriptRoot '..\artifacts\publish' }
if ([string]::IsNullOrEmpty($Output)) { $Output = Join-Path $PSScriptRoot '..\artifacts\TaskbarGroups-2.0.0-win-x64.zip' }

if (-not (Test-Path $PublishDir)) {
    throw "Publish output not found at $PublishDir. Run scripts/publish.ps1 first."
}

$staging = Join-Path $env:TEMP ('tg-install-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
try {
    $appDir = Join-Path $staging 'TaskbarGroups'
    Copy-Item $PublishDir $appDir -Recurse -Force

    # An installed copy must not carry the portable marker.
    $marker = Join-Path $appDir 'portable.txt'
    if (Test-Path $marker) { Remove-Item $marker -Force }

    New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
    if (Test-Path $Output) { Remove-Item $Output -Force }

    Compress-Archive -Path $appDir -DestinationPath $Output -CompressionLevel Optimal

    Get-Item $Output | ForEach-Object { 'Install ZIP: {0} ({1:N1} MB)' -f $_.FullName, ($_.Length / 1MB) }
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
