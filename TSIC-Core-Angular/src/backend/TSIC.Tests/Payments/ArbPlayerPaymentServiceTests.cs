using AuthorizeNet.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Payments;

/// <summary>
/// Tests for the player-facing ARB (recurring billing) path on PaymentService
/// (ProcessPaymentAsync with PaymentOption.ARB → ProcessArbAsync).
///
/// Core regression: every fee modifier (discount code, early-bird, scholarship, late fee,
/// prior payment) nets into OwedTotal via FeeMath before ARB reads it. When that leaves
/// nothing to finance, ARB must NOT create a $0 recurring subscription (Authorize.Net rejects
/// it, which left the reg bActive=0 with no subscription id) — it must activate the registrant
/// directly, mirroring the "owes nothing -> active" rule used by the discount endpoint and the
/// immediate-charge path. A positive balance still creates the subscription and activates.
/// </summary>
public class ArbPlayerPaymentServiceTests
{
    private const string FamilyUserId = "family-1";
    private const string ActingUserId = "user-1";

    private readonly Mock<IJobRepository> _jobs = new();
    private readonly Mock<IRegistrationRepository> _regRepo = new();
    private readonly Mock<ITeamRepository> _teams = new();
    private readonly Mock<IFamiliesRepository> _families = new();
    private readonly Mock<IRegistrationAccountingRepository> _acct = new();
    private readonly Mock<IAdnApiService> _adn = new();
    private readonly Mock<IFeeResolutionService> _feeService = new();
    private readonly Mock<ITeamLookupService> _teamLookup = new();
    private readonly Mock<IRegistrationFeeAdjustmentService> _feeAdj = new();
    private readonly Mock<IEcheckSettlementRepository> _settleRepo = new();
    private readonly Mock<IPaymentStateService> _paymentState = new();
    private readonly Mock<ILogger<PaymentService>> _logger = new();

    private PaymentService BuildSut() => new(
        _jobs.Object, _regRepo.Object, _teams.Object, _families.Object, _acct.Object,
        _adn.Object, _feeService.Object, _teamLookup.Object, _feeAdj.Object, _settleRepo.Object,
        _logger.Object, _paymentState.Object);

    private void StubJobAndCreds(Guid jobId, bool adnArb = true, int occurrences = 10)
    {
        _jobs.Setup(j => j.GetJobPaymentInfoAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPaymentInfo
            {
                AdnArb = adnArb,
                AdnArbbillingOccurences = occurrences,
                AdnArbintervalLength = 1,
                AdnArbstartDate = null
            });
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(jobId, It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.SANDBOX);
        _adn.Setup(a => a.ADN_VerifyCardWithPennyAuth(It.IsAny<AdnAuthorizeRequest>()))
            .Returns(new AdnPennyVerifyResult { Success = true });
    }

    private void StubRegs(Guid jobId, params Registrations[] regs)
    {
        _regRepo.Setup(r => r.GetByJobAndFamilyWithUsersAsync(jobId, FamilyUserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regs.ToList());
    }

    private void StubArbCreateSuccess(string subscriptionId)
    {
        _adn.Setup(a => a.ADN_ARB_CreateMonthlySubscription(It.IsAny<AdnArbCreateRequest>()))
            .Returns(new ARBCreateSubscriptionResponse
            {
                subscriptionId = subscriptionId,
                messages = new messagesType { resultCode = messageTypeEnum.Ok }
            });
    }

    // AssignedTeamId left null so fee normalization and the deposit-scenario probe short-circuit
    // without external fee/team lookups — the test isolates the ARB basis -> per-occurrence logic.
    private static Registrations Reg(Guid jobId, decimal owed, decimal feeProcessing = 0m) => new()
    {
        RegistrationId = Guid.NewGuid(),
        JobId = jobId,
        UserId = Guid.NewGuid().ToString(),
        FamilyUserId = FamilyUserId,
        FeeBase = owed,
        FeeProcessing = feeProcessing,
        FeeTotal = owed + feeProcessing,
        OwedTotal = owed + feeProcessing,
        PaidTotal = 0m,
        BActive = false, // review-tab state: written inactive, awaiting payment outcome
        Modified = DateTime.UtcNow
    };

    private static CreditCardInfo ValidCard() => new()
    {
        Number = "4111111111111111",
        Expiry = "1230",
        Code = "123",
        FirstName = "Jane",
        LastName = "Doe",
        Address = "123 Main",
        Zip = "10001",
        Email = "jane@example.com",
        Phone = "5555551212"
    };

    private static PaymentRequestDto Req() => new()
    {
        JobPath = "test-job",
        PaymentOption = PaymentOption.ARB,
        CreditCard = ValidCard()
    };

    [Fact]
    public async Task ZeroBasis_afterModifiers_activatesWithoutCreatingSubscription()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 0m); // a discount/early-bird/etc. fully covered the balance
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        var sut = BuildSut();

        var result = await sut.ProcessPaymentAsync(jobId, FamilyUserId, Req(), ActingUserId);

        result.Success.Should().BeTrue();
        reg.BActive.Should().BeTrue("a registrant who owes nothing must be activated");
        reg.AdnSubscriptionId.Should().BeNull("no recurring subscription is created when nothing is owed");
        _adn.Verify(a => a.ADN_ARB_CreateMonthlySubscription(It.IsAny<AdnArbCreateRequest>()), Times.Never);
    }

    [Fact]
    public async Task PositiveBasis_createsSubscriptionAndActivates()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 100m);
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        StubArbCreateSuccess("SUB-1");
        var sut = BuildSut();

        var result = await sut.ProcessPaymentAsync(jobId, FamilyUserId, Req(), ActingUserId);

        result.Success.Should().BeTrue();
        reg.BActive.Should().BeTrue();
        reg.AdnSubscriptionId.Should().Be("SUB-1");
        reg.AdnSubscriptionStatus.Should().Be("active");
        _adn.Verify(a => a.ADN_ARB_CreateMonthlySubscription(It.IsAny<AdnArbCreateRequest>()), Times.Once);
    }
}
