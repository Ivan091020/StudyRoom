param(
    [string]$BackupDir = "$PSScriptRoot\..\Backups",
    [string]$PgBinPath = "C:\Program Files\PostgreSQL\18\bin",
    [string]$DbName = "studyroom_db",
    [string]$DbUser = "postgres",
    [string]$DbPassword = "postgres"
)

$ErrorActionPreference = "Stop"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$targetFolder = Join-Path $BackupDir "backup_$timestamp"
New-Item -ItemType Directory -Force -Path $targetFolder | Out-Null

$env:PGPASSWORD = $DbPassword
$pgDumpExe = Join-Path $PgBinPath "pg_dump.exe"
$sqlDumpFile = Join-Path $targetFolder "$DbName.sql"

Write-Host "[1/2] Backing up PostgreSQL database ($DbName)..." -ForegroundColor Cyan
& $pgDumpExe -h localhost -p 5432 -U $DbUser -d $DbName -F p -f $sqlDumpFile
if ($LASTEXITCODE -ne 0) {
    throw "pg_dump failed with exit code: $LASTEXITCODE"
}

Write-Host "[2/2] Archiving lecture materials (App_Data/Materials)..." -ForegroundColor Cyan
$materialsPath = Join-Path "$PSScriptRoot\.." "App_Data\Materials"
$materialsZip = Join-Path $targetFolder "Materials.zip"
if (Test-Path $materialsPath) {
    Compress-Archive -Path "$materialsPath\*" -DestinationPath $materialsZip -Force
}

Write-Host "Backup completed successfully: $targetFolder" -ForegroundColor Green
Get-ChildItem $targetFolder | Select-Object Name, Length, LastWriteTime
