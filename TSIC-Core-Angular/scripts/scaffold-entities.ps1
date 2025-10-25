# Update dotnet-ef tool
dotnet tool update --global dotnet-ef

# Scaffold entities and context
dotnet ef dbcontext scaffold "Server=.\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True;" Microsoft.EntityFrameworkCore.SqlServer --project src\backend\TSIC.Infrastructure\TSIC.Infrastructure.csproj --output-dir ..\..\backend\TSIC.Domain\Entities --context-dir Data\SqlDbContext\Context --namespace TSIC.Domain.Entities --context-namespace TSIC.Infrastructure.Data.SqlDbContext --context SqlDbContext --no-build --force --no-onconfiguring --no-pluralize