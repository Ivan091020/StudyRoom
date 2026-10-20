param(
    [Parameter(Mandatory = $true)]
    [string]$BackupFolderPath,
    [string]$PgBinPath = "C:\Program Files\PostgreSQL\18\bin",
    [string]$DbName = "studyroom_db",
    [string]$DbUser = "postgres",
    [string]$DbPassword = "postgres"
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $BackupFolderPath)) {
    throw "Backup folder not found: $BackupFolderPath"
}

$env:PGPASSWORD = $DbPassword
$psqlExe = Join-Path $PgBinPath "psql.exe"
$sqlDumpFile = Join-Path $BackupFolderPath "$DbName.sql"

if (Test-Path $sqlDumpFile) {
    Write-Host "[1/2] Restoring PostgreSQL database ($DbName) from $sqlDumpFile..." -ForegroundColor Cyan
    & $psqlExe -h localhost -p 5432 -U $DbUser -d $DbName -f $sqlDumpFile | Out-Null
}

$materialsZip = Join-Path $BackupFolderPath "Materials.zip"
$materialsTarget = Join-Path "$PSScriptRoot\.." "App_Data\Materials"
if (Test-Path $materialsZip) {
    Write-Host "[2/2] Restoring lecture materials to $materialsTarget..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $materialsTarget | Out-Null
    Expand-Archive -Path $materialsZip -DestinationPath $materialsTarget -Force
}

Write-Host "Restore completed successfully!" -ForegroundColor Green

