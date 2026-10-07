<#
.SYNOPSIS
    Installs Taskbar Groups for the current user.

.DESCRIPTION
    Copies the application to %LOCALAPPDATA%\Programs\TaskbarGroups (no
    elevation required), creates a Start Menu shortcut, and optionally a
    desktop shortcut and a startup entry. Re-running replaces the previous
    copy; the Data folder is never touched by an install or an uninstall.

    Paired with uninstall.ps1. No external toolchain is required: the public
    contract is the DataPathResolver rules, not a specific MSI layout, so this
    script and a future MSI behave identically.
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\artifacts\publish'),
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\TaskbarGroups'),
    [switch]$DesktopShortcut,
    [switch]$StartWithWindows
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $Source 'TaskbarGroups.dll'))) {
    throw "No published build found at $Source. Run scripts/publish.ps1 first."
}

Write-Host "Installing to $InstallDir ..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# Mirror the folder: replaced files go, user data beside the exe does not.
foreach ($child in Get-ChildItem $Source) {
    Copy-Item $child.FullName (Join-Path $InstallDir $child.Name) -Recurse -Force
}

# Installed mode: data in %LOCALAPPDATA%\TaskbarGroups, not beside the exe.
$marker = Join-Path $InstallDir 'portable.txt'
if (Test-Path $marker) { Remove-Item $marker -Force }

$exe = Join-Path $InstallDir 'TaskbarGroups.exe'
if (-not (Test-Path $exe)) {
    # A published apphost exe is expected; produce a clear error rather than a
    # silent broken install.
    throw "Expected $exe to exist in the publish output."
}

$shell = New-Object -ComObject WScript.Shell

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $startMenu 'Taskbar Groups.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description = 'Smart taskbar launcher and Windows workspace manager'
$shortcut.Save()

if ($DesktopShortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $desktopShortcut = $shell.CreateShortcut((Join-Path $desktop 'Taskbar Groups.lnk'))
    $desktopShortcut.TargetPath = $exe
    $desktopShortcut.WorkingDirectory = $InstallDir
    $desktopShortcut.Save()
}

if ($StartWithWindows) {
    $startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
    $startupShortcut = $shell.CreateShortcut((Join-Path $startup 'Taskbar Groups.lnk'))
    $startupShortcut.TargetPath = $exe
    $startupShortcut.Arguments = '--tray'
    $startupShortcut.WorkingDirectory = $InstallDir
    $startupShortcut.Save()
}

Write-Host "Installed. Start Menu shortcut: $shortcutPath"
