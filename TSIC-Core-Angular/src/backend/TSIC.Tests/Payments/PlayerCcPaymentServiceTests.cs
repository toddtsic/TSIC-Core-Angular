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
        _adn.Setup(a => a.ADN_Charge(It.IsAny<AdnChargeRequest>()))
            .Returns(new createTransactionResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Error },
                transactionResponse = new transactionResponse
                {
                    errors = [new transactionResponseError { errorText = errorText, errorCode = "2" }]
                }
            });
    }

    private static Registrations Reg(Guid jobId, decimal owed) =>
        new()
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            FamilyUserId = FamilyUserId,
            AssignedTeamId = Guid.NewGuid(),
            AssignedAgegroupId = Guid.NewGuid(),
            FeeBase = owed,
            FeeProcessing = 0m,
            FeeDiscount = 0m,
            FeeLatefee = 0m,
            FeeTotal = owed,
            OwedTotal = owed,
            PaidTotal = 0m,
        };

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
