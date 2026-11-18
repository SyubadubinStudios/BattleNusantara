# Backs up the account database. / Mencadangkan basis data akun.
#   ./scripts/backup.ps1                         embedded SQLite (tested) / SQLite tertanam (teruji)
#   ./scripts/backup.ps1 -Postgres "<conn uri>"  PostgreSQL via pg_dump on PATH (run once in a drill / dijalankan sekali dalam latihan)
# SQLite backups are safe while the server runs. Keeps the newest -Keep files. / Aman saat server berjalan.
param(
    [string]$Destination = "backups",
    [int]$Keep = 14,
    [string]$Postgres = "",
    [string]$Server = "src/BattleNusantara.Server"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$target = if ([System.IO.Path]::IsPathRooted($Destination)) { $Destination } else { Join-Path $root $Destination }
New-Item -ItemType Directory -Force $target | Out-Null
$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")

if ($Postgres) {
    $file = Join-Path $target "nusantara-$stamp.dump"
    & pg_dump --format=custom --file=$file --dbname=$Postgres
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE." }
    $pattern = "nusantara-*.dump"
} else {
    $file = Join-Path $target "nusantara-$stamp.db"
    & dotnet run --project (Join-Path $root $Server) -c Release --no-launch-profile -- backup $file
    if ($LASTEXITCODE -ne 0) { throw "Backup failed with exit code $LASTEXITCODE." }
    $pattern = "nusantara-*.db"
}
if (-not (Test-Path $file)) { throw "Backup file was not created: $file" }

# Names sort by time, so the oldest files come first. / Nama terurut waktu.
$old = Get-ChildItem $target -Filter $pattern | Sort-Object Name -Descending | Select-Object -Skip ([Math]::Max(1, $Keep))
foreach ($item in $old) { Remove-Item $item.FullName -Confirm:$false }
Write-Output "Backup: $file"
