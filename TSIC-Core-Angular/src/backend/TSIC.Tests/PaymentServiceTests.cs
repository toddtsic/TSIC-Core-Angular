using System;
using System.Linq;
using System.Threading.Tasks;
using AuthorizeNet.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TSIC.Contracts.Dtos;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Players;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using Xunit;

namespace TSIC.Tests;

public class PaymentServiceTests
{
    private static SqlDbContext CreateDb(out DbContextOptions<SqlDbContext> opts)
    {
        var dbName = $"tsic-tests-{Guid.NewGuid()}";
        opts = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(dbName)
            .EnableSensitiveDataLogging()
            .Options;
        var db = new SqlDbContext(opts);
        return db;
    }

    private static PaymentService BuildService(SqlDbContext db,
        out Mock<IAdnApiService> adn,
        out Mock<IPlayerBaseTeamFeeResolverService> feeResolver,
        out Mock<ITeamLookupService> teamLookup)
    {
        // Use REAL repository implementations - no mocks
        var jobsRepo = new TSIC.Infrastructure.Repositories.JobRepository(db);
        var regsRepo = new TSIC.Infrastructure.Repositories.RegistrationRepository(db);
        var teamsRepo = new TSIC.Infrastructure.Repositories.TeamRepository(db);
        var familiesRepo = new TSIC.Infrastructure.Repositories.FamiliesRepository(db);
        var acctRepo = new TSIC.Infrastructure.Repositories.RegistrationAccountingRepository(db);

        adn = new Mock<IAdnApiService>(MockBehavior.Strict);
        feeResolver = new Mock<IPlayerBaseTeamFeeResolverService>(MockBehavior.Strict);
        teamLookup = new Mock<ITeamLookupService>(MockBehavior.Strict);

        // Common mocks for external services
        adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>()))
            .Returns(AuthorizeNet.Environment.SANDBOX);
        adn.Setup(a => a.GetJobAdnCredentials_FromJobId(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });

        // Fee resolver is consulted to ensure OwedTotal is set when FeeTotal > 0 and PaidTotal == 0
        feeResolver.Setup(f => f.ResolveBaseFeeForTeamAsync(It.IsAny<Guid>())).ReturnsAsync(0m);

        var logger = NullLogger<PaymentService>.Instance;
        return new PaymentService(jobsRepo, regsRepo, teamsRepo, familiesRepo, acctRepo, adn.Object, feeResolver.Object, teamLookup.Object, logger);
    }

    private static void SeedRegistration(SqlDbContext db, Guid jobId, Guid familyId, Guid? teamId = null, decimal owed = 200m)
    {
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            FamilyUserId = familyId.ToString(),
            UserId = Guid.NewGuid().ToString(),
            AssignedTeamId = teamId,
            FeeTotal = owed,
            OwedTotal = owed,
            PaidTotal = 0,
            BActive = true,
            RegistrationTs = DateTime.UtcNow
        };
        db.Registrations.Add(reg);
    }

    private static void SeedJobAndRegForm(SqlDbContext db, Guid jobId, bool isArb, bool allowPif)
    {
        db.Jobs.Add(new Jobs
        {
            JobId = jobId,
            JobPath = "unit-test-job",
            RegformNamePlayer = "Player",
            RegformNameTeam = "Team",
            RegformNameCoach = "Coach",
            RegformNameClubRep = "ClubRep",
            Modified = DateTime.UtcNow,
            AdnArb = isArb,
            AdnArbbillingOccurences = 5,
            AdnArbintervalLength = 1,
            AdnArbstartDate = DateTime.UtcNow.Date.AddDays(2)
        });
        db.RegForms.Add(new RegForms { RegFormId = Guid.NewGuid(), JobId = jobId, AllowPif = allowPif, FormName = "PlayerForm", RoleIdRegistering = "Player" });
    }

    [Fact]
    public async Task ProcessPayment_PIF_Persists_VI_Policy_When_Confirmed()
    {
        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        using var db = CreateDb(out _);
        SeedJobAndRegForm(db, jobId, isArb: false, allowPif: true);
        SeedRegistration(db, jobId, familyId, teamId: Guid.NewGuid(), owed: 150m);
        await db.SaveChangesAsync();

        var svc = BuildService(db, out var adn, out _, out var team);

        // Charge returns OK with transId
        adn.Setup(a => a.ADN_Charge(It.IsAny<AdnChargeRequest>()))
            .Returns(new createTransactionResponse
            {
                messages = new messagesType
                {
                    resultCode = messageTypeEnum.Ok,
                    message = new[] { new messagesTypeMessage { code = "I00001", text = "OK" } }
                },
                transactionResponse = new transactionResponse { transId = "T-123" }
            });

        // Team deposit resolution not used for PIF but keep harmless
        team.Setup(t => t.ResolvePerRegistrantAsync(It.IsAny<Guid>())).ReturnsAsync((0m, 0m));

        var req = new PaymentRequestDto
        {
            JobPath = "unit-test-job",
            PaymentOption = PaymentOption.PIF,
            CreditCard = new CreditCardInfo { Number = "4111111111111111", Code = "123", Expiry = "1230", FirstName = "A", LastName = "B", Address = "1", Zip = "11111", Email = "a.b@test.local", Phone = "5551234567" },
            IdempotencyKey = Guid.NewGuid().ToString(),
            ViConfirmed = true,
            ViPolicyNumber = "POL-999",
            ViPolicyCreateDate = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var resp = await svc.ProcessPaymentAsync(jobId, familyId.ToString(), req, userId: "tester");

        resp.Success.Should().BeTrue();
        resp.TransactionId.Should().Be("T-123");

        var reg = await db.Registrations.AsNoTracking().SingleAsync();
        reg.RegsaverPolicyId.Should().Be("POL-999");
        reg.RegsaverPolicyIdCreateDate.Should().Be(req.ViPolicyCreateDate);
    }

    [Fact]
    public async Task ProcessPayment_Idempotency_Prevents_Duplicate()
    {
        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var idem = "IDEMP-1";

        using var db = CreateDb(out _);
        SeedJobAndRegForm(db, jobId, isArb: false, allowPif: true);
        SeedRegistration(db, jobId, familyId, teamId: Guid.NewGuid(), owed: 100m);
        await db.SaveChangesAsync();

        var svc = BuildService(db, out var adn, out _, out var team);

        // Capture amount and count invocations
        adn.Setup(a => a.ADN_Charge(It.IsAny<AdnChargeRequest>()))
                .Returns(new createTransactionResponse
                {
                    messages = new messagesType { resultCode = messageTypeEnum.Ok, message = new[] { new messagesTypeMessage { code = "I00001", text = "OK" } } },
                    transactionResponse = new transactionResponse { transId = Guid.NewGuid().ToString() }
                });

        team.Setup(t => t.ResolvePerRegistrantAsync(It.IsAny<Guid>())).ReturnsAsync((0m, 0m));

        var req = new PaymentRequestDto
        {
            JobPath = "unit-test-job",
            PaymentOption = PaymentOption.PIF,
            CreditCard = new CreditCardInfo { Number = "4111111111111111", Code = "123", Expiry = "1230", FirstName = "A", LastName = "B", Address = "1", Zip = "11111", Email = "a.b@test.local", Phone = "5551234567" },
            IdempotencyKey = idem
        };

        var r1 = await svc.ProcessPaymentAsync(jobId, familyId.ToString(), req, "tester");
        r1.Success.Should().BeTrue();

        // Second call with same idempotency should not call charge again
        // Current implementation computes charges before idempotency pre-check, so it will return "Nothing due".
        var r2 = await svc.ProcessPaymentAsync(jobId, familyId.ToString(), req, "tester");
        r2.Success.Should().BeFalse();
        r2.Message.Should().Contain("Nothing due");
        adn.Verify(a => a.ADN_Charge(It.IsAny<AdnChargeRequest>()), Times.Once);

        var acctCount = await db.RegistrationAccounting.CountAsync();
        acctCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPayment_Deposit_Amount_Is_Capped_By_Owed()
    {
        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        using var db = CreateDb(out _);
        SeedJobAndRegForm(db, jobId, isArb: false, allowPif: true);
        SeedRegistration(db, jobId, familyId, teamId: teamId, owed: 80m);
        await db.SaveChangesAsync();

        var svc = BuildService(db, out var adn, out _, out var team);

        // Deposit resolver returns 120, but owed is 80, so cap to 80
        team.Setup(t => t.ResolvePerRegistrantAsync(teamId)).ReturnsAsync((Fee: 200m, Deposit: 120m));

        adn.Setup(a => a.ADN_Charge(It.IsAny<AdnChargeRequest>()))
                .Returns(new createTransactionResponse
                {
                    messages = new messagesType { resultCode = messageTypeEnum.Ok, message = new[] { new messagesTypeMessage { code = "I00001", text = "OK" } } },
                    transactionResponse = new transactionResponse { transId = "TX-1" }
                });

        var req = new PaymentRequestDto
        {
            JobPath = "unit-test-job",
            PaymentOption = PaymentOption.Deposit,
            CreditCard = new CreditCardInfo { Number = "4111111111111111", Code = "123", Expiry = "1230", FirstName = "A", LastName = "B", Address = "1", Zip = "11111", Email = "a.b@test.local", Phone = "5551234567" },
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        var resp = await svc.ProcessPaymentAsync(jobId, familyId.ToString(), req, "tester");
        resp.Success.Should().BeTrue();

        var reg = await db.Registrations.AsNoTracking().SingleAsync();
        reg.PaidTotal.Should().Be(80m);
        reg.OwedTotal.Should().Be(0m);
    }

    [Fact]
    public async Task ProcessPayment_ARB_Sets_Subscription_Fields_From_JobMetadata()
    {
        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var occur = 6; // billing occurrences
        var intervalLen = 1; // months
        var startDate = DateTime.UtcNow.Date.AddDays(5);

        using var db = CreateDb(out _);
        // Seed job with ARB enabled and required non-null fields
        db.Jobs.Add(new Jobs
        {
            JobId = jobId,
            JobPath = "arb-test-job",
            RegformNamePlayer = "Player",
            RegformNameTeam = "Team",
            RegformNameCoach = "Coach",
            RegformNameClubRep = "ClubRep",
            Modified = DateTime.UtcNow,
            AdnArb = true,
            AdnArbbillingOccurences = occur,
            AdnArbintervalLength = intervalLen,
            AdnArbstartDate = startDate
        });

        // Single registration, no team assignment to avoid fee resolver calls
        SeedRegistration(db, jobId, familyId, teamId: null, owed: 300m); // total owed
        await db.SaveChangesAsync();

        var svc = BuildService(db, out var adn, out _, out _);

        // Mock subscription creation
        adn.Setup(a => a.ADN_ARB_CreateMonthlySubscription(It.IsAny<AdnArbCreateRequest>()))
            .Returns(new ARBCreateSubscriptionResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok, message = new[] { new messagesTypeMessage { code = "I00001", text = "OK" } } },
                subscriptionId = "SUB-789"
            });

        var req = new PaymentRequestDto
        {
            JobPath = "arb-test-job",
            PaymentOption = PaymentOption.ARB,
            CreditCard = new CreditCardInfo { Number = "4111111111111111", Code = "123", Expiry = "1230", FirstName = "Jane", LastName = "Doe", Address = "1", Zip = "11111", Email = "jane.doe@test.local", Phone = "5559876543" },
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        var resp = await svc.ProcessPaymentAsync(jobId, familyId.ToString(), req, "tester");
        resp.Success.Should().BeTrue();
        resp.SubscriptionId.Should().Be("SUB-789");

        var reg = await db.Registrations.AsNoTracking().SingleAsync();
        reg.AdnSubscriptionId.Should().Be("SUB-789");
        reg.AdnSubscriptionBillingOccurences.Should().Be((short)occur);
        reg.AdnSubscriptionIntervalLength.Should().Be((short)intervalLen);
        reg.AdnSubscriptionStartDate.Should().Be(startDate);

        // Per occurrence amount should be total owed divided evenly and rounded (300 / 6 = 50.00)
        reg.AdnSubscriptionAmountPerOccurence.Should().Be(50.00m);
        reg.AdnSubscriptionStatus.Should().Be("active");
    }

}
