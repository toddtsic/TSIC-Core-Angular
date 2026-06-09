using FluentAssertions;
using Moq;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// Payment-phase (deposit vs. full-payment) resolution.
///
/// The phase precedence lives in ONE place — <see cref="ResolvedFee.ResolveFullPaymentPhase"/>:
/// a per-scope JobFees override (team → agegroup → league, cascaded into
/// <see cref="ResolvedFee.BFullPaymentRequired"/>) wins; otherwise the job-level baseline
/// (Jobs.B{Players,Teams}FullPaymentRequired, passed by the caller as ctx.IsFullPaymentRequired).
///
/// These tests prove (a) the pure precedence rule and (b) that the FeeResolutionService
/// stamping chokepoint actually honors a per-scope override even when the job baseline is off —
/// i.e. a brand-new registration / team for a converted scope is stamped at full payment.
/// </summary>
public class PaymentPhaseResolutionTests
{
    // ── Pure resolver: the single source of truth for phase precedence ──

    [Theory(DisplayName = "ResolveFullPaymentPhase: override wins, null falls back to baseline")]
    [InlineData(true, false, true)]    // scope override ON beats job baseline OFF
    [InlineData(false, true, false)]   // scope override OFF beats job baseline ON (future tri-state)
    [InlineData(null, true, true)]     // no override → job baseline ON
    [InlineData(null, false, false)]   // no override → job baseline OFF
    public void ResolveFullPaymentPhase_OverrideWins_ElseBaseline(
        bool? scopeOverride, bool jobBaseline, bool expected)
    {
        var resolved = new ResolvedFee { FeeConfigured = true, BFullPaymentRequired = scopeOverride };
        ResolvedFee.ResolveFullPaymentPhase(resolved, jobBaseline).Should().Be(expected);
    }

    [Theory(DisplayName = "ResolveFullPaymentPhase: null resolved fee → job baseline")]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveFullPaymentPhase_NullResolved_UsesBaseline(bool jobBaseline)
    {
        ResolvedFee.ResolveFullPaymentPhase(null, jobBaseline).Should().Be(jobBaseline);
    }

    // ── Stamping chokepoint: per-scope override drives the actual FeeBase ──
    //
    // Fee shape: Deposit $100 + BalanceDue $400. Deposit phase → player FeeBase $100,
    // team FeeBase $100; full-payment phase → FeeBase $500. Processing left OFF
    // (GetJobFeeSettingsAsync unmocked → BAddProcessingFees defaults false) so the
    // assertions isolate the phase decision.

    private static (FeeResolutionService Svc, FeeDataBuilder Builder,
        Guid JobId, Guid LeagueId, Guid AgegroupId, Guid TeamId) Arrange(string roleId)
    {
        var ctx = DbContextFactory.Create();
        var builder = new FeeDataBuilder(ctx);
        var job = builder.AddJob();
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, "U12");
        var team = builder.AddTeam(job.JobId, ag.AgegroupId, "Hawks");

        var feeRepo = new FeeRepository(ctx);
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);
        var paymentState = new PaymentStateService(new RegistrationAccountingRepository(ctx), jobRepo.Object);
        var svc = new FeeResolutionService(feeRepo, jobRepo.Object, paymentState);
        return (svc, builder, job.JobId, league.LeagueId, ag.AgegroupId, team.TeamId);
    }

    private static Registrations NewReg(Guid jobId) => new()
    {
        RegistrationId = Guid.NewGuid(),
        JobId = jobId,
        FeeDonation = 0m,
        PaidTotal = 0m,
        Modified = DateTime.UtcNow
    };

    [Fact(DisplayName = "Player: team-scope override ON flips a NEW reg to full payment even though job baseline is OFF")]
    public async Task Player_NewReg_TeamOverride_OverridesJobBaselineOff()
    {
        var a = Arrange(RoleConstants.Player);
        // League fee carries deposit+balance; the TEAM row sets the phase override only.
        a.Builder.AddJobFee(a.JobId, RoleConstants.Player, leagueId: a.LeagueId, deposit: 100m, balanceDue: 400m);
        a.Builder.AddJobFee(a.JobId, RoleConstants.Player, agegroupId: a.AgegroupId, teamId: a.TeamId, bFullPaymentRequired: true);
        await a.Builder.SaveAsync();

        var reg = NewReg(a.JobId);
        await a.Svc.ApplyNewRegistrationFeesAsync(
            reg, a.JobId, a.AgegroupId, a.TeamId,
            new FeeApplicationContext { IsFullPaymentRequired = false, AddProcessingFees = false });

        reg.FeeBase.Should().Be(500m, "team-scope override forces full payment despite job baseline OFF");
    }

    [Fact(DisplayName = "Player: no override + job baseline OFF → deposit-phase stamp")]
    public async Task Player_NewReg_NoOverride_BaselineOff_DepositPhase()
    {
        var a = Arrange(RoleConstants.Player);
        a.Builder.AddJobFee(a.JobId, RoleConstants.Player, leagueId: a.LeagueId, deposit: 100m, balanceDue: 400m);
        await a.Builder.SaveAsync();

        var reg = NewReg(a.JobId);
        await a.Svc.ApplyNewRegistrationFeesAsync(
            reg, a.JobId, a.AgegroupId, a.TeamId,
            new FeeApplicationContext { IsFullPaymentRequired = false, AddProcessingFees = false });

        reg.FeeBase.Should().Be(100m, "no override → job baseline OFF → deposit only");
    }

    [Fact(DisplayName = "Player: no override + job baseline ON → legacy job-wide full payment still works")]
    public async Task Player_NewReg_NoOverride_BaselineOn_FullPayment()
    {
        var a = Arrange(RoleConstants.Player);
        a.Builder.AddJobFee(a.JobId, RoleConstants.Player, leagueId: a.LeagueId, deposit: 100m, balanceDue: 400m);
        await a.Builder.SaveAsync();

        var reg = NewReg(a.JobId);
        await a.Svc.ApplyNewRegistrationFeesAsync(
            reg, a.JobId, a.AgegroupId, a.TeamId,
            new FeeApplicationContext { IsFullPaymentRequired = true, AddProcessingFees = false });

        reg.FeeBase.Should().Be(500m, "no override → job baseline ON → full payment (legacy behavior preserved)");
    }

    [Fact(DisplayName = "Team: agegroup-scope override ON flips a NEW team to full payment despite job baseline OFF")]
    public async Task Team_NewTeam_AgegroupOverride_OverridesJobBaselineOff()
    {
        var a = Arrange(RoleConstants.ClubRep);
        a.Builder.AddJobFee(a.JobId, RoleConstants.ClubRep, leagueId: a.LeagueId, deposit: 100m, balanceDue: 400m);
        a.Builder.AddJobFee(a.JobId, RoleConstants.ClubRep, agegroupId: a.AgegroupId, bFullPaymentRequired: true);
        await a.Builder.SaveAsync();

        var team = new Domain.Entities.Teams
        {
            TeamId = a.TeamId,
            JobId = a.JobId,
            AgegroupId = a.AgegroupId,
            FeeDonation = 0m,
            PaidTotal = 0m,
            Modified = DateTime.UtcNow
        };

        await a.Svc.ApplyNewTeamFeesAsync(
            team, a.JobId, a.AgegroupId,
            new TeamFeeApplicationContext
            {
                IsFullPaymentRequired = false,
                AddProcessingFees = false,
                ProcessingFeePercent = 0.035m
            });

        team.FeeBase.Should().Be(500m, "agegroup-scope override forces full payment despite job baseline OFF");
    }

    [Fact(DisplayName = "Team: no override + job baseline OFF → deposit-phase stamp (FeeBase = deposit)")]
    public async Task Team_NewTeam_NoOverride_BaselineOff_DepositPhase()
    {
        var a = Arrange(RoleConstants.ClubRep);
        a.Builder.AddJobFee(a.JobId, RoleConstants.ClubRep, leagueId: a.LeagueId, deposit: 100m, balanceDue: 400m);
        await a.Builder.SaveAsync();

        var team = new Domain.Entities.Teams
        {
            TeamId = a.TeamId,
            JobId = a.JobId,
            AgegroupId = a.AgegroupId,
            FeeDonation = 0m,
            PaidTotal = 0m,
            Modified = DateTime.UtcNow
        };

        await a.Svc.ApplyNewTeamFeesAsync(
            team, a.JobId, a.AgegroupId,
            new TeamFeeApplicationContext
            {
                IsFullPaymentRequired = false,
                AddProcessingFees = false,
                ProcessingFeePercent = 0.035m
            });

        team.FeeBase.Should().Be(100m, "no override → job baseline OFF → deposit only");
    }
}
