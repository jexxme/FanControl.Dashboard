<#
.SYNOPSIS
  Removes the Dashboard plugin from Fan Control and restarts Fan Control.

.PARAMETER FanControlDir
  Fan Control's folder (the one with FanControl.exe). Default: the folder of the running Fan Control, else
  C:\Program Files (x86)\FanControl.

.PARAMETER RemoveSettings
  Also delete the dashboard's settings (%AppData%\FanControl.Dashboard).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Uninstall-Dashboard.ps1 -RemoveSettings
#>
param(
    [string]$FanControlDir,
    [switch]$RemoveSettings,
    [string]$SettingsDir = (Join-Path $env:APPDATA 'FanControl.Dashboard'),
    [switch]$Elevated
)
$ErrorActionPreference = 'Stop'

if (-not $FanControlDir) {
    $running = Get-Process FanControl -ErrorAction SilentlyContinue | Select-Object -First 1
    $FanControlDir = if ($running -and $running.Path) { Split-Path $running.Path } else { "${env:ProgramFiles(x86)}\FanControl" }
}
$dll = Join-Path $FanControlDir 'Plugins\FanControl.Dashboard.dll'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    # settings live in the signed-in user's AppData, so remove them before elevating (the admin token may be another account)
    if ($RemoveSettings -and (Test-Path $SettingsDir)) { Remove-Item -Recurse -Force $SettingsDir; Write-Host "Removed $SettingsDir" }
    if (-not (Test-Path $dll)) { Write-Host "The plugin is not installed ($dll not found)."; exit 0 }
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated -FanControlDir `"$FanControlDir`""
    exit
}

try {
    $process = Get-Process FanControl -ErrorAction SilentlyContinue | Select-Object -First 1
    $exe = if ($process -and $process.Path) { $process.Path } else { Join-Path $FanControlDir 'FanControl.exe' }
    if ($process) { Get-Process FanControl | Stop-Process -Force; Start-Sleep -Seconds 2 }
    if (Test-Path $dll) { Remove-Item -LiteralPath $dll -Force; Write-Host "Removed $dll" -ForegroundColor Green }
    if ($RemoveSettings -and (Test-Path $SettingsDir)) { Remove-Item -Recurse -Force $SettingsDir; Write-Host "Removed $SettingsDir" }
    if ($process) {
        $task = Get-ScheduledTask -TaskName 'FanControl' -ErrorAction SilentlyContinue
        if ($task) { Start-ScheduledTask -TaskName 'FanControl' } else { Start-Process $exe -WorkingDirectory $FanControlDir }
        Write-Host 'Fan Control restarted.'
    }
}
catch {
    Write-Host "Uninstall failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($Elevated) { Read-Host 'Press Enter to close' }
    exit 1
}
if ($Elevated) { Start-Sleep -Seconds 3 }
