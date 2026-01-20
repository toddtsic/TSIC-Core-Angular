using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Data.Identity;
using TSIC.Application.Services.Teams;

namespace TSIC.Tests.Integration;

/// <summary>
/// Test factory for integration tests using in-memory database.
/// Provides isolated test environment for each test run.
/// </summary>
public class WebApplicationTestFactory : WebApplicationFactory<Program>
{
    private static readonly string MainDbName = $"TestDb_Main_{Guid.NewGuid()}";
    private static readonly string IdentityDbName = $"TestDb_Identity_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set environment to "Testing" so Program.cs skips SqlServer registration
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Add in-memory databases for testing (shared per test host)
            // Use Scoped lifetime so all scopes within a request share the same context instance
            services.AddDbContext<SqlDbContext>(options =>
                options.UseInMemoryDatabase(MainDbName));

            services.AddDbContext<TsicIdentityDbContext>(options =>
                options.UseInMemoryDatabase(IdentityDbName));

            // Use a lightweight test authentication handler so integration tests can hit authorized endpoints
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build();
            });

            // Override TeamFeeCalculator with test default processing fee
            services.RemoveAll<ITeamFeeCalculator>();
            services.AddScoped<ITeamFeeCalculator>(sp => new TeamFeeCalculator(0.035m));

            // Build service provider and ensure databases are created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var sqlDb = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
            var identityDb = scope.ServiceProvider.GetRequiredService<TsicIdentityDbContext>();

            sqlDb.Database.EnsureCreated();
            identityDb.Database.EnsureCreated();
        });
    }

    private static void SeedTestData(SqlDbContext context)
    {
        // Add basic test data that integration tests depend on
        // This will be expanded as we write tests
    }
}
