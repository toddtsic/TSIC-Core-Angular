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
/// The phase rule lives in ONE place — <see cref="ResolvedFee.ResolveFullPaymentPhase"/>:
/// the per-scope JobFees stamp (team → agegroup → league, cascaded into
/// <see cref="ResolvedFee.BFullPaymentRequired"/>) decides; no stamp anywhere in the cascade
/// means deposit phase. The legacy job-level columns (Jobs.B{Players,Teams}FullPaymentRequired)
/// are ABANDONED — 6a §8P materialized them into per-scope stamps, and the Program.cs startup
/// guard refuses to boot against an unstamped DB, so nothing ever falls back to them.
///
/// These tests prove (a) the pure rule and (b) that the FeeResolutionService stamping
/// chokepoint honors a per-scope stamp — a brand-new registration / team for a stamped scope
/// is stamped at full payment, and an unstamped scope stays deposit.
/// </summary>
public class PaymentPhaseResolutionTests
{
    // ── Pure resolver: the single source of truth for the phase rule ──

    [Theory(DisplayName = "ResolveFullPaymentPhase: per-scope stamp decides; no stamp → deposit")]
    [InlineData(true, true)]     // stamped ON → full payment
    [InlineData(false, false)]   // explicit OFF → deposit
    [InlineData(null, false)]    // no stamp anywhere in the cascade → deposit (no job fallback — columns abandoned)
    public void ResolveFullPaymentPhase_ScopeStampDecides_ElseDeposit(bool? scopeStamp, bool expected)
    {
        var resolved = new ResolvedFee { FeeConfigured = true, BFullPaymentRequired = scopeStamp };
        ResolvedFee.ResolveFullPaymentPhase(resolved).Should().Be(expected);
    }

    [Fact(DisplayName = "ResolveFullPaymentPhase: null resolved fee → deposit")]
    public void ResolveFullPaymentPhase_NullResolved_Deposit()
    {
        ResolvedFee.ResolveFullPaymentPhase(null).Should().BeFalse();
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
        var paymentState = new PaymentStateService(new RegistrationAccountingRepository(ctx), jobRepo.Object, new FeeRepository(ctx), new TeamRepository(ctx));
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

    [Fact(DisplayName = "Player: team-scope stamp ON flips a NEW reg to full payment")]
    public async Task Player_NewReg_TeamStamp_FullPayment()
    {
        var a = Arrange(RoleConstants.Player);
        // League fee carries deposit+balance; the TEAM row sets the phase stamp only.
        a.Builder.AddJobFee(a.JobId, RoleConstants.Player, leagueId: a.LeagueId, deposit: 100m, balanceDue: 400m);
        a.Builder.AddJobFee(a.JobId, RoleConstants.Player, agegroupId: a.AgegroupId, teamId: a.TeamId, bFullPaymentRequired: true);
        await a.Builder.SaveAsync();

        var reg = NewReg(a.JobId);
        await a.Svc.ApplyNewRegistrationFeesAsync(
            reg, a.JobId, a.AgegroupId, a.TeamId,
            new FeeApplicationContext { AddProcessingFees = false });

        reg.FeeBase.Should().Be(500m, "team-scope stamp forces full payment");
    }

    [Fact(DisplayName = "Player: no stamp → deposit phase (abandoned job columns cannot force full payment)")]
    public async Task Player_NewReg_NoStamp_DepositPhase()
    {
        var a = Arrange(RoleConstants.Player);
        a.Builder.AddJobFee(a.JobId, RoleConstants.Player, leagueId: a.LeagueId, deposit: 100m, balanceDue: 400m);
        await a.Builder.SaveAsync();

        var reg = NewReg(a.JobId);
        await a.Svc.ApplyNewRegistrationFeesAsync(
            reg, a.JobId, a.AgegroupId, a.TeamId,
            new FeeApplicationContext { AddProcessingFees = false });

        // Regression guard for the baseline removal: pre-abandonment, Jobs.BFullPaymentRequired
        // could force 500 here with no stamp. Now nothing outside the cascade can.
        reg.FeeBase.Should().Be(100m, "no stamp anywhere in the cascade → deposit only");
    }

    [Fact(DisplayName = "Team: agegroup-scope stamp ON flips a NEW team to full payment")]
    public async Task Team_NewTeam_AgegroupStamp_FullPayment()
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
                AddProcessingFees = false,
                ProcessingFeePercent = 0.035m
            });

        team.FeeBase.Should().Be(500m, "agegroup-scope stamp forces full payment");
    }

    [Fact(DisplayName = "Team: no stamp → deposit phase (FeeBase = deposit)")]
    public async Task Team_NewTeam_NoStamp_DepositPhase()
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
                AddProcessingFees = false,
                ProcessingFeePercent = 0.035m
            });

        team.FeeBase.Should().Be(100m, "no stamp anywhere in the cascade → deposit only");
    }
}
