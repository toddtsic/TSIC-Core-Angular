using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Seeds test data for registration capacity tests.
///
/// Two modes:
///   1. Entity-only (for Moq tests) — call Build* methods, use objects in mock setups
///   2. In-memory DB (for placement tests) — call Add* methods, then SaveAsync()
///
/// Usage (mock mode):
///   var b = new RegistrationDataBuilder();
///   var team = b.BuildTeam(jobId, agegroupId, maxCount: 10);
///
/// Usage (in-memory DB mode):
///   var ctx = DbContextFactory.Create();
///   var b = new RegistrationDataBuilder(ctx);
///   var job = b.AddJob();
///   var league = b.AddLeague(job.JobId);
///   var ag = b.AddAgegroup(league.LeagueId, maxTeams: 16);
///   await b.SaveAsync();
/// </summary>
public class RegistrationDataBuilder
{
    private readonly SqlDbContext? _ctx;

    /// <summary>Mock-only mode — builds entities without DB persistence.</summary>
    public RegistrationDataBuilder() { }

    /// <summary>In-memory DB mode — entities are added to context for persistence.</summary>
    public RegistrationDataBuilder(SqlDbContext ctx) => _ctx = ctx;

    // ── Build methods (mock mode — returns detached entities) ──

    public static Jobs BuildJob(bool bUseWaitlists = false)
    {
        return new Jobs
        {
            JobId = Guid.NewGuid(),
            JobPath = $"test-{Guid.NewGuid():N}"[..20],
            RegformNamePlayer = "Player",
            RegformNameTeam = "Team",
            RegformNameCoach = "Coach",
            RegformNameClubRep = "Club Rep",
            BUseWaitlists = bUseWaitlists,
            Modified = DateTime.UtcNow,
            ExpiryAdmin = DateTime.UtcNow.AddYears(1),
            ExpiryUsers = DateTime.UtcNow.AddYears(1),
            SportId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
        };
    }

    public static Agegroups BuildAgegroup(Guid leagueId, int maxTeams = 100, int maxTeamsPerClub = 0)
    {
        return new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = $"Boys U12 Test",
            MaxTeams = maxTeams,
            MaxTeamsPerClub = maxTeamsPerClub,
            Modified = DateTime.UtcNow,
        };
    }

    public static Teams BuildTeam(Guid jobId, Guid agegroupId, int maxCount = 0, Guid? teamId = null)
    {
        return new Teams
        {
            TeamId = teamId ?? Guid.NewGuid(),
            JobId = jobId,
            AgegroupId = agegroupId,
            TeamName = $"Test Team {Guid.NewGuid():N}"[..20],
            MaxCount = maxCount,
            Active = true,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
    }

    public static Registrations BuildRegistration(
        Guid jobId,
        Guid? teamId = null,
        string? playerId = null,
        string? familyUserId = null)
    {
        return new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            AssignedTeamId = teamId,
            UserId = playerId ?? Guid.NewGuid().ToString(),
            FamilyUserId = familyUserId ?? "family-test",
            BActive = false,
            Modified = DateTime.UtcNow,
            RoleId = "DAC0C570-94AA-4A88-8D73-6034F1F72F3A", // Player
        };
    }

    // ── Add methods (in-memory DB mode) ──

    public Jobs AddJob(bool bUseWaitlists = false)
    {
        var job = BuildJob(bUseWaitlists);
        _ctx!.Jobs.Add(job);
        return job;
    }

    public Leagues AddLeague(Guid jobId, string name = "Test League")
    {
        var league = new Leagues
        {
            LeagueId = Guid.NewGuid(),
            LeagueName = name,
            Modified = DateTime.UtcNow,
        };
        _ctx!.Leagues.Add(league);

        // Junction table
        _ctx.JobLeagues.Add(new JobLeagues
        {
            JobLeagueId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = league.LeagueId,
            BIsPrimary = true,
        });

        return league;
    }

    public Agegroups AddAgegroup(Guid leagueId, int maxTeams = 100, int maxTeamsPerClub = 0, string name = "Boys U12 Test")
    {
        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = name,
            MaxTeams = maxTeams,
            MaxTeamsPerClub = maxTeamsPerClub,
            Modified = DateTime.UtcNow,
        };
        _ctx!.Agegroups.Add(ag);
        return ag;
    }

    public Divisions AddDivision(Guid agegroupId, string name = "Division A")
    {
        var div = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = agegroupId,
            DivName = name,
            Modified = DateTime.UtcNow,
        };
        _ctx!.Divisions.Add(div);
        return div;
    }

    public Teams AddTeam(Guid jobId, Guid agegroupId, Guid leagueId, int maxCount = 0, string name = "Test Team")
    {
        var team = BuildTeam(jobId, agegroupId, maxCount);
        team.LeagueId = leagueId;
        team.TeamName = name;
        _ctx!.Teams.Add(team);
        return team;
    }

    public async Task SaveAsync() => await _ctx!.SaveChangesAsync();
}
