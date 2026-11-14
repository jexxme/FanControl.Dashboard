<#
.SYNOPSIS
  Installs the Dashboard plugin into Fan Control and restarts Fan Control.

.DESCRIPTION
  Copies FanControl.Dashboard.dll (next to this script) into Fan Control's Plugins folder. Writing there needs
  administrator rights, so the script asks for them once (UAC). Fan Control is closed for a few seconds while the
  file is copied; during that time your fans follow the BIOS/firmware settings, as they do whenever Fan Control is not running.

.PARAMETER FanControlDir
  Fan Control's folder (the one with FanControl.exe). Default: the folder of the running Fan Control, else
  C:\Program Files (x86)\FanControl.

.PARAMETER NoRestart
  Copy the plugin but do not restart Fan Control (it is still closed if it was running, because the file would be locked).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Install-Dashboard.ps1
#>
param(
    [string]$FanControlDir,
    [switch]$NoRestart,
    [switch]$Elevated
)
$ErrorActionPreference = 'Stop'

function Find-FanControl {
    $running = Get-Process FanControl -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($running -and $running.Path) { return Split-Path $running.Path }
    foreach ($dir in "${env:ProgramFiles(x86)}\FanControl", "$env:ProgramFiles\FanControl") {
        if (Test-Path (Join-Path $dir 'FanControl.exe')) { return $dir }
    }
    return $null
}

if (-not $FanControlDir) { $FanControlDir = Find-FanControl }
if (-not $FanControlDir -or -not (Test-Path (Join-Path $FanControlDir 'FanControl.exe'))) {
    Write-Host 'Fan Control was not found. Pass its folder with -FanControlDir "<folder with FanControl.exe>".' -ForegroundColor Red
    exit 1
}
$src = Join-Path $PSScriptRoot 'FanControl.Dashboard.dll'
if (-not (Test-Path $src)) { Write-Host "FanControl.Dashboard.dll not found next to this script ($PSScriptRoot)." -ForegroundColor Red; exit 1 }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated -FanControlDir `"$FanControlDir`""
    if ($NoRestart) { $argList += ' -NoRestart' }
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList $argList
    exit
}

try {
    $version = (Get-Item (Join-Path $FanControlDir 'FanControl.exe')).VersionInfo
    Write-Host "Fan Control $($version.ProductVersion) in $FanControlDir"
    $plugins = Join-Path $FanControlDir 'Plugins'
    New-Item -ItemType Directory -Force $plugins | Out-Null

    $process = Get-Process FanControl -ErrorAction SilentlyContinue | Select-Object -First 1
    $exe = if ($process -and $process.Path) { $process.Path } else { Join-Path $FanControlDir 'FanControl.exe' }
    if ($process) {
        Write-Host 'Closing Fan Control...'
        Get-Process FanControl -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }

    Copy-Item -LiteralPath $src -Destination $plugins -Force
    # files downloaded from the internet are marked as such; .NET Framework can refuse to load marked plugin files
    Unblock-File -LiteralPath (Join-Path $plugins 'FanControl.Dashboard.dll')
    Write-Host "Installed $(Join-Path $plugins 'FanControl.Dashboard.dll')" -ForegroundColor Green

    if (-not $NoRestart) {
        # Fan Control's "Start with Windows" option creates a scheduled task called FanControl; use it when present
        # so Fan Control starts the same way (and with the same rights) as at logon.
        $task = Get-ScheduledTask -TaskName 'FanControl' -ErrorAction SilentlyContinue
        if ($task) { Start-ScheduledTask -TaskName 'FanControl' } else { Start-Process $exe -WorkingDirectory $FanControlDir }
        Write-Host 'Fan Control restarted. The Dashboard appears at the top of the Home page.'
    }
}
catch {
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($Elevated) { Read-Host 'Press Enter to close' }
    exit 1
}
if ($Elevated) { Start-Sleep -Seconds 4 }
