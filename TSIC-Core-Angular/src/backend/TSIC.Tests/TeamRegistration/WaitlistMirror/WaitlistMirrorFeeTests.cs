using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.TeamRegistration.WaitlistMirror;

/// <summary>
/// WAITLIST MIRROR — FEE + CAPACITY TESTS
///
/// The money fix behind Ann's "full team shows the full price" bug. When a real
/// team fills, a WAITLIST mirror team is minted and stamped with an explicit
/// $0 Player <c>fees.JobFees</c> row at TEAM scope (cascade priority 3). Without
/// that row the resolver would cascade a waitlisted player up to the league tier
/// (charged) or fail loud with "Fee not set".
///
/// What these prove (all against a real fee cascade on the in-memory DB):
///   - Filling a team mints the twin AND stamps it $0 → resolver returns
///     (0, 0, FeeConfigured = true), never "Fee not set".
///   - Capacity counts PENDING players (BActive = false) — a pending registrant
///     consumes a spot, so the next registrant waitlists.
///   - GetAssignedPlayerCountAsync = active + pending PLAYERS, excluding staff.
///   - Mint is idempotent — a second overflow reuses the twin and its single $0 row.
///   - EnsureWaitlistMirrorAsync mints proactively with NO capacity check (mint-on-fill).
///
/// Waitlists are now MANDATORY for every job, so there is no "does not use waitlists"
/// hard-stop path to test — a full team always routes to its twin.
///
/// Hybrid: real AgeGroup/Division/Team/Fee repositories (in-memory DB, so the
/// minted mirror + its $0 row persist and re-resolve) + mocked IJobRepository.
/// </summary>
public class WaitlistMirrorFeeTests
{
    private static async Task<(
        TeamPlacementService svc,
        SqlDbContext ctx,
        Guid jobId,
        Teams realTeam,
        Agegroups agegroup)>
        CreateAsync(
            int maxCount = 1,
            int activePlayers = 0,
            int pendingPlayers = 0,
            int staff = 0)
    {
        var ctx = DbContextFactory.Create();
        var builder = new RegistrationDataBuilder(ctx);

        var job = builder.AddJob();
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, maxTeams: 100, name: "Boys U14");
        var div = builder.AddDivision(ag.AgegroupId, "Division A");
        var team = builder.AddTeam(job.JobId, ag.AgegroupId, league.LeagueId, maxCount: maxCount, name: "Hawks");
        team.DivId = div.DivId;

        for (var i = 0; i < activePlayers; i++)
            ctx.Registrations.Add(MakeReg(job.JobId, team.TeamId, RoleConstants.Player, bActive: true));
        for (var i = 0; i < pendingPlayers; i++)
            ctx.Registrations.Add(MakeReg(job.JobId, team.TeamId, RoleConstants.Player, bActive: false));
        for (var i = 0; i < staff; i++)
            ctx.Registrations.Add(MakeReg(job.JobId, team.TeamId, RoleConstants.Staff, bActive: true));

        await builder.SaveAsync();

        var jobRepo = new Mock<IJobRepository>();
        jobRepo
            .Setup(j => j.GetUsesWaitlistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // waitlists are mandatory

        var svc = new TeamPlacementService(
            jobRepo.Object,
            new AgeGroupRepository(ctx),
            new TeamRepository(ctx),
            new DivisionRepository(ctx),
            new FeeRepository(ctx));

        return (svc, ctx, job.JobId, team, ag);
    }

    private static Registrations MakeReg(Guid jobId, Guid teamId, string roleId, bool bActive) => new()
    {
        RegistrationId = Guid.NewGuid(),
        JobId = jobId,
        AssignedTeamId = teamId,
        UserId = Guid.NewGuid().ToString(),
        FamilyUserId = "fam-test",
        RoleId = roleId,
        BActive = bActive,
        // Fresh in-flight reservation: a pending reg counts toward capacity only while its
        // RegistrationTs is inside the hold window (SeatHoldPolicy). Stamp "now" so these
        // pending-consumes-a-spot cases exercise the within-window path; a LAPSED pending
        // reg (old timestamp) no longer counts — covered by its own test.
        RegistrationTs = DateTime.Now,
        Modified = DateTime.UtcNow,
    };

    // ── The money fix: mint stamps a $0 team-scoped row ────────────────

    [Fact(DisplayName = "Full team (active player) → mints WAITLIST twin stamped $0, resolver returns FeeConfigured + $0")]
    public async Task FullByActivePlayer_MintsTwin_WithZeroFeeRow()
    {
        var (svc, ctx, jobId, realTeam, _) = await CreateAsync(maxCount: 1, activePlayers: 1);

        var result = await svc.ResolveRosterPlacementAsync(jobId, realTeam.TeamId);

        result.IsWaitlisted.Should().BeTrue();
        result.TeamId.Should().NotBe(realTeam.TeamId, "registration must route to the twin, not the full real team");

        var twin = await ctx.Teams.SingleAsync(t => t.TeamId == result.TeamId);
        twin.TeamName.Should().Be("WAITLIST - Hawks");

        // The $0 stamp exists at TEAM scope on the twin (cascade priority 3).
        var feeRow = await ctx.JobFees.SingleOrDefaultAsync(f =>
            f.TeamId == twin.TeamId && f.RoleId == RoleConstants.Player);
        feeRow.Should().NotBeNull("the mint must stamp an explicit $0 Player fee row on the twin");
        feeRow!.Deposit.Should().Be(0m);
        feeRow.BalanceDue.Should().Be(0m);

        // And the cascade resolves it as configured-and-free, NOT "Fee not set".
        var resolved = await new FeeRepository(ctx)
            .GetResolvedFeeAsync(jobId, RoleConstants.Player, twin.AgegroupId, twin.TeamId);
        resolved!.FeeConfigured.Should().BeTrue("a $0 row is configured — the fail-loud block must not fire");
        resolved.EffectiveBalanceDue.Should().Be(0m);
        resolved.EffectiveDeposit.Should().Be(0m);
    }

    // ── Capacity includes pending ──────────────────────────────────────

    [Fact(DisplayName = "Capacity: a single PENDING (BActive=false) player fills a max-1 team → next registrant waitlists")]
    public async Task PendingPlayer_ConsumesSpot_NextRegistrantWaitlists()
    {
        // No active players — only one pending. Pre-fix this counted as 0 and the next
        // registrant landed on the real team at full price (the label/charge divergence).
        var (svc, _, jobId, realTeam, _) = await CreateAsync(maxCount: 1, pendingPlayers: 1);

        var result = await svc.ResolveRosterPlacementAsync(jobId, realTeam.TeamId);

        result.IsWaitlisted.Should().BeTrue("a pending registrant consumes a roster spot");
    }

    [Fact(DisplayName = "Capacity: under max → places on the real team, no twin minted")]
    public async Task UnderCapacity_PlacesReal_NoMint()
    {
        var (svc, ctx, jobId, realTeam, _) = await CreateAsync(maxCount: 2, activePlayers: 1);

        var result = await svc.ResolveRosterPlacementAsync(jobId, realTeam.TeamId);

        result.IsWaitlisted.Should().BeFalse();
        result.TeamId.Should().Be(realTeam.TeamId);
        (await ctx.Teams.AnyAsync(t => t.TeamName == "WAITLIST - Hawks"))
            .Should().BeFalse("no overflow → nothing should be minted");
    }

    [Fact(DisplayName = "GetAssignedPlayerCountAsync = active + pending PLAYERS, excludes staff")]
    public async Task AssignedPlayerCount_CountsActivePlusPending_ExcludesStaff()
    {
        var (_, ctx, _, realTeam, _) = await CreateAsync(
            maxCount: 10, activePlayers: 1, pendingPlayers: 1, staff: 1);

        var count = await new TeamRepository(ctx).GetAssignedPlayerCountAsync(realTeam.TeamId);

        count.Should().Be(2, "1 active + 1 pending player; the staff member must not inflate roster capacity");
    }

    // ── Idempotency ────────────────────────────────────────────────────

    [Fact(DisplayName = "Second overflow reuses the same twin and its single $0 fee row (idempotent)")]
    public async Task SecondOverflow_ReusesTwin_SingleFeeRow()
    {
        var (svc, ctx, jobId, realTeam, _) = await CreateAsync(maxCount: 1, activePlayers: 1);

        var first = await svc.ResolveRosterPlacementAsync(jobId, realTeam.TeamId);
        var second = await svc.ResolveRosterPlacementAsync(jobId, realTeam.TeamId);

        second.TeamId.Should().Be(first.TeamId, "both overflows route to the same twin");

        (await ctx.Teams.CountAsync(t => t.TeamName == "WAITLIST - Hawks"))
            .Should().Be(1, "must not mint a duplicate twin");
        (await ctx.JobFees.CountAsync(f => f.TeamId == first.TeamId && f.RoleId == RoleConstants.Player))
            .Should().Be(1, "must not stamp a duplicate $0 row");
    }

    // ── Mint-on-fill (proactive, no capacity check) ────────────────────

    [Fact(DisplayName = "EnsureWaitlistMirrorAsync mints the twin + its $0 row proactively, even under capacity")]
    public async Task EnsureWaitlistMirror_MintsProactively_NoCapacityCheck()
    {
        // Under capacity (0/10): the proactive mint-on-fill hook fires the instant a team
        // reaches max, so it must NOT gate on a capacity recount.
        var (svc, ctx, jobId, realTeam, _) = await CreateAsync(maxCount: 10, activePlayers: 0);

        await svc.EnsureWaitlistMirrorAsync(jobId, realTeam.TeamId);

        var twin = await ctx.Teams.SingleAsync(t => t.TeamName == "WAITLIST - Hawks");
        var resolved = await new FeeRepository(ctx)
            .GetResolvedFeeAsync(jobId, RoleConstants.Player, twin.AgegroupId, twin.TeamId);
        resolved!.FeeConfigured.Should().BeTrue();
        resolved.EffectiveBalanceDue.Should().Be(0m);
    }
}
