$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot ".."
Set-Location $projectDir

$portableDir = Join-Path $projectDir "publish\portable"
if (-not (Test-Path (Join-Path $portableDir "ZiaMonitoring.App.exe"))) {
    Write-Output "Build portable manquant. Construction..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Build-Portable.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Echec du build portable (code $LASTEXITCODE)."
    }
}

$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup introuvable (ISCC.exe). Installez-le depuis https://jrsoftware.org/isdl.php puis relancez ce script."
}

[xml]$csproj = Get-Content (Join-Path $projectDir "ZiaMonitoring.App.csproj")
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) {
    $version = "1.0.0"
}

$outputDir = Join-Path $projectDir "publish\setup"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$issPath = Join-Path $PSScriptRoot "inno\ZiaMonitoringSetup.iss"
& $iscc "/DMyAppVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "Compilation Inno Setup en echec (code $LASTEXITCODE)."
}

$outputExe = Join-Path $outputDir "ZiaMonitoring-Setup.exe"
if (-not (Test-Path $outputExe)) {
    throw "Installeur non genere : $outputExe"
}

Write-Output "Installeur pret : $outputExe"
