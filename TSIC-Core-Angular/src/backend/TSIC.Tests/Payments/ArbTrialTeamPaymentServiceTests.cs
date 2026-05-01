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
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Payments;

/// <summary>
/// Tests for the ARB-Trial team registration path in PaymentService.
///
/// Covers:
///   • Happy path CC: per-team ADN ARB-Trial create + team stamp + rep aggregate sync
///   • Happy path eCheck: ADN ARB-Trial Bank variant + BEnableEcheck gate
///   • Penny-verify card failure surfaces synchronously (no ARB calls)
///   • Validation: AdnArbTrial=false, AdnStartDateAfterTrial missing, ambiguous/missing payment method
///   • Mid-batch failure stops the loop, prior subs preserved (capture-what-you-can)
///   • Fallback path when today >= AdnStartDateAfterTrial → single ADN_Charge per team, Mode flagged
/// </summary>
public class ArbTrialTeamPaymentServiceTests
{
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

    // ── Stub helpers ───────────────────────────────────────────────────

    private void StubJobAndCreds(
        Guid regId,
        Guid jobId,
        DateTime? balanceDate = null,
        bool adnArbTrial = true,
        bool enableEcheck = true,
        bool addProcessingFees = true,
        bool applyToDeposit = false)
    {
        _regRepo.Setup(r => r.GetRegistrationJobIdAsync(regId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobId);
        _jobs.Setup(j => j.GetJobFeeSettingsAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                PaymentMethodsAllowedCode = 0,
                BEnableEcheck = enableEcheck,
                AdnArbTrial = adnArbTrial,
                AdnStartDateAfterTrial = balanceDate ?? DateTime.Now.Date.AddDays(30),
                BAddProcessingFees = addProcessingFees,
                BApplyProcessingFeesToTeamDeposit = applyToDeposit
            });
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(jobId, It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.SANDBOX);
        _feeService.Setup(f => f.GetEffectiveProcessingRateAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.035m);
        _feeService.Setup(f => f.GetEffectiveEcheckProcessingRateAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.015m);
    }

    private void StubTeams(Guid jobId, IReadOnlyCollection<Guid> teamIds, params Teams[] teams)
    {
        _teams.Setup(t => t.GetTeamsWithJobAndCustomerAsync(
                jobId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.OrderBy(x => x).SequenceEqual(teamIds.OrderBy(x => x))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(teams.ToList());
    }

    private void StubFeeResolution(Guid jobId, Teams team, decimal deposit, decimal balance)
    {
        _feeService.Setup(f => f.ResolveFeeAsync(
                jobId, RoleConstants.ClubRep, team.AgegroupId, team.TeamId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { Deposit = deposit, BalanceDue = balance });
    }

    private void StubPennyVerifySuccess()
    {
        _adn.Setup(a => a.ADN_VerifyCardWithPennyAuth(It.IsAny<AdnAuthorizeRequest>()))
            .Returns(new AdnPennyVerifyResult { Success = true });
    }

    private void StubArbCcSuccess(params string[] subscriptionIds)
    {
        if (subscriptionIds.Length == 0) subscriptionIds = ["SUB-1"];
        var seq = new MockSequence();
        foreach (var sub in subscriptionIds)
        {
            _adn.InSequence(seq).Setup(a => a.ADN_ARB_CreateTrialSubscription_Cc(It.IsAny<AdnArbCreateTrialRequest>()))
                .Returns(new AdnArbCreateResult
                {
                    Success = true,
                    SubscriptionId = sub,
                    MessageForUser = "ok",
                    GatewayMessage = "ok"
                });
        }
    }

    private static Teams Team(Guid jobId, int teamAi = 1, string name = "U10 Red", Guid? agegroupId = null)
    {
        return new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            AgegroupId = agegroupId ?? Guid.NewGuid(),
            TeamName = name,
            FeeBase = 0m,
            FeeProcessing = 0m,
            FeeDiscount = 0m,
            FeeDiscountMp = 0m,
            FeeLatefee = 0m,
            FeeTotal = 0m,
            OwedTotal = 0m,
            PaidTotal = 0m,
            Active = true,
            TeamAi = teamAi,
            Modified = DateTime.UtcNow,
            Job = new Jobs
            {
                JobId = jobId,
                JobAi = 100,
                Customer = new Customers { CustomerId = Guid.NewGuid(), CustomerAi = 5 }
            }
        };
    }

    private static CreditCardInfo ValidCard() => new()
    {
        Number = "4111111111111111",
        Code = "123",
        Expiry = "1230",
        FirstName = "Pat",
        LastName = "Coach",
        Address = "123 Main",
        Zip = "10001",
        Email = "pat@club.test",
        Phone = "5555551212"
    };

    private static BankAccountInfo ValidBank() => new()
    {
        AccountType = "checking",
        RoutingNumber = "123456789",
        AccountNumber = "987654321",
        NameOnAccount = "Acme Soccer Club",
        FirstName = "Pat",
        LastName = "Coach",
        Address = "123 Main",
        Zip = "10001",
        Email = "pat@club.test",
        Phone = "5555551212"
    };

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Cc_happyPath_twoTeams_perTeamSubsAndAggregateSync()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var t1 = Team(jobId, teamAi: 1, name: "U10 Red");
        var t2 = Team(jobId, teamAi: 2, name: "U12 Blue");

        StubJobAndCreds(regId, jobId, balanceDate: DateTime.Now.Date.AddDays(30));
        StubTeams(jobId, [t1.TeamId, t2.TeamId], t1, t2);
        StubFeeResolution(jobId, t1, deposit: 200m, balance: 300m);
        StubFeeResolution(jobId, t2, deposit: 200m, balance: 300m);
        StubPennyVerifySuccess();
        StubArbCcSuccess("SUB-T1", "SUB-T2");
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [t1.TeamId, t2.TeamId], ValidCard(), null);

        result.Success.Should().BeTrue();
        result.Mode.Should().BeNull();
        result.Teams.Should().HaveCount(2);
        result.Teams.Should().OnlyContain(r => r.Registered);
        result.NotAttempted.Should().BeEmpty();
        // Per-team ARB sub creates (no bundling)
        _adn.Verify(a => a.ADN_ARB_CreateTrialSubscription_Cc(It.IsAny<AdnArbCreateTrialRequest>()), Times.Exactly(2));
        // Penny-verify runs ONCE per submit, not per team
        _adn.Verify(a => a.ADN_VerifyCardWithPennyAuth(It.IsAny<AdnAuthorizeRequest>()), Times.Once);
        // Team rows stamped with the new sub IDs
        t1.AdnSubscriptionId.Should().Be("SUB-T1");
        t1.AdnSubscriptionStatus.Should().Be("active");
        t2.AdnSubscriptionId.Should().Be("SUB-T2");
        // Aggregate sync runs once for the whole batch (rep's Registrations row picks up team deltas)
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(regId, ActingUserId, It.IsAny<CancellationToken>()), Times.Once);
        // No RegistrationAccounting / Settlement rows in the trial path — money flows when ADN's batch fires
        _addedAccounting.Should().BeEmpty();
        _addedSettlements.Should().BeEmpty();
    }

    [Fact]
    public async Task Cc_happyPath_arbRequestCarriesDepositTomorrowAndBalanceOnConfiguredDate()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var balanceDate = DateTime.Now.Date.AddDays(30);
        var team = Team(jobId);
        StubJobAndCreds(regId, jobId, balanceDate: balanceDate);
        StubTeams(jobId, [team.TeamId], team);
        StubFeeResolution(jobId, team, deposit: 200m, balance: 300m);
        StubPennyVerifySuccess();
        AdnArbCreateTrialRequest? captured = null;
        _adn.Setup(a => a.ADN_ARB_CreateTrialSubscription_Cc(It.IsAny<AdnArbCreateTrialRequest>()))
            .Callback<AdnArbCreateTrialRequest>(r => captured = r)
            .Returns(new AdnArbCreateResult { Success = true, SubscriptionId = "SUB-1", MessageForUser = "ok", GatewayMessage = "ok" });
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [team.TeamId], ValidCard(), null);

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.StartDate.Date.Should().Be(DateTime.Now.Date.AddDays(1));
        captured.IntervalLengthDays.Should().Be((short)(balanceDate - DateTime.Now.Date.AddDays(1)).Days);
        // Splitter at CC rate (0.035) on netBase 500 with bApplyToDeposit=false:
        //   balance processing = 300 * 0.035 = 10.50; total processing = 10.50
        //   depositCharge = 200, balanceCharge = 300 + 10.50 = 310.50
        captured.TrialAmount.Should().Be(200m);
        captured.PerIntervalCharge.Should().Be(310.50m);
    }

    [Fact]
    public async Task Echeck_happyPath_usesBankVariantAtEcRate()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var team = Team(jobId);
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [team.TeamId], team);
        StubFeeResolution(jobId, team, deposit: 200m, balance: 300m);
        AdnArbCreateTrialBankAccountRequest? captured = null;
        _adn.Setup(a => a.ADN_ARB_CreateTrialSubscription_Bank(It.IsAny<AdnArbCreateTrialBankAccountRequest>()))
            .Callback<AdnArbCreateTrialBankAccountRequest>(r => captured = r)
            .Returns(new AdnArbCreateResult { Success = true, SubscriptionId = "SUB-1", MessageForUser = "ok", GatewayMessage = "ok" });
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [team.TeamId], null, ValidBank());

        result.Success.Should().BeTrue();
        // No penny-verify on eCheck path (no real-time validation available)
        _adn.Verify(a => a.ADN_VerifyCardWithPennyAuth(It.IsAny<AdnAuthorizeRequest>()), Times.Never);
        // Routed to Bank variant, not CC
        _adn.Verify(a => a.ADN_ARB_CreateTrialSubscription_Bank(It.IsAny<AdnArbCreateTrialBankAccountRequest>()), Times.Once);
        _adn.Verify(a => a.ADN_ARB_CreateTrialSubscription_Cc(It.IsAny<AdnArbCreateTrialRequest>()), Times.Never);
        captured.Should().NotBeNull();
        // Splitter at EC rate (0.015) on netBase 500 with bApplyToDeposit=false:
        //   balance processing = 300 * 0.015 = 4.50; depositCharge = 200, balanceCharge = 304.50
        captured!.TrialAmount.Should().Be(200m);
        captured.PerIntervalCharge.Should().Be(304.50m);
    }

    [Fact]
    public async Task Cc_pennyVerifyFails_returnsCardVerifyFailedAndNoArbCalls()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var team = Team(jobId);
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [team.TeamId], team);
        _adn.Setup(a => a.ADN_VerifyCardWithPennyAuth(It.IsAny<AdnAuthorizeRequest>()))
            .Returns(new AdnPennyVerifyResult { Success = false, ErrorMessage = "card declined" });
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [team.TeamId], ValidCard(), null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("CARD_VERIFY_FAILED");
        _adn.Verify(a => a.ADN_ARB_CreateTrialSubscription_Cc(It.IsAny<AdnArbCreateTrialRequest>()), Times.Never);
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArbTrialNotEnabled_returnsArbTrialNotEnabled()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        StubJobAndCreds(regId, jobId, adnArbTrial: false);
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [Guid.NewGuid()], ValidCard(), null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("ARB_TRIAL_NOT_ENABLED");
    }

    [Fact]
    public async Task BalanceDateMissing_returnsArbTrialBalanceDateMissing()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        _regRepo.Setup(r => r.GetRegistrationJobIdAsync(regId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobId);
        _jobs.Setup(j => j.GetJobFeeSettingsAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                PaymentMethodsAllowedCode = 0,
                BEnableEcheck = true,
                AdnArbTrial = true,
                AdnStartDateAfterTrial = null
            });
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [Guid.NewGuid()], ValidCard(), null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("ARB_TRIAL_BALANCE_DATE_MISSING");
    }

    [Fact]
    public async Task BothPaymentMethodsSupplied_returnsAmbiguous()
    {
        var sut = BuildSut();
        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            Guid.NewGuid(), ActingUserId, [Guid.NewGuid()], ValidCard(), ValidBank());
        result.Success.Should().BeFalse();
        result.Error.Should().Be("PAYMENT_METHOD_AMBIGUOUS");
    }

    [Fact]
    public async Task NoPaymentMethodSupplied_returnsRequired()
    {
        var sut = BuildSut();
        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            Guid.NewGuid(), ActingUserId, [Guid.NewGuid()], null, null);
        result.Success.Should().BeFalse();
        result.Error.Should().Be("PAYMENT_METHOD_REQUIRED");
    }

    [Fact]
    public async Task Echeck_butJobBEnableEcheckFalse_returnsEcheckNotEnabled()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        StubJobAndCreds(regId, jobId, enableEcheck: false);
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [Guid.NewGuid()], null, ValidBank());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("ECHECK_NOT_ENABLED");
        _adn.Verify(a => a.ADN_ARB_CreateTrialSubscription_Bank(It.IsAny<AdnArbCreateTrialBankAccountRequest>()), Times.Never);
    }

    [Fact]
    public async Task MidBatchFailure_priorTeamRegistered_secondMarkedFailed_thirdNotAttempted()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var t1 = Team(jobId, teamAi: 1);
        var t2 = Team(jobId, teamAi: 2);
        var t3 = Team(jobId, teamAi: 3);
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [t1.TeamId, t2.TeamId, t3.TeamId], t1, t2, t3);
        StubFeeResolution(jobId, t1, 200m, 300m);
        StubFeeResolution(jobId, t2, 200m, 300m);
        StubFeeResolution(jobId, t3, 200m, 300m);
        StubPennyVerifySuccess();
        var seq = new MockSequence();
        _adn.InSequence(seq).Setup(a => a.ADN_ARB_CreateTrialSubscription_Cc(It.IsAny<AdnArbCreateTrialRequest>()))
            .Returns(new AdnArbCreateResult { Success = true, SubscriptionId = "SUB-T1", MessageForUser = "ok", GatewayMessage = "ok" });
        _adn.InSequence(seq).Setup(a => a.ADN_ARB_CreateTrialSubscription_Cc(It.IsAny<AdnArbCreateTrialRequest>()))
            .Returns(new AdnArbCreateResult { Success = false, MessageForUser = "card declined", GatewayMessage = "card declined" });
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [t1.TeamId, t2.TeamId, t3.TeamId], ValidCard(), null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("PARTIAL_SUCCESS");
        result.Teams.Should().HaveCount(2);
        result.Teams[0].Registered.Should().BeTrue();
        result.Teams[0].AdnSubscriptionId.Should().Be("SUB-T1");
        result.Teams[1].Registered.Should().BeFalse();
        result.Teams[1].FailureReason.Should().Be("card declined");
        result.NotAttempted.Should().ContainSingle(g => g == t3.TeamId);
        // First team kept (capture-what-you-can: do NOT roll back successes)
        t1.AdnSubscriptionId.Should().Be("SUB-T1");
        t2.AdnSubscriptionId.Should().BeNull();
        // Sync still runs because at least one team registered
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(regId, ActingUserId, It.IsAny<CancellationToken>()), Times.Once);
        // Loop stops; third team is never sent to ADN
        _adn.Verify(a => a.ADN_ARB_CreateTrialSubscription_Cc(It.IsAny<AdnArbCreateTrialRequest>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Fallback_whenTodayPastBalanceDate_chargesFullAmountWithModeFlag()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var team = Team(jobId);
        // Configured balance date already passed
        StubJobAndCreds(regId, jobId, balanceDate: DateTime.Now.Date.AddDays(-1));
        StubTeams(jobId, [team.TeamId], team);
        StubFeeResolution(jobId, team, deposit: 200m, balance: 300m);
        AdnChargeRequest? captured = null;
        _adn.Setup(a => a.ADN_Charge(It.IsAny<AdnChargeRequest>()))
            .Callback<AdnChargeRequest>(r => captured = r)
            .Returns(new createTransactionResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                transactionResponse = new transactionResponse { transId = "TX-FB", messages = [new transactionResponseMessage()] }
            });
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [team.TeamId], ValidCard(), null);

        result.Success.Should().BeTrue();
        result.Mode.Should().Be("FALLBACK_FULL_CHARGE");
        // Single ADN_Charge for the full amount, not an ARB sub create
        _adn.Verify(a => a.ADN_Charge(It.IsAny<AdnChargeRequest>()), Times.Once);
        _adn.Verify(a => a.ADN_ARB_CreateTrialSubscription_Cc(It.IsAny<AdnArbCreateTrialRequest>()), Times.Never);
        _adn.Verify(a => a.ADN_VerifyCardWithPennyAuth(It.IsAny<AdnAuthorizeRequest>()), Times.Never);
        captured.Should().NotBeNull();
        // Full amount = 200 deposit + 300 balance + 10.50 processing (CC rate, balance only)
        captured!.Amount.Should().Be(510.50m);
        // Accounting + sync run for the fallback path
        _addedAccounting.Should().HaveCount(1);
        _addedAccounting[0].AdnTransactionId.Should().Be("TX-FB");
        _addedAccounting[0].TeamId.Should().Be(team.TeamId);
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(regId, ActingUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fallback_echeck_appliesProcessingFeeCreditAndWritesSettlement()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var team = Team(jobId);
        StubJobAndCreds(regId, jobId, balanceDate: DateTime.Now.Date.AddDays(-1));
        StubTeams(jobId, [team.TeamId], team);
        StubFeeResolution(jobId, team, deposit: 200m, balance: 300m);
        _adn.Setup(a => a.ADN_ChargeBankAccount(It.IsAny<AdnChargeBankAccountRequest>()))
            .Returns(new createTransactionResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                transactionResponse = new transactionResponse { transId = "TX-FB-EC", messages = [new transactionResponseMessage()] }
            });
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [team.TeamId], null, ValidBank());

        result.Success.Should().BeTrue();
        result.Mode.Should().Be("FALLBACK_FULL_CHARGE");
        // (CC − EC) credit applied via _feeAdj before charging — uniform with ProcessTeamEcheckPaymentAsync
        // so sweep NSF reversal works the same way.
        _feeAdj.Verify(f => f.ReduceTeamProcessingFeeForEcheckAsync(team, It.IsAny<decimal>(), jobId, ActingUserId), Times.Once);
        // Settlement row written for sweep tracking
        _addedSettlements.Should().HaveCount(1);
        _addedSettlements[0].AdnTransactionId.Should().Be("TX-FB-EC");
        _addedSettlements[0].Status.Should().Be("Pending");
    }

    [Fact]
    public async Task RegistrationNotFound_returnsRegNotFound()
    {
        var regId = Guid.NewGuid();
        _regRepo.Setup(r => r.GetRegistrationJobIdAsync(regId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, [Guid.NewGuid()], ValidCard(), null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("REG_NOT_FOUND");
    }

    [Fact]
    public async Task TeamNotFound_returnsTeamNotFound()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        StubJobAndCreds(regId, jobId);
        var teamIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        StubTeams(jobId, teamIds, Team(jobId)); // only one returned vs two requested
        var sut = BuildSut();

        var result = await sut.ProcessTeamArbTrialPaymentAsync(
            regId, ActingUserId, teamIds, ValidCard(), null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("TEAM_NOT_FOUND");
    }
}
