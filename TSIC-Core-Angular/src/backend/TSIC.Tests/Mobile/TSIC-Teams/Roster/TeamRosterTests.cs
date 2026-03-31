using FluentAssertions;
using Moq;
using TSIC.API.Services.Shared.Firebase;
using TSIC.API.Services.Teams;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.TSIC_Teams.Roster;

public class TeamRosterTests
{
    private static (TeamManagementService svc, MobileDataBuilder builder)
        CreateService(Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var builder = new MobileDataBuilder(ctx);
        var teamRepo = new TeamRepository(ctx);
        var teamDocsRepo = new TeamDocsRepository(ctx);
        var pushRepo = new PushNotificationRepository(ctx);
        var firebasePush = new Mock<IFirebasePushService>();
        var svc = new TeamManagementService(teamRepo, teamDocsRepo, pushRepo, firebasePush.Object);
        return (svc, builder);
    }

    [Fact(DisplayName = "Roster returns staff and players separately")]
    public async Task GetRoster_SplitsStaffAndPlayers()
    {
        var ctx = DbContextFactory.Create();
        var (svc, b) = CreateService(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var team = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);

        var staffUser = b.AddUser("coach1", "Mike", "Smith");
        b.AddRegistration(staffUser.Id, job.JobId, RoleConstants.Staff, team.TeamId);

        var playerUser = b.AddUser("player1", "Jimmy", "Jones");
        b.AddRegistration(playerUser.Id, job.JobId, RoleConstants.Player, team.TeamId);

        await b.SaveAsync();

        var result = await svc.GetRosterAsync(team.TeamId);

        result.Staff.Should().HaveCount(1);
        result.Staff[0].FirstName.Should().Be("Mike");
        result.Players.Should().HaveCount(1);
        result.Players[0].FirstName.Should().Be("Jimmy");
    }

    [Fact(DisplayName = "Roster excludes inactive registrations")]
    public async Task GetRoster_ExcludesInactive()
    {
        var ctx = DbContextFactory.Create();
        var (svc, b) = CreateService(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);

        var activePlayer = b.AddUser("active");
        b.AddRegistration(activePlayer.Id, job.JobId, RoleConstants.Player, team.TeamId, active: true);

        var inactivePlayer = b.AddUser("inactive");
        b.AddRegistration(inactivePlayer.Id, job.JobId, RoleConstants.Player, team.TeamId, active: false);

        await b.SaveAsync();

        var result = await svc.GetRosterAsync(team.TeamId);

        result.Players.Should().HaveCount(1);
        result.Players[0].UserName.Should().Be("active");
    }

    [Fact(DisplayName = "Roster includes parent contact info from Families")]
    public async Task GetRoster_IncludesParentContacts()
    {
        var ctx = DbContextFactory.Create();
        var (svc, b) = CreateService(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);

        var familyUser = b.AddUser("familyparent", "Parent", "User");
        // Create family record linked to this user
        var family = new Domain.Entities.Families
        {
            FamilyUserId = familyUser.Id,
            MomFirstName = "Jane",
            MomLastName = "Doe",
            MomEmail = "jane@test.com",
            MomCellphone = "555-1234",
            DadFirstName = "John",
            DadLastName = "Doe",
            DadEmail = "john@test.com",
            DadCellphone = "555-5678",
            Modified = DateTime.UtcNow
        };
        ctx.Families.Add(family);

        var playerUser = b.AddUser("kidplayer", "Kid", "Doe");
        var reg = b.AddRegistration(playerUser.Id, job.JobId, RoleConstants.Player, team.TeamId);
        reg.FamilyUserId = familyUser.Id;

        await b.SaveAsync();

        var result = await svc.GetRosterAsync(team.TeamId);

        result.Players.Should().HaveCount(1);
        result.Players[0].Mom.Should().Contain("Jane");
        result.Players[0].MomEmail.Should().Be("jane@test.com");
        result.Players[0].Dad.Should().Contain("John");
        result.Players[0].DadEmail.Should().Be("john@test.com");
    }

    [Fact(DisplayName = "Roster includes uniform number and school")]
    public async Task GetRoster_IncludesUniformAndSchool()
    {
        var ctx = DbContextFactory.Create();
        var (svc, b) = CreateService(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);

        var playerUser = b.AddUser("player1");
        var reg = b.AddRegistration(playerUser.Id, job.JobId, RoleConstants.Player, team.TeamId);
        reg.UniformNo = "10";
        reg.SchoolName = "Lincoln High";

        await b.SaveAsync();

        var result = await svc.GetRosterAsync(team.TeamId);

        result.Players[0].UniformNumber.Should().Be("10");
        result.Players[0].School.Should().Be("Lincoln High");
    }
}
