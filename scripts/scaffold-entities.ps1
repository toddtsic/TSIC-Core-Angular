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

# Fix SqlDbContext to support Identity
Write-Host "`nFixing SqlDbContext for Identity support..." -ForegroundColor Cyan
$contextFile = "TSIC-Core-Angular\src\backend\TSIC.Infrastructure\Data\SqlDbContext\Context\SqlDbContext.cs"

$content = Get-Content $contextFile -Raw

# Add Identity using statements
$content = $content -replace 'using Microsoft\.EntityFrameworkCore;', @'
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
'@

# Change base class from DbContext to IdentityDbContext<IdentityUser>
$content = $content -replace 'public partial class SqlDbContext : DbContext', 'public partial class SqlDbContext : IdentityDbContext<IdentityUser>'

# Add base.OnModelCreating call in OnModelCreating method
$content = $content -replace '(protected override void OnModelCreating\(ModelBuilder modelBuilder\)\s*\{)', '$1`n        base.OnModelCreating(modelBuilder);'

Set-Content -Path $contextFile -Value $content

Write-Host "`nScaffolding complete!" -ForegroundColor Green
Write-Host "SqlDbContext now inherits from IdentityDbContext<IdentityUser>" -ForegroundColor Green
Write-Host "Added base.OnModelCreating(modelBuilder) call" -ForegroundColor Green