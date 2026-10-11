param(
    [string]$TargetDir,
    [string]$CertificateThumbprint,
    [switch]$ForceTestCert
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not $TargetDir) {
    $TargetDir = Join-Path $projectRoot 'artifacts\release\SpeedMeterPro-1.0.0-rc.1-win-x64'
}

if (-not (Test-Path -LiteralPath $TargetDir)) {
    throw "Target directory '$TargetDir' does not exist."
}

Write-Host "=== SpeedMeter Pro Release Authenticode Signing ===" -ForegroundColor Cyan
Write-Host "Target Directory: $TargetDir"

$cert = $null

if ($CertificateThumbprint) {
    $cert = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) {
        $cert = Get-Item "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    }
    if (-not $cert) {
        throw "Specified certificate thumbprint '$CertificateThumbprint' not found."
    }
}

if (-not $cert -and -not $ForceTestCert) {
    # Look for existing SpeedMeter signing certificate
    $cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like "*SpeedMeter Pro*" } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

if (-not $cert) {
    Write-Host "Creating local Authenticode Release Candidate certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=SpeedMeter Pro Release Candidate Signing, O=SpeedMeter Pro, C=US" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -HashAlgorithm SHA256 `
        -KeyLength 2048 `
        -NotAfter (Get-Date).AddYears(3)
    Write-Host "Created test certificate with Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
} else {
    Write-Host "Using certificate: $($cert.Subject) [$($cert.Thumbprint)]" -ForegroundColor Green
}

$filesToSign = @(
    "SpeedMeter.App.exe",
    "SpeedMeter.App.dll",
    "SpeedMeter.Core.dll",
    "SpeedMeter.Windows.dll",
    "SpeedMeter.Storage.dll",
    "SpeedMeter.Application.dll"
)

$timestampServer = "http://timestamp.digicert.com"
$signingFailures = 0

foreach ($fileName in $filesToSign) {
    $filePath = Join-Path $TargetDir $fileName
    if (-not (Test-Path -LiteralPath $filePath)) {
        Write-Warning "File not found for signing: $filePath"
        continue
    }

    Write-Host "Signing $fileName..." -NoNewline
    $sigResult = $null
    try {
        # Attempt signing with timestamp server
        $sigResult = Set-AuthenticodeSignature -FilePath $filePath -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $timestampServer -ErrorAction Stop
    } catch {
        # Fallback to local signature without timestamp if timestamp server unreachable
        Write-Host " (timestamp offline, signing locally)" -NoNewline -ForegroundColor Yellow
        $sigResult = Set-AuthenticodeSignature -FilePath $filePath -Certificate $cert -HashAlgorithm SHA256
    }

    $verify = Get-AuthenticodeSignature -FilePath $filePath
    if ($verify.Status -eq 'Valid' -or $verify.Status -eq 'UnknownError') {
        # UnknownError indicates a valid self-signed certificate whose root is not in the system trusted root store
        Write-Host " [OK] ($($verify.Status))" -ForegroundColor Green
    } else {
        Write-Host " [FAILED] ($($verify.Status))" -ForegroundColor Red
        $signingFailures++
    }
}

if ($signingFailures -gt 0) {
    throw "Authenticode signing failed for $signingFailures file(s)."
}

Write-Host "Authenticode signing completed successfully." -ForegroundColor Green
