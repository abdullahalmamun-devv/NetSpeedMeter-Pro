param(
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0-rc.1',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')

Write-Host "=== SpeedMeter Pro Release Packaging ($Version - $Runtime) ===" -ForegroundColor Cyan

$releaseRoot = Join-Path $projectRoot 'artifacts\release'
if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force -ErrorAction SilentlyContinue
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

# 3. Generate Install.ps1 and Uninstall.ps1 scripts for distribution packages
function Add-InstallerScripts([string]$distDir) {
    $installScript = @'
param(
    [switch]$LaunchAfterInstall,
    [switch]$CreateDesktopShortcut,
    [switch]$Silent
)

$ErrorActionPreference = 'Stop'
$sourceDir = $PSScriptRoot
$appName = 'SpeedMeter Pro'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\SpeedMeterPro'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $startMenuDir "$appName.lnk"
$exePath = Join-Path $installDir 'SpeedMeter.App.exe'

if (-not $Silent) {
    Write-Host "Installing $appName to $installDir..." -ForegroundColor Cyan
}

# Stop running instance if any
$running = Get-Process -Name 'SpeedMeter.App' -ErrorAction SilentlyContinue
if ($running) {
    if (-not $Silent) { Write-Host "Closing existing $appName process..." -ForegroundColor Yellow }
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# Create installation directory and copy files
if (-not (Test-Path -LiteralPath $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

Copy-Item -Path "$sourceDir\*" -Destination $installDir -Recurse -Force

# Create Start Menu Shortcut
$wscript = New-Object -ComObject WScript.Shell
$shortcut = $wscript.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = "$exePath,0"
$shortcut.Description = "SpeedMeter Pro - Precision Network Performance Monitor"
$shortcut.Save()

# Optional Desktop Shortcut
if ($CreateDesktopShortcut) {
    $desktopDir = [Environment]::GetFolderPath('Desktop')
    $desktopShortcutPath = Join-Path $desktopDir "$appName.lnk"
    $desktopShortcut = $wscript.CreateShortcut($desktopShortcutPath)
    $desktopShortcut.TargetPath = $exePath
    $desktopShortcut.WorkingDirectory = $installDir
    $desktopShortcut.IconLocation = "$exePath,0"
    $desktopShortcut.Description = "SpeedMeter Pro"
    $desktopShortcut.Save()
}

# Register Windows Uninstall Key (Standard User - HKCU)
$uninstallRegKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SpeedMeterPro'
if (-not (Test-Path -LiteralPath $uninstallRegKey)) {
    New-Item -Path $uninstallRegKey -Force | Out-Null
}

Set-ItemProperty -Path $uninstallRegKey -Name 'DisplayName' -Value $appName -Type String
Set-ItemProperty -Path $uninstallRegKey -Name 'DisplayVersion' -Value '1.0.0-rc.1' -Type String
Set-ItemProperty -Path $uninstallRegKey -Name 'Publisher' -Value 'SpeedMeter Pro' -Type String
Set-ItemProperty -Path $uninstallRegKey -Name 'InstallLocation' -Value $installDir -Type String
Set-ItemProperty -Path $uninstallRegKey -Name 'DisplayIcon' -Value "$exePath,0" -Type String
Set-ItemProperty -Path $uninstallRegKey -Name 'UninstallString' -Value "powershell.exe -ExecutionPolicy Bypass -File `"$installDir\Uninstall.ps1`"" -Type String
Set-ItemProperty -Path $uninstallRegKey -Name 'QuietUninstallString' -Value "powershell.exe -ExecutionPolicy Bypass -File `"$installDir\Uninstall.ps1`" -Silent" -Type String
Set-ItemProperty -Path $uninstallRegKey -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallRegKey -Name 'NoRepair' -Value 1 -Type DWord

if (-not $Silent) {
    Write-Host "$appName installation completed successfully!" -ForegroundColor Green
}

if ($LaunchAfterInstall) {
    Start-Process -FilePath $exePath
}
'@

    $uninstallScript = @'
param(
    [switch]$PurgeUserData,
    [switch]$Silent
)

$ErrorActionPreference = 'Stop'
$appName = 'SpeedMeter Pro'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\SpeedMeterPro'
$userDataDir = Join-Path $env:LOCALAPPDATA 'SpeedMeterPro'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $startMenuDir "$appName.lnk"
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) "$appName.lnk"
$uninstallRegKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SpeedMeterPro'
$runRegKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

if (-not $Silent) {
    Write-Host "Uninstalling $appName..." -ForegroundColor Cyan
}

# Stop running processes
$running = Get-Process -Name 'SpeedMeter.App' -ErrorAction SilentlyContinue
if ($running) {
    if (-not $Silent) { Write-Host "Stopping running process..." -ForegroundColor Yellow }
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# Remove Shortcuts
if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $desktopShortcutPath) {
    Remove-Item -LiteralPath $desktopShortcutPath -Force -ErrorAction SilentlyContinue
}

# Remove Startup Registry Entry
try {
    $currentVal = Get-ItemProperty -Path $runRegKey -Name 'SpeedMeterPro' -ErrorAction SilentlyContinue
    if ($currentVal) {
        Remove-ItemProperty -Path $runRegKey -Name 'SpeedMeterPro' -Force -ErrorAction SilentlyContinue
    }
} catch { }

# Remove Uninstall Registry Entry
if (Test-Path -LiteralPath $uninstallRegKey) {
    Remove-Item -LiteralPath $uninstallRegKey -Recurse -Force -ErrorAction SilentlyContinue
}

# Handle User Data Directory (%LocalAppData%\SpeedMeterPro)
if ($PurgeUserData) {
    if (Test-Path -LiteralPath $userDataDir) {
        if (-not $Silent) { Write-Host "Purging user data and history database ($userDataDir)..." -ForegroundColor Yellow }
        Remove-Item -LiteralPath $userDataDir -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    if (-not $Silent) {
        Write-Host "Preserved user data and history database at: $userDataDir" -ForegroundColor Gray
        Write-Host "(Run uninstall with -PurgeUserData to completely erase all historical databases)." -ForegroundColor Gray
    }
}

# Remove Program Binaries
# Since Uninstall.ps1 may be executing from $installDir, spawn an asynchronous cleanup script if needed or remove files
if (Test-Path -LiteralPath $installDir) {
    if (-not $Silent) { Write-Host "Removing program binaries..." -ForegroundColor Yellow }
    Get-ChildItem -Path $installDir -Exclude 'Uninstall.ps1' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    
    # Self-deleting batch process for the remaining Uninstall.ps1 and directory
    $cleanupBat = [System.IO.Path]::Combine($env:TEMP, "speedmeter_uninstall_$([Guid]::NewGuid().ToString('N')).bat")
    $batContent = "@echo off`r`nping 127.0.0.1 -n 2 > nul`r`nrd /s /q `"$installDir`"`r`ndel `"%~f0`""
    [System.IO.File]::WriteAllText($cleanupBat, $batContent)
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$cleanupBat`"" -WindowStyle Hidden
}

if (-not $Silent) {
    Write-Host "$appName has been successfully uninstalled." -ForegroundColor Green
}
'@

    $readmeContent = @"
SpeedMeter Pro 1.0.0-rc.1 (Release Candidate)
==============================================

Precision Network Performance Monitor for Windows 10/11 x64.
Standard-user desktop application with zero cloud dependencies and zero accounts.

INSTALLATION:
-------------
1. Run Install.ps1 with PowerShell:
   powershell.exe -ExecutionPolicy Bypass -File .\Install.ps1

   Options:
   -LaunchAfterInstall      Starts SpeedMeter Pro immediately after setup
   -CreateDesktopShortcut   Creates a desktop shortcut in addition to the Start Menu shortcut

2. Or run SpeedMeter.App.exe directly from this directory.

UNINSTALLATION:
---------------
Run Uninstall.ps1 from the installation directory or use Windows Settings > Apps:
powershell.exe -ExecutionPolicy Bypass -File .\Uninstall.ps1

Options:
-PurgeUserData            Deletes all network history and SQLite databases (%LocalAppData%\SpeedMeterPro)

DATA STORAGE:
-------------
All historical network telemetry is stored locally on this computer:
%LocalAppData%\SpeedMeterPro\speedmeter.db
"@

    Set-Content -Path (Join-Path $distDir 'Install.ps1') -Value $installScript -Encoding UTF8
    Set-Content -Path (Join-Path $distDir 'Uninstall.ps1') -Value $uninstallScript -Encoding UTF8
    Set-Content -Path (Join-Path $distDir 'README.txt') -Value $readmeContent -Encoding UTF8
}

Add-InstallerScripts -distDir $scDir
Add-InstallerScripts -distDir $fddDir

# 4. Sign Binaries
Write-Host "Signing Self-Contained binaries..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'sign.ps1') -TargetDir $scDir

Write-Host "Signing Framework-Dependent binaries..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'sign.ps1') -TargetDir $fddDir

# 5. Create ZIP Packages
$scZip = Join-Path $releaseRoot "SpeedMeterPro-$Version-$Runtime.zip"
$fddZip = Join-Path $releaseRoot "SpeedMeterPro-$Version-$Runtime-fdd.zip"

Write-Host "Creating Self-Contained ZIP archive: $scZip..." -ForegroundColor Yellow
Compress-Archive -Path "$scDir\*" -DestinationPath $scZip -CompressionLevel Optimal

Write-Host "Creating Framework-Dependent ZIP archive: $fddZip..." -ForegroundColor Yellow
Compress-Archive -Path "$fddDir\*" -DestinationPath $fddZip -CompressionLevel Optimal

# 6. Report Package Metrics
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

Write-Host ""
Write-Host "=== Packaging Summary ===" -ForegroundColor Green
Write-Host "Self-Contained ($Runtime):"
Write-Host "  Files: $scFiles"
Write-Host "  Uncompressed Size: $([Math]::Round($scSize / 1MB, 2)) MB ($scSize bytes)"
Write-Host "  ZIP Size:          $([Math]::Round($scZipSize / 1MB, 2)) MB ($scZipSize bytes)"
Write-Host "  ZIP SHA-256:       $scZipHash"
Write-Host ""
Write-Host "Framework-Dependent ($Runtime):"
Write-Host "  Files: $fddFiles"
Write-Host "  Uncompressed Size: $([Math]::Round($fddSize / 1MB, 2)) MB ($fddSize bytes)"
Write-Host "  ZIP Size:          $([Math]::Round($fddZipSize / 1MB, 2)) MB ($fddZipSize bytes)"
Write-Host "  ZIP SHA-256:       $fddZipHash"
Write-Host ""
Write-Host "Package creation completed successfully." -ForegroundColor Green
