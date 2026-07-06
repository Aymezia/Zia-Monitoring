$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot ".."
Set-Location $projectDir

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    throw "dotnet executable not found at $dotnet"
}

& $dotnet publish -c Release -r win-x64 -p:WindowsPackageType=None -p:PublishSingleFile=true -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -o .\publish\onefile
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

Write-Output "One-file build ready at: $projectDir\publish\onefile\ZiaMonitoring.App.exe"
Write-Output "Warning: One-file WinUI output may be unstable depending on WindowsAppSDK runtime behavior."
