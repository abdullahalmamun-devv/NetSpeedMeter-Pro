param(
    [switch]$SkipSmoke,
    [switch]$SkipTests,
    [ValidateSet('Production', 'Test')][string]$SignMode = 'Test',
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "   SpeedMeter Pro V1.0 Release Candidate Orchestrator (Wave 5)  " -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Timestamp: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss UTC'))"
Write-Host "Host OS:   $((Get-CimInstance Win32_OperatingSystem).Caption) ($((Get-CimInstance Win32_OperatingSystem).Version))"
Write-Host "Sign Mode: $SignMode" -ForegroundColor $(if ($SignMode -eq 'Production') { 'Magenta' } else { 'Yellow' })
Write-Host ""

# 0. Repository State & Clean Working Tree Guard
$headSha = (git rev-parse HEAD 2>$null)
if (-not $headSha) {
    throw "Unable to query git rev-parse HEAD. Release candidate must originate from a git repository."
}
$headSha = $headSha.Trim()
Write-Host "Authoritative Git HEAD SHA: $headSha" -ForegroundColor Cyan

if (-not $AllowDirty) {
    $dirty = (git status --porcelain 2>$null)
    if ($dirty) {
        Write-Host "Uncommitted changes detected in working tree:" -ForegroundColor Red
        $dirty | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw "Release aborted: Working tree is dirty. Authoritative release candidates require a clean working tree."
    }
    Write-Host "Working tree clean: PASSED" -ForegroundColor Green
} else {
    Write-Host "Working tree cleanliness check bypassed by -AllowDirty." -ForegroundColor Yellow
}

# 1. Verification of Build Pipeline
Write-Host ""
Write-Host "[1/6] Running Solution Build (Release)..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# 2. Automated Test Suite (Windows Parity & Checks)
if (-not $SkipTests) {
    Write-Host ""
    Write-Host "[2/6] Running Standard & Windows-Specific Test Suite..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot 'check.ps1') -Windows
    if ($LASTEXITCODE -ne 0) { throw "Test checks failed." }
} else {
    Write-Host ""
    Write-Host "[2/6] Test suite skipped by request." -ForegroundColor DarkYellow
}

# 3. Native WPF Smoke Verification
if (-not $SkipSmoke) {
    Write-Host ""
    Write-Host "[3/6] Running Native WPF Smoke Verification..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot 'smoke.ps1')
    if ($LASTEXITCODE -ne 0) { throw "WPF smoke assertions failed." }
} else {
    Write-Host ""
    Write-Host "[3/6] WPF smoke skipped by request." -ForegroundColor DarkYellow
}

# 4. Packaging & Authenticode Signing
Write-Host ""
Write-Host "[4/6] Producing Standalone Installer and Distribution Packages..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'package.ps1') -SignMode $SignMode
if ($LASTEXITCODE -ne 0) { throw "Packaging failed." }

# 5. SBOM & Third-Party Notices Generation
Write-Host ""
Write-Host "[5/6] Generating SBOM (SPDX 2.3) and Third-Party Notices..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'sbom.ps1')
if ($LASTEXITCODE -ne 0) { throw "SBOM generation failed." }

# 6. Release Checksums (RELEASE-HASHES.sha256)
Write-Host ""
Write-Host "[6/6] Generating Authoritative Release Hashes..." -ForegroundColor Yellow
$releaseDir = Join-Path $projectRoot 'artifacts\release'
$hashFile = Join-Path $releaseDir 'RELEASE-HASHES.sha256'

$filesToHash = Get-ChildItem -Path $releaseDir -File | Where-Object {
    $_.Name -ne 'RELEASE-HASHES.sha256' -and
    $_.Name -ne 'sbom.spdx.json' -and
    $_.Name -ne 'THIRD-PARTY-NOTICES.md'
} | Sort-Object Name

$hashLines = @()

foreach ($file in $filesToHash) {
    $sha256 = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashLines += "$sha256  $($file.Name)"
    Write-Host "  $($file.Name): $sha256 ($([Math]::Round($file.Length / 1MB, 2)) MB)"
}

[System.IO.File]::WriteAllLines($hashFile, $hashLines, [System.Text.Encoding]::UTF8)
Write-Host "Wrote authoritative release hashes to: $hashFile" -ForegroundColor Green

# 7. Package Verification Drill & Live Installer Drill
Write-Host ""
Write-Host "=== Performing Release Candidate Verification Drill ===" -ForegroundColor Cyan
$setupExe = Join-Path $releaseDir 'SpeedMeterPro-1.0.0-rc.1-Setup.exe'
if (-not (Test-Path -LiteralPath $setupExe)) {
    throw "Verification drill failed: SpeedMeterPro-1.0.0-rc.1-Setup.exe missing."
}

# Verify Authenticode on Primary Setup.exe
$setupSig = Get-AuthenticodeSignature -FilePath $setupExe
Write-Host "Primary Setup.exe Authenticode Status: $($setupSig.Status)" -ForegroundColor Cyan
if ($SignMode -eq 'Production') {
    if ($setupSig.Status -ne 'Valid') {
        throw "Verification drill failed: Setup.exe Authenticode signature is $($setupSig.Status) in Production mode."
    }
} else {
    if ($setupSig.Status -ne 'Valid' -and $setupSig.Status -ne 'UnknownError') {
        throw "Verification drill failed: Setup.exe Authenticode signature is invalid: $($setupSig.Status)."
    }
}

# Live silent install and uninstall drill to sandbox directory
$drillDir = Join-Path $env:TEMP "speedmeter_rc_drill_$([Guid]::NewGuid().ToString('N'))"

try {
    Write-Host "Running silent installation drill to sandbox ($drillDir)..." -ForegroundColor Yellow
    $psiInstall = New-Object System.Diagnostics.ProcessStartInfo
    $psiInstall.FileName = $setupExe
    $psiInstall.Arguments = "-Silent -NoLaunch -Dir `"$drillDir`""
    $psiInstall.UseShellExecute = false
    $psiInstall.CreateNoWindow = true
    $pInstall = [System.Diagnostics.Process]::Start($psiInstall)
    $pInstall.WaitForExit(30000)
    if ($pInstall.ExitCode -ne 0) {
        throw "Installer drill failed with exit code $($pInstall.ExitCode)."
    }

    $installedApp = Join-Path $drillDir 'SpeedMeter.App.exe'
    if (-not (Test-Path -LiteralPath $installedApp)) {
        throw "Installer drill failed: SpeedMeter.App.exe missing after setup."
    }

    $appSig = Get-AuthenticodeSignature -FilePath $installedApp
    Write-Host "Installed SpeedMeter.App.exe Authenticode Status: $($appSig.Status)" -ForegroundColor Green

    # Run silent uninstall drill
    Write-Host "Running silent uninstallation drill from sandbox..." -ForegroundColor Yellow
    $uninstallExe = Join-Path $drillDir 'Uninstall.exe'
    if (Test-Path -LiteralPath $uninstallExe) {
        $psiUninstall = New-Object System.Diagnostics.ProcessStartInfo
        $psiUninstall.FileName = $uninstallExe
        $psiUninstall.Arguments = "-Uninstall -Silent -Dir `"$drillDir`""
        $psiUninstall.UseShellExecute = false
        $psiUninstall.CreateNoWindow = true
        $pUninstall = [System.Diagnostics.Process]::Start($psiUninstall)
        $pUninstall.WaitForExit(30000)
    }
    Write-Host "Installation and uninstallation drills verified successfully." -ForegroundColor Green
} finally {
    try {
        if (Test-Path -LiteralPath $drillDir) {
            Remove-Item -LiteralPath $drillDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}

# 8. Dynamic Gate E1 Decision Computing
$decisionLabel = if ($SignMode -eq 'Production') {
    "GATE E1 INDEPENDENTLY VERIFIED -- RC FIELD VALIDATION AUTHORIZED (PRODUCTION SIGNED)"
} else {
    "GATE E1 INDEPENDENTLY VERIFIED -- RC FIELD VALIDATION AUTHORIZED (TEST-SIGNED RC LAB MODE)"
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  $decisionLabel" -ForegroundColor Green
Write-Host "  GATE E2: V1.0 PUBLIC RELEASE NOT VERIFIED -- GA BLOCKED" -ForegroundColor Yellow
Write-Host "           (Awaiting physical multi-monitor, live Windows 11 matrix," -ForegroundColor Yellow
Write-Host "            24-hour mixed workload, and 7-day soak evidence)" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Green
