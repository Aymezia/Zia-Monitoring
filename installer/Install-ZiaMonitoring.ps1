param(
    [string]$SourcePath = "",
    [switch]$CreateDesktopShortcut = $true
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $PSScriptRoot "..\publish\portable"
}

$SourcePath = [System.IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path $SourcePath)) {
    throw "Source path not found: $SourcePath"
}

$installRoot = Join-Path $env:LOCALAPPDATA "ZiaMonitoring"
$installDir = Join-Path $installRoot "app"
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

Remove-Item -Path (Join-Path $installDir "*") -Recurse -Force -ErrorAction SilentlyContinue

if ((Get-Item $SourcePath).PSIsContainer) {
    Copy-Item -Path (Join-Path $SourcePath "*") -Destination $installDir -Recurse -Force
}
else {
    Copy-Item -Path $SourcePath -Destination (Join-Path $installDir "ZiaMonitoring.App.exe") -Force
}

$targetExe = Join-Path $installDir "ZiaMonitoring.App.exe"
if (-not (Test-Path $targetExe)) {
    throw "Installed executable not found at expected path: $targetExe"
}

$wsh = New-Object -ComObject WScript.Shell

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Zia Monitoring"
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
$startMenuLnk = Join-Path $startMenuDir "Zia Monitoring.lnk"
$sc1 = $wsh.CreateShortcut($startMenuLnk)
$sc1.TargetPath = $targetExe
$sc1.WorkingDirectory = $installDir
$sc1.IconLocation = "$targetExe,0"
$sc1.Save()

if ($CreateDesktopShortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $desktopLnk = Join-Path $desktop "Zia Monitoring.lnk"
    $sc2 = $wsh.CreateShortcut($desktopLnk)
    $sc2.TargetPath = $targetExe
    $sc2.WorkingDirectory = $installDir
    $sc2.IconLocation = "$targetExe,0"
    $sc2.Save()
}

$uninstallScript = Join-Path $installRoot "Uninstall-ZiaMonitoring.ps1"
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-ZiaMonitoring.ps1") -Destination $uninstallScript -Force

Write-Output "Installation complete."
Write-Output "Executable: $targetExe"
Write-Output "Start Menu: $startMenuLnk"
if ($CreateDesktopShortcut) { Write-Output "Desktop shortcut created." }
