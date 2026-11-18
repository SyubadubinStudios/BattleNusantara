param(
    [ValidateSet('Desktop', 'Server')][string]$HostType = 'Desktop',
    [switch]$Lan
)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    if ($HostType -eq 'Desktop') {
        dotnet run --project src/BattleNusantara.Desktop -c Release
    } else {
        $listenAddress = if ($Lan) { 'http://0.0.0.0:5080' } else { 'http://localhost:5080' }
        dotnet run --project src/BattleNusantara.Server -c Release --no-launch-profile --urls $listenAddress
    }
    if ($LASTEXITCODE -ne 0) { throw "Application exited with code $LASTEXITCODE" }
} finally { Pop-Location }
