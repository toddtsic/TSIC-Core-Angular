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
/// Tests for the customer-facing credit-card submission path on the team flow
/// (<see cref="PaymentService.ProcessTeamPaymentAsync"/>).
///
/// The headline guarantee: each team is charged its OWN <c>OwedTotal</c> — the
/// resolver-maintained single source of truth — never an equal split of the
/// client-supplied total. This is the go-live "money moves wrong" fix (002, Issue 1):
/// the old code charged <c>totalAmount / teamIds.Count</c>, which mis-allocated
/// whenever teams came from agegroups with different fee structures.
/// </summary>
public class TeamCcPaymentServiceTests
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

    private PaymentService BuildSut()
    {
        _acct.Setup(a => a.Add(It.IsAny<RegistrationAccounting>()))
            .Callback<RegistrationAccounting>(_addedAccounting.Add);
        // CC never credits proc (methodRate == ccRate), so empty states suffice; the
        // engine still queries them. Empty dict + empty state (no prior payments).
        _paymentState.Setup(p => p.ForTeamsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PaymentState>());
        _paymentState.Setup(p => p.ForTeamAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentState.Empty(bAddProcessingFees: false, ccRate: 0m, echeckRate: 0m));
        return new PaymentService(
            _jobs.Object, _regRepo.Object, _teams.Object, _families.Object, _acct.Object,
            _adn.Object, _feeService.Object, _teamLookup.Object, _feeAdj.Object, _settleRepo.Object,
            _logger.Object, _paymentState.Object);
    }

    private void StubJobAndCreds(Guid regId, Guid jobId)
    {
        _regRepo.Setup(r => r.GetRegistrationJobIdAsync(regId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobId);
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

    private void StubAdnChargeSuccess(params string[] transIds)
    {
        if (transIds.Length == 0) transIds = ["TX-1"];
        var seq = new MockSequence();
        foreach (var tx in transIds)
        {
            _adn.InSequence(seq).Setup(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()))
                .Returns(new AdnTxnResult { Success = true, TransactionId = tx, ResponseCode = "1", MessageForUser = "Approved" });
        }
    }

    private static Teams Team(Guid jobId, decimal owed, int teamAi, string name = "Test Team")
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

    private static CreditCardInfo ValidCard() => new()
    {
        Number = "4111111111111111",
        Code = "123",
        Expiry = "12/2030",
        FirstName = "Pat",
        LastName = "Coach",
        Address = "123 Main",
        Zip = "10001",
        Email = "pat@club.test",
        Phone = "555-555-1212"
    };

    [Fact]
    public async Task MixedFeeStructures_chargesEachTeamItsOwnOwed_notAnEqualSplit()
    {
        // The bug this fix exists for: two teams from different agegroups owe different
        // amounts ($300 and $500). The old code charged $400 to each (800 / 2). Correct
        // behavior charges each team its own OwedTotal.
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var t1 = Team(jobId, owed: 300m, teamAi: 1, name: "U10 Red");
        var t2 = Team(jobId, owed: 500m, teamAi: 2, name: "U12 Blue");
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [t1.TeamId, t2.TeamId], t1, t2);
        StubAdnChargeSuccess("TX-T1", "TX-T2");
        var sut = BuildSut();

        var result = await sut.ProcessTeamPaymentAsync(regId, ActingUserId, [t1.TeamId, t2.TeamId], 800m, ValidCard());

        result.Success.Should().BeTrue();
        // Each team charged its own owed — never the $400 equal split.
        _adn.Verify(a => a.ADN_Charge_Result(It.Is<AdnChargeRequest>(r => r.Amount == 300m)), Times.Once);
        _adn.Verify(a => a.ADN_Charge_Result(It.Is<AdnChargeRequest>(r => r.Amount == 500m)), Times.Once);
        _adn.Verify(a => a.ADN_Charge_Result(It.Is<AdnChargeRequest>(r => r.Amount == 400m)), Times.Never);

        _addedAccounting.Should().HaveCount(2);
        _addedAccounting.Should().OnlyContain(r => r.PaymentMethodId == CcMethodId);
        _addedAccounting.Select(r => r.Payamt).Should().BeEquivalentTo(new decimal?[] { 300m, 500m });

        // Team balances driven to zero; aggregate synced once for the batch.
        t1.OwedTotal.Should().Be(0m);
        t2.OwedTotal.Should().Be(0m);
        t1.PaidTotal.Should().Be(300m);
        t2.PaidTotal.Should().Be(500m);
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(regId, ActingUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClientTotalDisagreesWithServerOwed_returnsAmountMismatch_andNoCharge()
    {
        // Server owed is $800 ($300 + $500); client submits a stale $400. The tripwire
        // must reject rather than charge an amount the rep never saw.
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var t1 = Team(jobId, owed: 300m, teamAi: 1);
        var t2 = Team(jobId, owed: 500m, teamAi: 2);
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [t1.TeamId, t2.TeamId], t1, t2);
        var sut = BuildSut();

        var result = await sut.ProcessTeamPaymentAsync(regId, ActingUserId, [t1.TeamId, t2.TeamId], 400m, ValidCard());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("AMOUNT_MISMATCH");
        _adn.Verify(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()), Times.Never);
        _addedAccounting.Should().BeEmpty();
    }

    [Fact]
    public async Task AllTeamsPaidInFull_returnsNothingDue_andNoCharge()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        var t1 = Team(jobId, owed: 0m, teamAi: 1);
        var t2 = Team(jobId, owed: 0m, teamAi: 2);
        StubJobAndCreds(regId, jobId);
        StubTeams(jobId, [t1.TeamId, t2.TeamId], t1, t2);
        var sut = BuildSut();

        var result = await sut.ProcessTeamPaymentAsync(regId, ActingUserId, [t1.TeamId, t2.TeamId], 0m, ValidCard());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("NOTHING_DUE");
        _adn.Verify(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()), Times.Never);
    }

    [Fact]
    public async Task TeamNotFound_returnsTeamNotFound_andNoCharge()
    {
        var jobId = Guid.NewGuid();
        var regId = Guid.NewGuid();
        StubJobAndCreds(regId, jobId);
        var teamIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        // Repo returns only one team though the caller asked for two.
        StubTeams(jobId, teamIds, Team(jobId, owed: 300m, teamAi: 1));
        var sut = BuildSut();

        var result = await sut.ProcessTeamPaymentAsync(regId, ActingUserId, teamIds, 300m, ValidCard());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
        _adn.Verify(a => a.ADN_Charge_Result(It.IsAny<AdnChargeRequest>()), Times.Never);
    }
}
