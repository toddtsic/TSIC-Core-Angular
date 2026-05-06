using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.Domain.Entities;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.TeamRegistration.TeamNameSync;

// Locks in the SaveChanges chokepoint behavior: a team's name must stay in
// sync across ClubTeams.ClubTeamName, Teams.TeamName (across all jobs), and
// Schedule.T1Name/T2Name. See SqlDbContext.TeamNameSync.cs.
public class TeamNameSyncTests
{
    [Fact]
    public async Task Renaming_ClubTeam_propagates_to_every_linked_Teams_row_and_their_Schedule_rows()
    {
        await using var ctx = DbContextFactory.Create();
        var seed = SeedLibraryLinkedTeamInTwoJobs(ctx);
        await ctx.SaveChangesAsync();

        var clubTeam = await ctx.ClubTeams.FirstAsync(c => c.ClubTeamId == seed.ClubTeamId);
        clubTeam.ClubTeamName = "Renamed";
        await ctx.SaveChangesAsync();

        var teams = await ctx.Teams.AsNoTracking()
            .Where(t => t.ClubTeamId == seed.ClubTeamId).ToListAsync();
        teams.Should().OnlyContain(t => t.TeamName == "Renamed");

        var schedules = await ctx.Schedule.AsNoTracking().ToListAsync();
        schedules.Where(s => s.T1Id == seed.TeamA || s.T1Id == seed.TeamB)
            .Should().OnlyContain(s => s.T1Name == "Renamed");
        schedules.Where(s => s.T2Id == seed.TeamA || s.T2Id == seed.TeamB)
            .Should().OnlyContain(s => s.T2Name == "Renamed");
    }

    [Fact]
    public async Task Renaming_a_library_linked_Teams_row_propagates_back_to_ClubTeams_and_to_siblings()
    {
        await using var ctx = DbContextFactory.Create();
        var seed = SeedLibraryLinkedTeamInTwoJobs(ctx);
        await ctx.SaveChangesAsync();

        var teamA = await ctx.Teams.FirstAsync(t => t.TeamId == seed.TeamA);
        teamA.TeamName = "Renamed";
        await ctx.SaveChangesAsync();

        var ct = await ctx.ClubTeams.AsNoTracking()
            .FirstAsync(c => c.ClubTeamId == seed.ClubTeamId);
        ct.ClubTeamName.Should().Be("Renamed");

        var teamB = await ctx.Teams.AsNoTracking().FirstAsync(t => t.TeamId == seed.TeamB);
        teamB.TeamName.Should().Be("Renamed");

        var schedB = await ctx.Schedule.AsNoTracking()
            .FirstAsync(s => s.T1Id == seed.TeamB);
        schedB.T1Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Renaming_an_orphan_Team_does_not_touch_any_ClubTeams_row()
    {
        await using var ctx = DbContextFactory.Create();
        var seed = SeedOrphanAndLibraryLinkedTeams(ctx);
        await ctx.SaveChangesAsync();

        var orphan = await ctx.Teams.FirstAsync(t => t.TeamId == seed.OrphanTeamId);
        orphan.TeamName = "Orphan Renamed";
        await ctx.SaveChangesAsync();

        var libraryTeam = await ctx.ClubTeams.AsNoTracking()
            .FirstAsync(c => c.ClubTeamId == seed.ClubTeamId);
        libraryTeam.ClubTeamName.Should().Be("Original Library Name");

        var libraryLinkedTeam = await ctx.Teams.AsNoTracking()
            .FirstAsync(t => t.TeamId == seed.LibraryLinkedTeamId);
        libraryLinkedTeam.TeamName.Should().Be("Original Library Name");
    }

    [Fact]
    public async Task When_both_sides_modified_with_different_names_ClubTeams_wins()
    {
        await using var ctx = DbContextFactory.Create();
        var seed = SeedLibraryLinkedTeamInTwoJobs(ctx);
        await ctx.SaveChangesAsync();

        var clubTeam = await ctx.ClubTeams.FirstAsync(c => c.ClubTeamId == seed.ClubTeamId);
        var teamA = await ctx.Teams.FirstAsync(t => t.TeamId == seed.TeamA);

        clubTeam.ClubTeamName = "Library Wins";
        teamA.TeamName = "Teams Side Loses";
        await ctx.SaveChangesAsync();

        var finalCt = await ctx.ClubTeams.AsNoTracking()
            .FirstAsync(c => c.ClubTeamId == seed.ClubTeamId);
        finalCt.ClubTeamName.Should().Be("Library Wins");

        var finalTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.ClubTeamId == seed.ClubTeamId).ToListAsync();
        finalTeams.Should().OnlyContain(t => t.TeamName == "Library Wins");
    }

    // ── seed helpers ─────────────────────────────────────────────────────

    private record LibraryLinkedSeed(int ClubTeamId, Guid TeamA, Guid TeamB);
    private record OrphanAndLibrarySeed(int ClubTeamId, Guid LibraryLinkedTeamId, Guid OrphanTeamId);

    private static LibraryLinkedSeed SeedLibraryLinkedTeamInTwoJobs(Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var clubId = 1001;
        ctx.Clubs.Add(new Clubs { ClubId = clubId, ClubName = "Test Club" });

        var ct = new ClubTeams
        {
            ClubTeamId = 5001,
            ClubId = clubId,
            ClubTeamName = "Original",
            ClubTeamGradYear = "2030",
            Active = true,
        };
        ctx.ClubTeams.Add(ct);

        var jobA = SeedJob(ctx);
        var jobB = SeedJob(ctx);

        var teamA = SeedTeam(ctx, jobA, ct.ClubTeamId, "Original");
        var teamB = SeedTeam(ctx, jobB, ct.ClubTeamId, "Original");

        SeedGame(ctx, jobA, t1Id: teamA, t2Id: null, t1Name: "Original");
        SeedGame(ctx, jobA, t1Id: null, t2Id: teamA, t2Name: "Original");
        SeedGame(ctx, jobB, t1Id: teamB, t2Id: null, t1Name: "Original");
        SeedGame(ctx, jobB, t1Id: null, t2Id: teamB, t2Name: "Original");

        return new LibraryLinkedSeed(ct.ClubTeamId, teamA, teamB);
    }

    private static OrphanAndLibrarySeed SeedOrphanAndLibraryLinkedTeams(Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var clubId = 2001;
        ctx.Clubs.Add(new Clubs { ClubId = clubId, ClubName = "Test Club 2" });

        var ct = new ClubTeams
        {
            ClubTeamId = 6001,
            ClubId = clubId,
            ClubTeamName = "Original Library Name",
            ClubTeamGradYear = "2030",
            Active = true,
        };
        ctx.ClubTeams.Add(ct);

        var job = SeedJob(ctx);
        var libraryLinked = SeedTeam(ctx, job, ct.ClubTeamId, "Original Library Name");
        var orphan = SeedTeam(ctx, job, clubTeamId: null, teamName: "Orphan");

        return new OrphanAndLibrarySeed(ct.ClubTeamId, libraryLinked, orphan);
    }

    private static Guid SeedJob(Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var jobId = Guid.NewGuid();
        ctx.Jobs.Add(new Jobs
        {
            JobId = jobId,
            JobPath = $"job-{jobId:N}"[..20],
            RegformNamePlayer = "Player",
            RegformNameTeam = "Team",
            RegformNameCoach = "Coach",
            RegformNameClubRep = "Club Rep",
            BUseWaitlists = false,
            BShowTeamNameOnlyInSchedules = true,
            Modified = DateTime.UtcNow,
            ExpiryAdmin = DateTime.UtcNow.AddYears(1),
            ExpiryUsers = DateTime.UtcNow.AddYears(1),
        });
        return jobId;
    }

    private static Guid SeedTeam(
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx,
        Guid jobId,
        int? clubTeamId,
        string teamName)
    {
        var teamId = Guid.NewGuid();
        ctx.Teams.Add(new Teams
        {
            TeamId = teamId,
            JobId = jobId,
            LeagueId = Guid.NewGuid(),
            AgegroupId = Guid.NewGuid(),
            DivId = Guid.NewGuid(),
            ClubTeamId = clubTeamId,
            TeamName = teamName,
            Active = true,
            Season = "Spring",
            Year = "2026",
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        });
        return teamId;
    }

    private static int _gameCounter = 1;

    private static void SeedGame(
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx,
        Guid jobId,
        Guid? t1Id,
        Guid? t2Id,
        string? t1Name = null,
        string? t2Name = null)
    {
        ctx.Schedule.Add(new Schedule
        {
            Gid = _gameCounter++,
            JobId = jobId,
            LeagueId = Guid.NewGuid(),
            FieldId = Guid.NewGuid(),
            AgegroupId = Guid.NewGuid(),
            DivId = Guid.NewGuid(),
            T1Id = t1Id,
            T2Id = t2Id,
            T1Type = "T",
            T2Type = "T",
            T1Name = t1Name,
            T2Name = t2Name,
            GDate = DateTime.UtcNow,
            Rnd = 1,
            Season = "Spring",
            Year = "2026",
            LebUserId = "test-user",
            Modified = DateTime.UtcNow,
        });
    }
}
