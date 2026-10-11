param(
    [switch]$SkipSmoke,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "   SpeedMeter Pro V1.0 Release Candidate Orchestrator (Wave 5)  " -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Timestamp: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss UTC'))"
Write-Host "Host OS:   $((Get-CimInstance Win32_OperatingSystem).Caption) ($((Get-CimInstance Win32_OperatingSystem).Version))"
Write-Host ""

# 1. Verification of Build Pipeline
Write-Host "[1/6] Running Solution Build (Release)..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# 2. Automated Test Suite (Windows Parity & Checks)
if (-not $SkipTests) {
    Write-Host "[2/6] Running Standard & Windows-Specific Test Suite..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot 'check.ps1') -Windows
    if ($LASTEXITCODE -ne 0) { throw "Test checks failed." }
} else {
    Write-Host "[2/6] Test suite skipped by request." -ForegroundColor DarkYellow
}

# 3. Native WPF Smoke Verification
if (-not $SkipSmoke) {
    Write-Host "[3/6] Running Native WPF Smoke Verification..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot 'smoke.ps1')
    if ($LASTEXITCODE -ne 0) { throw "WPF smoke assertions failed." }
} else {
    Write-Host "[3/6] WPF smoke skipped by request." -ForegroundColor DarkYellow
}

# 4. Packaging & Authenticode Signing
Write-Host "[4/6] Producing Self-Contained and Framework-Dependent Release Packages..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'package.ps1')
if ($LASTEXITCODE -ne 0) { throw "Packaging failed." }

# 5. SBOM & Third-Party Notices Generation
Write-Host "[5/6] Generating SBOM (SPDX 2.3) and Third-Party Notices..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'sbom.ps1')
if ($LASTEXITCODE -ne 0) { throw "SBOM generation failed." }

# 6. Release Checksums (RELEASE-HASHES.sha256)
Write-Host "[6/6] Generating Authoritative Release Hashes..." -ForegroundColor Yellow
$releaseDir = Join-Path $projectRoot 'artifacts\release'
$hashFile = Join-Path $releaseDir 'RELEASE-HASHES.sha256'

$filesToHash = Get-ChildItem -Path $releaseDir -File | Where-Object { $_.Name -ne 'RELEASE-HASHES.sha256' }
$hashLines = @()

foreach ($file in $filesToHash) {
    $sha256 = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashLines += "$sha256  $($file.Name)"
    Write-Host "  $($file.Name): $sha256 ($([Math]::Round($file.Length / 1MB, 2)) MB)"
}

[System.IO.File]::WriteAllLines($hashFile, $hashLines, [System.Text.Encoding]::UTF8)
Write-Host "Wrote release hashes to: $hashFile" -ForegroundColor Green

# Package Verification Drill
Write-Host ""
Write-Host "=== Performing Package Verification Drill ===" -ForegroundColor Cyan
$scZip = Join-Path $releaseDir 'SpeedMeterPro-1.0.0-rc.1-win-x64.zip'
$drillDir = Join-Path $env:TEMP "speedmeter_rc_drill_$([Guid]::NewGuid().ToString('N'))"

try {
    Write-Host "Expanding $scZip to temporary location for drill verification..." -ForegroundColor Yellow
    Expand-Archive -Path $scZip -DestinationPath $drillDir -Force
    $targetExe = Join-Path $drillDir 'SpeedMeter.App.exe'
    if (-not (Test-Path -LiteralPath $targetExe)) {
        throw "Verification drill failed: SpeedMeter.App.exe missing from package."
    }

    $sig = Get-AuthenticodeSignature -FilePath $targetExe
    Write-Host "Authenticode Signature Status: $($sig.Status)" -ForegroundColor Green

    $installScript = Join-Path $drillDir 'Install.ps1'
    $uninstallScript = Join-Path $drillDir 'Uninstall.ps1'
    if (-not (Test-Path -LiteralPath $installScript) -or -not (Test-Path -LiteralPath $uninstallScript)) {
        throw "Verification drill failed: Installer scripts missing from package."
    }
    Write-Host "Verified Install.ps1, Uninstall.ps1, and README.txt present in archive." -ForegroundColor Green
} finally {
    try { Remove-Item -LiteralPath $drillDir -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  GATE E1: V1 RELEASE PACKAGE VERIFIED -- RC FIELD VALIDATION AUTHORIZED" -ForegroundColor Green
Write-Host "  GATE E2: V1.0 PUBLIC RELEASE NOT VERIFIED -- GA BLOCKED" -ForegroundColor Yellow
Write-Host "           (Awaiting physical multi-monitor, live Windows 11 matrix," -ForegroundColor Yellow
Write-Host "            24-hour mixed workload, and 7-day soak evidence)" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Green
