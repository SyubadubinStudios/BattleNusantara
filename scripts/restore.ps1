# Restores the account database from a backup. STOP THE SERVER FIRST. / Memulihkan basis data akun. HENTIKAN SERVER DAHULU.
#   ./scripts/restore.ps1 -File backups/nusantara-20261116-101500.db                 embedded SQLite (tested)
#   ./scripts/restore.ps1 -File backups/nusantara-....dump -Postgres "<conn uri>"    PostgreSQL via pg_restore on PATH (run once in a drill)
# The current data is replaced. The backup is verified before anything is changed. / Data saat ini diganti.
param(
    [Parameter(Mandatory = $true)][string]$File,
    [string]$Postgres = "",
    [string]$Server = "src/BattleNusantara.Server",
    [switch]$Force
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = (Resolve-Path $File).Path
if (-not $Force) { throw "This replaces the current account database with $source. Stop the server, then run again with -Force. / Ini mengganti basis data akun. Hentikan server, lalu jalankan lagi dengan -Force." }

if ($Postgres) {
    & pg_restore --clean --if-exists --single-transaction --dbname=$Postgres $source
    if ($LASTEXITCODE -ne 0) { throw "pg_restore failed with exit code $LASTEXITCODE." }
} else {
    & dotnet run --project (Join-Path $root $Server) -c Release --no-launch-profile -- restore $source
    if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE." }
}
Write-Output "Restored from: $source"
