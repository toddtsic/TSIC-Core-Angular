using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Integration;

/// <summary>
/// Test factory for integration tests using in-memory database.
/// Provides isolated test environment for each test run.
/// </summary>
public class WebApplicationTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real database registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<SqlDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database for testing
            services.AddDbContext<SqlDbContext>(options =>
            {
                options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}");
            });

            // Build service provider and ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var db = scopedServices.GetRequiredService<SqlDbContext>();
            
            db.Database.EnsureCreated();
            
            // Seed test data if needed
            SeedTestData(db);
        });
    }

    private static void SeedTestData(SqlDbContext context)
    {
        // Add basic test data that integration tests depend on
        // This will be expanded as we write tests
    }
}
