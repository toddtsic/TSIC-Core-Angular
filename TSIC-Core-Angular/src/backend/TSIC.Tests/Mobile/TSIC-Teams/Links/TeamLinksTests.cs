using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TSIC.API.Services.Shared.Firebase;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.TSIC_Teams.Links;

public class TeamLinksTests
{
    private static (TeamManagementService svc, MobileDataBuilder builder, Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
        CreateService()
    {
        var ctx = DbContextFactory.Create();
        var builder = new MobileDataBuilder(ctx);
        var teamRepo = new TeamRepository(ctx);
        var teamDocsRepo = new TeamDocsRepository(ctx);
        var pushRepo = new PushNotificationRepository(ctx);
        var firebasePush = new Mock<IFirebasePushService>();
        var svc = new TeamManagementService(teamRepo, teamDocsRepo, pushRepo, firebasePush.Object);
        return (svc, builder, ctx);
    }

    [Fact(DisplayName = "Get links returns team-scoped + job-scoped links")]
    public async Task GetLinks_ReturnsBothScopes()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId, jobId: job.JobId);

        // Team-scoped link
        ctx.TeamDocs.Add(new Domain.Entities.TeamDocs
        {
            DocId = Guid.NewGuid(), TeamId = team.TeamId, Label = "Team Doc",
            DocUrl = "https://example.com/team", UserId = MobileDataBuilder.DefaultUserId, CreateDate = DateTime.UtcNow
        });
        // Job-scoped link (visible to all teams)
        ctx.TeamDocs.Add(new Domain.Entities.TeamDocs
        {
            DocId = Guid.NewGuid(), JobId = job.JobId, Label = "Job Doc",
            DocUrl = "https://example.com/job", UserId = MobileDataBuilder.DefaultUserId, CreateDate = DateTime.UtcNow
        });
        await b.SaveAsync();

        var result = await svc.GetLinksAsync(team.TeamId);

        result.Should().HaveCount(2);
        result.Should().Contain(l => l.Label == "Team Doc");
        result.Should().Contain(l => l.Label == "Job Doc");
    }

    [Fact(DisplayName = "Add team link creates record")]
    public async Task AddLink_CreatesRecord()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        await b.SaveAsync();

        var result = await svc.AddLinkAsync(team.TeamId, MobileDataBuilder.DefaultUserId,
            new AddTeamLinkRequest { Label = "Practice Schedule", DocUrl = "https://example.com/schedule", AddAllTeams = false });

        result.Label.Should().Be("Practice Schedule");
        result.TeamId.Should().Be(team.TeamId);
        result.JobId.Should().BeNull("team-scoped, not job-scoped");
    }

    [Fact(DisplayName = "Add link with AddAllTeams sets JobId instead of TeamId")]
    public async Task AddLink_AddAllTeams_SetsJobId()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        await b.SaveAsync();

        var result = await svc.AddLinkAsync(team.TeamId, MobileDataBuilder.DefaultUserId,
            new AddTeamLinkRequest { Label = "Global Doc", DocUrl = "https://example.com/global", AddAllTeams = true });

        result.TeamId.Should().BeNull("job-scoped when AddAllTeams");
        result.JobId.Should().NotBeNull();
    }

    [Fact(DisplayName = "Delete link removes record")]
    public async Task DeleteLink_RemovesRecord()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var doc = b.AddJobDoc(job.JobId, "To Delete", "https://example.com/delete");
        await b.SaveAsync();

        var deleted = await svc.DeleteLinkAsync(doc.DocId);

        deleted.Should().BeTrue();
        var remaining = await ctx.TeamDocs.AsNoTracking().Where(d => d.DocId == doc.DocId).CountAsync();
        remaining.Should().Be(0);
    }

    [Fact(DisplayName = "Delete nonexistent link returns false")]
    public async Task DeleteLink_NotFound_ReturnsFalse()
    {
        var (svc, b, _) = CreateService();
        await b.SaveAsync();

        var deleted = await svc.DeleteLinkAsync(Guid.NewGuid());

        deleted.Should().BeFalse();
    }
}
