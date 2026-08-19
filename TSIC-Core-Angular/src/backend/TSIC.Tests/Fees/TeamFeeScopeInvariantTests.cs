using FluentAssertions;
using Moq;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// The team-tier scope invariant: a team-scoped <c>fees.JobFees</c> row's AgegroupId always
/// equals its team's, because the team tier is really keyed (JobId, RoleId, TeamId) — a team
/// lives in exactly one agegroup. <see cref="FeeResolutionService.RepointTeamScopedFeesAsync"/>
/// is the one writer, called on every write of Teams.AgegroupId.
///
/// This covers the branch a manual E2E cannot reach: a team that already carries a row at the
/// TARGET agegroup as well as the stale one. That pair is only reachable from moves made before
/// the invariant existed (move → card reads blank → director retypes the price), and it is legal
/// to insert because UX_JobFees_Scope is unique on (JobId, RoleId, AgegroupId, TeamId) — which
/// does not constrain the team tier's real key. The winner must MERGE INTO the occupied slot
/// rather than be repointed onto it; repointing would be a unique violation that fails the whole
/// pool transfer, not just the fee.
///
/// LIMIT: the InMemory provider does not enforce unique indexes, so this asserts the merge LOGIC
/// (one row survives, at the target, carrying the newest values) — not that SQL Server would have
/// rejected the alternative. The collision is avoided by construction, which is the point.
/// </summary>
public class TeamFeeScopeInvariantTests
{
    [Fact(DisplayName = "Repoint: rows at BOTH the old and target agegroup collapse to one row at the target, newest values win")]
    public async Task Repoint_RowAlreadyAtTarget_MergesToSingleRow_NewestWins()
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);
        var fees = new FeeDataBuilder(ctx);

        var job = acct.AddJob();
        var league = acct.AddLeague(job.JobId);
        var oldAg = acct.AddAgegroup(league.LeagueId, "U12");
        var targetAg = acct.AddAgegroup(league.LeagueId, "U14");
        var team = acct.AddTeam(job.JobId, oldAg.AgegroupId, teamName: "Hawks");

        // Pinned to the agegroup the team has left — but it carries the director's LATEST price.
        var stale = fees.AddJobFee(job.JobId, RoleConstants.Player,
            agegroupId: oldAg.AgegroupId, teamId: team.TeamId, balanceDue: 680m);
        stale.Modified = new DateTime(2026, 8, 19, 14, 0, 0);

        // Already occupying the target slot: older, and a different price.
        var atTarget = fees.AddJobFee(job.JobId, RoleConstants.Player,
            agegroupId: targetAg.AgegroupId, teamId: team.TeamId, balanceDue: 500m);
        atTarget.Modified = new DateTime(2026, 8, 17, 9, 0, 0);

        await fees.SaveAsync();

        await BuildService(ctx).RepointTeamScopedFeesAsync(
            team.TeamId, targetAg.AgegroupId, "director-user");
        await ctx.SaveChangesAsync();

        var rows = ctx.JobFees
            .Where(f => f.TeamId == team.TeamId && f.RoleId == RoleConstants.Player)
            .ToList();

        rows.Should().HaveCount(1,
            "a team lives in exactly one agegroup, so it holds exactly one team-scoped row per role");
        rows[0].AgegroupId.Should().Be(targetAg.AgegroupId,
            "the surviving row sits where the team now is, so the cascade's team tier can see it");
        rows[0].BalanceDue.Should().Be(680m,
            "newest Modified wins — the director's most recent price, not whichever row happened to occupy the target");
        rows[0].JobFeeId.Should().Be(atTarget.JobFeeId,
            "the row ALREADY at the target survives and is merged into; repointing the winner onto an occupied slot would violate UX_JobFees_Scope");
    }

    [Fact(DisplayName = "Repoint: Player and ClubRep rows are decided independently, never against each other")]
    public async Task Repoint_PerRole_DoesNotCollapseAcrossRoles()
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);
        var fees = new FeeDataBuilder(ctx);

        var job = acct.AddJob();
        var league = acct.AddLeague(job.JobId);
        var oldAg = acct.AddAgegroup(league.LeagueId, "U12");
        var targetAg = acct.AddAgegroup(league.LeagueId, "U14");
        var team = acct.AddTeam(job.JobId, oldAg.AgegroupId, teamName: "Hawks");

        fees.AddJobFee(job.JobId, RoleConstants.Player,
            agegroupId: oldAg.AgegroupId, teamId: team.TeamId, balanceDue: 149m);
        fees.AddJobFee(job.JobId, RoleConstants.ClubRep,
            agegroupId: oldAg.AgegroupId, teamId: team.TeamId, balanceDue: 900m);
        await fees.SaveAsync();

        await BuildService(ctx).RepointTeamScopedFeesAsync(
            team.TeamId, targetAg.AgegroupId, "director-user");
        await ctx.SaveChangesAsync();

        var rows = ctx.JobFees.Where(f => f.TeamId == team.TeamId).ToList();

        rows.Should().HaveCount(2, "Player and ClubRep price independently — two rows for one team is CORRECT when the roles differ");
        rows.Should().OnlyContain(r => r.AgegroupId == targetAg.AgegroupId, "both follow the team");
        rows.Single(r => r.RoleId == RoleConstants.Player).BalanceDue.Should().Be(149m);
        rows.Single(r => r.RoleId == RoleConstants.ClubRep).BalanceDue.Should().Be(900m);
    }

    private static FeeResolutionService BuildService(Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        var paymentState = new PaymentStateService(
            new RegistrationAccountingRepository(ctx), jobRepo.Object,
            new FeeRepository(ctx), new TeamRepository(ctx));
        return new FeeResolutionService(new FeeRepository(ctx), jobRepo.Object, paymentState);
    }
}
