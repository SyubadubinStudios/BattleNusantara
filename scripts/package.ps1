# Builds the Windows installer: publishes the desktop game and the LAN server (self-contained, win-x64)
# and compiles installer/BattleNusantara.iss with Inno Setup 6.
# Membangun installer Windows: mem-publish game desktop dan server LAN, lalu mengompilasi skrip Inno Setup.
#   ./scripts/package.ps1                 -> artifacts/installer/BattleNusantara-<version>-win-x64-setup.exe
#   ./scripts/package.ps1 -SkipPublish    reuse artifacts/publish
param(
    [switch]$SkipPublish,
    [string]$Iscc = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "No <Version> in Directory.Build.props." }
$publish = Join-Path $root "artifacts/publish"
$output = Join-Path $root "artifacts/installer"

if (-not $SkipPublish) {
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force -Confirm:$false }
    foreach ($target in @("Desktop", "Server")) {
        & dotnet publish (Join-Path $root "src/BattleNusantara.$target") -c Release -r win-x64 --self-contained true -o (Join-Path $publish $target) -p:DebugType=none
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish $target failed with exit code $LASTEXITCODE." }
    }
    # The server must not ship a development database.
    $data = Join-Path $publish "Server/data"
    if (Test-Path $data) { Remove-Item $data -Recurse -Force -Confirm:$false }
}

if (-not $Iscc) {
    $candidates = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }
    $Iscc = $candidates | Select-Object -First 1
}
if (-not $Iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install it with: winget install JRSoftware.InnoSetup" }

New-Item -ItemType Directory -Force $output | Out-Null
& $Iscc "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$output" (Join-Path $root "installer/BattleNusantara.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }
$installer = Join-Path $output "BattleNusantara-$version-win-x64-setup.exe"
if (-not (Test-Path $installer)) { throw "Installer was not created: $installer" }
$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$installer.sha256" -Value "$hash  $(Split-Path -Leaf $installer)" -Encoding ascii
Write-Output "Installer: $installer"
Write-Output "SHA-256:   $hash"
