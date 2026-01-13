using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;
using Xunit;

namespace TSIC.Tests;

public class VerticalInsureServiceTests
{
    private static SqlDbContext CreateDb(out DbContextOptions<SqlDbContext> opts)
    {
        var name = $"vi-tests-{Guid.NewGuid()}";
        opts = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new SqlDbContext(opts);
    }

    private static VerticalInsureService BuildService(SqlDbContext db, IHostEnvironment env)
    {
        // Use REAL repository implementations - no mocks
        var jobRepo = new JobRepository(db);
        var regRepo = new RegistrationRepository(db);
        var familyRepo = new FamilyRepository(db);

        var logger = new Mock<ILogger<VerticalInsureService>>().Object;
        var teamLookup = new Mock<ITeamLookupService>();
        var teamRepo = new Mock<ITeamRepository>().Object;
        var userRepo = new Mock<IUserRepository>().Object;
        var mockOptions = Options.Create(new VerticalInsureSettings
        {
            DevClientId = "test_GREVHKFHJY87CGWW9RF15JD50W5PPQ7U",
            DevSecret = "test_JtlEEBkFNNybGLyOwCCFUeQq9j3zK9dUEJfJMeyqPMRjMsWfzUk0JRqoHypxofZJqeH5nuK0042Yd5TpXMZOf8yVj9X9YDFi7LW50ADsVXDyzuiiq9HLVopbwaNXwqWI",
            ProdClientId = "live_VJ8O8O81AZQ8MCSKWM98928597WUHSMS",
            ProdSecret = "live_PP6xn8fImrpBNj4YqTU8vlAwaqQ7Q8oSRxcVQkf419saU4OuQVCXQSuP4yUNyBMCwilStIsWDaaZnMlfJ1HqVJPBWydR5qE3yNr4HxBVr7rCYxl4ofgIesZbsAS0TfED"
        });

        return new VerticalInsureService(jobRepo, regRepo, familyRepo, teamRepo, userRepo, env, logger, teamLookup.Object, mockOptions);
    }

    [Fact]
    public async Task PurchasePolicies_Synthesizes_PolicyNumbers()
    {
        using var db = CreateDb(out _);
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");

        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid().ToString();
        // Seed registrations
        for (int i = 0; i < 3; i++)
        {
            db.Registrations.Add(new TSIC.Domain.Entities.Registrations
            {
                RegistrationId = Guid.NewGuid(),
                JobId = jobId,
                FamilyUserId = familyId,
                UserId = Guid.NewGuid().ToString(),
                FeeTotal = 100,
                OwedTotal = 100,
                PaidTotal = 0,
                BActive = true,
                RegistrationTs = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var regs = await db.Registrations.Where(r => r.JobId == jobId && r.FamilyUserId == familyId).ToListAsync();
        var registrationIds = regs.Select(r => r.RegistrationId).ToList();
        var quoteIds = registrationIds.Select(r => $"Q-{r.ToString("N").Substring(0, 6)}").ToList();

        var svc = BuildService(db, env.Object);
        var res = await svc.PurchasePoliciesAsync(jobId, familyId, registrationIds, quoteIds, token: null, card: null, ct: CancellationToken.None);

        res.Success.Should().BeTrue();
        res.Policies.Count.Should().Be(3);
        foreach (var kv in res.Policies)
        {
            kv.Value.Should().StartWith("POL-");
        }

        // Ensure persistence
        var persisted = await db.Registrations.AsNoTracking().Where(r => r.JobId == jobId).ToListAsync();
        // Use List.TrueForAll for analyzer preference (S6603)
        persisted.TrueForAll(r => !string.IsNullOrWhiteSpace(r.RegsaverPolicyId)).Should().BeTrue();
    }

    [Fact]
    public async Task PurchasePolicies_Fails_On_Count_Mismatch()
    {
        using var db = CreateDb(out _);
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");

        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid().ToString();
        db.Registrations.Add(new TSIC.Domain.Entities.Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            FamilyUserId = familyId,
            UserId = Guid.NewGuid().ToString(),
            FeeTotal = 50,
            OwedTotal = 50,
            PaidTotal = 0,
            BActive = true,
            RegistrationTs = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reg = await db.Registrations.SingleAsync();
        var svc = BuildService(db, env.Object);
        var res = await svc.PurchasePoliciesAsync(jobId, familyId, new[] { reg.RegistrationId }, new[] { "Q-ONLY", "Q-EXTRA" }, token: null, card: null, ct: CancellationToken.None);

        res.Success.Should().BeFalse();
        res.Error.Should().NotBeNull();
        res.Error!.Should().ContainEquivalentOf("mismatch");
        (await db.Registrations.AsNoTracking().SingleAsync()).RegsaverPolicyId.Should().BeNull();
    }

    /// <summary>
    /// INTEGRATION TEST: Validates sandbox credentials can authenticate with VerticalInsure API.
    /// Skipped by default - run manually to verify sandbox connectivity.
    /// Requires: VI_DEV_SECRET or VerticalInsure:DevSecret in user-secrets
    /// </summary>
    [Fact(Skip = "Manual integration test - calls real VerticalInsure sandbox API")]
    public async Task RealAPI_Sandbox_Credentials_Authenticate()
    {
        // Load credentials from environment variables or user-secrets ONLY
        var clientId = Environment.GetEnvironmentVariable("VI_DEV_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("VI_DEV_SECRET");

        // Fail fast if credentials not configured
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException(
                "VerticalInsure sandbox credentials not configured. Set environment variables:\n" +
                "  VI_DEV_CLIENT_ID\n" +
                "  VI_DEV_SECRET\n" +
                "Or configure in user-secrets under VerticalInsure:DevClientId and VerticalInsure:DevSecret");
        }

        // Make actual API call to verify authentication
        using var client = new HttpClient { BaseAddress = new Uri("https://api.verticalinsure.com/") };
        var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "v1/purchase/registration-cancellation/batch");
        request.Headers.Add("Authorization", $"Basic {authString}");
        request.Headers.Add("User-Agent", "TSIC.Tests");
        request.Content = new StringContent("{\"quotes\":[],\"payment_method\":{}}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        // We expect either:
        // - 400 Bad Request (credentials work, but request is invalid - that's OK for connectivity test)
        // - 401 Unauthorized (credentials are WRONG - test should FAIL)
        // - Other success/error codes (credentials work)
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "Authentication failed - check sandbox credentials in user-secrets or VI_DEV_SECRET environment variable");

        // If we got here, credentials are valid (even if request was rejected for other reasons)
        (response.StatusCode == HttpStatusCode.BadRequest ||
         response.StatusCode == HttpStatusCode.OK ||
         response.StatusCode == HttpStatusCode.Created ||
         (int)response.StatusCode >= 400).Should().BeTrue(
            $"Expected non-401 response, got {(int)response.StatusCode} {response.StatusCode}");
    }

    /// <summary>
    /// INTEGRATION TEST: Validates production credentials can authenticate with VerticalInsure API.
    /// Skipped by default - run manually to verify production connectivity.
    /// Requires: VI_PROD_SECRET or VerticalInsure:ProdSecret in user-secrets
    /// WARNING: This will hit production API - use with caution
    /// </summary>
    [Fact(Skip = "Manual integration test - calls real VerticalInsure PRODUCTION API")]
    public async Task RealAPI_Production_Credentials_Authenticate()
    {
        // Load credentials from environment variables or user-secrets ONLY
        var clientId = Environment.GetEnvironmentVariable("VI_PROD_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("VI_PROD_SECRET");

        // Fail fast if credentials not configured
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException(
                "VerticalInsure production credentials not configured. Set environment variables:\n" +
                "  VI_PROD_CLIENT_ID\n" +
                "  VI_PROD_SECRET\n" +
                "Or configure in user-secrets under VerticalInsure:ProdClientId and VerticalInsure:ProdSecret");
        }

        // Make actual API call to verify authentication
        using var client = new HttpClient { BaseAddress = new Uri("https://api.verticalinsure.com/") };
        var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "v1/purchase/registration-cancellation/batch");
        request.Headers.Add("Authorization", $"Basic {authString}");
        request.Headers.Add("User-Agent", "TSIC.Tests");
        request.Content = new StringContent("{\"quotes\":[],\"payment_method\":{}}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        // We expect either:
        // - 400 Bad Request (credentials work, but request is invalid - that's OK for connectivity test)
        // - 401 Unauthorized (credentials are WRONG - test should FAIL)
        // - Other success/error codes (credentials work)
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "Authentication failed - check production credentials in user-secrets or VI_PROD_SECRET environment variable");

        // If we got here, credentials are valid (even if request was rejected for other reasons)
        (response.StatusCode == HttpStatusCode.BadRequest ||
         response.StatusCode == HttpStatusCode.OK ||
         response.StatusCode == HttpStatusCode.Created ||
         (int)response.StatusCode >= 400).Should().BeTrue(
            $"Expected non-401 response, got {(int)response.StatusCode} {response.StatusCode}");
    }
}