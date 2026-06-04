using FluentAssertions;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// Deposit/BalanceDue cascade resolution (<see cref="FeeRepository.GetResolvedFeeAsync"/>).
///
/// The base-fee cascade is Team → Agegroup → League — most-specific non-null wins,
/// per field. League is the top tier; the Job tier is NOT a source for Player/ClubRep
/// base fees (it survives only for team-less adult roles via GetJobLevelFeeAsync).
/// </summary>
public class FeeCascadeResolutionTests
{
    private sealed record Fixture(
        SqlDbContext Ctx, FeeDataBuilder Builder,
        Guid JobId, Guid LeagueId, Guid AgegroupId, Guid TeamId);

    private static Fixture Arrange()
    {
        var ctx = DbContextFactory.Create();
        var b = new FeeDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U12");
        var team = b.AddTeam(job.JobId, ag.AgegroupId, "Hawks");
        return new Fixture(ctx, b, job.JobId, league.LeagueId, ag.AgegroupId, team.TeamId);
    }

    [Fact(DisplayName = "League base fee resolves when no agegroup or team row exists")]
    public async Task League_ResolvesAsTopTier()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, leagueId: f.LeagueId, balanceDue: 200m);
        await f.Builder.SaveAsync();

        var resolved = await new FeeRepository(f.Ctx)
            .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);

        resolved!.FeeConfigured.Should().BeTrue();
        resolved.BalanceDue.Should().Be(200m);
    }

    [Fact(DisplayName = "Agegroup row overrides the league row")]
    public async Task Agegroup_OverridesLeague()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, leagueId: f.LeagueId, balanceDue: 200m);
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, balanceDue: 150m);
        await f.Builder.SaveAsync();

        var resolved = await new FeeRepository(f.Ctx)
            .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);

        resolved!.BalanceDue.Should().Be(150m);
    }

    [Fact(DisplayName = "Team row overrides the agegroup row")]
    public async Task Team_OverridesAgegroup()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, balanceDue: 150m);
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, teamId: f.TeamId, balanceDue: 120m);
        await f.Builder.SaveAsync();

        var resolved = await new FeeRepository(f.Ctx)
            .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);

        resolved!.BalanceDue.Should().Be(120m);
    }

    [Fact(DisplayName = "Per-field coalesce: league BalanceDue + agegroup Deposit both resolve")]
    public async Task PerField_Coalesce_AcrossTiers()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, leagueId: f.LeagueId, balanceDue: 200m); // BalanceDue only
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, deposit: 50m);  // Deposit only
        await f.Builder.SaveAsync();

        var resolved = await new FeeRepository(f.Ctx)
            .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);

        resolved!.Deposit.Should().Be(50m, "deposit cascades from the agegroup tier");
        resolved.BalanceDue.Should().Be(200m, "balance due cascades from the league tier");
    }

    [Fact(DisplayName = "Job-level row is NOT a base-fee source (cascade ignores it)")]
    public async Task JobLevel_IsNotABaseTier()
    {
        var f = Arrange();
        // Job-level row only (all scope ids null) — legacy shape, no longer resolved.
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, balanceDue: 200m);
        await f.Builder.SaveAsync();

        var resolved = await new FeeRepository(f.Ctx)
            .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);

        resolved!.FeeConfigured.Should().BeFalse("job is no longer a tier in the base-fee cascade");
    }
}
