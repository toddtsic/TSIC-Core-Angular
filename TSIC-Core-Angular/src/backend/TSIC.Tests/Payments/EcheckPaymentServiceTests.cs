using AuthorizeNet.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Payments;

/// <summary>
/// Tests for the customer-facing eCheck (ACH) submission path on PaymentService.
///
/// Covers:
///   • Happy path: PIF eCheck → ADN debit → RA + Settlement created (status Pending)
///   • Multi-reg PIF: ONE ADN debit per registration; one RA + Settlement per registration
///   • Partial success: one reg captures, one declines → captured RA + Settlement, FAILED placeholder for the decline
///   • Validation: BEnableEcheck off → ECHECK_NOT_ENABLED
///   • Validation: ARB option → ARB_NOT_ECHECK
///   • Validation: missing/invalid bank fields → BANK_* error codes
///   • Gateway failure → FAILED placeholder RA (Active=false), no Settlement
///   • CC-symmetric eCheck (go-live 002, Issue 5): the gateway is debited the eCheck
///     gross — CC owed minus the (ccRate − echeckRate) proc credit — never the CC gross;
///     the credit is booked per registration so each reg's OwedTotal lands at 0.
/// </summary>
public class EcheckPaymentServiceTests
{
    private const string FamilyUserId = "family-1";
    private const string ActingUserId = "user-1";
    private static readonly Guid EcheckMethodId = Guid.Parse("2EECA575-A268-E111-9D56-F04DA202060D");

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

    private readonly List<RegistrationAccounting> _addedAccounting = [];
    private readonly List<Settlement> _addedSettlements = [];

    private PaymentService BuildSut()
    {
        _acct.Setup(a => a.Add(It.IsAny<RegistrationAccounting>()))
            .Callback<RegistrationAccounting>(_addedAccounting.Add);
        _settleRepo.Setup(s => s.Add(It.IsAny<Settlement>()))
            .Callback<Settlement>(_addedSettlements.Add);
        // The engine reads PaymentState per registration for principal-remaining (to size
        // the eCheck proc credit) and job rates. Default: no prior payments; the empty dict
        // forces every reg onto emptyState; ccRate 3.8% / echeckRate 1.0%. Regs carrying no
        // baked-in proc (FeeProcessing 0) self-cap the credit to 0 — see Reg() helper.
        _paymentState.Setup(p => p.ForRegistrationsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PaymentState>());
        _paymentState.Setup(p => p.ForRegistrationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentState.Empty(bAddProcessingFees: true, ccRate: 0.038m, echeckRate: 0.01m));
        return new PaymentService(
            _jobs.Object, _regRepo.Object, _teams.Object, _families.Object, _acct.Object,
            _adn.Object, _feeService.Object, _teamLookup.Object, _feeAdj.Object, _settleRepo.Object,
            _logger.Object, _paymentState.Object, new Mock<ITeamPlacementService>().Object);
    }

    private void StubJobAndCreds(Guid jobId, bool enableEcheck = true, bool allowPif = true, bool fullPaymentRequired = false)
    {
        _jobs.Setup(j => j.GetJobPaymentInfoAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPaymentInfo
            {
                AllowPif = allowPif,
                BPlayersFullPaymentRequired = fullPaymentRequired,
                BEnableEcheck = enableEcheck
            });
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(jobId))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment()).Returns(AuthorizeNet.Environment.SANDBOX);
    }

    private void StubRegs(Guid jobId, params Registrations[] regs)
    {
        _regRepo.Setup(r => r.GetByJobAndFamilyWithUsersAsync(jobId, FamilyUserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regs.ToList());
        // The canonical engine reloads by id (EF identity map in prod). Return the SAME
        // instances so the test's reg variables observe the engine's mutations.
        var byIds = regs.ToDictionary(r => r.RegistrationId);
        _regRepo.Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid> ids, CancellationToken _) => ids.Where(byIds.ContainsKey).Select(id => byIds[id]).ToList());
    }

    private void StubAdnSuccess(string transId)
    {
        _adn.Setup(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()))
            .Returns(new AdnTxnResult { Success = true, TransactionId = transId, ResponseCode = "1", MessageForUser = "Approved" });
    }

    private void StubAdnFailure(string errorText)
    {
        _adn.Setup(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()))
            .Returns(new AdnTxnResult { Success = false, MessageForUser = errorText });
    }

    private static Registrations Reg(Guid jobId, decimal owed = 100m, decimal feeProcessing = 0m) =>
        new()
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
            BActive = true,
            Modified = DateTime.UtcNow
        };

    private static BankAccountInfo ValidBank(string accountType = "checking", string routing = "123456789", string account = "987654321") => new()
    {
        AccountType = accountType,
        RoutingNumber = routing,
        AccountNumber = account,
        NameOnAccount = "Jane Doe",
        FirstName = "Jane",
        LastName = "Doe",
        Address = "123 Main",
        Zip = "10001",
        Email = "jane@example.com",
        Phone = "555-555-1212"
    };

    private static PaymentRequestDto Req(BankAccountInfo? bank, PaymentOption opt = PaymentOption.PIF) => new()
    {
        JobPath = "test-job",
        PaymentOption = opt,
        BankAccount = bank
    };

    [Fact]
    public async Task Happy_PIF_singleReg_chargesAdnAndCreatesRaAndSettlement()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 100m);
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        StubAdnSuccess("TX-123");
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank()), ActingUserId);

        result.Success.Should().BeTrue();
        result.TransactionId.Should().Be("TX-123");
        _addedAccounting.Should().HaveCount(1);
        _addedAccounting[0].PaymentMethodId.Should().Be(EcheckMethodId);
        _addedAccounting[0].Payamt.Should().Be(100m);
        _addedAccounting[0].AdnTransactionId.Should().Be("TX-123");
        _addedSettlements.Should().HaveCount(1);
        _addedSettlements[0].Status.Should().Be("Pending");
        _addedSettlements[0].AdnTransactionId.Should().Be("TX-123");
        _addedSettlements[0].AccountLast4.Should().Be("4321");
        _addedSettlements[0].AccountType.Should().Be("checking");
    }

    [Fact]
    public async Task Happy_PIF_multipleRegs_perRegChargeAndOneRaPlusSettlementPerReg()
    {
        var jobId = Guid.NewGuid();
        var reg1 = Reg(jobId, owed: 100m);
        var reg2 = Reg(jobId, owed: 250m);
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg1, reg2);
        StubAdnSuccess("TX-AA");
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank()), ActingUserId);

        result.Success.Should().BeTrue();
        // ONE ADN debit PER registration (CC-symmetric) — never a bundled family debit.
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 100m)), Times.Once);
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 250m)), Times.Once);
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 350m)), Times.Never);
        _addedAccounting.Should().HaveCount(2);
        _addedAccounting.Sum(r => r.Payamt ?? 0m).Should().Be(350m);
        _addedAccounting.Should().OnlyContain(r => r.AdnTransactionId == "TX-AA");
        _addedSettlements.Should().HaveCount(2);
        _addedSettlements.Should().OnlyContain(s => s.AdnTransactionId == "TX-AA" && s.Status == "Pending");
    }

    [Fact]
    public async Task EcheckDisabled_returnsEcheckNotEnabled()
    {
        var jobId = Guid.NewGuid();
        StubJobAndCreds(jobId, enableEcheck: false);
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank()), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ECHECK_NOT_ENABLED");
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()), Times.Never);
    }

    [Fact]
    public async Task MissingBankAccount_returnsBankRequired()
    {
        var jobId = Guid.NewGuid();
        StubJobAndCreds(jobId);
        StubRegs(jobId, Reg(jobId));
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(bank: null), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("BANK_REQUIRED");
    }

    [Theory]
    [InlineData("12345678", "BANK_ROUTING_INVALID")]   // 8 digits
    [InlineData("1234567890", "BANK_ROUTING_INVALID")] // 10 digits
    public async Task RoutingMustBeNineDigits(string routing, string expectedCode)
    {
        var jobId = Guid.NewGuid();
        StubJobAndCreds(jobId);
        StubRegs(jobId, Reg(jobId));
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank(routing: routing)), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(expectedCode);
    }

    [Fact]
    public async Task InvalidAccountType_returnsBankTypeInvalid()
    {
        var jobId = Guid.NewGuid();
        StubJobAndCreds(jobId);
        StubRegs(jobId, Reg(jobId));
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank(accountType: "money-market")), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("BANK_TYPE_INVALID");
    }

    [Fact]
    public async Task NameOnAccountTooLong_returnsBankNameInvalid()
    {
        var jobId = Guid.NewGuid();
        StubJobAndCreds(jobId);
        StubRegs(jobId, Reg(jobId));
        var bank = ValidBank();
        var longName = bank with { NameOnAccount = new string('x', 23) };
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(longName), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("BANK_NAME_INVALID");
    }

    [Fact]
    public async Task GatewayFailure_returnsError_writesFailedPlaceholderRa_noSettlement()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 100m);
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        StubAdnFailure("Test gateway error");
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank()), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("CHARGE_GATEWAY_ERROR");
        // CC-symmetric audit trail: a declined eCheck leaves a FAILED placeholder RA (Active=false,
        // Payamt=0, Comment="FAILED: …") instead of vanishing — the old bundled path wrote nothing.
        _addedAccounting.Should().ContainSingle();
        _addedAccounting[0].Active.Should().BeFalse();
        _addedAccounting[0].Payamt.Should().Be(0m);
        (_addedAccounting[0].Comment ?? "").Should().StartWith("FAILED");
        // No settlement — only captured charges are tracked for clearance/NSF.
        _addedSettlements.Should().BeEmpty();
    }

    [Fact]
    public async Task EcheckWithProcessingFees_chargesEcheckGross_notCcGross_andOwedLandsAtZero()
    {
        // Issue 5 regression: reg owes $1000 principal + $38 CC proc (3.8%) = $1038 CC owed.
        // Paid by eCheck (1.0%) the customer must be debited the ECHECK gross $1010 — never the
        // CC gross $1038 — and OwedTotal must land at 0. The old path debited the CC gross while
        // separately reducing the reg's owed, double-counting the proc difference (overcharge).
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 1000m, feeProcessing: 38m); // owed param = principal; OwedTotal = 1038
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg);
        StubAdnSuccess("TX-1");
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank()), ActingUserId);

        result.Success.Should().BeTrue();
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 1010m)), Times.Once);
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 1038m)), Times.Never);
        _addedAccounting.Should().ContainSingle();
        _addedAccounting[0].Payamt.Should().Be(1010m);   // eCheck gross stored (CC-symmetric)
        _addedAccounting[0].PaymentMethodId.Should().Be(EcheckMethodId);
        reg.OwedTotal.Should().Be(0m);                   // the bug left this non-zero
        reg.PaidTotal.Should().Be(1010m);
        reg.FeeProcessing.Should().Be(10m);              // 1000 × echeckRate (1%) proc retained
        reg.FeeTotal.Should().Be(1010m);                 // FeeTotal dropped by the credit too — no phantom balance
        // Player path no longer routes through the fee-adjustment service — it computes the
        // credit inline (mirrors the team engine).
        _feeAdj.Verify(f => f.ReduceProcessingFeeForEcheckAsync(It.IsAny<Registrations>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MultipleRegsWithProc_eachDebitedItsOwnEcheckGross_perRegDebits()
    {
        // Two regs with different fee structures: $311.40 and $519.00 CC owed. Each reg is
        // debited its OWN eCheck gross in its OWN ADN transaction ($303 and $505) — proving the
        // proc credit is per-reg proportional and the charge is per-registration (granular NSF),
        // never a pooled/averaged/bundled figure.
        var jobId = Guid.NewGuid();
        var reg1 = Reg(jobId, owed: 300m, feeProcessing: 11.40m); // OwedTotal 311.40
        var reg2 = Reg(jobId, owed: 500m, feeProcessing: 19.00m); // OwedTotal 519.00
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg1, reg2);
        StubAdnSuccess("TX-AA");
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank()), ActingUserId);

        result.Success.Should().BeTrue();
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 303m)), Times.Once);
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 505m)), Times.Once);
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 808m)), Times.Never);
        _addedAccounting.Select(r => r.Payamt).Should().BeEquivalentTo(new decimal?[] { 303m, 505m });
        reg1.OwedTotal.Should().Be(0m);
        reg2.OwedTotal.Should().Be(0m);
    }

    [Fact]
    public async Task PartialSuccess_capturesFirstReg_marksSecondFailed_settlesOnlyTheCapture()
    {
        // Per-reg transactions mean a batch can split: reg1 clears, reg2 declines. The captured
        // reg gets its RA + Settlement; the declined reg gets a FAILED placeholder and NO
        // settlement. (The old bundled debit was all-or-nothing — one decline killed the family.)
        var jobId = Guid.NewGuid();
        var reg1 = Reg(jobId, owed: 100m);
        var reg2 = Reg(jobId, owed: 250m);
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg1, reg2);
        _adn.SetupSequence(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()))
            .Returns(new AdnTxnResult { Success = true, TransactionId = "TX-OK", ResponseCode = "1", MessageForUser = "Approved" })
            .Returns(new AdnTxnResult { Success = false, MessageForUser = "Declined" });
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank()), ActingUserId);

        result.Success.Should().BeFalse(); // any decline → overall failure; the parent retries the declined reg
        _addedAccounting.Should().HaveCount(2);
        _addedAccounting.Should().ContainSingle(r => r.Active == true && r.Payamt == 100m && r.AdnTransactionId == "TX-OK");
        _addedAccounting.Should().ContainSingle(r => r.Active == false && (r.Comment ?? "").StartsWith("FAILED"));
        _addedSettlements.Should().ContainSingle();
        _addedSettlements[0].AdnTransactionId.Should().Be("TX-OK");
    }
}
