<#
.SYNOPSIS
    Removes the installed copy, shortcuts and startup entry.

.DESCRIPTION
    Deletes %LOCALAPPDATA%\Programs\TaskbarGroups and the shortcuts created by
    install.ps1. It never deletes user data: the data lives in
    %LOCALAPPDATA%\TaskbarGroups (installed mode) or beside the portable exe
    (portable mode), and is kept, along with any backups, so an uninstall is
    never a destructive operation.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\TaskbarGroups'),
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'

$shortcuts = @(
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Taskbar Groups.lnk'),
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\Taskbar Groups.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Taskbar Groups.lnk')
)

foreach ($shortcut in $shortcuts) {
    if (Test-Path $shortcut) { Remove-Item $shortcut -Force; Write-Host "Removed $shortcut" }
}

$exe = Join-Path $InstallDir 'TaskbarGroups.exe'
Get-Process TaskbarGroups -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir"
}

if ($RemoveData) {
    $data = Join-Path $env:LOCALAPPDATA 'TaskbarGroups'
    if (Test-Path $data) { Remove-Item $data -Recurse -Force; Write-Host "Removed $data" }
}

Write-Host 'Uninstalled. User data and backups were kept unless -RemoveData was passed.'
