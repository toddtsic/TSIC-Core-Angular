using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.TSIC_Teams.Attendance;

public class TeamAttendanceTests
{
    private static (TeamAttendanceService svc, MobileDataBuilder builder, Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
        CreateService()
    {
        var ctx = DbContextFactory.Create();
        var builder = new MobileDataBuilder(ctx);
        var repo = new TeamAttendanceRepository(ctx);
        var svc = new TeamAttendanceService(repo);
        return (svc, builder, ctx);
    }

    private static void SeedEventType(Infrastructure.Data.SqlDbContext.SqlDbContext ctx, int id = 1, string name = "Practice")
    {
        ctx.TeamAttendanceTypes.Add(new Domain.Entities.TeamAttendanceTypes { Id = id, AttendanceType = name });
    }

    [Fact(DisplayName = "Create event returns event with zero counts")]
    public async Task CreateEvent_ReturnsWithZeroCounts()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        SeedEventType(ctx);
        await b.SaveAsync();

        var result = await svc.CreateEventAsync(team.TeamId, MobileDataBuilder.DefaultUserId,
            new CreateAttendanceEventRequest
            {
                Comment = "Monday practice",
                EventTypeId = 1,
                EventDate = DateTime.Today.AddDays(1),
                EventLocation = "Field 3"
            });

        result.EventId.Should().BeGreaterThan(0);
        result.TeamId.Should().Be(team.TeamId);
        result.Present.Should().Be(0);
        result.NotPresent.Should().Be(0);
        result.EventType.Should().Be("Practice");
    }

    [Fact(DisplayName = "Get events returns list with attendance counts")]
    public async Task GetEvents_ReturnsCounts()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        SeedEventType(ctx);
        await b.SaveAsync();

        // Create event + add RSVP records
        var evt = await svc.CreateEventAsync(team.TeamId, MobileDataBuilder.DefaultUserId,
            new CreateAttendanceEventRequest { Comment = "Practice", EventTypeId = 1, EventDate = DateTime.Today, EventLocation = "Field 1" });

        var p1 = b.AddUser("player1");
        var p2 = b.AddUser("player2");
        await b.SaveAsync();

        await svc.UpdateRsvpAsync(evt.EventId, new UpdateRsvpRequest { PlayerId = p1.Id, Present = true }, MobileDataBuilder.DefaultUserId);
        await svc.UpdateRsvpAsync(evt.EventId, new UpdateRsvpRequest { PlayerId = p2.Id, Present = false }, MobileDataBuilder.DefaultUserId);

        var events = await svc.GetEventsAsync(team.TeamId);

        events.Should().HaveCount(1);
        events[0].Present.Should().Be(1);
        events[0].NotPresent.Should().Be(1);
    }

    [Fact(DisplayName = "Update RSVP creates new record")]
    public async Task UpdateRsvp_CreatesRecord()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        SeedEventType(ctx);
        await b.SaveAsync();

        var evt = await svc.CreateEventAsync(team.TeamId, MobileDataBuilder.DefaultUserId,
            new CreateAttendanceEventRequest { Comment = "Game", EventTypeId = 1, EventDate = DateTime.Today, EventLocation = "Field 1" });

        var player = b.AddUser("player1");
        await b.SaveAsync();

        await svc.UpdateRsvpAsync(evt.EventId, new UpdateRsvpRequest { PlayerId = player.Id, Present = true }, MobileDataBuilder.DefaultUserId);

        var records = await ctx.TeamAttendanceRecords.AsNoTracking().Where(r => r.EventId == evt.EventId).ToListAsync();
        records.Should().HaveCount(1);
        records[0].Present.Should().BeTrue();
    }

    [Fact(DisplayName = "Update RSVP toggles existing record")]
    public async Task UpdateRsvp_TogglesExisting()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        SeedEventType(ctx);
        await b.SaveAsync();

        var evt = await svc.CreateEventAsync(team.TeamId, MobileDataBuilder.DefaultUserId,
            new CreateAttendanceEventRequest { Comment = "Game", EventTypeId = 1, EventDate = DateTime.Today, EventLocation = "Field 1" });

        var player = b.AddUser("player1");
        await b.SaveAsync();

        // First: present
        await svc.UpdateRsvpAsync(evt.EventId, new UpdateRsvpRequest { PlayerId = player.Id, Present = true }, MobileDataBuilder.DefaultUserId);
        // Then: not present
        await svc.UpdateRsvpAsync(evt.EventId, new UpdateRsvpRequest { PlayerId = player.Id, Present = false }, MobileDataBuilder.DefaultUserId);

        var records = await ctx.TeamAttendanceRecords.AsNoTracking().Where(r => r.EventId == evt.EventId).ToListAsync();
        records.Should().HaveCount(1, "no duplicate — updates existing");
        records[0].Present.Should().BeFalse("toggled to not present");
    }

    [Fact(DisplayName = "Delete event removes event and its records")]
    public async Task DeleteEvent_RemovesEventAndRecords()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        SeedEventType(ctx);
        await b.SaveAsync();

        var evt = await svc.CreateEventAsync(team.TeamId, MobileDataBuilder.DefaultUserId,
            new CreateAttendanceEventRequest { Comment = "Game", EventTypeId = 1, EventDate = DateTime.Today, EventLocation = "Field 1" });

        var player = b.AddUser("player1");
        await b.SaveAsync();
        await svc.UpdateRsvpAsync(evt.EventId, new UpdateRsvpRequest { PlayerId = player.Id, Present = true }, MobileDataBuilder.DefaultUserId);

        var deleted = await svc.DeleteEventAsync(evt.EventId);

        deleted.Should().BeTrue();
        (await ctx.TeamAttendanceEvents.AsNoTracking().CountAsync()).Should().Be(0);
        (await ctx.TeamAttendanceRecords.AsNoTracking().CountAsync()).Should().Be(0, "cascade delete records");
    }

    [Fact(DisplayName = "Get player history returns ordered records")]
    public async Task GetPlayerHistory_ReturnsOrdered()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        SeedEventType(ctx);
        await b.SaveAsync();

        var evt1 = await svc.CreateEventAsync(team.TeamId, MobileDataBuilder.DefaultUserId,
            new CreateAttendanceEventRequest { Comment = "Old", EventTypeId = 1, EventDate = DateTime.Today.AddDays(-7), EventLocation = "F1" });
        var evt2 = await svc.CreateEventAsync(team.TeamId, MobileDataBuilder.DefaultUserId,
            new CreateAttendanceEventRequest { Comment = "New", EventTypeId = 1, EventDate = DateTime.Today, EventLocation = "F1" });

        var player = b.AddUser("player1");
        await b.SaveAsync();

        await svc.UpdateRsvpAsync(evt1.EventId, new UpdateRsvpRequest { PlayerId = player.Id, Present = true }, MobileDataBuilder.DefaultUserId);
        await svc.UpdateRsvpAsync(evt2.EventId, new UpdateRsvpRequest { PlayerId = player.Id, Present = false }, MobileDataBuilder.DefaultUserId);

        var history = await svc.GetPlayerHistoryAsync(team.TeamId, player.Id);

        history.Should().HaveCount(2);
        history[0].EventDate.Should().BeAfter(history[1].EventDate, "newest first");
    }

    [Fact(DisplayName = "Get event types returns seeded types")]
    public async Task GetEventTypes_ReturnsAll()
    {
        var (svc, b, ctx) = CreateService();
        SeedEventType(ctx, 1, "Practice");
        SeedEventType(ctx, 2, "Game");
        SeedEventType(ctx, 3, "Meeting");
        await b.SaveAsync();

        var types = await svc.GetEventTypesAsync();

        types.Should().HaveCount(3);
        types.Select(t => t.AttendanceType).Should().Contain("Practice").And.Contain("Game").And.Contain("Meeting");
    }
}
