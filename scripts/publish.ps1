<#
.SYNOPSIS
    Publishes Taskbar Groups as a self-contained folder ready to zip or install.

.DESCRIPTION
    Runs `dotnet publish` for the App project in Release, then stages the output
    together with the license, the legacy data README and a sample portable
    marker in artifacts/publish. No developer machine paths are baked in; the
    staged folder is relocatable.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($Output)) {
    $Output = Join-Path $PSScriptRoot '..\artifacts\publish'
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\TaskbarGroups.App\TaskbarGroups.App.csproj'

New-Item -ItemType Directory -Force -Path $Output | Out-Null

Write-Host "Publishing $project ($Configuration, $Runtime) ..."
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $Output `
    /p:PublishSingleFile=false `
    /p:DebugType=embedded

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Copy-Item (Join-Path $root 'LICENSE') (Join-Path $Output 'LICENSE.txt') -Force
Copy-Item (Join-Path $root 'README.md') (Join-Path $Output 'README.md') -Force

Write-Host "Published to $Output"
Get-ChildItem $Output -Recurse -File | Measure-Object -Property Length -Sum |
    ForEach-Object { "Total size: {0:N1} MB across {1} files" -f ($_.Sum / 1MB), $_.Count }
