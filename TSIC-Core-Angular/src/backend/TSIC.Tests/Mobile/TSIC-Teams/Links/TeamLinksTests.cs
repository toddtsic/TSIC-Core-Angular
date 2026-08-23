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
        var (svc, b, _) = CreateService();
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
        var (svc, b, _) = CreateService();
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
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId, jobId: job.JobId);
        var doc = b.AddJobDoc(job.JobId, "To Delete", "https://example.com/delete");
        await b.SaveAsync();

        var deleted = await svc.DeleteLinkAsync(doc.DocId, team.TeamId);

        deleted.Should().BeTrue("a job-level doc is visible to every team in that job");
        var remaining = await ctx.TeamDocs.AsNoTracking().Where(d => d.DocId == doc.DocId).CountAsync();
        remaining.Should().Be(0);
    }

    [Fact(DisplayName = "Delete nonexistent link returns false")]
    public async Task DeleteLink_NotFound_ReturnsFalse()
    {
        var (svc, b, _) = CreateService();
        await b.SaveAsync();

        var deleted = await svc.DeleteLinkAsync(Guid.NewGuid(), Guid.NewGuid());

        deleted.Should().BeFalse();
    }

    // ── Ownership scoping (docId alone used to delete any team's link) ──

    [Fact(DisplayName = "Delete refuses a link owned by another team in the same job")]
    public async Task DeleteLink_OtherTeamSameJob_Refused()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var victim = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId, jobId: job.JobId);
        var attacker = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId, jobId: job.JobId);

        ctx.TeamDocs.Add(new Domain.Entities.TeamDocs
        {
            DocId = Guid.NewGuid(), TeamId = victim.TeamId, Label = "Victim Doc",
            UserId = MobileDataBuilder.DefaultUserId,
            DocUrl = "https://example.com/victim", CreateDate = DateTime.Now
        });
        await b.SaveAsync();
        var victimDocId = await ctx.TeamDocs.Where(d => d.TeamId == victim.TeamId)
            .Select(d => d.DocId).FirstAsync();

        var deleted = await svc.DeleteLinkAsync(victimDocId, attacker.TeamId);

        deleted.Should().BeFalse("docId must be tied to the team on the route");
        var remaining = await ctx.TeamDocs.AsNoTracking()
            .Where(d => d.DocId == victimDocId).CountAsync();
        remaining.Should().Be(1, "the victim's link must survive");
    }

    [Fact(DisplayName = "Delete refuses a job-level link from a team in another job")]
    public async Task DeleteLink_JobDocFromOtherJob_Refused()
    {
        var (svc, b, ctx) = CreateService();

        var jobA = b.AddJob();
        var docA = b.AddJobDoc(jobA.JobId, "Job A Doc", "https://example.com/a");

        var jobB = b.AddJob();
        var leagueB = b.AddLeague(jobB.JobId);
        var agB = b.AddAgegroup(leagueB.LeagueId);
        var divB = b.AddDivision(agB.AgegroupId);
        var teamB = b.AddTeam(divB.DivId, agegroupId: agB.AgegroupId, jobId: jobB.JobId);
        await b.SaveAsync();

        var deleted = await svc.DeleteLinkAsync(docA.DocId, teamB.TeamId);

        deleted.Should().BeFalse("a team cannot reach another job's doc");
        var remaining = await ctx.TeamDocs.AsNoTracking()
            .Where(d => d.DocId == docA.DocId).CountAsync();
        remaining.Should().Be(1);
    }
}
