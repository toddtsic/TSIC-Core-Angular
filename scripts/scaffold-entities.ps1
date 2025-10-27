# Scaffold entities and context from SQL Server DB-first

# Ensure we're in the correct directory (TSIC-Core-Angular)
Set-Location $PSScriptRoot\..

# Ensure dotnet-ef is up-to-date
Write-Host "Updating dotnet-ef to the latest version..." -ForegroundColor Cyan
dotnet tool update --global dotnet-ef

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

# No Identity integration needed - SqlDbContext remains a plain DbContext
# Identity operations are handled by TsicIdentityDbContext

Write-Host "`nScaffolding complete!" -ForegroundColor Green
Write-Host "SqlDbContext is ready for business logic queries" -ForegroundColor Green
Write-Host "Use TsicIdentityDbContext for Identity operations (UserManager, etc.)" -ForegroundColor Green