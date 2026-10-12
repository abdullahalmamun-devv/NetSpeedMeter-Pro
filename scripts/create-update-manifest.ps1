#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$PackageUrl,
    [Parameter(Mandatory)][string]$ReleaseNotesUrl,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$MinSupportedVersion,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateSet('rc', 'stable')][string]$Channel = 'rc',
    [int]$TargetDbSchemaVersion = 3
)

$ErrorActionPreference = 'Stop'
if (-not [Uri]::IsWellFormedUriString($PackageUrl, [UriKind]::Absolute) -or
    ([Uri]$PackageUrl).Scheme -ne 'https') { throw 'PackageUrl must be absolute HTTPS.' }
if (-not [Uri]::IsWellFormedUriString($ReleaseNotesUrl, [UriKind]::Absolute) -or
    ([Uri]$ReleaseNotesUrl).Scheme -ne 'https') { throw 'ReleaseNotesUrl must be absolute HTTPS.' }
if ($TargetDbSchemaVersion -lt 1) { throw 'TargetDbSchemaVersion must be positive.' }

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$resolvedKey = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
$package = Get-Item -LiteralPath $resolvedPackage
if ($package.Length -le 0) { throw 'Package is empty.' }
$sha256 = (Get-FileHash -LiteralPath $resolvedPackage -Algorithm SHA256).Hash.ToLowerInvariant()
$builtAt = [DateTimeOffset]::UtcNow

# Keep this field order and delimiter serialization in lockstep with
# UpdateVerificationService.GetCanonicalManifestBytes. The operator must inspect
# the resulting public JSON and verify it with the target binary before upload.
$manifest = [ordered]@{
    schemaVersion = 1
    product = 'SpeedMeterPro'
    channel = $Channel
    version = $Version
    buildDateUtc = $builtAt.ToString('o')
    minSupportedVersion = $MinSupportedVersion
    packageUrl = $PackageUrl
    packageSha256 = $sha256
    packageSizeBytes = [long]$package.Length
    targetDbSchemaVersion = $TargetDbSchemaVersion
    releaseNotesUrl = $ReleaseNotesUrl
    publisher = 'SpeedMeter Pro'
}
$canonical = '{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}' -f @(
    $manifest.schemaVersion, $manifest.product, $manifest.channel, $manifest.version,
    $builtAt.ToUnixTimeSeconds(), $manifest.minSupportedVersion, $manifest.packageUrl,
    $manifest.packageSha256, $manifest.packageSizeBytes, $manifest.targetDbSchemaVersion,
    $manifest.releaseNotesUrl, $manifest.publisher)

$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $pem = [System.IO.File]::ReadAllText($resolvedKey)
    $rsa.ImportFromPem($pem)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($canonical)
    $signature = $rsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $manifest.signature = [Convert]::ToBase64String($signature)
} finally {
    $rsa.Dispose()
    Remove-Variable pem -ErrorAction SilentlyContinue
}

$json = $manifest | ConvertTo-Json -Depth 3
$fullOutput = [System.IO.Path]::GetFullPath($OutputPath)
$directory = [System.IO.Path]::GetDirectoryName($fullOutput)
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
[System.IO.File]::WriteAllText($fullOutput, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote signed public manifest: $fullOutput"
Write-Host "Package SHA-256: $sha256"
Write-Host "Package bytes: $($package.Length)"
Write-Host 'Private key remained at the externally supplied path.'
