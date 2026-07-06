$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot ".."
Set-Location $projectDir

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    throw "dotnet executable not found at $dotnet"
}

& $dotnet publish ZiaMonitoring.App.csproj -c Release -r win-x64 -p:WindowsPackageType=None -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -o .\publish\portable
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

Write-Output "Portable build ready at: $projectDir\publish\portable\ZiaMonitoring.App.exe"
