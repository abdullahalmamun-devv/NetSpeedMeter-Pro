param(
    [switch]$SkipSmoke,
    [switch]$SkipTests,
    [ValidateSet('Production', 'Test')][string]$SignMode = 'Test',
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-sandbox.ps1')

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "   SpeedMeter Pro V1.0 RC.4 Release Candidate Orchestrator   " -ForegroundColor Cyan
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
& (Join-Path $PSScriptRoot 'package.ps1') -Version '1.0.0-rc.4' -SignMode $SignMode
if ($LASTEXITCODE -ne 0) { throw "Packaging failed." }

# 5. SBOM & Third-Party Notices Generation
Write-Host ""
Write-Host "[5/6] Generating SBOM (SPDX 2.3) and Third-Party Notices..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'sbom.ps1') -Version '1.0.0-rc.4'
if ($LASTEXITCODE -ne 0) { throw "SBOM generation failed." }

# 6. Release Checksums (RELEASE-HASHES.sha256)
Write-Host ""
Write-Host "[6/6] Generating Authoritative Release Hashes..." -ForegroundColor Yellow
$releaseDir = Join-Path $projectRoot 'artifacts\release\1.0.0-rc.4'
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
$setupExe = Join-Path $releaseDir 'SpeedMeterPro-1.0.0-rc.4-Setup.exe'
if (-not (Test-Path -LiteralPath $setupExe)) {
    throw "Verification drill failed: SpeedMeterPro-1.0.0-rc.4-Setup.exe missing."
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
$drillRoot = New-TestSandboxRoot
$drillDir = Join-Path $drillRoot 'install'

try {
    Write-Host "Running silent installation drill to sandbox ($drillDir)..." -ForegroundColor Yellow
    $psiInstall = New-Object System.Diagnostics.ProcessStartInfo
    $psiInstall.FileName = $setupExe
    $psiInstall.Arguments = "-Silent -NoLaunch -Dir `"$drillDir`""
    $psiInstall.UseShellExecute = $false
    $psiInstall.CreateNoWindow = $true
    $pInstall = [System.Diagnostics.Process]::Start($psiInstall)
    if (-not $pInstall.WaitForExit(30000)) {
        throw "Installer drill timed out after 30 seconds."
    }
    if ($pInstall.ExitCode -ne 0) {
        throw "Installer drill failed with exit code $($pInstall.ExitCode)."
    }

    $installedApp = Join-Path $drillDir 'SpeedMeter.App.exe'
    $installedDll = Join-Path $drillDir 'SpeedMeter.App.dll'
    if (-not (Test-Path -LiteralPath $installedApp) -or -not (Test-Path -LiteralPath $installedDll)) {
        throw "Installer drill failed: SpeedMeter.App binary missing after setup."
    }

    $appSig = Get-AuthenticodeSignature -FilePath $installedApp
    Write-Host "Installed SpeedMeter.App.exe Authenticode Status: $($appSig.Status)" -ForegroundColor Green

    # Inspect embedded commit identity (Blocker E: SourceRevisionId == ReleaseSourceSha)
    $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installedDll)
    $prodVersion = $vi.ProductVersion
    Write-Host "Installed SpeedMeter.App.dll ProductVersion: $prodVersion" -ForegroundColor Cyan
    if ($prodVersion -notmatch [regex]::Escape($headSha)) {
        throw "Installer drill failed: Installed binary ProductVersion ($prodVersion) does not contain ReleaseSourceSha ($headSha)."
    }
    Write-Host "Installed binary commit identity ($headSha): VERIFIED" -ForegroundColor Green

    # Run silent uninstall drill
    Write-Host "Running silent uninstallation drill from sandbox..." -ForegroundColor Yellow
    $uninstallExe = Join-Path $drillDir 'Uninstall.exe'
    if (-not (Test-Path -LiteralPath $uninstallExe)) {
        throw "Uninstaller drill failed: Uninstall.exe missing after setup."
    }
    $psiUninstall = New-Object System.Diagnostics.ProcessStartInfo
    $psiUninstall.FileName = $uninstallExe
    $psiUninstall.Arguments = "-Uninstall -Silent -Dir `"$drillDir`""
    $psiUninstall.UseShellExecute = $false
    $psiUninstall.CreateNoWindow = $true
    $pUninstall = [System.Diagnostics.Process]::Start($psiUninstall)
    if (-not $pUninstall.WaitForExit(30000)) {
        throw "Uninstaller drill timed out after 30 seconds."
    }
    if ($pUninstall.ExitCode -ne 0) {
        throw "Uninstaller drill failed with exit code $($pUninstall.ExitCode)."
    }
    # The installed bootstrap exits after handing off to the out-of-tree helper.
    # Wait for the helper's durable completion result, not just parent exit or
    # the target rename into a still-populated quarantine directory.
    $normalizedDrillDir = [System.IO.Path]::GetFullPath($drillDir).TrimEnd([char[]]@('\','/')).ToUpperInvariant()
    $targetHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($normalizedDrillDir)))
    $resultPath = Join-Path (Join-Path $env:TEMP 'SpeedMeterPro.Cleanup') ("result-$targetHash.json")
    $cleanupWait = [System.Diagnostics.Stopwatch]::StartNew()
    $cleanupResult = $null
    while ($cleanupWait.Elapsed -lt [TimeSpan]::FromSeconds(30)) {
        if (Test-Path -LiteralPath $resultPath) {
            $cleanupResult = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            if ($cleanupResult.State -eq 'Failed') {
                throw "Uninstaller helper failed: $($cleanupResult.Message)"
            }
            if ($cleanupResult.State -eq 'Completed') { break }
        }
        Start-Sleep -Milliseconds 100
    }
    if ($null -eq $cleanupResult -or $cleanupResult.State -ne 'Completed' -or
        $cleanupResult.TargetHash -ne $targetHash -or -not $cleanupResult.Nonce) {
        throw "Uninstaller drill failed: no matching helper completion within 30 seconds."
    }
    # The external uninstaller itself is a copied Setup. A separate signed
    # janitor must remove that large running copy after it exits. Require the
    # exact nonce directory to disappear before calling the drill complete.
    $nonceGuid = [Guid]::Empty
    if (-not [Guid]::TryParseExact([string]$cleanupResult.Nonce, 'N', [ref]$nonceGuid)) {
        throw 'Uninstaller drill failed: helper completion nonce is invalid.'
    }
    $nonceDir = Join-Path (Join-Path $env:TEMP 'SpeedMeterPro.Cleanup') $cleanupResult.Nonce
    $janitorResultPath = Join-Path (Join-Path $env:TEMP 'SpeedMeterPro.Cleanup') ("janitor-result-$($cleanupResult.Nonce).txt")
    $janitorWait = [System.Diagnostics.Stopwatch]::StartNew()
    $janitorState = $null
    while ($janitorWait.Elapsed -lt [TimeSpan]::FromSeconds(30)) {
        if (Test-Path -LiteralPath $janitorResultPath) {
            $janitorLines = @(Get-Content -LiteralPath $janitorResultPath -TotalCount 2)
            $janitorState = $janitorLines[0]
            if ($janitorState -eq 'Failed') {
                throw "Uninstaller janitor failed: $($janitorLines -join ' ')"
            }
            if ($janitorState -eq 'Completed') { break }
        }
        Start-Sleep -Milliseconds 100
    }
    if ($janitorState -ne 'Completed' -or (Test-Path -LiteralPath $nonceDir)) {
        throw "Uninstaller drill failed: signed janitor did not confirm removal of out-of-tree Setup: $nonceDir"
    }
    $janitorExe = Join-Path (Join-Path $env:TEMP 'SpeedMeterPro.Cleanup') 'SpeedMeterPro.TempCleanup.exe'
    if (-not (Test-Path -LiteralPath $janitorExe)) {
        throw 'Uninstaller drill failed: dedicated signed janitor is missing.'
    }
    $janitorInfo = Get-Item -LiteralPath $janitorExe
    if ($janitorInfo.Length -le 0 -or $janitorInfo.Length -ge 20MB) {
        throw "Uninstaller drill failed: dedicated janitor size is out of bounds ($($janitorInfo.Length) bytes)."
    }
    $janitorSig = Get-AuthenticodeSignature -FilePath $janitorExe
    if (($SignMode -eq 'Production' -and $janitorSig.Status -ne 'Valid') -or
        ($SignMode -eq 'Test' -and $janitorSig.Status -notin @('Valid', 'UnknownError')) -or
        $janitorSig.SignerCertificate.Subject -notlike '*SpeedMeter Pro*') {
        throw "Uninstaller drill failed: dedicated janitor signature/publisher was rejected ($($janitorSig.Status))."
    }
    Write-Host "Dedicated signed janitor: $($janitorInfo.Length) bytes; Authenticode $($janitorSig.Status)." -ForegroundColor Green
    $quarantine = @(Get-ChildItem -LiteralPath $drillRoot -Directory -Force |
        Where-Object { $_.Name -like '.install.removing.*' })
    if ((Test-Path -LiteralPath $drillDir) -or $quarantine.Count -ne 0) {
        throw "Uninstaller drill failed: product install or quarantine directory remains after helper completion."
    }
    Write-Host "Installation and uninstallation drills verified successfully." -ForegroundColor Green
} finally {
    Remove-TestSandboxRoot $drillRoot
}

# 8. Package evidence summary. Gate decisions require independent review.
if ($SkipTests -or $SkipSmoke -or $AllowDirty) {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Yellow
    Write-Host "  DEVELOPER PACKAGE -- NOT GATE VERIFIED" -ForegroundColor Yellow
    Write-Host "  (Bypass flags detected: SkipTests=$SkipTests, SkipSmoke=$SkipSmoke, AllowDirty=$AllowDirty)" -ForegroundColor Yellow
    Write-Host "================================================================" -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  RC.4 PACKAGE DRILL PASSED -- INDEPENDENT CONSUMER REVIEW REQUIRED" -ForegroundColor Green
Write-Host "  GATE E2 FIELD VALIDATION NOT AUTHORIZED BY THIS SCRIPT" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Green
