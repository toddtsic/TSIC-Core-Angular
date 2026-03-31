using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Seeds test data for registration and team search tests.
///
/// Usage:
///   var ctx = DbContextFactory.Create();
///   var b = new SearchDataBuilder(ctx);
///   var job = b.AddJob();
///   var league = b.AddLeague(job.JobId);
///   var ag = b.AddAgegroup(league.LeagueId, "U14 Boys");
///   var user = b.AddUser("John", "Doe", "john@test.com");
///   var role = b.AddRole(RoleConstants.Player, "Player");
///   var reg = b.AddRegistration(job.JobId, user.Id, role.Id, feeTotal: 100m);
///   await b.SaveAsync();
/// </summary>
public class SearchDataBuilder
{
    private readonly SqlDbContext _ctx;

    public SearchDataBuilder(SqlDbContext ctx)
    {
        _ctx = ctx;
    }

    // ── Job ──

    public Jobs AddJob()
    {
        var job = new Jobs
        {
            JobId = Guid.NewGuid(),
            JobPath = $"test-job-{Guid.NewGuid():N}"[..20],
            RegformNamePlayer = "Player",
            RegformNameTeam = "Team",
            RegformNameCoach = "Coach",
            RegformNameClubRep = "Club Rep",
            Modified = DateTime.UtcNow
        };
        _ctx.Jobs.Add(job);
        return job;
    }

    // ── User (AspNetUsers) ──

    public AspNetUsers AddUser(
        string firstName = "Test",
        string lastName = "User",
        string? email = null,
        string? cellphone = null,
        string? gender = null,
        DateTime? dob = null)
    {
        var id = Guid.NewGuid().ToString();
        var resolvedEmail = email ?? $"{firstName.ToLower()}.{lastName.ToLower()}@test.com";
        var user = new AspNetUsers
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Email = resolvedEmail,
            UserName = resolvedEmail,
            NormalizedEmail = resolvedEmail.ToUpper(),
            NormalizedUserName = resolvedEmail.ToUpper(),
            Cellphone = cellphone,
            Gender = gender,
            Dob = dob,
            SecurityStamp = Guid.NewGuid().ToString(),
            LebUserId = id,
            Modified = DateTime.UtcNow
        };
        _ctx.AspNetUsers.Add(user);
        return user;
    }

    // ── Role (AspNetRoles) ──

    public AspNetRoles AddRole(string roleId, string name)
    {
        var role = new AspNetRoles
        {
            Id = roleId,
            Name = name,
            NormalizedName = name.ToUpper()
        };
        _ctx.AspNetRoles.Add(role);
        return role;
    }

    // ── League + Agegroup + Division ──

    public Leagues AddLeague(Guid jobId)
    {
        var league = new Leagues
        {
            LeagueId = Guid.NewGuid(),
            SportId = Guid.NewGuid(),
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

    public Agegroups AddAgegroup(Guid leagueId, string name = "U14 Boys")
    {
        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = name,
            Modified = DateTime.UtcNow
        };
        _ctx.Agegroups.Add(ag);
        return ag;
    }

    public Divisions AddDivision(Guid agegroupId, string name = "Division 1")
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

    // ── Registration ──

    public Registrations AddRegistration(
        Guid jobId,
        string userId,
        string roleId,
        decimal feeBase = 100m,
        decimal feeProcessing = 0m,
        decimal feeTotal = 100m,
        decimal paidTotal = 0m,
        bool active = true,
        string? clubName = null,
        string? position = null,
        string? gradYear = null,
        string? schoolGrade = null,
        string? schoolName = null,
        Guid? assignedTeamId = null,
        Guid? assignedAgegroupId = null,
        Guid? assignedDivId = null,
        DateTime? registrationTs = null)
    {
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            UserId = userId,
            RoleId = roleId,
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            FeeDiscount = 0,
            FeeDonation = 0,
            FeeLatefee = 0,
            FeeTotal = feeTotal,
            PaidTotal = paidTotal,
            OwedTotal = feeTotal - paidTotal,
            BActive = active,
            ClubName = clubName,
            Position = position,
            GradYear = gradYear,
            SchoolGrade = schoolGrade,
            SchoolName = schoolName,
            AssignedTeamId = assignedTeamId,
            AssignedAgegroupId = assignedAgegroupId,
            AssignedDivId = assignedDivId,
            RegistrationTs = registrationTs ?? DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };
        _ctx.Registrations.Add(reg);
        return reg;
    }

    // ── Team ──

    public Teams AddTeam(
        Guid jobId,
        Guid leagueId,
        Guid agegroupId,
        string teamName = "Test Team",
        bool active = true,
        Guid? clubRepRegistrationId = null,
        Guid? divId = null,
        string? levelOfPlay = null,
        decimal feeBase = 500m,
        decimal feeProcessing = 0m,
        decimal paidTotal = 0m)
    {
        var feeTotal = feeBase + feeProcessing;
        var team = new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = leagueId,
            AgegroupId = agegroupId,
            TeamName = teamName,
            Active = active,
            ClubrepRegistrationid = clubRepRegistrationId,
            DivId = divId,
            LevelOfPlay = levelOfPlay,
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            FeeDiscount = 0,
            FeeDonation = 0,
            FeeLatefee = 0,
            FeeTotal = feeTotal,
            PaidTotal = paidTotal,
            OwedTotal = feeTotal - paidTotal,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };
        _ctx.Teams.Add(team);
        return team;
    }

    public async Task SaveAsync() => await _ctx.SaveChangesAsync();
}
