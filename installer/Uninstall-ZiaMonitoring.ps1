$ErrorActionPreference = "SilentlyContinue"

$installRoot = Join-Path $env:LOCALAPPDATA "ZiaMonitoring"
$installDir = Join-Path $installRoot "app"
$targetExe = Join-Path $installDir "ZiaMonitoring.App.exe"

Get-Process "ZiaMonitoring.App" -ErrorAction SilentlyContinue | Stop-Process -Force

$desktop = [Environment]::GetFolderPath("Desktop")
$desktopLnk = Join-Path $desktop "Zia Monitoring.lnk"
if (Test-Path $desktopLnk) { Remove-Item $desktopLnk -Force }

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Zia Monitoring"
if (Test-Path $startMenuDir) { Remove-Item $startMenuDir -Recurse -Force }

if (Test-Path $installRoot) { Remove-Item $installRoot -Recurse -Force }

Write-Output "Zia Monitoring uninstalled."
