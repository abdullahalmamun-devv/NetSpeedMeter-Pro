. "$PSScriptRoot/common.ps1"
Restore-Project 'src/SpeedMeter.App/SpeedMeter.App.csproj'
& $dotnetPath run --project src/SpeedMeter.App -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Application exited with an error.' }
