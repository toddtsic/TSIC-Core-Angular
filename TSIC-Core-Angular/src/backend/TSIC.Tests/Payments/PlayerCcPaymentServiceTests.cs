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
/// End-to-end tests for the parent self-pay CC path on
/// <see cref="PaymentService.ProcessPaymentAsync"/> — covers orchestration
/// the engine-direct PlayerCcChargeTests cannot reach (NormalizeFees →
/// UpgradeRegistrationsToPif → ComputeCharges → engine → caller-side state).
///
/// Regression: go-live 003 Issue 1 — a declined PIF CC charge previously
/// left the registration permanently in PIF posture (e.g. $500 deposit →
/// $1900 PIF owed with $0 paid) because the engine's pre-gateway
/// SaveChangesAsync flushed the PIF mutations from the shared scoped
/// DbContext before the gateway hit. The snapshot is now captured BEFORE
/// UpgradeRegistrationsToPifAsync mutates the tracked entities, and
/// ExecutePrimaryChargeAsync restores those four fields on engine failure.
/// </summary>
public class PlayerCcPaymentServiceTests
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

    private PaymentService BuildSut()
    {
        _paymentState.Setup(p => p.ForRegistrationsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PaymentState>());
        _paymentState.Setup(p => p.ForRegistrationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentState.Empty(bAddProcessingFees: false, ccRate: 0m, echeckRate: 0m));
        // Default job rate context (proc off). The deposit charge engine reads this to gross the
        // deposit by the CC rate; proc-on tests override it after BuildSut.
        _paymentState.Setup(p => p.ForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentState.Empty(bAddProcessingFees: false, ccRate: 0m, echeckRate: 0m));
        // Agegroup resolves through the team now — echo a team carrying an agegroup for the reg's AssignedTeamId.
        _teams.Setup(t => t.GetTeamFromTeamId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid teamId, CancellationToken _) => new Teams { TeamId = teamId, AgegroupId = Guid.NewGuid() });
        return new PaymentService(
            _jobs.Object, _regRepo.Object, _teams.Object, _families.Object, _acct.Object,
            _adn.Object, _feeService.Object, _teamLookup.Object, _feeAdj.Object, _settleRepo.Object,
            _logger.Object, _paymentState.Object);
    }

    private void StubJobAndCreds(Guid jobId, bool allowPif = true)
    {
        _jobs.Setup(j => j.GetJobPaymentInfoAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPaymentInfo { AllowPif = allowPif });
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(jobId, It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.SANDBOX);
        _regRepo.Setup(r => r.GetRegistrationWithInvoiceDataAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationWithInvoiceData { CustomerAi = 5, JobAi = 100, RegistrationAi = 200 });
    }

    private void StubRegs(Guid jobId, params Registrations[] regs)
    {
        _regRepo.Setup(r => r.GetByJobAndFamilyWithUsersAsync(jobId, FamilyUserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regs.ToList());
        var byIds = regs.ToDictionary(r => r.RegistrationId);
        _regRepo.Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid> ids, CancellationToken _) =>
                ids.Where(byIds.ContainsKey).Select(id => byIds[id]).ToList());
    }

    /// <summary>
    /// Stub <c>ApplyPifUpgradeAsync</c> to actually mutate the tracked reg's
    /// FeeBase/FeeProcessing/FeeTotal/OwedTotal to <paramref name="newOwed"/>,
    /// so the snapshot/restore behaviour is observable.
    /// </summary>
    private void StubPifUpgrade(decimal newOwed)
    {
        _feeService.Setup(f => f.ApplyPifUpgradeAsync(
                It.IsAny<Registrations>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<FeeApplicationContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Registrations, Guid, Guid, Guid, FeeApplicationContext, CancellationToken>((reg, _, _, _, _, _) =>
            {
                reg.FeeBase = newOwed;
                reg.FeeProcessing = 0m;
                reg.FeeTotal = newOwed;
                reg.OwedTotal = newOwed;
            })
            .Returns(Task.CompletedTask);
    }

    private void StubAdnDecline(string errorText = "Card declined")
    {
        _adn.Setup(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()))
            .Returns(new AdnTxnResult { Success = false, ResponseCode = "2", GatewayCode = "2", MessageForUser = errorText });
    }

    private static Registrations Reg(Guid jobId, decimal owed) =>
        new()
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            FamilyUserId = FamilyUserId,
            AssignedTeamId = Guid.NewGuid(),
            FeeBase = owed,
            FeeProcessing = 0m,
            FeeDiscount = 0m,
            FeeLatefee = 0m,
            FeeTotal = owed,
            OwedTotal = owed,
            PaidTotal = 0m,
            BActive = false, // mirrors PlayerRegistrationService creation default
        };

    private void StubAdnSuccess(string transId = "txn-success")
    {
        _adn.Setup(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()))
            .Returns(new AdnTxnResult { Success = true, TransactionId = transId, ResponseCode = "1", MessageForUser = "Approved" });
    }

    private static CreditCardInfo ValidCard() => new()
    {
        Number = "4111111111111111",
        Code = "123",
        Expiry = "1230",
        FirstName = "Pat",
        LastName = "Parent",
        Address = "123 Main",
        Zip = "10001",
        Email = "pat@home.test",
        Phone = "5555551212"
    };

    private static PaymentRequestDto PifReq() => new()
    {
        JobPath = "test-job",
        PaymentOption = PaymentOption.PIF,
        CreditCard = ValidCard()
    };

    [Fact(DisplayName = "CC success → BActive flips true (003 Issue 5)")]
    public async Task CcSuccess_FlipsBActiveTrue()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 250m);
        reg.BActive = false; // Pre-payment state from PlayerRegistrationService:459/495
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        // No PIF upgrade — paying the deposit straight via PIF.
        StubAdnSuccess();
        var sut = BuildSut();

        var result = await sut.ProcessPaymentAsync(jobId, FamilyUserId, PifReq(), ActingUserId);

        result.Success.Should().BeTrue();
        // Pre-refactor (UpdateRegistrationsForCharge) set BActive=true on parent CC
        // success. The canonical engine success branch must do the same — without it,
        // every "active registration" query in the system filters the paid player out.
        reg.BActive.Should().BeTrue("paid CC registration must be active");
        reg.PaidTotal.Should().Be(250m);
        reg.OwedTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "Promise guard: ExpectedTotal below the server charge → AMOUNT_CHANGED, no gateway hit")]
    public async Task ExpectedTotalMismatch_RefusesBeforeCharge()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 250m); // server will charge OwedTotal = 250
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        StubAdnSuccess(); // stubbed but must never be reached
        var sut = BuildSut();

        // The screen claimed $200 but the server's authoritative charge is $250 (e.g. a dropped
        // proc fee, the very class of bug this guard exists for). Refuse rather than bill $250.
        var req = new PaymentRequestDto
        {
            JobPath = "test-job",
            PaymentOption = PaymentOption.PIF,
            CreditCard = ValidCard(),
            ExpectedTotal = 200m
        };

        var result = await sut.ProcessPaymentAsync(jobId, FamilyUserId, req, ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("AMOUNT_CHANGED");
        _adn.Verify(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()), Times.Never,
            "no card may be charged when the amount disagrees with what the screen promised");
        reg.PaidTotal.Should().Be(0m);
        reg.BActive.Should().BeFalse();
    }

    [Fact(DisplayName = "Promise guard: ExpectedTotal matching the server charge → proceeds normally")]
    public async Task ExpectedTotalMatch_ChargesNormally()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 250m);
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        decimal charged = 0m;
        _adn.Setup(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()))
            .Callback<AdnChargeRequest>(r => charged = r.Amount)
            .Returns(new AdnTxnResult { Success = true, TransactionId = "txn", ResponseCode = "1", MessageForUser = "Approved" });
        var sut = BuildSut();

        var req = new PaymentRequestDto
        {
            JobPath = "test-job",
            PaymentOption = PaymentOption.PIF,
            CreditCard = ValidCard(),
            ExpectedTotal = 250m // matches OwedTotal
        };

        var result = await sut.ProcessPaymentAsync(jobId, FamilyUserId, req, ActingUserId);

        result.Success.Should().BeTrue();
        charged.Should().Be(250m);
        reg.PaidTotal.Should().Be(250m);
    }

    private PaymentRequestDto DepositReq(decimal donation) => new()
    {
        JobPath = "test-job",
        PaymentOption = PaymentOption.Deposit,
        CreditCard = ValidCard(),
        Donation = donation
    };

    /// <summary>
    /// Stub <c>RecomputeRegistrationFinancialsAsync</c> to simulate the canonical deposit-path
    /// recompute the orchestration relies on: proc re-levied on FeeBase + the (already-stamped)
    /// donation, pushing the proc-inclusive gift into OwedTotal. The real proc math lives in
    /// PaymentStateTests; here we only need OwedTotal to move by the gift so the orchestration
    /// (donationGross add + the AMOUNT_MISMATCH tripwire) is observable.
    /// </summary>
    private void StubDepositDonationRecompute(decimal newFeeProcessing, decimal newOwed)
    {
        _feeService.Setup(f => f.RecomputeRegistrationFinancialsAsync(
                It.IsAny<Registrations>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Registrations, Guid, CancellationToken>((reg, _, _) =>
            {
                reg.FeeProcessing = newFeeProcessing;
                reg.FeeTotal = reg.FeeBase + newFeeProcessing - reg.FeeDiscount + reg.FeeLatefee + reg.FeeDonation;
                reg.OwedTotal = newOwed;
            })
            .Returns(Task.CompletedTask);
    }

    [Fact(DisplayName = "Deposit + donation → charges deposit + gift (principal + proc), stamps FeeDonation on primary")]
    public async Task DepositDonation_ChargesDepositPlusGift()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 100m);
        reg.FeeProcessing = 3m; reg.FeeTotal = 103m; reg.OwedTotal = 103m; // proc-enabled deposit-phase posture
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        _teamLookup.Setup(t => t.ResolvePerRegistrantAsync(It.IsAny<Guid>())).ReturnsAsync((100m, 100m)); // (fee, deposit)
        StubDepositDonationRecompute(newFeeProcessing: 3.75m, newOwed: 128.75m); // proc on (100 + 25 gift); +25 gift
        decimal charged = 0m;
        _adn.Setup(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()))
            .Callback<AdnChargeRequest>(r => charged = r.Amount)
            .Returns(new AdnTxnResult { Success = true, TransactionId = "txn", ResponseCode = "1", MessageForUser = "Approved" });
        var sut = BuildSut();
        // Proc-enabled job: 3% CC rate. The deposit charge grosses the $100 deposit by its own
        // proc ($3), matching the OwedTotal the payment screen shows.
        _paymentState.Setup(p => p.ForJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentState.Empty(bAddProcessingFees: true, ccRate: 0.03m, echeckRate: 0m));

        var result = await sut.ProcessPaymentAsync(jobId, FamilyUserId, DepositReq(25m), ActingUserId);

        result.Success.Should().BeTrue();
        reg.FeeDonation.Should().Be(25m, "the gift is stamped on the primary registration");
        // deposit principal (100) + deposit proc (3) + gift principal (25) + proc on the gift (0.75).
        // The deposit now carries its own proc (the bug charged the bare $100 and stranded the $3),
        // so the card is hit for the full proc-inclusive OwedTotal and nothing is left owed.
        charged.Should().Be(128.75m);
        reg.PaidTotal.Should().Be(128.75m);
        reg.OwedTotal.Should().Be(0m, "deposit + its proc + the proc-inclusive gift are all collected");
    }

    [Fact(DisplayName = "Deposit + donation decline → gift rolled back (no stranded FeeDonation)")]
    public async Task DepositDonationDecline_RollsBackGift()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 100m);
        reg.FeeProcessing = 3m; reg.FeeTotal = 103m; reg.OwedTotal = 103m;
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        _teamLookup.Setup(t => t.ResolvePerRegistrantAsync(It.IsAny<Guid>())).ReturnsAsync((100m, 100m));
        StubDepositDonationRecompute(newFeeProcessing: 3.75m, newOwed: 128.75m);
        StubAdnDecline();
        var sut = BuildSut();

        var result = await sut.ProcessPaymentAsync(jobId, FamilyUserId, DepositReq(25m), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("CHARGE_GATEWAY_ERROR");
        // The snapshot is taken BEFORE the stamp/recompute and now carries FeeDonation, so a
        // declined deposit+donation restores the pre-gift posture instead of stranding the gift
        // (the engine's pre-gateway SaveChanges otherwise persists FeeDonation=25 with no charge).
        reg.FeeDonation.Should().Be(0m);
        reg.FeeBase.Should().Be(100m);
        reg.FeeProcessing.Should().Be(3m);
        reg.OwedTotal.Should().Be(103m);
        reg.PaidTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "PIF + ADN decline → pre-PIF fee fields are restored (003 Issue 1)")]
    public async Task PifDecline_RestoresPrePifFeeFields()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 500m); // Pre-PIF (deposit) posture
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        StubPifUpgrade(newOwed: 1900m);   // Upgrade would push the reg to $1900
        StubAdnDecline();
        var sut = BuildSut();

        var result = await sut.ProcessPaymentAsync(jobId, FamilyUserId, PifReq(), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("CHARGE_GATEWAY_ERROR");
        // Without the snapshot/restore (the original broken fix), FeeBase would be
        // $1900 here — the engine's pre-gateway SaveChanges committed PIF state and
        // the decline left it in place. The fix snapshots BEFORE the upgrade and
        // restores on engine failure.
        reg.FeeBase.Should().Be(500m);
        reg.FeeProcessing.Should().Be(0m);
        reg.FeeTotal.Should().Be(500m);
        reg.OwedTotal.Should().Be(500m);
        reg.PaidTotal.Should().Be(0m);
    }
}
