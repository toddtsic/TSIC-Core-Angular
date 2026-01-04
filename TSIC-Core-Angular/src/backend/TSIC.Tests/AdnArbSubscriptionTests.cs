using Microsoft.EntityFrameworkCore;
using System;
using AuthorizeNet.Api.Contracts.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Moq;
using TSIC.API.Configuration;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Dtos;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;

namespace TSIC.Tests;

public sealed class AdnArbSubscriptionTests
{
    private sealed class DevEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "TSIC.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }

    [Fact]
    public void ARB_CreateSubscription_Sandbox_ReturnsResponse()
    {
        // Load credentials from environment variables (no hardcoded fallbacks for security)
        var login = Environment.GetEnvironmentVariable("ADN_SANDBOX_LOGINID");
        var key = Environment.GetEnvironmentVariable("ADN_SANDBOX_TRANSACTIONKEY");

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Authorize.Net sandbox credentials not configured. " +
                "Set ADN_SANDBOX_LOGINID and ADN_SANDBOX_TRANSACTIONKEY environment variables.");
        }

        var inMemory = new List<KeyValuePair<string, string?>>
        {
            new("AuthorizeNet:SandboxLoginId", login),
            new("AuthorizeNet:SandboxTransactionKey", key)
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        // Wrap configuration in IOptions<AdnSettings>
        var settings = new AdnSettings
        {
            SandboxLoginId = login,
            SandboxTransactionKey = key
        };
        var options = Options.Create(settings);

        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<AdnApiService>();

        // Use REAL CustomerRepository with in-memory database
        var opts = new DbContextOptionsBuilder<TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext>()
            .UseInMemoryDatabase($"adn-arb-test-{Guid.NewGuid()}")
            .Options;
        var db = new TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext(opts);
        var customerRepo = new TSIC.Infrastructure.Repositories.CustomerRepository(db);

        var service = new AdnApiService(new DevEnv(), logger, options, customerRepo);
        var env = service.GetADNEnvironment(); // should be SANDBOX under Development

        // Use test VISA; service maps 4242.. -> 4111.. for sandbox
        var request = new AdnArbCreateRequest(
            Env: env,
            LoginId: login,
            TransactionKey: key,
            CardNumber: "4242424242424242",
            CardCode: "123",
            Expiry: "0627",
            FirstName: "Test",
            LastName: "User",
            Address: "100 Test Lane",
            Zip: "85712",
            Email: "test@example.com",
            Phone: "5551234567",
            InvoiceNumber: "1_2_3", // pattern customerAI_jobAI_registrationAI demo
            Description: "Registration Payment",
            PerIntervalCharge: 10.00m,
            StartDate: DateTime.UtcNow.AddDays(1),
            BillingOccurrences: 2,
            IntervalLength: 1
        );

        // Act
        var result = service.ADN_ARB_CreateMonthlySubscription_Result(request);

        // Assert (do not enforce success in case sandbox credentials differ; ensure a structured response)
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.GatewayMessage));
    }
}
