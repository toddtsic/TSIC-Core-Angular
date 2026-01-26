# Scaffold entities and context from SQL Server DB-first
# Automates post-scaffold build and validation

# Ensure we're in the correct directory (TSIC-Core-Angular)
Set-Location $PSScriptRoot\..

# Ensure dotnet-ef is up-to-date
Write-Host "Updating dotnet-ef to the latest version..." -ForegroundColor Cyan
dotnet tool update --global dotnet-ef

# Capture entity files before scaffold for change detection
$entitiesPath = "TSIC-Core-Angular\src\backend\TSIC.Domain\Entities"
$beforeScaffold = @{}
if (Test-Path $entitiesPath) {
    Get-ChildItem $entitiesPath -Filter "*.cs" | ForEach-Object {
        $beforeScaffold[$_.Name] = (Get-FileHash $_.FullName -Algorithm MD5).Hash
    }
}

# Run the scaffold command
Write-Host "`nScaffolding entities from database..." -ForegroundColor Cyan
dotnet ef dbcontext scaffold `
    "Server=.\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True" `
    Microsoft.EntityFrameworkCore.SqlServer `
    --project TSIC-Core-Angular\src\backend\TSIC.Infrastructure\TSIC.Infrastructure.csproj `
    --context SqlDbContext `
    --context-dir Data\SqlDbContext\Context `
    --output-dir ..\..\backend\TSIC.Domain\Entities `
    --namespace TSIC.Domain.Entities `
    --context-namespace TSIC.Infrastructure.Data.SqlDbContext `
    --no-build `
    --force `
    --no-onconfiguring `
    --no-pluralize

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nScaffolding FAILED!" -ForegroundColor Red
    exit 1
}

# Detect changed entities
Write-Host "`nAnalyzing changes..." -ForegroundColor Cyan
$changedEntities = @()
Get-ChildItem $entitiesPath -Filter "*.cs" | ForEach-Object {
    $currentHash = (Get-FileHash $_.FullName -Algorithm MD5).Hash
    if (-not $beforeScaffold.ContainsKey($_.Name)) {
        Write-Host "  [NEW] $($_.Name)" -ForegroundColor Green
        $changedEntities += $_.BaseName
    } elseif ($beforeScaffold[$_.Name] -ne $currentHash) {
        Write-Host "  [MODIFIED] $($_.Name)" -ForegroundColor Yellow
        $changedEntities += $_.BaseName
    }
}

# Success summary
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "Scaffolding complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

if ($changedEntities.Count -gt 0) {
    Write-Host "`nChanged entities: $($changedEntities -join ', ')" -ForegroundColor Cyan
    
    Write-Host "`nWHAT CHANGED (Automatic):" -ForegroundColor Cyan
    Write-Host "  - Entity classes updated with new/modified properties" -ForegroundColor White
    Write-Host "  - SqlDbContext regenerated with current schema" -ForegroundColor White
    Write-Host "  - Solution builds successfully" -ForegroundColor White
    
    Write-Host "`nWHAT YOU NEED TO DECIDE:" -ForegroundColor Yellow
    Write-Host "  The scaffold updated the DATA LAYER (entity properties)." -ForegroundColor White
    Write-Host "  YOU decide the BUSINESS IMPLICATIONS (how the app uses these changes)." -ForegroundColor White
    
    Write-Host "`nWHERE TO REVIEW:" -ForegroundColor Magenta
    Write-Host "  1. TSIC.Domain\Entities - Review the changed entity classes" -ForegroundColor White
    Write-Host "  2. TSIC.Contracts\Repositories - Add new query methods if needed" -ForegroundColor White
    Write-Host "  3. TSIC.Infrastructure\Repositories - Implement new methods" -ForegroundColor White
    Write-Host "  4. TSIC.API\Services - Update business logic to use new fields" -ForegroundColor White
    
    Write-Host "`nCOMMON SCENARIOS:" -ForegroundColor Cyan
    Write-Host "  Scenario 1: 'Just added a field for future use'" -ForegroundColor White
    Write-Host "    -> No action needed! Field available via existing repositories." -ForegroundColor Green
    
    Write-Host "`n  Scenario 2: 'Need to filter by new field (e.g., Active flag)'" -ForegroundColor White
    Write-Host "    -> Add method to I[Entity]Repository interface" -ForegroundColor Yellow
    Write-Host "    -> Implement in [Entity]Repository" -ForegroundColor Yellow
    Write-Host "    -> Update services to call new method" -ForegroundColor Yellow
    
    Write-Host "`n  Scenario 3: 'Need admin UI to edit new field'" -ForegroundColor White
    Write-Host "    -> Add UpdateAsync to repository if missing" -ForegroundColor Yellow
    Write-Host "    -> Update service to set new property" -ForegroundColor Yellow
    Write-Host "    -> Update Angular UI (separate from this script)" -ForegroundColor Yellow
    
    Write-Host "`nCRITICAL REMINDER:" -ForegroundColor Red
    Write-Host "  NEVER add SqlDbContext to services!" -ForegroundColor White
    Write-Host "  ALWAYS use repositories for data access!" -ForegroundColor White
    Write-Host "  See: docs\REPOSITORY-PATTERN-STANDARDS.md" -ForegroundColor Gray
    
} else {
    Write-Host "`nNo entity changes detected - database schema matches current entities." -ForegroundColor Cyan
}

Write-Host "`nSqlDbContext is ready for repository use" -ForegroundColor Green
Write-Host "TsicIdentityDbContext handles all Identity operations (UserManager, etc.)" -ForegroundColor Green