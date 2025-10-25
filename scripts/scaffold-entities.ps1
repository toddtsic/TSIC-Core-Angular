# Scaffold entities and context from SQL Server DB-first

# Ensure we're in the correct directory (TSIC-Core-Angular)
Set-Location $PSScriptRoot\..

# Ensure dotnet-ef is up-to-date
Write-Host "Updating dotnet-ef to the latest version..."
dotnet tool update --global dotnet-ef

# Run the scaffold command
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