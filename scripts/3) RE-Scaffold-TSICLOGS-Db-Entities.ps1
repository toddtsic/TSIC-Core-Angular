# Scaffold entities and context from the TSICLogs database (DB-first)
#
# Companion to "3) RE-Scaffold-Db-Entities.ps1", which owns TSICV5.
# The two scripts share nothing: different database, different context,
# different output folder, different namespace. Neither can overwrite the
# other's files.
#
#   TSICV5   -> SqlDbContext   -> TSIC.Domain\Entities      (203 classes)
#   TSICLogs -> LogsDbContext  -> TSIC.Domain\LogEntities   (5 classes)

# Ensure we're in the correct directory (TSIC-Core-Angular)
Set-Location $PSScriptRoot\..

$ConnectionString = "Server=.\SS2016;Database=TSICLogs;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"

$projectPath   = "TSIC-Core-Angular\src\backend\TSIC.Infrastructure\TSIC.Infrastructure.csproj"
$entitiesPath  = "TSIC-Core-Angular\src\backend\TSIC.Domain\LogEntities"
$contextPath   = "TSIC-Core-Angular\src\backend\TSIC.Infrastructure\Data\LogsDbContext\Context"
$v5EntitiesDir = "TSIC-Core-Angular\src\backend\TSIC.Domain\Entities"

# ---------------------------------------------------------------------------
# Guard 1: refuse to run against anything but TSICLogs.
# A hardcoded connection string is one careless edit away from scaffolding
# TSICV5's 203 entities into LogEntities.
# ---------------------------------------------------------------------------
if ($ConnectionString -notmatch '(?i)Database\s*=\s*TSICLogs\s*;') {
    Write-Host "ABORT: connection string does not target TSICLogs." -ForegroundColor Red
    Write-Host "  $ConnectionString" -ForegroundColor Gray
    exit 1
}

# ---------------------------------------------------------------------------
# Guard 2: refuse to write anywhere near the TSICV5 entity folder.
# Compares resolved absolute paths, so ..\ tricks and casing don't slip past.
# ---------------------------------------------------------------------------
$v5Full  = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $v5EntitiesDir))
$logFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $entitiesPath))
if ($logFull.TrimEnd('\') -ieq $v5Full.TrimEnd('\') -or $logFull.StartsWith($v5Full.TrimEnd('\') + '\', 'OrdinalIgnoreCase')) {
    Write-Host "ABORT: output directory resolves inside the TSICV5 entity folder." -ForegroundColor Red
    Write-Host "  TSICV5   : $v5Full" -ForegroundColor Gray
    Write-Host "  TSICLogs : $logFull" -ForegroundColor Gray
    exit 1
}

# Ensure dotnet-ef is up-to-date
Write-Host "Updating dotnet-ef to the latest version..." -ForegroundColor Cyan
dotnet tool update --global dotnet-ef

# Capture entity files before scaffold for change detection
$beforeScaffold = @{}
if (Test-Path $entitiesPath) {
    Get-ChildItem $entitiesPath -Filter "*.cs" | ForEach-Object {
        $beforeScaffold[$_.Name] = (Get-FileHash $_.FullName -Algorithm MD5).Hash
    }
}

# Run the scaffold command
Write-Host "`nScaffolding TSICLogs entities from database..." -ForegroundColor Cyan
dotnet ef dbcontext scaffold `
    $ConnectionString `
    Microsoft.EntityFrameworkCore.SqlServer `
    --project $projectPath `
    --context LogsDbContext `
    --context-dir Data\LogsDbContext\Context `
    --output-dir ..\..\backend\TSIC.Domain\LogEntities `
    --namespace TSIC.Domain.LogEntities `
    --context-namespace TSIC.Infrastructure.Data.LogsDbContext `
    --schema logs `
    --no-build `
    --force `
    --no-onconfiguring `
    --no-pluralize

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nScaffolding FAILED!" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# Verify the shape of what came out. TSICLogs has exactly five tables; if the
# scaffold emits anything else, the connection string or the schema filter is
# not doing what this script assumes and the output should not be committed.
# ---------------------------------------------------------------------------
$expected = @('AppUsage', 'AppClients', 'Platforms', 'Browsers', 'DeviceClasses')

Write-Host "`nAnalyzing changes..." -ForegroundColor Cyan
$actual = @()
$changedEntities = @()
Get-ChildItem $entitiesPath -Filter "*.cs" | ForEach-Object {
    $actual += $_.BaseName
    $currentHash = (Get-FileHash $_.FullName -Algorithm MD5).Hash
    if (-not $beforeScaffold.ContainsKey($_.Name)) {
        Write-Host "  [NEW] $($_.Name)" -ForegroundColor Green
        $changedEntities += $_.BaseName
    } elseif ($beforeScaffold[$_.Name] -ne $currentHash) {
        Write-Host "  [MODIFIED] $($_.Name)" -ForegroundColor Yellow
        $changedEntities += $_.BaseName
    }
}

$unexpected = $actual | Where-Object { $expected -notcontains $_ }
$missing    = $expected | Where-Object { $actual -notcontains $_ }

if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
    Write-Host "`n========================================" -ForegroundColor Red
    Write-Host "UNEXPECTED SCAFFOLD OUTPUT - DO NOT COMMIT" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    if ($missing.Count -gt 0) {
        Write-Host "  Missing   : $($missing -join ', ')" -ForegroundColor Yellow
    }
    if ($unexpected.Count -gt 0) {
        Write-Host "  Unexpected: $($unexpected -join ', ')" -ForegroundColor Yellow
        Write-Host "  These came from a database this script did not expect." -ForegroundColor White
    }
    Write-Host "`n  Review $entitiesPath before doing anything else." -ForegroundColor White
    exit 1
}

$contextFile = Join-Path $contextPath "LogsDbContext.cs"
if (-not (Test-Path $contextFile)) {
    Write-Host "`nLogsDbContext.cs was not produced at $contextPath" -ForegroundColor Red
    exit 1
}

# Success summary
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "TSICLogs scaffolding complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Entities : $entitiesPath  ($($actual.Count) classes)" -ForegroundColor White
Write-Host "  Context  : $contextFile" -ForegroundColor White

if ($changedEntities.Count -gt 0) {
    Write-Host "`nChanged entities: $($changedEntities -join ', ')" -ForegroundColor Cyan
} else {
    Write-Host "`nNo entity changes detected - TSICLogs schema matches current entities." -ForegroundColor Cyan
}

Write-Host "`nUNTOUCHED BY THIS SCRIPT:" -ForegroundColor Magenta
Write-Host "  TSIC.Domain\Entities  (TSICV5 - owned by '3) RE-Scaffold-Db-Entities.ps1')" -ForegroundColor White
Write-Host "  SqlDbContext, TSICIdentityDbContext" -ForegroundColor White

Write-Host "`nREMINDER:" -ForegroundColor Red
Write-Host "  LogsDbContext is read-side only. The write path is the usage" -ForegroundColor White
Write-Host "  filter, and it does not go through EF." -ForegroundColor White
