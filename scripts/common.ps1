$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) { $dotnetPath = $localDotnet }
else { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:DOTNET_ROOT = Split-Path -Parent $dotnetPath
Set-Location -LiteralPath $projectRoot
function Restore-Project([string]$target) {
    & $dotnetPath restore $target --configfile (Join-Path $projectRoot 'NuGet.Config') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
}
