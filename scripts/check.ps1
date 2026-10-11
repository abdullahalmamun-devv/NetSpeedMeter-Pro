param([switch]$Windows)
. "$PSScriptRoot/common.ps1"
$checkArgs = @()
if ($Windows) { $checkArgs += '--windows' }
Restore-Project 'tests/SpeedMeter.Checks/SpeedMeter.Checks.csproj'
& $dotnetPath run --project tests/SpeedMeter.Checks -c Release --no-restore -- @checkArgs
if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
