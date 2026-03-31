using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Seeds test data for mobile app endpoint tests (TSIC-Events + TSIC-Teams).
///
/// Usage:
///   var ctx = DbContextFactory.Create();
///   var b = new MobileDataBuilder(ctx);
///   var job = b.AddJob("Summer Cup", "summer-cup");
///   var league = b.AddLeague(job.JobId);
///   var ag = b.AddAgegroup(league.LeagueId, "U10");
///   var div = b.AddDivision(ag.AgegroupId, "Gold");
///   var t1 = b.AddTeam(div.DivId, "Eagles");
///   var t2 = b.AddTeam(div.DivId, "Hawks");
///   var field = b.AddField("Field 1", "Allentown", "PA");
///   b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId, t1.TeamId, t2.TeamId, DateTime.Today, rnd: 1);
///   await b.SaveAsync();
/// </summary>
public class MobileDataBuilder
{
    private readonly SqlDbContext _ctx;
    private int _gameCounter = 1;

    public MobileDataBuilder(SqlDbContext ctx)
    {
        _ctx = ctx;
        SeedSport();
        SeedRoles();
        SeedDefaultUser();
    }

    // Well-known IDs
    public static readonly Guid DefaultSportId = Guid.Parse("A1B2C3D4-0000-0000-0000-000000000001");
    public static readonly string DefaultUserId = "test-user-001";

    private void SeedSport()
    {
        _ctx.Sports.Add(new Sports
        {
            SportId = DefaultSportId,
            SportName = "Soccer",
            Ai = 1,
            Modified = DateTime.UtcNow
        });
    }

    private void SeedDefaultUser()
    {
        _ctx.AspNetUsers.Add(new AspNetUsers
        {
            Id = DefaultUserId,
            UserName = "system",
            NormalizedUserName = "SYSTEM",
            FirstName = "System",
            LastName = "User",
            LebUserId = DefaultUserId
        });
    }

    private void SeedRoles()
    {
        _ctx.AspNetRoles.AddRange(
            new AspNetRoles { Id = RoleConstants.Player, Name = "Player", NormalizedName = "PLAYER" },
            new AspNetRoles { Id = RoleConstants.Staff, Name = "Staff", NormalizedName = "STAFF" },
            new AspNetRoles { Id = RoleConstants.Director, Name = "Director", NormalizedName = "DIRECTOR" }
        );
    }

    // ── Jobs ──

    public Jobs AddJob(
        string name = "Test Tournament",
        string jobPath = "test-tournament",
        bool publicAccess = true,
        bool suspended = false,
        DateTime? expiry = null,
        Guid? sportId = null)
    {
        var job = new Jobs
        {
            JobId = Guid.NewGuid(),
            JobName = name,
            JobPath = jobPath,
            SportId = sportId ?? DefaultSportId,
            BScheduleAllowPublicAccess = publicAccess,
            BSuspendPublic = suspended,
            ExpiryUsers = expiry ?? DateTime.UtcNow.AddDays(30),
            RegformNamePlayer = "Player",
            RegformNameTeam = "Team",
            RegformNameCoach = "Coach",
            RegformNameClubRep = "Club Rep",
            Modified = DateTime.UtcNow
        };
        _ctx.Jobs.Add(job);

        // Add display options (required for logo queries)
        _ctx.JobDisplayOptions.Add(new JobDisplayOptions
        {
            JobId = job.JobId,
            LogoHeader = $"{job.JobId}_logoheader.png",
            LebUserId = DefaultUserId,
            Modified = DateTime.UtcNow
        });

        return job;
    }

    // ── League hierarchy ──

    public Leagues AddLeague(Guid jobId, Guid? sportId = null)
    {
        var league = new Leagues
        {
            LeagueId = Guid.NewGuid(),
            SportId = sportId ?? DefaultSportId,
            Modified = DateTime.UtcNow
        };
        _ctx.Leagues.Add(league);
        _ctx.JobLeagues.Add(new JobLeagues
        {
            JobId = jobId,
            LeagueId = league.LeagueId,
            Modified = DateTime.UtcNow
        });
        return league;
    }

    public Agegroups AddAgegroup(Guid leagueId, string name = "U10", string? color = "#FF0000")
    {
        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = name,
            Color = color,
            Modified = DateTime.UtcNow
        };
        _ctx.Agegroups.Add(ag);
        return ag;
    }

    public Divisions AddDivision(Guid agegroupId, string name = "Gold")
    {
        var div = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = agegroupId,
            DivName = name,
            Modified = DateTime.UtcNow
        };
        _ctx.Divisions.Add(div);
        return div;
    }

    public Teams AddTeam(Guid divId, string name = "Test Team", Guid? agegroupId = null)
    {
        var team = new Teams
        {
            TeamId = Guid.NewGuid(),
            DivId = divId,
            AgegroupId = agegroupId ?? Guid.Empty,
            TeamName = name,
            Active = true,
            Modified = DateTime.UtcNow
        };
        _ctx.Teams.Add(team);
        return team;
    }

    // ── Fields ──

    public Fields AddField(
        string name = "Field 1",
        string? city = "Allentown",
        string? state = "PA",
        double? lat = 40.6,
        double? lon = -75.5,
        string? address = "123 Main St",
        string? zip = "18101")
    {
        var field = new Fields
        {
            FieldId = Guid.NewGuid(),
            FName = name,
            City = city,
            State = state,
            Latitude = lat,
            Longitude = lon,
            Address = address,
            Zip = zip,
            Modified = DateTime.UtcNow
        };
        _ctx.Fields.Add(field);
        return field;
    }

    // ── Schedule (Games) ──

    public Schedule AddGame(
        Guid jobId, Guid leagueId, Guid fieldId,
        Guid agegroupId, Guid divId,
        Guid? t1Id, Guid? t2Id,
        DateTime gDate,
        byte rnd = 1,
        string t1Type = "T", string t2Type = "T",
        string? t1Name = null, string? t2Name = null,
        string? agegroupName = null, string? divName = null,
        int? t1Score = null, int? t2Score = null)
    {
        var game = new Schedule
        {
            Gid = _gameCounter++,
            JobId = jobId,
            LeagueId = leagueId,
            FieldId = fieldId,
            AgegroupId = agegroupId,
            DivId = divId,
            T1Id = t1Id,
            T2Id = t2Id,
            T1Name = t1Name,
            T2Name = t2Name,
            T1Type = t1Type,
            T2Type = t2Type,
            T1Score = t1Score,
            T2Score = t2Score,
            GDate = gDate,
            Rnd = rnd,
            AgegroupName = agegroupName,
            DivName = divName,
            Season = "Spring",
            Year = "2026",
            LebUserId = DefaultUserId,
            Modified = DateTime.UtcNow
        };
        _ctx.Schedule.Add(game);
        return game;
    }

    public void SetGameScore(int gid, int t1Score, int t2Score)
    {
        var game = _ctx.Schedule.Local.FirstOrDefault(g => g.Gid == gid);
        if (game != null)
        {
            game.T1Score = t1Score;
            game.T2Score = t2Score;
            game.GStatusCode = 2; // Completed
        }
    }

    // ── Devices ──

    public Devices AddDevice(string token = "device-token-001", string type = "ios")
    {
        var device = new Devices
        {
            Id = token,
            Token = token,
            Type = type,
            Active = true,
            Modified = DateTime.UtcNow
        };
        _ctx.Devices.Add(device);
        return device;
    }

    public DeviceJobs AddDeviceJob(string deviceId, Guid jobId)
    {
        var dj = new DeviceJobs
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            JobId = jobId,
            Modified = DateTime.UtcNow
        };
        _ctx.DeviceJobs.Add(dj);
        return dj;
    }

    public DeviceTeams AddDeviceTeam(string deviceId, Guid teamId)
    {
        var dt = new DeviceTeams
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            TeamId = teamId,
            Modified = DateTime.UtcNow
        };
        _ctx.DeviceTeams.Add(dt);
        return dt;
    }

    // ── Event Docs & Alerts ──

    public TeamDocs AddJobDoc(Guid jobId, string label, string docUrl, string userId = "")
    {
        if (string.IsNullOrEmpty(userId)) userId = DefaultUserId;
        var doc = new TeamDocs
        {
            DocId = Guid.NewGuid(),
            JobId = jobId,
            Label = label,
            DocUrl = docUrl,
            UserId = userId,
            CreateDate = DateTime.UtcNow
        };
        _ctx.TeamDocs.Add(doc);
        return doc;
    }

    public JobPushNotificationsToAll AddPushAlert(Guid jobId, string pushText, string userId = "")
    {
        if (string.IsNullOrEmpty(userId)) userId = DefaultUserId;
        var alert = new JobPushNotificationsToAll
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            LebUserId = userId,
            PushText = pushText,
            DeviceCount = 0,
            Modified = DateTime.UtcNow
        };
        _ctx.JobPushNotificationsToAll.Add(alert);
        return alert;
    }

    public GameClockParams AddGameClockParams(
        Guid jobId,
        decimal halfMinutes = 25,
        decimal halfTimeMinutes = 5,
        decimal transitionMinutes = 5,
        decimal playoffMinutes = 30)
    {
        var gc = new GameClockParams
        {
            JobId = jobId,
            HalfMinutes = halfMinutes,
            HalfTimeMinutes = halfTimeMinutes,
            TransitionMinutes = transitionMinutes,
            PlayoffMinutes = playoffMinutes,
            Modified = DateTime.UtcNow
        };
        _ctx.GameClockParams.Add(gc);
        return gc;
    }

    // ── Users & Registrations ──

    public AspNetUsers AddUser(
        string username = "testuser",
        string? firstName = "Test",
        string? lastName = "User",
        string? email = "test@example.com")
    {
        var id = Guid.NewGuid().ToString();
        var user = new AspNetUsers
        {
            Id = id,
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            NormalizedEmail = email?.ToUpperInvariant(),
            LebUserId = id
        };
        _ctx.AspNetUsers.Add(user);
        return user;
    }

    public Registrations AddRegistration(
        string userId, Guid jobId, string roleId,
        Guid? teamId = null, bool active = true)
    {
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            UserId = userId,
            JobId = jobId,
            RoleId = roleId,
            AssignedTeamId = teamId,
            BActive = active,
            Modified = DateTime.UtcNow
        };
        _ctx.Registrations.Add(reg);
        return reg;
    }

    public async Task SaveAsync() => await _ctx.SaveChangesAsync();
}
