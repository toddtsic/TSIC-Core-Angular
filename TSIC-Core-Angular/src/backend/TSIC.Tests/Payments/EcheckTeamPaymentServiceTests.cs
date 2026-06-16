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
/// Tests for the customer-facing eCheck (ACH) submission path on the team flow.
///
/// Covers:
///   • Happy path: 2 teams → 2 separate ADN debits (per-team invoice for refundability),
///     2 RA + 2 Settlement rows (Pending), club-rep financials re-aggregated
///   • Validation: BEnableEcheck off → ECHECK_NOT_ENABLED
///   • Validation: registration not found → REG_NOT_FOUND
///   • Validation: missing/invalid bank fields → BANK_* error codes
///   • Per-team processing-fee credit applied before each gateway call
///   • Partial failure: succeeded teams keep settlements; failed teams skipped
/// </summary>
public class EcheckTeamPaymentServiceTests
{
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
        // The engine reads PaymentState for principal-remaining (to size the eCheck proc
        // credit) and job rates. Default: no prior payments; ccRate 3.8% / echeckRate 1.0%.
        _paymentState.Setup(p => p.ForTeamsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PaymentState>());
        _paymentState.Setup(p => p.ForTeamAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentState.Empty(bAddProcessingFees: true, ccRate: 0.038m, echeckRate: 0.01m));
        return new PaymentService(
            _jobs.Object, _regRepo.Object, _teams.Object, _families.Object, _acct.Object,
            _adn.Object, _feeService.Object, _teamLookup.Object, _feeAdj.Object, _settleRepo.Object,
            _logger.Object, _paymentState.Object, new Mock<ITeamPlacementService>().Object);
    }

    private void StubJobAndCreds(Guid regId, Guid jobId, bool enableEcheck = true)
    {
        _regRepo.Setup(r => r.GetRegistrationJobIdAsync(regId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobId);
        _jobs.Setup(j => j.GetJobPaymentInfoAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPaymentInfo { BEnableEcheck = enableEcheck });
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(jobId, It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.SANDBOX);
    }

    private void StubTeams(Guid jobId, IReadOnlyCollection<Guid> teamIds, params Teams[] teams)
    {
        _teams.Setup(t => t.GetTeamsWithJobAndCustomerAsync(
                jobId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.OrderBy(x => x).SequenceEqual(teamIds.OrderBy(x => x))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(teams.ToList());
    }

    private void StubAdnSuccess(Action<MockSequence>? sequence = null, params string[] transIds)
    {
        if (transIds.Length == 0) transIds = ["TX-1"];
        var seq = new MockSequence();
        foreach (var tx in transIds)
        {
            _adn.InSequence(seq).Setup(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()))
                .Returns(new AdnTxnResult { Success = true, TransactionId = tx, ResponseCode = "1", MessageForUser = "Approved" });
        }
    }

    private static Teams Team(Guid jobId, decimal owed = 200m, int teamAi = 1, string name = "Test Team")
    {
        return new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            TeamName = name,
            FeeBase = owed,
            FeeProcessing = 0m,
            FeeTotal = owed,
            OwedTotal = owed,
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

    // Team carrying the baked-in CC processing fee (matches the BuildSut ccRate of 3.8%).
    private static Teams TeamWithProc(Guid jobId, decimal principal, decimal ccProc, int teamAi = 1, string name = "Test Team")
    {
        return new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            TeamName = name,
            FeeBase = principal,
            FeeProcessing = ccProc,
            FeeTotal = principal + ccProc,
            OwedTotal = principal + ccProc,
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
        Phone = "555-555-1212"
    };

    [Fact]
    public async Task Happy_twoTeams_separateAdnCallsAndPerTeamRaPlusSettlement()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var t1 = Team(jobId, owed: 200m, teamAi: 1, name: "U10 Red");
        var t2 = Team(jobId, owed: 200m, teamAi: 2, name: "U12 Blue");
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [t1.TeamId, t2.TeamId], t1, t2);
        StubAdnSuccess(null, "TX-T1", "TX-T2");
        var sut = BuildSut();

        var result = await sut.ProcessTeamEcheckPaymentAsync(regId, ActingUserId, [t1.TeamId, t2.TeamId], 400m, ValidBank());

        result.Success.Should().BeTrue();
        // Each team is charged separately (per-team invoice for refund traceability)
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 200m)), Times.Exactly(2));
        _addedAccounting.Should().HaveCount(2);
        _addedAccounting.Should().OnlyContain(r => r.PaymentMethodId == EcheckMethodId);
        _addedAccounting.Select(r => r.AdnTransactionId).Should().BeEquivalentTo("TX-T1", "TX-T2");
        _addedAccounting.Select(r => r.TeamId).Should().BeEquivalentTo(new Guid?[] { t1.TeamId, t2.TeamId });
        _addedSettlements.Should().HaveCount(2);
        _addedSettlements.Should().OnlyContain(s => s.Status == "Pending");
        _addedSettlements.Select(s => s.AdnTransactionId).Should().BeEquivalentTo("TX-T1", "TX-T2");
        // Club rep aggregate sync runs once for the whole batch
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(regId, ActingUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EcheckDisabled_returnsEcheckNotEnabled()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        StubJobAndCreds(regId, jobId, enableEcheck: false);
        var sut = BuildSut();

        var result = await sut.ProcessTeamEcheckPaymentAsync(regId, ActingUserId, [Guid.NewGuid()], 100m, ValidBank());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("ECHECK_NOT_ENABLED");
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()), Times.Never);
    }

    [Fact]
    public async Task RegistrationNotFound_returnsRegNotFound()
    {
        var regId = Guid.NewGuid();
        _regRepo.Setup(r => r.GetRegistrationJobIdAsync(regId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        var sut = BuildSut();

        var result = await sut.ProcessTeamEcheckPaymentAsync(regId, ActingUserId, [Guid.NewGuid()], 100m, ValidBank());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("REG_NOT_FOUND");
    }

    [Fact]
    public async Task InvalidBankAccount_returnsBankErrorAndNoCharge()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        StubJobAndCreds(regId, jobId);
        var sut = BuildSut();

        var bank = ValidBank() with { RoutingNumber = "12345678" }; // 8 digits
        var result = await sut.ProcessTeamEcheckPaymentAsync(regId, ActingUserId, [Guid.NewGuid()], 100m, bank);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("BANK_ROUTING_INVALID");
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()), Times.Never);
    }

    [Fact]
    public async Task TeamNotFound_returnsTeamNotFound()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        StubJobAndCreds(regId, jobId);
        var teamIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        // Repo returns only one team though caller asked for two
        StubTeams(jobId, teamIds, Team(jobId));
        var sut = BuildSut();

        var result = await sut.ProcessTeamEcheckPaymentAsync(regId, ActingUserId, teamIds, 400m, ValidBank());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("TEAM_NOT_FOUND");
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()), Times.Never);
    }

    [Fact]
    public async Task EcheckWithProcessingFees_chargesEcheckGross_notCcGross_andOwedLandsAtZero()
    {
        // Issue 5 regression: team owes $1000 principal + $38 CC proc (3.8%) = $1038 CC owed.
        // Paid by eCheck (1.0%), the customer must be debited the ECHECK gross $1010 — never
        // the CC gross $1038 — and OwedTotal must land at 0, not negative.
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var team = TeamWithProc(jobId, principal: 1000m, ccProc: 38m, teamAi: 1);
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [team.TeamId], team);
        StubAdnSuccess(null, "TX-1");
        var sut = BuildSut();

        var result = await sut.ProcessTeamEcheckPaymentAsync(regId, ActingUserId, [team.TeamId], 1010m, ValidBank());

        result.Success.Should().BeTrue();
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 1010m)), Times.Once);
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 1038m)), Times.Never);
        _addedAccounting.Should().ContainSingle();
        _addedAccounting[0].Payamt.Should().Be(1010m);   // eCheck gross stored (CC-symmetric convention)
        _addedAccounting[0].PaymentMethodId.Should().Be(EcheckMethodId);
        team.OwedTotal.Should().Be(0m);                  // the bug left this negative
        team.PaidTotal.Should().Be(1010m);
        team.FeeProcessing.Should().Be(10m);             // 1000 × echeckRate (1%) proc retained
    }

    [Fact]
    public async Task MixedFeeStructures_chargesEachTeamItsOwnEcheckGross_notAnEqualSplit()
    {
        // Issue 1 (eCheck twin): two teams from different agegroups owe $311.40 and $519.00
        // CC. The old code split the client total evenly ($830.40 / 2 = $415.20). Correct
        // behaviour debits each team its own eCheck gross: $303.00 and $505.00.
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var t1 = TeamWithProc(jobId, principal: 300m, ccProc: 11.40m, teamAi: 1, name: "U10 Red");
        var t2 = TeamWithProc(jobId, principal: 500m, ccProc: 19.00m, teamAi: 2, name: "U12 Blue");
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [t1.TeamId, t2.TeamId], t1, t2);
        StubAdnSuccess(null, "TX-T1", "TX-T2");
        var sut = BuildSut();

        var result = await sut.ProcessTeamEcheckPaymentAsync(regId, ActingUserId, [t1.TeamId, t2.TeamId], 808m, ValidBank());

        result.Success.Should().BeTrue();
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 303m)), Times.Once);
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 505m)), Times.Once);
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.Is<AdnChargeBankAccountRequest>(r => r.Amount == 415.20m)), Times.Never);
        _addedAccounting.Select(r => r.Payamt).Should().BeEquivalentTo(new decimal?[] { 303m, 505m });
        t1.OwedTotal.Should().Be(0m);
        t2.OwedTotal.Should().Be(0m);
    }

    [Fact]
    public async Task ClientSubmitsCcTotalForEcheck_returnsAmountMismatch_andNoCharge()
    {
        // The displayed CC owed is $1038 but the eCheck total is $1010. If the client submits
        // the CC figure, fail closed — never silently debit an amount the rep didn't approve.
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var team = TeamWithProc(jobId, principal: 1000m, ccProc: 38m, teamAi: 1);
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [team.TeamId], team);
        var sut = BuildSut();

        var result = await sut.ProcessTeamEcheckPaymentAsync(regId, ActingUserId, [team.TeamId], 1038m, ValidBank());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("AMOUNT_MISMATCH");
        _adn.Verify(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()), Times.Never);
    }

    [Fact]
    public async Task PartialFailure_succeededTeamKeepsSettlement_failedSkipped()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var t1 = Team(jobId, owed: 200m, teamAi: 1);
        var t2 = Team(jobId, owed: 200m, teamAi: 2);
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [t1.TeamId, t2.TeamId], t1, t2);
        var seq = new MockSequence();
        _adn.InSequence(seq).Setup(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()))
            .Returns(new AdnTxnResult { Success = true, TransactionId = "TX-OK", ResponseCode = "1", MessageForUser = "Approved" });
        _adn.InSequence(seq).Setup(a => a.ADN_ChargeBankAccount_Result(It.IsAny<AdnChargeBankAccountRequest>()))
            .Returns(new AdnTxnResult { Success = false, MessageForUser = "boom" });
        var sut = BuildSut();

        var result = await sut.ProcessTeamEcheckPaymentAsync(regId, ActingUserId, [t1.TeamId, t2.TeamId], 400m, ValidBank());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("PARTIAL_SUCCESS");
        result.TransactionId.Should().Be("TX-OK");
        _addedAccounting.Should().HaveCount(1);
        _addedAccounting[0].AdnTransactionId.Should().Be("TX-OK");
        _addedSettlements.Should().HaveCount(1);
        _addedSettlements[0].AdnTransactionId.Should().Be("TX-OK");
    }
}
