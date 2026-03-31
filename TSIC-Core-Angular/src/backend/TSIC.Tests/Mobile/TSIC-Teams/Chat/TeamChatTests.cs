using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.TSIC_Teams.Chat;

public class TeamChatTests
{
    [Fact(DisplayName = "Add message stores in DB and returns entity")]
    public async Task AddMessage_StoresAndReturns()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        await b.SaveAsync();

        var repo = new ChatRepository(ctx);
        var msg = await repo.AddMessageAsync(team.TeamId, MobileDataBuilder.DefaultUserId, "Hello team!");

        msg.MessageId.Should().NotBeEmpty();
        msg.Message.Should().Be("Hello team!");
        msg.TeamId.Should().Be(team.TeamId);

        var stored = await ctx.ChatMessages.AsNoTracking().FirstOrDefaultAsync(m => m.MessageId == msg.MessageId);
        stored.Should().NotBeNull();
    }

    [Fact(DisplayName = "Get messages returns newest first with pagination")]
    public async Task GetMessages_NewestFirst_Paginated()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        await b.SaveAsync();

        var repo = new ChatRepository(ctx);
        await repo.AddMessageAsync(team.TeamId, MobileDataBuilder.DefaultUserId, "First");
        await Task.Delay(10); // ensure different timestamps
        await repo.AddMessageAsync(team.TeamId, MobileDataBuilder.DefaultUserId, "Second");
        await Task.Delay(10);
        await repo.AddMessageAsync(team.TeamId, MobileDataBuilder.DefaultUserId, "Third");

        // Page 1: skip 0, take 2
        var page1 = await repo.GetMessagesAsync(team.TeamId, skip: 0, take: 2);
        page1.Should().HaveCount(2);
        page1[0].Message.Should().Be("Third", "newest first");
        page1[1].Message.Should().Be("Second");

        // Page 2: skip 2, take 2
        var page2 = await repo.GetMessagesAsync(team.TeamId, skip: 2, take: 2);
        page2.Should().HaveCount(1);
        page2[0].Message.Should().Be("First");
    }

    [Fact(DisplayName = "Get message count returns correct total")]
    public async Task GetMessageCount_Correct()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        await b.SaveAsync();

        var repo = new ChatRepository(ctx);
        await repo.AddMessageAsync(team.TeamId, MobileDataBuilder.DefaultUserId, "One");
        await repo.AddMessageAsync(team.TeamId, MobileDataBuilder.DefaultUserId, "Two");

        var count = await repo.GetMessageCountAsync(team.TeamId);
        count.Should().Be(2);
    }

    [Fact(DisplayName = "Delete message removes from DB")]
    public async Task DeleteMessage_Removes()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        await b.SaveAsync();

        var repo = new ChatRepository(ctx);
        var msg = await repo.AddMessageAsync(team.TeamId, MobileDataBuilder.DefaultUserId, "To delete");

        var deleted = await repo.DeleteMessageAsync(msg.MessageId);
        await repo.SaveChangesAsync();

        deleted.Should().BeTrue();
        (await ctx.ChatMessages.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact(DisplayName = "Delete nonexistent message returns false")]
    public async Task DeleteMessage_NotFound_ReturnsFalse()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        await b.SaveAsync();

        var repo = new ChatRepository(ctx);
        var deleted = await repo.DeleteMessageAsync(Guid.NewGuid());

        deleted.Should().BeFalse();
    }

    [Fact(DisplayName = "Messages scoped to team — other team's messages excluded")]
    public async Task GetMessages_ScopedToTeam()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team1 = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var team2 = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        await b.SaveAsync();

        var repo = new ChatRepository(ctx);
        await repo.AddMessageAsync(team1.TeamId, MobileDataBuilder.DefaultUserId, "Eagles msg");
        await repo.AddMessageAsync(team2.TeamId, MobileDataBuilder.DefaultUserId, "Hawks msg");

        var eaglesMessages = await repo.GetMessagesAsync(team1.TeamId, 0, 50);
        eaglesMessages.Should().HaveCount(1);
        eaglesMessages[0].Message.Should().Be("Eagles msg");
    }
}
