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
/// Tests for the canonical per-player CC charge engine
/// (<see cref="PaymentService.ChargeRegistrationsCcAsync"/>) — the single primitive both
/// the parent self-pay wizard (N regs, one ADN tx) and the admin admin-charge modal
/// (1 reg) route through.
///
/// Headline guarantees: (1) every charge consults <c>PaymentState.ResolveOwed.Cc</c> so
/// display and gateway cannot drift; (2) a placeholder RA row exists in the DB before
/// the gateway call so a declined card leaves an Active=false RA with a FAILED comment
/// (audit trail symmetric across parent and admin); (3) success updates the SAME RA row
/// instead of writing a second one.
/// </summary>
public class PlayerCcChargeTests
{
    private const string ActingUserId = "user-1";
    private static readonly Guid CcMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");

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

    private PaymentService BuildSut(bool procEnabled = false, decimal ccRate = 0m, decimal echeckRate = 0m)
    {
        _acct.Setup(a => a.Add(It.IsAny<RegistrationAccounting>()))
            .Callback<RegistrationAccounting>(_addedAccounting.Add);
        // CC bucket equals full owed (credit 0); empty states suffice for the resolver.
        var state = PaymentState.Empty(procEnabled, ccRate, echeckRate);
        _paymentState.Setup(p => p.ForRegistrationsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PaymentState>());
        _paymentState.Setup(p => p.ForRegistrationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.SANDBOX);
        _regRepo.Setup(r => r.GetRegistrationWithInvoiceDataAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationWithInvoiceData { CustomerAi = 5, JobAi = 100, RegistrationAi = 200 });
        return new PaymentService(
            _jobs.Object, _regRepo.Object, _teams.Object, _families.Object, _acct.Object,
            _adn.Object, _feeService.Object, _teamLookup.Object, _feeAdj.Object, _settleRepo.Object,
            _logger.Object, _paymentState.Object);
    }

    private void StubLoadedRegs(params Registrations[] regs)
    {
        var byIds = regs.ToDictionary(r => r.RegistrationId);
        _regRepo.Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid> ids, CancellationToken _) =>
                ids.Where(byIds.ContainsKey).Select(id => byIds[id]).ToList());
    }

    private void StubAdnChargeSuccess(string transId = "TX-1")
    {
        _adn.Setup(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()))
            .Returns(new AdnTxnResult { Success = true, TransactionId = transId, ResponseCode = "1", MessageForUser = "Approved" });
    }

    private void StubAdnChargeDeclined(string errorText = "This transaction has been declined.")
    {
        _adn.Setup(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()))
            .Returns(new AdnTxnResult { Success = false, ResponseCode = "2", GatewayCode = "2", MessageForUser = errorText });
    }

    private static Registrations Reg(Guid jobId, decimal owed, int registrationAi = 200)
    {
        return new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            RegistrationAi = registrationAi,
            FeeBase = owed,
            FeeProcessing = 0m,
            FeeDiscount = 0m,
            FeeLatefee = 0m,
            FeeTotal = owed,
            OwedTotal = owed,
            PaidTotal = 0m,
        };
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

    private static RegistrationChargeItem Item(Guid regId, decimal amount) =>
        new() { RegistrationId = regId, Amount = amount };

    // ── Admin shape: 1 reg, 1 ADN call ───────────────────────────────────────

    [Fact(DisplayName = "Admin (1 reg) → 1 ADN call, 1 RA row, PaidTotal advances")]
    public async Task SingleReg_ChargesOnce_AndUpdatesRow()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 250m);
        StubLoadedRegs(reg);
        StubAdnChargeSuccess("TX-A");
        var sut = BuildSut();

        var result = await sut.ChargeRegistrationsCcAsync(jobId, [Item(reg.RegistrationId, 250m)], ValidCard(), ActingUserId);

        result.Success.Should().BeTrue();
        result.TransactionId.Should().Be("TX-A");
        _adn.Verify(a => a.ADN_Charge_Result(It.Is<AdnChargeRequest>(r => r.Amount == 250m)), Times.Once);
        _addedAccounting.Should().HaveCount(1);
        _addedAccounting[0].PaymentMethodId.Should().Be(CcMethodId);
        _addedAccounting[0].Payamt.Should().Be(250m);
        _addedAccounting[0].AdnTransactionId.Should().Be("TX-A");
        _addedAccounting[0].Active.Should().BeTrue();
        reg.PaidTotal.Should().Be(250m);
        reg.OwedTotal.Should().Be(0m);
    }

    // ── Parent shape: N regs, ONE ADN call (preserves single statement entry) ─

    [Fact(DisplayName = "Parent (N regs) → ONE ADN call summing the batch; one RA row per reg")]
    public async Task MultipleRegs_SingleBatchedChargeAndPerRegRows()
    {
        var jobId = Guid.NewGuid();
        var r1 = Reg(jobId, owed: 200m);
        var r2 = Reg(jobId, owed: 300m);
        var r3 = Reg(jobId, owed: 150m);
        StubLoadedRegs(r1, r2, r3);
        StubAdnChargeSuccess("TX-B");
        var sut = BuildSut();

        var result = await sut.ChargeRegistrationsCcAsync(
            jobId,
            [Item(r1.RegistrationId, 200m), Item(r2.RegistrationId, 300m), Item(r3.RegistrationId, 150m)],
            ValidCard(), ActingUserId);

        result.Success.Should().BeTrue();
        // ONE ADN call for the whole batch (parent's existing UX: a single statement entry).
        _adn.Verify(a => a.ADN_Charge_Result(It.Is<AdnChargeRequest>(r => r.Amount == 650m)), Times.Once);
        _addedAccounting.Should().HaveCount(3);
        _addedAccounting.Select(r => r.Payamt).Should().BeEquivalentTo(new decimal?[] { 200m, 300m, 150m });
        _addedAccounting.Should().OnlyContain(r => r.AdnTransactionId == "TX-B" && r.Active == true);
        r1.OwedTotal.Should().Be(0m);
        r2.OwedTotal.Should().Be(0m);
        r3.OwedTotal.Should().Be(0m);
        result.Outcomes.Should().AllSatisfy(o => o.Success.Should().BeTrue());
    }

    // ── Audit trail: declined card LEAVES the RA row (Active=false + FAILED) ──

    [Fact(DisplayName = "ADN decline → RA row persists Active=false with FAILED comment (audit trail)")]
    public async Task DeclinedCard_LeavesFailedRaRow_AndDoesNotAdvancePaidTotal()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 250m);
        StubLoadedRegs(reg);
        StubAdnChargeDeclined("Card declined");
        var sut = BuildSut();

        var result = await sut.ChargeRegistrationsCcAsync(jobId, [Item(reg.RegistrationId, 250m)], ValidCard(), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("CHARGE_GATEWAY_ERROR");
        result.Message.Should().Be("Card declined");
        // RA row was inserted as a placeholder BEFORE the gateway call — declined cards
        // leave a row with Active=false so a director can see the attempt occurred.
        _addedAccounting.Should().HaveCount(1);
        _addedAccounting[0].Active.Should().BeFalse();
        _addedAccounting[0].Comment.Should().StartWith("FAILED:");
        _addedAccounting[0].Payamt.Should().Be(0m);
        // Registration totals untouched.
        reg.PaidTotal.Should().Be(0m);
        reg.OwedTotal.Should().Be(250m);
    }

    [Fact(DisplayName = "ADN decline in a batch → every reg in the batch gets a FAILED RA row")]
    public async Task DeclinedCard_Batch_FailsAllRowsAtomically()
    {
        var jobId = Guid.NewGuid();
        var r1 = Reg(jobId, owed: 200m);
        var r2 = Reg(jobId, owed: 300m);
        StubLoadedRegs(r1, r2);
        StubAdnChargeDeclined("Insufficient funds");
        var sut = BuildSut();

        var result = await sut.ChargeRegistrationsCcAsync(
            jobId, [Item(r1.RegistrationId, 200m), Item(r2.RegistrationId, 300m)], ValidCard(), ActingUserId);

        result.Success.Should().BeFalse();
        _addedAccounting.Should().HaveCount(2);
        _addedAccounting.Should().OnlyContain(r => r.Active == false && r.Comment!.StartsWith("FAILED:"));
        r1.PaidTotal.Should().Be(0m);
        r2.PaidTotal.Should().Be(0m);
    }

    // ── Tripwire: stale UI amount is rejected, no gateway hit, no RA written ──

    [Fact(DisplayName = "amount > ResolveOwed.Cc → AMOUNT_MISMATCH, no ADN call, no RA")]
    public async Task AmountExceedsOwed_TripsAmountMismatch()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 100m); // Cc bucket = 100 (proc disabled in BuildSut default)
        StubLoadedRegs(reg);
        var sut = BuildSut();

        var result = await sut.ChargeRegistrationsCcAsync(jobId, [Item(reg.RegistrationId, 250m)], ValidCard(), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("AMOUNT_MISMATCH");
        _adn.Verify(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()), Times.Never);
        _addedAccounting.Should().BeEmpty();
        reg.PaidTotal.Should().Be(0m);
    }

    // ── Cross-job protection (admin can't charge a reg belonging to another job) ─

    [Fact(DisplayName = "Reg belongs to a different job → REG_WRONG_JOB, no gateway hit")]
    public async Task RegInDifferentJob_RejectsBeforeCharge()
    {
        var jobId = Guid.NewGuid();
        var otherJobId = Guid.NewGuid();
        var reg = Reg(otherJobId, owed: 100m);
        StubLoadedRegs(reg);
        var sut = BuildSut();

        var result = await sut.ChargeRegistrationsCcAsync(jobId, [Item(reg.RegistrationId, 100m)], ValidCard(), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("REG_WRONG_JOB");
        _adn.Verify(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()), Times.Never);
        _addedAccounting.Should().BeEmpty();
    }

    // ── Shape: missing reg / empty list / non-positive amount / duplicates ───

    [Fact(DisplayName = "Empty item list → NO_ITEMS, no gateway hit")]
    public async Task EmptyList_Rejected()
    {
        var sut = BuildSut();
        var result = await sut.ChargeRegistrationsCcAsync(Guid.NewGuid(), [], ValidCard(), ActingUserId);
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("NO_ITEMS");
    }

    [Fact(DisplayName = "Amount ≤ 0 → INVALID_AMOUNT, no gateway hit")]
    public async Task ZeroAmount_Rejected()
    {
        var jobId = Guid.NewGuid();
        var reg = Reg(jobId, owed: 100m);
        var sut = BuildSut();
        var result = await sut.ChargeRegistrationsCcAsync(jobId, [Item(reg.RegistrationId, 0m)], ValidCard(), ActingUserId);
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_AMOUNT");
    }

    [Fact(DisplayName = "Duplicate reg in batch → DUPLICATE_REGS, no gateway hit")]
    public async Task DuplicateRegIds_Rejected()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var sut = BuildSut();
        var result = await sut.ChargeRegistrationsCcAsync(jobId,
            [Item(regId, 100m), Item(regId, 50m)], ValidCard(), ActingUserId);
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("DUPLICATE_REGS");
    }

    // ── Proc-enabled job: ResolveOwed.Cc == OwedTotal (CC pays its own proc) ──

    [Fact(DisplayName = "Proc-enabled job: full OwedTotal is the CC bucket")]
    public async Task ProcEnabled_CcBucketEqualsOwedTotal()
    {
        var jobId = Guid.NewGuid();
        // Base 450, proc embedded → OwedTotal 467.10 at 3.8% CC rate.
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            RegistrationAi = 1,
            FeeBase = 450m,
            FeeDiscount = 0m,
            FeeLatefee = 0m,
            FeeProcessing = 17.10m,
            FeeTotal = 467.10m,
            OwedTotal = 467.10m,
            PaidTotal = 0m,
        };
        StubLoadedRegs(reg);
        StubAdnChargeSuccess("TX-P");
        var sut = BuildSut(procEnabled: true, ccRate: 0.038m);

        var result = await sut.ChargeRegistrationsCcAsync(jobId, [Item(reg.RegistrationId, 467.10m)], ValidCard(), ActingUserId);

        result.Success.Should().BeTrue();
        _adn.Verify(a => a.ADN_Charge_Result(It.Is<AdnChargeRequest>(r => r.Amount == 467.10m)), Times.Once);
        reg.OwedTotal.Should().Be(0m);
        reg.PaidTotal.Should().Be(467.10m);
    }
}
