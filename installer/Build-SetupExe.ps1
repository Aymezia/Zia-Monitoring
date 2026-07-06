$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot ".."
Set-Location $projectDir

$portableDir = Join-Path $projectDir "publish\portable"
$setupRoot = Join-Path $projectDir "publish\setup"
$stageDir = Join-Path $setupRoot "stage"
$archiveWorkDir = Join-Path $setupRoot "archive-work"
$outputExe = Join-Path $setupRoot "ZiaMonitoring-Setup.exe"

if (-not (Test-Path (Join-Path $portableDir "ZiaMonitoring.App.exe"))) {
    Write-Output "Portable build missing. Building portable output first..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Build-Portable.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Portable build failed with exit code $LASTEXITCODE"
    }
}

if (Test-Path $stageDir) {
    Remove-Item -Path $stageDir -Recurse -Force
}
if (Test-Path $archiveWorkDir) {
    Remove-Item -Path $archiveWorkDir -Recurse -Force
}

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
New-Item -ItemType Directory -Path $setupRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $archiveWorkDir "app") -Force | Out-Null

Copy-Item -Path (Join-Path $portableDir "*") -Destination (Join-Path $archiveWorkDir "app") -Recurse -Force

$payloadZip = Join-Path $stageDir "payload.zip"
Compress-Archive -Path (Join-Path $archiveWorkDir "app") -DestinationPath $payloadZip -Force

Copy-Item -Path (Join-Path $PSScriptRoot "Install-ZiaMonitoring.ps1") -Destination (Join-Path $stageDir "Install-ZiaMonitoring.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-ZiaMonitoring.ps1") -Destination (Join-Path $stageDir "Uninstall-ZiaMonitoring.ps1") -Force

$installCmdPath = Join-Path $stageDir "Install.cmd"
@"
@echo off
setlocal
set ROOT=%~dp0
set TMP=%TEMP%\ZiaMonitoringSetup_%RANDOM%%RANDOM%
mkdir "%TMP%" >nul 2>nul

powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%ROOT%payload.zip' -DestinationPath '%TMP%' -Force"
if errorlevel 1 (
    echo.
    echo Failed to extract payload.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%Install-ZiaMonitoring.ps1" -SourcePath "%TMP%\app" -CreateDesktopShortcut
if errorlevel 1 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Remove-Item -Path '%TMP%' -Recurse -Force -ErrorAction SilentlyContinue"
  echo.
  echo Installation failed.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "Remove-Item -Path '%TMP%' -Recurse -Force -ErrorAction SilentlyContinue"
echo.
echo Installation completed successfully.
echo You can launch Zia Monitoring from Start Menu.
pause
"@ | Set-Content -Path $installCmdPath -Encoding ASCII

$files = Get-ChildItem -Path $stageDir -File | Sort-Object Name
if ($files.Count -eq 0) {
    throw "No files found in setup stage folder."
}

$sourceEntries = @()
$stringEntries = @()
for ($i = 0; $i -lt $files.Count; $i++) {
    $token = "FILE$i"
    $sourceEntries += "%$token%="
    $stringEntries += "$token=$($files[$i].Name)"
}

$stageDirEscaped = $stageDir.Replace("\", "\\")
$outputEscaped = $outputExe.Replace("\", "\\")

$sedPath = Join-Path $setupRoot "ZiaMonitoring-Setup.sed"
$sedContent = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=Installation terminee.
TargetName=$outputEscaped
FriendlyName=Zia Monitoring Setup
AppLaunched=Install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=Install.cmd
UserQuietInstCmd=Install.cmd
SourceFiles=SourceFiles

[SourceFiles]
SourceFiles0=$stageDirEscaped\\

[SourceFiles0]
$($sourceEntries -join [Environment]::NewLine)

[Strings]
$($stringEntries -join [Environment]::NewLine)
"@

Set-Content -Path $sedPath -Value $sedContent -Encoding ASCII

$iexpress = Join-Path $env:WINDIR "System32\iexpress.exe"
if (-not (Test-Path $iexpress)) {
    throw "IExpress not found at $iexpress"
}

& $iexpress /N $sedPath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "IExpress packaging failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $outputExe)) {
    throw "Setup executable was not generated: $outputExe"
}

if (Test-Path $archiveWorkDir) {
    Remove-Item -Path $archiveWorkDir -Recurse -Force
}

Write-Output "Setup executable ready at: $outputExe"
