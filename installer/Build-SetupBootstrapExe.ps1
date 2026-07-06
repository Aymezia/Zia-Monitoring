$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot ".."
Set-Location $projectDir

$setupRoot = Join-Path $projectDir "publish\setup"
$msiPath = Join-Path $setupRoot "ZiaMonitoring-Setup.msi"
$bootstrapperProject = Join-Path $PSScriptRoot "bootstrapper\ZiaMonitoring.SetupBootstrapper.csproj"
$bootstrapperOut = Join-Path $setupRoot "bootstrapper"
$bootstrapperBuiltExe = Join-Path $bootstrapperOut "ZiaMonitoring.SetupBootstrapper.exe"
$outputExe = Join-Path $setupRoot "ZiaMonitoring-SetupBootstrap.exe"

if (-not (Test-Path $msiPath)) {
    Write-Output "MSI not found. Building MSI first..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Build-Msi.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "MSI build failed with exit code $LASTEXITCODE"
    }
}

New-Item -ItemType Directory -Path $setupRoot -Force | Out-Null
if (Test-Path $bootstrapperOut) {
    Remove-Item -Path $bootstrapperOut -Recurse -Force
}

& "C:\Program Files\dotnet\dotnet.exe" publish $bootstrapperProject -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o $bootstrapperOut
if ($LASTEXITCODE -ne 0) {
    throw "Bootstrapper build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $bootstrapperBuiltExe)) {
    throw "Bootstrapper executable not found: $bootstrapperBuiltExe"
}

Copy-Item -Path $bootstrapperBuiltExe -Destination $outputExe -Force

if (-not (Test-Path $outputExe)) {
    throw "Bootstrap setup executable was not generated: $outputExe"
}

Write-Output "Bootstrap setup executable ready at: $outputExe"
