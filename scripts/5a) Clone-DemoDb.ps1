<#
.SYNOPSIS
    Clones the TSIC dev database to a demo database with all PII morphed.

.DESCRIPTION
    1. Backs up the source database
    2. Restores it as TSIC_Demo (or specified target name)
    3. Runs PII morph SQL against the restored copy
    4. Swaps branding images to stock demo assets
    5. Outputs a cheat sheet mapping real job names to demo names

.PARAMETER SourceDb
    Source database name (default: TSIC-Core-Angular)

.PARAMETER TargetDb
    Target demo database name (default: TSIC_Demo)

.PARAMETER SqlInstance
    SQL Server instance (default: localhost)

.PARAMETER BackupPath
    Path for the intermediate .bak file (default: C:\tmp\demo-clone.bak)

.PARAMETER DemoAssetsPath
    Path to stock demo banner/logo images (default: scripts\demo-assets\)

.PARAMETER BannerFilesPath
    Physical path where branding images are served from.
    Read from appsettings if not specified.

.PARAMETER WhitelistJobs
    Optional array of JobPath values to flag as "curated demos" in the cheat sheet.
    All jobs are kept regardless — this just highlights your go-to demos.

.EXAMPLE
    .\scripts\Clone-DemoDb.ps1
    .\scripts\Clone-DemoDb.ps1 -TargetDb "TSIC_Demo_March" -WhitelistJobs @("summer-classic","winter-hoops")
#>

param(
    [string]$SourceDb = "TSIC-Core-Angular",
    [string]$TargetDb = "TSIC_Demo",
    [string]$SqlInstance = "localhost",
    [string]$BackupPath = "C:\tmp\demo-clone.bak",
    [string]$DemoAssetsPath = "",
    [string]$BannerFilesPath = "",
    [string[]]$WhitelistJobs = @()
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Default demo assets path
if (-not $DemoAssetsPath) {
    $DemoAssetsPath = Join-Path $scriptDir "demo-assets"
}

# Try to read BannerFilesPath from appsettings if not provided
if (-not $BannerFilesPath) {
    $appsettingsPath = Join-Path $scriptDir "..\TSIC-Core-Angular\src\backend\TSIC.API\appsettings.Development.json"
    if (Test-Path $appsettingsPath) {
        $config = Get-Content $appsettingsPath | ConvertFrom-Json
        $BannerFilesPath = $config.FileStorage.BannerFilesPath
        Write-Host "  Read BannerFilesPath from appsettings: $BannerFilesPath" -ForegroundColor DarkGray
    }
}

# ── Helper: run SQL against target DB ──
function Invoke-DemoSql {
    param([string]$Sql, [string]$Database = $TargetDb)
    sqlcmd -S $SqlInstance -d $Database -Q $Sql -b -I
    if ($LASTEXITCODE -ne 0) { throw "SQL command failed (exit code $LASTEXITCODE)" }
}

function Invoke-DemoSqlFile {
    param([string]$FilePath, [string]$Database = $TargetDb)
    sqlcmd -S $SqlInstance -d $Database -i $FilePath -b -I
    if ($LASTEXITCODE -ne 0) { throw "SQL script failed: $FilePath" }
}

# ═══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   TSIC Demo Database Clone & Morph Script    ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Source:  $SourceDb" -ForegroundColor Yellow
Write-Host "  Target:  $TargetDb" -ForegroundColor Yellow
Write-Host "  Server:  $SqlInstance" -ForegroundColor Yellow
Write-Host ""

# ── Step 1: Backup source ──
Write-Host "Step 1/5 — Backing up $SourceDb ..." -ForegroundColor Cyan

# Ensure backup directory exists
$backupDir = Split-Path -Parent $BackupPath
if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir -Force | Out-Null }

Invoke-DemoSql -Sql "BACKUP DATABASE [$SourceDb] TO DISK = N'$BackupPath' WITH INIT, COMPRESSION, STATS = 10;" -Database "master"
Write-Host "  Backup complete: $BackupPath" -ForegroundColor Green

# ── Step 2: Restore as target ──
Write-Host "Step 2/5 — Restoring as $TargetDb ..." -ForegroundColor Cyan

# Get logical file names from backup
$fileListSql = "RESTORE FILELISTONLY FROM DISK = N'$BackupPath'"
$fileList = sqlcmd -S $SqlInstance -d master -Q $fileListSql -h -1 -W -s "|"

# Drop target if exists, then restore with MOVE
$restoreSql = @"
IF DB_ID('$TargetDb') IS NOT NULL
BEGIN
    ALTER DATABASE [$TargetDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$TargetDb];
END

-- Get logical names and restore with MOVE
DECLARE @dataLogical NVARCHAR(256), @logLogical NVARCHAR(256);
DECLARE @ft TABLE (
    LogicalName NVARCHAR(128), PhysicalName NVARCHAR(260), [Type] CHAR(1),
    FileGroupName NVARCHAR(128), Size NUMERIC(20,0), MaxSize NUMERIC(20,0),
    FileId BIGINT, CreateLSN NUMERIC(25,0), DropLSN NUMERIC(25,0),
    UniqueId UNIQUEIDENTIFIER, ReadOnlyLSN NUMERIC(25,0), ReadWriteLSN NUMERIC(25,0),
    BackupSizeInBytes BIGINT, SourceBlockSize INT, FileGroupId INT,
    LogGroupGUID UNIQUEIDENTIFIER, DifferentialBaseLSN NUMERIC(25,0),
    DifferentialBaseGUID UNIQUEIDENTIFIER, IsReadOnly BIT, IsPresent BIT,
    TDEThumbprint VARBINARY(32), SnapshotURL NVARCHAR(360)
);
INSERT @ft EXEC('RESTORE FILELISTONLY FROM DISK = N''$BackupPath''');

SELECT @dataLogical = LogicalName FROM @ft WHERE [Type] = 'D';
SELECT @logLogical  = LogicalName FROM @ft WHERE [Type] = 'L';

DECLARE @dataPath NVARCHAR(512) = CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(512));
DECLARE @logPath  NVARCHAR(512) = CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(512));

DECLARE @sql NVARCHAR(MAX) = N'
RESTORE DATABASE [$TargetDb]
FROM DISK = N''$BackupPath''
WITH
    MOVE ''' + @dataLogical + N''' TO ''' + @dataPath + N'$TargetDb.mdf'',
    MOVE ''' + @logLogical  + N''' TO ''' + @logPath  + N'${TargetDb}_log.ldf'',
    REPLACE, STATS = 10;';

EXEC sp_executesql @sql;
"@

Invoke-DemoSql -Sql $restoreSql -Database "master"
Write-Host "  Restore complete: $TargetDb" -ForegroundColor Green

# ── Step 3: Run PII morph ──
Write-Host "Step 3/5 — Morphing PII ..." -ForegroundColor Cyan

$morphSqlPath = Join-Path $scriptDir "5b) morph-demo-pii.sql"
Invoke-DemoSqlFile -FilePath $morphSqlPath
Write-Host "  PII morph complete" -ForegroundColor Green

# ── Step 4: Swap branding images ──
Write-Host "Step 4/5 — Swapping branding images ..." -ForegroundColor Cyan

if ($BannerFilesPath -and (Test-Path $DemoAssetsPath)) {
    # Read demo image filenames from assets folder
    $demoBanner   = Get-ChildItem (Join-Path $DemoAssetsPath "demo-banner-bg.*")   -ErrorAction SilentlyContinue | Select-Object -First 1
    $demoOverlay  = Get-ChildItem (Join-Path $DemoAssetsPath "demo-banner-overlay.*") -ErrorAction SilentlyContinue | Select-Object -First 1
    $demoLogo     = Get-ChildItem (Join-Path $DemoAssetsPath "demo-logo-header.*")  -ErrorAction SilentlyContinue | Select-Object -First 1

    if ($demoBanner -or $demoOverlay -or $demoLogo) {
        # Get all job IDs from the demo DB
        $jobIdsSql = "SET NOCOUNT ON; SELECT CAST(JobId AS VARCHAR(36)) FROM Jobs.Jobs;"
        $jobIds = sqlcmd -S $SqlInstance -d $TargetDb -Q $jobIdsSql -h -1 -W | Where-Object { $_ -match '\S' }

        foreach ($jobId in $jobIds) {
            $jobId = $jobId.Trim()
            if (-not $jobId) { continue }

            # Find and replace existing branding images for this job
            $existingFiles = Get-ChildItem (Join-Path $BannerFilesPath "${jobId}_*") -ErrorAction SilentlyContinue

            # Copy demo assets with correct naming convention
            if ($demoBanner) {
                $ext = $demoBanner.Extension
                Copy-Item $demoBanner.FullName (Join-Path $BannerFilesPath "${jobId}_banner-bg${ext}") -Force
            }
            if ($demoOverlay) {
                $ext = $demoOverlay.Extension
                Copy-Item $demoOverlay.FullName (Join-Path $BannerFilesPath "${jobId}_banner-overlay${ext}") -Force
            }
            if ($demoLogo) {
                $ext = $demoLogo.Extension
                Copy-Item $demoLogo.FullName (Join-Path $BannerFilesPath "${jobId}_logo-header${ext}") -Force
            }
        }
        Write-Host "  Replaced branding images for $($jobIds.Count) jobs" -ForegroundColor Green
    }
    else {
        Write-Host "  No demo assets found in $DemoAssetsPath — skipping image swap" -ForegroundColor Yellow
        Write-Host "  (Place demo-banner-bg.jpg, demo-banner-overlay.png, demo-logo-header.png there)" -ForegroundColor DarkGray
    }
}
else {
    Write-Host "  BannerFilesPath not configured or demo-assets folder missing — skipping" -ForegroundColor Yellow
}

# ── Step 5: Generate cheat sheet ──
Write-Host "Step 5/5 — Generating cheat sheet ..." -ForegroundColor Cyan

$cheatSheetSql = @"
SET NOCOUNT ON;
SELECT
    j.JobPath,
    j.JobName,
    j.DisplayName,
    jdo.JobType,
    (SELECT COUNT(*) FROM Leagues.teams t
     INNER JOIN Leagues.agegroups ag ON t.AgId = ag.AgId
     INNER JOIN Jobs.Job_Leagues jl ON ag.LeagueSeasonId = jl.LeagueSeasonId
     WHERE jl.JobId = j.JobId) AS TeamCount,
    (SELECT COUNT(DISTINCT r.RegistrationId) FROM Jobs.Registrations r WHERE r.JobId = j.JobId) AS RegCount
FROM Jobs.Jobs j
LEFT JOIN Jobs.JobDisplayOptions jdo ON j.JobId = jdo.JobId
ORDER BY j.JobName;
"@

$cheatSheetData = sqlcmd -S $SqlInstance -d $TargetDb -Q $cheatSheetSql -s "|" -W -h -1 | Where-Object { $_ -match '\S' }

$cheatSheetPath = Join-Path $scriptDir "demo-cheat-sheet.txt"
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm"

$output = @"
╔══════════════════════════════════════════════════════════════╗
║              TSIC DEMO — JOB CHEAT SHEET                     ║
║              Generated: $timestamp                       ║
║              Demo Login: demo-admin / Demo2026!              ║
╚══════════════════════════════════════════════════════════════╝

"@

if ($WhitelistJobs.Count -gt 0) {
    $output += "★ CURATED DEMOS (highlighted for targeted walkthroughs)`r`n"
    $output += "─────────────────────────────────────────────────────`r`n"
}

foreach ($line in $cheatSheetData) {
    $parts = $line -split '\|'
    if ($parts.Count -ge 6) {
        $jobPath = $parts[0].Trim()
        $jobName = $parts[1].Trim()
        $displayName = $parts[2].Trim()
        $jobType = $parts[3].Trim()
        $teamCount = $parts[4].Trim()
        $regCount = $parts[5].Trim()

        $star = ""
        if ($WhitelistJobs -contains $jobPath) { $star = " ★" }

        $output += "${jobName}${star}`r`n"
        $output += "  Path: /${jobPath}   Type: ${jobType}   Teams: ${teamCount}   Registrations: ${regCount}`r`n"
        $output += "`r`n"
    }
}

$output += "`r`n───────────────────────────────────────────────────────────────`r`n"
$output += "Source DB: $SourceDb → Demo DB: $TargetDb`r`n"
$output += "All names, emails, phones, addresses, and financials are morphed.`r`n"
$output += "All branding images replaced with stock demo assets.`r`n"

Set-Content -Path $cheatSheetPath -Value $output
Write-Host "  Cheat sheet written to: $cheatSheetPath" -ForegroundColor Green

# ── Done ──
Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  Demo database ready: $TargetDb" -ForegroundColor Green
Write-Host "  Cheat sheet: $cheatSheetPath" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""

# Clean up backup file
if (Test-Path $BackupPath) {
    Remove-Item $BackupPath -Force
    Write-Host "  Cleaned up backup file" -ForegroundColor DarkGray
}
