using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Dtos; // CreditCardInfo
using TSIC.API.Services;
using TSIC.API.Dtos.VerticalInsure;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using Xunit;

namespace TSIC.Tests;

public class VerticalInsureServiceCardTokenTests
{
    private static SqlDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<SqlDbContext>().UseInMemoryDatabase($"vi-ct-{Guid.NewGuid()}").Options;
        return new SqlDbContext(opts);
    }

    private static Registrations SeedSingleRegistration(SqlDbContext db, Guid jobId, string familyId)
    {
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            FamilyUserId = familyId,
            UserId = Guid.NewGuid().ToString(),
            FeeTotal = 75,
            OwedTotal = 75,
            PaidTotal = 0,
            BActive = true,
            RegistrationTs = DateTime.UtcNow
        };
        db.Registrations.Add(reg);
        db.SaveChanges();
        return reg;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Responder(request));
        }
    }

    private static VerticalInsureService BuildService(SqlDbContext db, IHostEnvironment env, HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        factory.Setup(f => f.CreateClient("verticalinsure")).Returns(client);
        var logger = new Mock<ILogger<VerticalInsureService>>().Object;
        var teamLookup = new Mock<ITeamLookupService>();
        teamLookup.Setup(t => t.ResolvePerRegistrantAsync(It.IsAny<Guid>())).ReturnsAsync((Fee: 50m, Deposit: 0m));
        return new VerticalInsureService(db, env, logger, teamLookup.Object, factory.Object);
    }

    [Fact]
    public async Task Purchase_Uses_StripeToken_When_Provided()
    {
        using var db = CreateDb();
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");
        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid().ToString();
        var reg = SeedSingleRegistration(db, jobId, familyId);
        var handler = new CaptureHandler();
        handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(JsonSerializer.Serialize(new[] { new VIMakePlayerPaymentResponseDto { policy_status = "ACTIVE", policy_number = "POL-123", metadata = new VIPlayerMetadataDto { TsicRegistrationId = reg.RegistrationId } } }), Encoding.UTF8, "application/json")
        };
        var svc = BuildService(db, env.Object, handler);
        var res = await svc.PurchasePoliciesAsync(jobId, familyId, new[] { reg.RegistrationId }, new[] { "Q-1" }, token: "tok_abc", card: null, ct: CancellationToken.None);
        res.Success.Should().BeTrue();
        handler.LastRequest.Should().NotBeNull();
        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        body.Should().Contain("stripe:tok_abc");
        // Card object may still serialize (empty defaults); ensure no real card number present.
        body.Should().NotContain("411111");
    }

    [Fact]
    public async Task Purchase_Uses_Card_When_No_Token()
    {
        using var db = CreateDb();
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");
        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid().ToString();
        var reg = SeedSingleRegistration(db, jobId, familyId);
        var handler = new CaptureHandler();
        handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new[] { new VIMakePlayerPaymentResponseDto { policy_status = "ACTIVE", policy_number = "POL-456", metadata = new VIPlayerMetadataDto { TsicRegistrationId = reg.RegistrationId } } }), Encoding.UTF8, "application/json")
        };
        var svc = BuildService(db, env.Object, handler);
        var card = new CreditCardInfo { Number = "4111111111111111", Expiry = "0129", Code = "123", FirstName = "A", LastName = "B", Zip = "99999", Email = "a.b@test.local", Phone = "5551234567" };
        var res = await svc.PurchasePoliciesAsync(jobId, familyId, new[] { reg.RegistrationId }, new[] { "Q-1" }, token: null, card: card, ct: CancellationToken.None);
        res.Success.Should().BeTrue();
        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        body.Should().Contain("\"card\"");
        body.Should().Contain("\"month\":\"01\"");
        body.Should().Contain("\"year\":\"2029\"");
        body.Should().NotContain("stripe:tok_");
    }

    [Fact]
    public async Task Purchase_Failure_Does_Not_Persist()
    {
        using var db = CreateDb();
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");
        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid().ToString();
        var reg = SeedSingleRegistration(db, jobId, familyId);
        var handler = new CaptureHandler();
        handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{ }", Encoding.UTF8, "application/json")
        };
        var svc = BuildService(db, env.Object, handler);
        var res = await svc.PurchasePoliciesAsync(jobId, familyId, new[] { reg.RegistrationId }, new[] { "Q-1" }, token: "tok_bad", card: null, ct: CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Policies.Should().BeEmpty();
        (await db.Registrations.AsNoTracking().SingleAsync()).RegsaverPolicyId.Should().BeNull();
    }
}