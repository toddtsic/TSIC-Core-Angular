using AuthorizeNet.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Payments;

/// <summary>
/// Tests for the customer-facing eCheck (ACH) submission path on PaymentService.
///
/// Covers:
///   • Happy path: PIF eCheck → ADN debit → RA + Settlement created (status Pending)
///   • Multi-reg PIF: per-reg charges sum to total; one RA + Settlement per registration
///   • Validation: BEnableEcheck off → ECHECK_NOT_ENABLED
///   • Validation: ARB option → ARB_NOT_ECHECK
///   • Validation: missing/invalid bank fields → BANK_* error codes
///   • Gateway failure → no RA, no Settlement
///   • Processing-fee credit applied per registration before charge
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
    private readonly Mock<ILogger<PaymentService>> _logger = new();

    private readonly List<RegistrationAccounting> _addedAccounting = [];
    private readonly List<Settlement> _addedSettlements = [];

    private PaymentService BuildSut()
    {
        _acct.Setup(a => a.Add(It.IsAny<RegistrationAccounting>()))
            .Callback<RegistrationAccounting>(_addedAccounting.Add);
        _settleRepo.Setup(s => s.Add(It.IsAny<Settlement>()))
            .Callback<Settlement>(_addedSettlements.Add);
        return new PaymentService(
            _jobs.Object, _regRepo.Object, _teams.Object, _families.Object, _acct.Object,
            _adn.Object, _feeService.Object, _teamLookup.Object, _feeAdj.Object, _settleRepo.Object,
            _logger.Object);
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
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(jobId, It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.SANDBOX);
    }

    private void StubRegs(Guid jobId, params Registrations[] regs)
    {
        _regRepo.Setup(r => r.GetByJobAndFamilyWithUsersAsync(jobId, FamilyUserId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regs.ToList());
    }

    private void StubAdnSuccess(string transId)
    {
        _adn.Setup(a => a.ADN_ChargeBankAccount(It.IsAny<AdnChargeBankAccountRequest>()))
            .Returns(new createTransactionResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                transactionResponse = new transactionResponse { transId = transId }
            });
    }

    private void StubAdnFailure(string errorText)
    {
        _adn.Setup(a => a.ADN_ChargeBankAccount(It.IsAny<AdnChargeBankAccountRequest>()))
            .Returns(new createTransactionResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Error, message = [new messagesTypeMessage { text = errorText }] },
                transactionResponse = new transactionResponse { errors = [new transactionResponseError { errorText = errorText }] }
            });
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
    public async Task Happy_PIF_multipleRegs_oneChargeAndOneRaPlusSettlementPerReg()
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
        // Single ADN debit for the combined total
        _adn.Verify(a => a.ADN_ChargeBankAccount(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 350m)), Times.Once);
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
        _adn.Verify(a => a.ADN_ChargeBankAccount(It.IsAny<AdnChargeBankAccountRequest>()), Times.Never);
    }

    [Fact]
    public async Task ArbOption_returnsArbNotEcheck()
    {
        var jobId = Guid.NewGuid();
        var sut = BuildSut();

        var result = await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank(), PaymentOption.ARB), ActingUserId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ARB_NOT_ECHECK");
        _jobs.Verify(j => j.GetJobPaymentInfoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task GatewayFailure_returnsErrorAndWritesNoRaOrSettlement()
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
        _addedAccounting.Should().BeEmpty();
        _addedSettlements.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessingFeeCreditAppliedPerRegBeforeCharge()
    {
        var jobId = Guid.NewGuid();
        var reg1 = Reg(jobId, owed: 100m);
        var reg2 = Reg(jobId, owed: 250m);
        StubJobAndCreds(jobId);
        StubRegs(jobId, reg1, reg2);
        StubAdnSuccess("TX-FEE");
        var sut = BuildSut();

        await sut.ProcessEcheckPaymentAsync(jobId, FamilyUserId, Req(ValidBank()), ActingUserId);

        _feeAdj.Verify(f => f.ReduceProcessingFeeForEcheckAsync(reg1, 100m, jobId, ActingUserId), Times.Once);
        _feeAdj.Verify(f => f.ReduceProcessingFeeForEcheckAsync(reg2, 250m, jobId, ActingUserId), Times.Once);
    }
}
