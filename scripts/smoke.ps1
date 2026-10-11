. "$PSScriptRoot/common.ps1"
Restore-Project 'src/SpeedMeter.App/SpeedMeter.App.csproj'
& $dotnetPath build src/SpeedMeter.App -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$outputDir = Join-Path $projectRoot ('artifacts\smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$appPath = Join-Path $projectRoot 'src\SpeedMeter.App\bin\Release\net10.0-windows\SpeedMeter.App.dll'
$smokeProcess = Start-Process -FilePath $dotnetPath -ArgumentList @('"' + $appPath + '"', '--smoke', '"' + $outputDir + '"') -WindowStyle Hidden -PassThru
if (-not $smokeProcess.WaitForExit(30000)) { Stop-Process -Id $smokeProcess.Id; throw 'UI smoke test timed out.' }
if ($smokeProcess.ExitCode -ne 0) { throw "UI smoke test failed: $($smokeProcess.ExitCode)" }
$result = Get-Content -LiteralPath (Join-Path $outputDir 'smoke.json') -Raw | ConvertFrom-Json
if (-not $result.HasAdapter -or $result.ValidReadings -lt 2 -or -not $result.SnapshotMatches -or -not $result.UnitsMatch -or -not $result.PausePreservesTotals -or -not $result.ResetWhilePaused -or -not $result.ResumeIsLive) { throw 'UI smoke assertions failed.' }
if (-not $result.ClickThroughBeforeShow -or -not $result.ClickThroughRecoveredViaTray -or -not $result.WidgetPausedTruthful -or -not $result.WidgetStatesTruthful) { throw 'Widget audit assertions failed.' }
if (-not $result.QuickPanelSmokePass -or -not $result.NavDestinationsVerified -or -not $result.StorageBannerTruthful) { throw 'Product surface Wave 3 smoke assertions failed.' }
$result
Write-Output "Screenshot and results: $outputDir"
