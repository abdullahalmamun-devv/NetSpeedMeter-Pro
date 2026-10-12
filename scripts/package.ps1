param(
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0-rc.3',
    [string]$Runtime = 'win-x64',
    [ValidateSet('Production', 'Test')][string]$SignMode = 'Test'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')

Write-Host "=== SpeedMeter Pro Release Packaging ($Version - $Runtime) ===" -ForegroundColor Cyan
Write-Host "Sign Mode: $SignMode" -ForegroundColor $(if ($SignMode -eq 'Production') { 'Magenta' } else { 'Yellow' })

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.]+)?$') {
    throw 'Version contains unsupported path characters.'
}
$releaseParent = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\release'))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $releaseParent $Version))
if (-not $releaseRoot.StartsWith($releaseParent + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output escaped the product release directory.'
}
function Remove-GuardedReleaseDirectory([string]$Path) {
    if (-not [System.IO.Path]::IsPathFullyQualified($Path)) { throw 'Release cleanup path must be absolute.' }
    if (($Path -split '[\\/]') -contains '..' -or ($Path -split '[\\/]') -contains '.') { throw 'Release cleanup path contains dot segments.' }
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\','/'))
    if (-not $full.StartsWith($releaseParent + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release cleanup escaped the product release directory.'
    }
    $current = $full
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Release cleanup traverses a reparse point.' }
        $next = [System.IO.Path]::GetDirectoryName($current)
        if (-not $next -or $next -eq $current) { break }
        $current = $next
    }
    if (Test-Path -LiteralPath $full) {
        foreach ($entry in Get-ChildItem -LiteralPath $full -Recurse -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release cleanup tree contains a reparse point.' }
        }
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}
if (Test-Path -LiteralPath $releaseRoot) {
    Remove-GuardedReleaseDirectory $releaseRoot
}
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

$scDir = Join-Path $releaseRoot "SpeedMeterPro-$Version-$Runtime"
$fddDir = Join-Path $releaseRoot "SpeedMeterPro-$Version-$Runtime-fdd"

# 1. Publish Self-Contained Release
Write-Host "Publishing Self-Contained $Runtime..." -ForegroundColor Yellow
& $dotnetPath publish (Join-Path $projectRoot 'src\SpeedMeter.App\SpeedMeter.App.csproj') `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=false `
    -o $scDir `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Dotnet publish self-contained failed." }

# 2. Publish Framework-Dependent Release (for managed/enterprise environments)
Write-Host "Publishing Framework-Dependent $Runtime..." -ForegroundColor Yellow
& $dotnetPath publish (Join-Path $projectRoot 'src\SpeedMeter.App\SpeedMeter.App.csproj') `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $fddDir `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Dotnet publish framework-dependent failed." }

# 3. ZIPs are diagnostic payloads. The signed graphical Setup is the supported install and uninstall path.
# 4. Sign Binaries in unpacked distributions
Write-Host "Signing Self-Contained binaries..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'sign.ps1') -TargetDir $scDir -Mode $SignMode

Write-Host "Signing Framework-Dependent binaries..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'sign.ps1') -TargetDir $fddDir -Mode $SignMode

# 5. Create ZIP Packages
$scZip = Join-Path $releaseRoot "SpeedMeterPro-$Version-$Runtime.zip"
$fddZip = Join-Path $releaseRoot "SpeedMeterPro-$Version-$Runtime-fdd.zip"

Write-Host "Creating Self-Contained ZIP archive: $scZip..." -ForegroundColor Yellow
Compress-Archive -Path "$scDir\*" -DestinationPath $scZip -CompressionLevel Optimal

Write-Host "Creating Framework-Dependent ZIP archive: $fddZip..." -ForegroundColor Yellow
Compress-Archive -Path "$fddDir\*" -DestinationPath $fddZip -CompressionLevel Optimal

# 6. Build and Publish Standalone Canonical Installer
Write-Host "Building standalone canonical installer (SpeedMeterPro-$Version-Setup.exe)..." -ForegroundColor Yellow
$setupPublishDir = Join-Path $releaseRoot 'tmp_setup_build'
if (Test-Path -LiteralPath $setupPublishDir) {
    Remove-GuardedReleaseDirectory $setupPublishDir
}

& $dotnetPath publish (Join-Path $projectRoot 'src\SpeedMeter.Installer\SpeedMeter.Installer.csproj') `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PayloadZipPath="$scZip" `
    -o $setupPublishDir `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Dotnet publish installer failed." }

$setupExeSource = Join-Path $setupPublishDir 'SpeedMeterPro-Setup.exe'
$setupExeTarget = Join-Path $releaseRoot "SpeedMeterPro-$Version-Setup.exe"

if (-not (Test-Path -LiteralPath $setupExeSource)) {
    throw "Compiled installer SpeedMeterPro-Setup.exe not found at: $setupExeSource"
}

Copy-Item -Path $setupExeSource -Destination $setupExeTarget -Force
Remove-GuardedReleaseDirectory $setupPublishDir

# Sign Standalone Setup.exe
Write-Host "Signing Standalone Installer: $setupExeTarget..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'sign.ps1') -TargetFile $setupExeTarget -Mode $SignMode

# 7. Report Package Metrics
function Get-DirSize([string]$path) {
    (Get-ChildItem -Path $path -Recurse | Measure-Object -Property Length -Sum).Sum
}

$scFiles = (Get-ChildItem -Path $scDir -Recurse -File).Count
$scSize = Get-DirSize $scDir
$scZipSize = (Get-Item $scZip).Length
$scZipHash = (Get-FileHash -Path $scZip -Algorithm SHA256).Hash

$fddFiles = (Get-ChildItem -Path $fddDir -Recurse -File).Count
$fddSize = Get-DirSize $fddDir
$fddZipSize = (Get-Item $fddZip).Length
$fddZipHash = (Get-FileHash -Path $fddZip -Algorithm SHA256).Hash

$setupSize = (Get-Item $setupExeTarget).Length
$setupHash = (Get-FileHash -Path $setupExeTarget -Algorithm SHA256).Hash

Write-Host ""
Write-Host "=== Packaging Summary ===" -ForegroundColor Green
Write-Host "Primary Canonical Installer:"
Write-Host "  File:              $setupExeTarget"
Write-Host "  Size:              $([Math]::Round($setupSize / 1MB, 2)) MB ($setupSize bytes)"
Write-Host "  SHA-256:           $setupHash"
Write-Host ""
Write-Host "Self-Contained Secondary Diagnostic/Enterprise Archive ($Runtime):"
Write-Host "  Files:             $scFiles"
Write-Host "  Uncompressed Size: $([Math]::Round($scSize / 1MB, 2)) MB ($scSize bytes)"
Write-Host "  ZIP Size:          $([Math]::Round($scZipSize / 1MB, 2)) MB ($scZipSize bytes)"
Write-Host "  ZIP SHA-256:       $scZipHash"
Write-Host ""
Write-Host "Framework-Dependent Secondary Diagnostic/Enterprise Archive ($Runtime):"
Write-Host "  Files:             $fddFiles"
Write-Host "  Uncompressed Size: $([Math]::Round($fddSize / 1MB, 2)) MB ($fddSize bytes)"
Write-Host "  ZIP Size:          $([Math]::Round($fddZipSize / 1MB, 2)) MB ($fddZipSize bytes)"
Write-Host "  ZIP SHA-256:       $fddZipHash"
Write-Host ""
Write-Host "Package creation completed successfully." -ForegroundColor Green
