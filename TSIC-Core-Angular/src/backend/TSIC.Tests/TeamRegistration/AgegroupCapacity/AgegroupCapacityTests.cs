using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.TeamRegistration.AgegroupCapacity;

/// <summary>
/// AGEGROUP CAPACITY TESTS
///
/// Validates that TeamPlacementService correctly enforces agegroup-level
/// team limits (MaxTeams) and creates WAITLIST agegroups when enabled.
///
/// What these tests prove:
///   - Under capacity → team placed in original agegroup
///   - At capacity, no waitlists → throws InvalidOperationException
///   - At capacity, with waitlists → creates WAITLIST mirror agegroup in DB
///   - WAITLIST agegroup has correct name ("WAITLIST - {original}")
///   - Second overflow reuses existing WAITLIST (idempotent)
///
/// Hybrid approach: real AgeGroupRepository + DivisionRepository (in-memory DB)
/// with mocked ITeamRepository (to control the registered count) and
/// mocked IJobRepository (to control the waitlist feature flag).
/// </summary>
public class AgegroupCapacityTests
{
    // ── Factory: builds service with real repos + mock count control ──

    private static async Task<(
        TeamPlacementService svc,
        RegistrationDataBuilder builder,
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx,
        Mock<IJobRepository> jobRepo,
        Mock<ITeamRepository> teamRepo,
        Guid jobId,
        Agegroups agegroup)>
        CreateServiceAsync(int maxTeams = 16, bool usesWaitlists = false)
    {
        var ctx = DbContextFactory.Create();
        var builder = new RegistrationDataBuilder(ctx);

        // Seed: job → league → agegroup
        var job = builder.AddJob(bUseWaitlists: usesWaitlists);
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, maxTeams: maxTeams, name: "Boys U14");
        await builder.SaveAsync();

        // Mock: job repo controls the waitlist feature flag
        var jobRepo = new Mock<IJobRepository>();
        jobRepo
            .Setup(j => j.GetUsesWaitlistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usesWaitlists);

        // Mock: team repo controls the registered count (this is what we vary per test)
        var teamRepo = new Mock<ITeamRepository>();

        // Real repos backed by in-memory DB (so WAITLIST entities get persisted)
        var agegroupRepo = new AgeGroupRepository(ctx);
        var divisionRepo = new DivisionRepository(ctx);

        var svc = new TeamPlacementService(
            jobRepo.Object, agegroupRepo, teamRepo.Object, divisionRepo);

        return (svc, builder, ctx, jobRepo, teamRepo, job.JobId, ag);
    }

    // ── Tests ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "Placement: under capacity (15/16) → original agegroup, not waitlisted")]
    public async Task Placement_UnderCapacity_ReturnsOriginalAgegroup()
    {
        // Arrange — 15 teams registered, max is 16
        var (svc, _, _, _, teamRepo, jobId, ag) = await CreateServiceAsync(maxTeams: 16);
        teamRepo
            .Setup(t => t.GetRegisteredCountForAgegroupAsync(jobId, ag.AgegroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(15);

        // Act
        var result = await svc.ResolvePlacementAsync(jobId, ag.AgegroupId, "Test Team");

        // Assert — placed in the original agegroup
        result.AgegroupId.Should().Be(ag.AgegroupId, "team should go to the original agegroup");
        result.IsWaitlisted.Should().BeFalse();
    }

    [Fact(DisplayName = "Placement: at capacity (16/16), BUseWaitlists OFF → still creates waitlist (teams always waitlist)")]
    public async Task Placement_AtCapacity_NoWaitlistFlag_StillCreatesWaitlist()
    {
        // Arrange — 16 teams registered, max is 16, BUseWaitlists OFF
        // Team registration always auto-creates waitlists regardless of BUseWaitlists
        // (that flag is player-registration-only)
        var (svc, _, ctx, _, teamRepo, jobId, ag) = await CreateServiceAsync(maxTeams: 16, usesWaitlists: false);
        teamRepo
            .Setup(t => t.GetRegisteredCountForAgegroupAsync(jobId, ag.AgegroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(16);

        // Act
        var result = await svc.ResolvePlacementAsync(jobId, ag.AgegroupId, "Test Team");

        // Assert — redirected to waitlist, NOT rejected
        result.IsWaitlisted.Should().BeTrue();
        result.AgegroupId.Should().NotBe(ag.AgegroupId, "should be a new WAITLIST agegroup");

        var allAgegroups = await ctx.Agegroups.ToListAsync();
        allAgegroups.Should().HaveCount(2, "original + WAITLIST mirror");
    }

    [Fact(DisplayName = "Placement: at capacity (16/16), waitlists ON → creates WAITLIST agegroup")]
    public async Task Placement_AtCapacity_WithWaitlists_CreatesWaitlistAgegroup()
    {
        // Arrange — 16 teams registered, max is 16, waitlists ON
        var (svc, _, ctx, _, teamRepo, jobId, ag) = await CreateServiceAsync(maxTeams: 16, usesWaitlists: true);
        teamRepo
            .Setup(t => t.GetRegisteredCountForAgegroupAsync(jobId, ag.AgegroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(16);

        // Act
        var result = await svc.ResolvePlacementAsync(jobId, ag.AgegroupId, "Test Team");

        // Assert — redirected to a new agegroup, flagged as waitlisted
        result.IsWaitlisted.Should().BeTrue();
        result.AgegroupId.Should().NotBe(ag.AgegroupId, "should be a new WAITLIST agegroup, not the original");

        // Verify the WAITLIST agegroup was actually persisted in the database
        var allAgegroups = await ctx.Agegroups.ToListAsync();
        allAgegroups.Should().HaveCount(2, "original + WAITLIST mirror");
    }

    [Fact(DisplayName = "Placement: WAITLIST agegroup is named 'WAITLIST - {original name}'")]
    public async Task Placement_WaitlistAgegroup_NamedCorrectly()
    {
        // Arrange
        var (svc, _, ctx, _, teamRepo, jobId, ag) = await CreateServiceAsync(maxTeams: 16, usesWaitlists: true);
        teamRepo
            .Setup(t => t.GetRegisteredCountForAgegroupAsync(jobId, ag.AgegroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(16);

        // Act
        var result = await svc.ResolvePlacementAsync(jobId, ag.AgegroupId, "Test Team");

        // Assert — the WAITLIST agegroup has the expected naming convention
        result.WaitlistAgegroupName.Should().Be("WAITLIST - Boys U14");

        // Verify in the actual database
        var waitlistAg = await ctx.Agegroups.FirstAsync(a => a.AgegroupId == result.AgegroupId);
        waitlistAg.AgegroupName.Should().Be("WAITLIST - Boys U14");
    }

    [Fact(DisplayName = "Placement: second overflow reuses existing WAITLIST agegroup (idempotent)")]
    public async Task Placement_SecondOverflow_ReusesExistingWaitlist()
    {
        // Arrange — overflow twice
        var (svc, _, ctx, _, teamRepo, jobId, ag) = await CreateServiceAsync(maxTeams: 16, usesWaitlists: true);
        teamRepo
            .Setup(t => t.GetRegisteredCountForAgegroupAsync(jobId, ag.AgegroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(16);

        // Act — two separate overflow calls
        var result1 = await svc.ResolvePlacementAsync(jobId, ag.AgegroupId, "Team Alpha");
        var result2 = await svc.ResolvePlacementAsync(jobId, ag.AgegroupId, "Team Beta");

        // Assert — both redirected to the SAME waitlist agegroup
        result1.AgegroupId.Should().Be(result2.AgegroupId,
            "second overflow should reuse the existing WAITLIST agegroup, not create a duplicate");

        // Only 2 agegroups total (original + 1 waitlist)
        var count = await ctx.Agegroups.CountAsync();
        count.Should().Be(2, "should not create a second WAITLIST agegroup");
    }
}
