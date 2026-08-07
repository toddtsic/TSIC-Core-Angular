using FluentAssertions;
using TSIC.API.Services.Fees;
using TSIC.Contracts.Dtos.Ladt;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// Equivalence suite for <see cref="LadtFeeResolutionMapBuilder"/> — the display-only
/// twin of the charging path. The builder's feed IS the production read
/// (<see cref="FeeRepository.GetJobFeesByJobAsync"/>), and every resolved value is
/// pinned against the charge-path authority:
///   amounts/configured → <see cref="FeeRepository.GetResolvedFeeAsync"/>
///   phase              → <see cref="ResolvedFee.ResolveFullPaymentPhase"/>
///   modifiers          → <see cref="FeeRepository.GetActiveModifiersForCascadeAsync"/>
/// If the map and the charge path ever disagree, the map is wrong — these tests are the
/// contract that keeps the LADT grids honest about what actually charges.
/// </summary>
public class LadtFeeResolutionMapTests
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

    /// <summary>Single-chain tree (one league → one agegroup → one team).</summary>
    private static LadtFeeResolutionMapBuilder.TreeInput Tree(Fixture f) => new()
    {
        Leagues =
        [
            new LadtFeeResolutionMapBuilder.LeagueInput
            {
                LeagueId = f.LeagueId,
                Agegroups =
                [
                    new LadtFeeResolutionMapBuilder.AgegroupInput
                    {
                        AgegroupId = f.AgegroupId,
                        AgegroupName = "U12",
                        TeamIds = [f.TeamId]
                    }
                ]
            }
        ]
    };

    /// <summary>Builds the map from the PRODUCTION fee read — the same rows the endpoint serves.</summary>
    private static async Task<LadtFeeResolutionMapDto> BuildMapAsync(
        Fixture f, LadtFeeResolutionMapBuilder.TreeInput? tree = null)
    {
        var jobFees = await new FeeRepository(f.Ctx).GetJobFeesByJobAsync(f.JobId);
        return LadtFeeResolutionMapBuilder.Build(jobFees, tree ?? Tree(f), DateTime.Now);
    }

    private static LadtFeeRoleResolutionDto Player(LadtFeeResolutionMapDto map, Guid nodeId)
        => map.Nodes.Single(n => n.NodeId == nodeId).Player;

    // ═══════════════════════════════════════════
    // Equivalence with the charge path
    // ═══════════════════════════════════════════

    [Fact(DisplayName = "Equivalence matrix: map team entry == GetResolvedFeeAsync across all 64 tier combos")]
    public async Task Matrix_TeamEntry_EqualsChargePath()
    {
        // Distinct amount per (field, tier) so a wrong-tier win can't produce a false pass.
        string?[] tiers = [null, "league", "agegroup", "team"];

        foreach (var depositTier in tiers)
        {
            foreach (var balanceTier in tiers)
            {
                foreach (var phaseTier in tiers)
                {
                    var f = Arrange();
                    AddFieldRow(f, depositTier, deposit: depositTier switch
                    { "league" => 11m, "agegroup" => 12m, "team" => 13m, _ => null });
                    AddFieldRow(f, balanceTier, balanceDue: balanceTier switch
                    { "league" => 101m, "agegroup" => 102m, "team" => 103m, _ => null });
                    AddFieldRow(f, phaseTier, bFullPaymentRequired: phaseTier switch
                    { "league" => true, "agegroup" => false, "team" => true, _ => null });
                    await f.Builder.SaveAsync();

                    var resolved = await new FeeRepository(f.Ctx)
                        .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);
                    var entry = Player(await BuildMapAsync(f), f.TeamId);

                    var combo = $"deposit@{depositTier ?? "none"} balance@{balanceTier ?? "none"} phase@{phaseTier ?? "none"}";
                    entry.FeeConfigured.Should().Be(resolved!.FeeConfigured, combo);
                    entry.Deposit.Should().Be(resolved.Deposit, combo);
                    entry.BalanceDue.Should().Be(resolved.BalanceDue, combo);
                    entry.FullPayment.Should().Be(ResolvedFee.ResolveFullPaymentPhase(resolved), combo);
                    entry.DepositSource.Should().Be(depositTier, combo);
                    entry.BalanceDueSource.Should().Be(balanceTier, combo);
                    entry.PhaseSource.Should().Be(phaseTier, combo);
                }
            }
        }
    }

    private static void AddFieldRow(
        Fixture f, string? tier,
        decimal? deposit = null, decimal? balanceDue = null, bool? bFullPaymentRequired = null)
    {
        switch (tier)
        {
            case "league":
                f.Builder.AddJobFee(f.JobId, RoleConstants.Player, leagueId: f.LeagueId,
                    deposit: deposit, balanceDue: balanceDue, bFullPaymentRequired: bFullPaymentRequired);
                break;
            case "agegroup":
                f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId,
                    deposit: deposit, balanceDue: balanceDue, bFullPaymentRequired: bFullPaymentRequired);
                break;
            case "team":
                f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, teamId: f.TeamId,
                    deposit: deposit, balanceDue: balanceDue, bFullPaymentRequired: bFullPaymentRequired);
                break;
        }
    }

    [Fact(DisplayName = "Stale team row (mismatched AgegroupId) is invisible to map and charge path alike")]
    public async Task StaleTeamRow_ExcludedLikeChargePath()
    {
        var f = Arrange();
        var otherAg = f.Builder.AddAgegroup(f.LeagueId, "U14");
        // Row still pointing at the team but keyed under the agegroup it USED to sit in.
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player,
            agegroupId: otherAg.AgegroupId, teamId: f.TeamId, balanceDue: 999m);
        await f.Builder.SaveAsync();

        var resolved = await new FeeRepository(f.Ctx)
            .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);
        var entry = Player(await BuildMapAsync(f), f.TeamId);

        resolved!.FeeConfigured.Should().BeFalse("the charge path pair-matches team rows");
        entry.FeeConfigured.Should().Be(resolved.FeeConfigured, "the map must mirror the pair match");
        entry.BalanceDue.Should().BeNull();
    }

    [Fact(DisplayName = "Job-level row (all scope ids null) is no source for amounts OR phase")]
    public async Task JobLevelRow_IgnoredEverywhere()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, balanceDue: 200m, bFullPaymentRequired: true);
        await f.Builder.SaveAsync();

        var resolved = await new FeeRepository(f.Ctx)
            .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);
        var map = await BuildMapAsync(f);

        resolved!.FeeConfigured.Should().BeFalse();
        foreach (var nodeId in new[] { f.LeagueId, f.AgegroupId, f.TeamId })
        {
            var entry = Player(map, nodeId);
            entry.FeeConfigured.Should().BeFalse("job tier is not in any cascade chain");
            entry.PhaseSource.Should().BeNull("the legacy job-level stamp is abandoned as a phase source");
            entry.FullPayment.Should().BeFalse();
        }
    }

    [Fact(DisplayName = "NotConfigured vs configured $0: an explicit zero row IS configured")]
    public async Task ConfiguredZero_IsNotUnconfigured()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, balanceDue: 0m);
        await f.Builder.SaveAsync();

        var resolved = await new FeeRepository(f.Ctx)
            .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);
        var entry = Player(await BuildMapAsync(f), f.TeamId);

        resolved!.FeeConfigured.Should().BeTrue();
        entry.FeeConfigured.Should().BeTrue("an explicit $0 is a real setting, not absence");
        entry.BalanceDue.Should().Be(0m);
        entry.BalanceDueSource.Should().Be("agegroup");
    }

    // ═══════════════════════════════════════════
    // Phase sources
    // ═══════════════════════════════════════════

    [Fact(DisplayName = "Phase: league stamp propagates as the source at all three levels")]
    public async Task Phase_LeagueStamp_PropagatesDown()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, leagueId: f.LeagueId,
            balanceDue: 200m, bFullPaymentRequired: true);
        await f.Builder.SaveAsync();

        var map = await BuildMapAsync(f);
        foreach (var nodeId in new[] { f.LeagueId, f.AgegroupId, f.TeamId })
        {
            var entry = Player(map, nodeId);
            entry.FullPayment.Should().BeTrue();
            entry.PhaseSource.Should().Be("league");
        }
    }

    [Fact(DisplayName = "Phase: team false veto under agegroup true — most-specific wins, false is a source")]
    public async Task Phase_TeamFalseVeto_WinsWithSource()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId,
            balanceDue: 150m, bFullPaymentRequired: true);
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, teamId: f.TeamId,
            bFullPaymentRequired: false);
        await f.Builder.SaveAsync();

        var resolved = await new FeeRepository(f.Ctx)
            .GetResolvedFeeAsync(f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId);
        var map = await BuildMapAsync(f);

        var team = Player(map, f.TeamId);
        team.FullPayment.Should().Be(ResolvedFee.ResolveFullPaymentPhase(resolved),
            "the explicit false veto must match the charge path");
        team.FullPayment.Should().BeFalse();
        team.PhaseSource.Should().Be("team", "an explicit false is a stamp, not silence");

        var ag = Player(map, f.AgegroupId);
        ag.FullPayment.Should().BeTrue();
        ag.PhaseSource.Should().Be("agegroup");
    }

    // ═══════════════════════════════════════════
    // Modifier equivalence
    // ═══════════════════════════════════════════

    [Fact(DisplayName = "Modifiers: agegroup Early Bird resolves at the team with amounts pinned to the cascade repo")]
    public async Task Modifiers_AgegroupEarlyBird_EquivalentToRepo()
    {
        var f = Arrange();
        var agFee = f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, balanceDue: 150m);
        f.Builder.AddModifier(agFee.JobFeeId, FeeConstants.ModifierEarlyBird, 25m); // unbounded = always active
        await f.Builder.SaveAsync();

        var repo = new FeeRepository(f.Ctx);
        var configured = await repo.GetActiveModifiersForCascadeAsync(
            f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId, asOfDate: null);
        var activeNow = await repo.GetActiveModifiersForCascadeAsync(
            f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId, DateTime.Now);

        var entry = Player(await BuildMapAsync(f), f.TeamId);
        entry.EarlyBird.Should().NotBeNull();
        entry.EarlyBird!.Amount.Should().Be(
            configured.Where(m => m.ModifierType == FeeConstants.ModifierEarlyBird).Sum(m => m.Amount),
            "configured amount == the window-ignored cascade winner");
        entry.EarlyBird.Source.Should().Be("agegroup");
        entry.EarlyBird.Active.Should().BeTrue();
        entry.EarlyBird.ActiveAmount.Should().Be(
            activeNow.Where(m => m.ModifierType == FeeConstants.ModifierEarlyBird).Sum(m => m.Amount),
            "active amount == the asOf-now cascade winner");
        entry.LateFee.Should().BeNull("no late fee configured anywhere");
    }

    [Fact(DisplayName = "Modifiers: expired team Late Fee — configured winner is the team, active winner the agegroup")]
    public async Task Modifiers_ExpiredSpecificScope_SplitsConfiguredFromActive()
    {
        var f = Arrange();
        var agFee = f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, balanceDue: 150m);
        f.Builder.AddModifier(agFee.JobFeeId, FeeConstants.ModifierLateFee, 10m); // active (unbounded)
        var teamFee = f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, teamId: f.TeamId);
        f.Builder.AddModifier(teamFee.JobFeeId, FeeConstants.ModifierLateFee, 15m,
            startDate: DateTime.Now.AddDays(-30), endDate: DateTime.Now.AddDays(-1)); // expired
        await f.Builder.SaveAsync();

        var activeNow = await new FeeRepository(f.Ctx).GetActiveModifiersForCascadeAsync(
            f.JobId, RoleConstants.Player, f.AgegroupId, f.TeamId, DateTime.Now);

        var entry = Player(await BuildMapAsync(f), f.TeamId);
        entry.LateFee.Should().NotBeNull();
        entry.LateFee!.Amount.Should().Be(15m, "the team carries the type, windows ignored — today's display");
        entry.LateFee.Source.Should().Be("team");
        entry.LateFee.Active.Should().BeFalse("the team's window has expired");
        entry.LateFee.ActiveAmount.Should().Be(
            activeNow.Where(m => m.ModifierType == FeeConstants.ModifierLateFee).Sum(m => m.Amount),
            "the charging number comes from the most-specific ACTIVE scope");
        entry.LateFee.ActiveAmount.Should().Be(10m);
        entry.LateFee.ActiveSource.Should().Be("agegroup");
    }

    // ═══════════════════════════════════════════
    // Below summaries
    // ═══════════════════════════════════════════

    [Fact(DisplayName = "Below/amounts: two team overrides that differ from the agegroup read count 2, varies")]
    public async Task Below_TeamOverridesDiffer_FromAgegroup()
    {
        var f = Arrange();
        var team2 = f.Builder.AddTeam(f.JobId, f.AgegroupId, "Eagles");
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, balanceDue: 150m);
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, teamId: f.TeamId, balanceDue: 120m);
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, teamId: team2.TeamId, balanceDue: 120m);
        await f.Builder.SaveAsync();

        var tree = Tree(f) with
        {
            Leagues =
            [
                new LadtFeeResolutionMapBuilder.LeagueInput
                {
                    LeagueId = f.LeagueId,
                    Agegroups =
                    [
                        new LadtFeeResolutionMapBuilder.AgegroupInput
                        {
                            AgegroupId = f.AgegroupId,
                            AgegroupName = "U12",
                            TeamIds = [f.TeamId, team2.TeamId]
                        }
                    ]
                }
            ]
        };
        var map = await BuildMapAsync(f, tree);

        var ag = Player(map, f.AgegroupId);
        ag.Below.Should().NotBeNull();
        ag.Below!.Amounts.OverrideCount.Should().Be(2);
        ag.Below.Amounts.Agrees.Should().BeFalse("both teams resolve $120 against the agegroup's $150");
        ag.Below.Amounts.DistinctValues.Should().HaveCount(1, "both overriding teams resolve the same pair");
        ag.Below.Amounts.DistinctValues[0].BalanceDue.Should().Be(120m);

        // League spans BOTH tiers: the agegroup override + two team overrides.
        var league = Player(map, f.LeagueId);
        league.Below!.Amounts.OverrideCount.Should().Be(3);

        // Team nodes are leaves — no below summary.
        Player(map, f.TeamId).Below.Should().BeNull();
    }

    [Fact(DisplayName = "Below/amounts: overrides that resolve to the node's own values agree")]
    public async Task Below_AgreeingOverrides_ReadAgrees()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, balanceDue: 150m);
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, teamId: f.TeamId, balanceDue: 150m);
        await f.Builder.SaveAsync();

        var ag = Player(await BuildMapAsync(f), f.AgegroupId);
        ag.Below!.Amounts.OverrideCount.Should().Be(1);
        ag.Below.Amounts.Agrees.Should().BeTrue("the team's resolved pair equals the agegroup's");
    }

    [Fact(DisplayName = "Below/phase: a team stamp under an unstamped agegroup surfaces as a phase override")]
    public async Task Below_PhaseStamp_Surfaces()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, balanceDue: 150m);
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, teamId: f.TeamId,
            bFullPaymentRequired: true);
        await f.Builder.SaveAsync();

        var ag = Player(await BuildMapAsync(f), f.AgegroupId);
        ag.FullPayment.Should().BeFalse("no stamp at or above the agegroup");
        ag.Below!.Phase.OverrideCount.Should().Be(1);
        ag.Below.Phase.Agrees.Should().BeFalse("the team turned full payment ON against the node's deposit phase");
        ag.Below.Phase.DistinctValues.Should().Equal(true);
    }

    [Fact(DisplayName = "Below: WAITLIST/Dropped bucket agegroups and their teams are excluded from disclosure")]
    public async Task Below_SystemBuckets_Excluded()
    {
        var f = Arrange();
        var bucketAg = f.Builder.AddAgegroup(f.LeagueId, "WAITLIST - U12");
        var bucketTeam = f.Builder.AddTeam(f.JobId, bucketAg.AgegroupId, "WAITLIST mirror");
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, balanceDue: 150m);
        // Minted $0 rows on the bucket — a director never set these.
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: bucketAg.AgegroupId, balanceDue: 0m);
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: bucketAg.AgegroupId, teamId: bucketTeam.TeamId, balanceDue: 0m);
        await f.Builder.SaveAsync();

        var tree = new LadtFeeResolutionMapBuilder.TreeInput
        {
            Leagues =
            [
                new LadtFeeResolutionMapBuilder.LeagueInput
                {
                    LeagueId = f.LeagueId,
                    Agegroups =
                    [
                        new LadtFeeResolutionMapBuilder.AgegroupInput
                        { AgegroupId = f.AgegroupId, AgegroupName = "U12", TeamIds = [f.TeamId] },
                        new LadtFeeResolutionMapBuilder.AgegroupInput
                        { AgegroupId = bucketAg.AgegroupId, AgegroupName = "WAITLIST - U12", TeamIds = [bucketTeam.TeamId] }
                    ]
                }
            ]
        };
        var map = await BuildMapAsync(f, tree);

        var league = Player(map, f.LeagueId);
        league.Below!.Amounts.OverrideCount.Should().Be(1,
            "only the real agegroup counts — the bucket and its team are not director-set scopes");

        // The bucket still gets its own node entries (its grid row must join).
        map.Nodes.Should().Contain(n => n.NodeId == bucketAg.AgegroupId);
        map.Nodes.Should().Contain(n => n.NodeId == bucketTeam.TeamId);
        Player(map, bucketAg.AgegroupId).Below!.Amounts.OverrideCount.Should().Be(0,
            "a bucket's own teams are excluded from its disclosure");
    }

    // ═══════════════════════════════════════════
    // TwoPhase
    // ═══════════════════════════════════════════

    [Fact(DisplayName = "TwoPhase: a team-level deposit+balance makes the verdict true at every level above")]
    public async Task TwoPhase_TeamDeposit_TrueUpTheChain()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, agegroupId: f.AgegroupId, teamId: f.TeamId,
            deposit: 50m, balanceDue: 120m);
        await f.Builder.SaveAsync();

        var map = await BuildMapAsync(f);
        Player(map, f.TeamId).TwoPhase.Should().BeTrue();
        Player(map, f.AgegroupId).TwoPhase.Should().BeTrue("a descendant resolves deposit+balance > 0");
        Player(map, f.LeagueId).TwoPhase.Should().BeTrue();
    }

    [Fact(DisplayName = "TwoPhase: balance-only everywhere is Single — even with a full-payment stamp present")]
    public async Task TwoPhase_NoDepositAnywhere_False()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, leagueId: f.LeagueId,
            balanceDue: 200m, bFullPaymentRequired: true);
        await f.Builder.SaveAsync();

        var map = await BuildMapAsync(f);
        foreach (var nodeId in new[] { f.LeagueId, f.AgegroupId, f.TeamId })
        {
            Player(map, nodeId).TwoPhase.Should().BeFalse("no deposit resolves anywhere in the subtree");
        }
    }

    // ═══════════════════════════════════════════
    // Shape guarantees
    // ═══════════════════════════════════════════

    [Fact(DisplayName = "Every node emits both roles; roles resolve independently")]
    public async Task Roles_AlwaysEmitted_IndependentlyResolved()
    {
        var f = Arrange();
        f.Builder.AddJobFee(f.JobId, RoleConstants.Player, leagueId: f.LeagueId, balanceDue: 200m);
        await f.Builder.SaveAsync();

        var map = await BuildMapAsync(f);
        map.Nodes.Should().HaveCount(3);
        map.Nodes.Select(n => n.Level).Should().BeEquivalentTo([0, 1, 3]);

        var teamNode = map.Nodes.Single(n => n.NodeId == f.TeamId);
        teamNode.Player.FeeConfigured.Should().BeTrue();
        teamNode.ClubRep.FeeConfigured.Should().BeFalse("no Club Rep row exists — roles never bleed");
        teamNode.ClubRep.RoleId.Should().Be(RoleConstants.ClubRep);

        // Levels 0/1 always carry a below summary (verified-empty ≠ missing); leaves never do.
        Player(map, f.LeagueId).Below.Should().NotBeNull();
        Player(map, f.AgegroupId).Below.Should().NotBeNull();
        Player(map, f.TeamId).Below.Should().BeNull();
    }
}
