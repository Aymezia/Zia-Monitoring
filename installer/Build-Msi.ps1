$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot ".."
Set-Location $projectDir

$portableDir = Join-Path $projectDir "publish\portable"
$outputDir = Join-Path $projectDir "publish\setup"
$wxsPath = Join-Path $PSScriptRoot "wix\ZiaMonitoring.Setup.wxs"
$outputMsi = Join-Path $outputDir "ZiaMonitoring-Setup.msi"

if (-not (Test-Path (Join-Path $portableDir "ZiaMonitoring.App.exe"))) {
    Write-Output "Portable build missing. Building portable output first..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Build-Portable.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Portable build failed with exit code $LASTEXITCODE"
    }
}

$wix = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
if (-not (Test-Path $wix)) {
    Write-Output "WiX CLI not found. Installing it now..."
    & dotnet tool install --global wix
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install WiX CLI."
    }
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& $wix eula accept wix7 | Out-Null
& $wix build $wxsPath -o $outputMsi
if ($LASTEXITCODE -ne 0) {
    throw "MSI build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $outputMsi)) {
    throw "MSI not generated: $outputMsi"
}

Write-Output "MSI setup ready at: $outputMsi"
