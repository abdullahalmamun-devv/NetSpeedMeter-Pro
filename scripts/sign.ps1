param(
    [string]$TargetDir,
    [string]$TargetFile,
    [string]$CertificateThumbprint,
    [ValidateSet('Production', 'Test')][string]$Mode = 'Test',
    [switch]$ForceTestCert
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')

Write-Host "=== SpeedMeter Pro Release Authenticode Signing ===" -ForegroundColor Cyan
Write-Host "Signing Mode: $Mode" -ForegroundColor $(if ($Mode -eq 'Production') { 'Magenta' } else { 'Yellow' })

if ($Mode -eq 'Test') {
    Write-Host "[TEST-SIGNED -- NOT FOR PUBLIC DISTRIBUTION]" -ForegroundColor Yellow
}

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
    if ($Mode -eq 'Production') {
        # Production requires a valid certificate chaining to a trusted root
        $candidates = Get-ChildItem -Path Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -like "*SpeedMeter Pro*" -and -not $_.Subject -like "*Release Candidate Signing*" } |
            Sort-Object NotAfter -Descending

        foreach ($c in $candidates) {
            $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
            $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
            if ($chain.Build($c)) {
                $cert = $c
                break
            }
        }

        if (-not $cert) {
            throw "Production signing failed: No valid Authenticode certificate chaining to a trusted root was found for 'SpeedMeter Pro'. Ensure a production code-signing certificate is installed in CurrentUser\My or LocalMachine\My, or use -Mode Test for development validation."
        }
    } else {
        # Test mode: look for existing SpeedMeter test or production certificate
        $cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -like "*SpeedMeter Pro*" } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
    }
}

if (-not $cert) {
    if ($Mode -eq 'Production') {
        throw "Production signing requires an authentic code-signing certificate. Automatic self-signed generation is prohibited in Production mode."
    }

    Write-Host "Creating local Authenticode Release Candidate test certificate..." -ForegroundColor Yellow
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

$filesToSign = @()

if ($TargetFile) {
    if (-not (Test-Path -LiteralPath $TargetFile)) {
        throw "Target file '$TargetFile' does not exist."
    }
    $filesToSign += (Resolve-Path $TargetFile).Path
}

if ($TargetDir) {
    if (-not (Test-Path -LiteralPath $TargetDir)) {
        throw "Target directory '$TargetDir' does not exist."
    }

    $candidates = @(
        "SpeedMeterPro-Setup.exe",
        "SpeedMeter.App.exe",
        "Uninstall.exe",
        "SpeedMeter.App.dll",
        "SpeedMeter.Core.dll",
        "SpeedMeter.Windows.dll",
        "SpeedMeter.Storage.dll",
        "SpeedMeter.Application.dll"
    )

    foreach ($name in $candidates) {
        $p = Join-Path $TargetDir $name
        if (Test-Path -LiteralPath $p) {
            $filesToSign += (Resolve-Path $p).Path
        }
    }

    # Also include any other first-party .dll or .exe in target dir
    Get-ChildItem -Path $TargetDir -File | Where-Object {
        ($_.Extension -eq '.exe' -or ($_.Extension -eq '.dll' -and $_.Name -like 'SpeedMeter*')) -and
        ($filesToSign -notcontains $_.FullName)
    } | ForEach-Object { $filesToSign += $_.FullName }
}

if ($filesToSign.Count -eq 0) {
    Write-Warning "No files found to sign."
    return
}

$timestampServer = "http://timestamp.digicert.com"
$signingFailures = 0

foreach ($filePath in $filesToSign) {
    $fileName = Split-Path -Leaf $filePath
    Write-Host "Signing $fileName..." -NoNewline
    $sigResult = $null

    try {
        # Attempt signing with timestamp server
        $sigResult = Set-AuthenticodeSignature -FilePath $filePath -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $timestampServer -ErrorAction Stop
    } catch {
        if ($Mode -eq 'Production') {
            Write-Host " [FAILED: RFC 3161 timestamping failed in Production mode]" -ForegroundColor Red
            throw "Production signing failed: RFC 3161 timestamp server '$timestampServer' unreachable: $_"
        } else {
            Write-Host " (timestamp offline, signing locally)" -NoNewline -ForegroundColor Yellow
            $sigResult = Set-AuthenticodeSignature -FilePath $filePath -Certificate $cert -HashAlgorithm SHA256
        }
    }

    $verify = Get-AuthenticodeSignature -FilePath $filePath
    if ($Mode -eq 'Production' -and -not $verify.TimeStamperCertificate) {
        Write-Host " [FAILED: Missing RFC 3161 timestamp in Production mode]" -ForegroundColor Red
        $signingFailures++
    } elseif ($verify.Status -eq 'Valid') {
        Write-Host " [OK] (Valid)" -ForegroundColor Green
    } elseif ($Mode -eq 'Test' -and $verify.Status -eq 'UnknownError') {
        # UnknownError is expected for self-signed test certificate whose root is not in Trusted Root CAs
        Write-Host " [OK] (Test Root - UnknownError accepted in Test mode)" -ForegroundColor Yellow
    } else {
        Write-Host " [FAILED] ($($verify.Status))" -ForegroundColor Red
        $signingFailures++
    }
}

if ($signingFailures -gt 0) {
    throw "Authenticode signing failed for $signingFailures file(s)."
}

Write-Host "Authenticode signing completed successfully ($($filesToSign.Count) files signed)." -ForegroundColor Green
