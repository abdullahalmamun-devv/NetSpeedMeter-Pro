. "$PSScriptRoot/common.ps1"
Restore-Project 'SpeedMeterPro.slnx'
& $dotnetPath build SpeedMeterPro.slnx -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
