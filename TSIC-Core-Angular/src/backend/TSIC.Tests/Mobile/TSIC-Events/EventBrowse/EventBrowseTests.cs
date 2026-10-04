using FluentAssertions;
using TSIC.API.Services;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.TSIC_Events.EventBrowse;

public class EventBrowseTests
{
    private static (EventBrowseService svc, MobileDataBuilder builder, Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
        CreateService()
    {
        var ctx = DbContextFactory.Create();
        var builder = new MobileDataBuilder(ctx);
        var jobRepo = new JobRepository(ctx);
        var pushRepo = new PushNotificationRepository(ctx);
        var svc = new EventBrowseService(jobRepo, pushRepo);
        return (svc, builder, ctx);
    }

    [Fact(DisplayName = "Active public events → returns non-expired, non-suspended, public-access jobs")]
    public async Task GetActiveEvents_ReturnsPublicJobs()
    {
        var (svc, b, _) = CreateService();
        b.AddJob("Public Cup", "public-cup", publicAccess: true);
        b.AddJob("Private Cup", "private-cup", publicAccess: false);
        b.AddJob("Suspended Cup", "suspended-cup", publicAccess: true, suspended: true);
        await b.SaveAsync();

        var result = await svc.GetActiveEventsAsync();

        result.Should().HaveCount(1);
        result[0].JobName.Should().Be("Public Cup");
    }

    [Fact(DisplayName = "Expired job excluded")]
    public async Task GetActiveEvents_ExcludesExpired()
    {
        var (svc, b, _) = CreateService();
        b.AddJob("Active", "active", expiry: DateTime.UtcNow.AddDays(10));
        b.AddJob("Expired", "expired", expiry: DateTime.UtcNow.AddDays(-1));
        await b.SaveAsync();

        var result = await svc.GetActiveEventsAsync();

        result.Should().HaveCount(1);
        result[0].JobName.Should().Be("Active");
    }

    [Fact(DisplayName = "Expired job with a game in the past 9 months still listed")]
    public async Task GetActiveEvents_IncludesExpiredWithRecentGame()
    {
        var (svc, b, _) = CreateService();
        var job = b.AddJob("Finished Cup", "finished-cup", expiry: DateTime.Now.AddDays(-1));
        AddGameOn(b, job.JobId, DateTime.Now.AddMonths(-8));
        await b.SaveAsync();

        var result = await svc.GetActiveEventsAsync();

        result.Should().ContainSingle(e => e.JobName == "Finished Cup");
    }

    [Fact(DisplayName = "Expired job whose last game is older than 9 months excluded")]
    public async Task GetActiveEvents_ExcludesExpiredWithOldGames()
    {
        var (svc, b, _) = CreateService();
        var job = b.AddJob("Old Cup", "old-cup", expiry: DateTime.Now.AddDays(-1));
        AddGameOn(b, job.JobId, DateTime.Now.AddMonths(-10));
        await b.SaveAsync();

        var result = await svc.GetActiveEventsAsync();

        result.Should().BeEmpty();
    }

    [Fact(DisplayName = "Recent past event still hidden when suspended from the app")]
    public async Task GetActiveEvents_RecentPastEventSuspended_Excluded()
    {
        var (svc, b, _) = CreateService();
        var job = b.AddJob("Opted Out", "opted-out", expiry: DateTime.Now.AddDays(-1), suspended: true);
        AddGameOn(b, job.JobId, DateTime.Now.AddMonths(-1));
        await b.SaveAsync();

        var result = await svc.GetActiveEventsAsync();

        result.Should().BeEmpty();
    }

    private static void AddGameOn(MobileDataBuilder b, Guid jobId, DateTime gDate) =>
        b.AddGame(jobId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), gDate);

    [Fact(DisplayName = "Suspended job excluded")]
    public async Task GetActiveEvents_ExcludesSuspended()
    {
        var (svc, b, _) = CreateService();
        b.AddJob("Good", "good");
        b.AddJob("Suspended", "suspended", suspended: true);
        await b.SaveAsync();

        var result = await svc.GetActiveEventsAsync();

        result.Should().ContainSingle(e => e.JobName == "Good");
    }

    [Fact(DisplayName = "Non-public job excluded")]
    public async Task GetActiveEvents_ExcludesNonPublic()
    {
        var (svc, b, _) = CreateService();
        b.AddJob("Public", "public", publicAccess: true);
        b.AddJob("Private", "private", publicAccess: false);
        await b.SaveAsync();

        var result = await svc.GetActiveEventsAsync();

        result.Should().ContainSingle(e => e.JobName == "Public");
    }

    [Fact(DisplayName = "Get alerts → returns newest first")]
    public async Task GetAlerts_NewestFirst()
    {
        var (svc, b, _) = CreateService();
        var job = b.AddJob();
        b.AddPushAlert(job.JobId, "First alert");
        var alert2 = b.AddPushAlert(job.JobId, "Second alert");
        // Make second alert newer
        alert2.Modified = DateTime.UtcNow.AddMinutes(5);
        await b.SaveAsync();

        var result = await svc.GetAlertsAsync(job.JobId);

        result.Should().HaveCount(2);
        result[0].PushText.Should().Be("Second alert", "newest first");
    }

    [Fact(DisplayName = "Get game clock → returns config")]
    public async Task GetGameClock_ReturnsConfig()
    {
        var (svc, b, _) = CreateService();
        var job = b.AddJob();
        b.AddGameClockParams(job.JobId, halfMinutes: 25, halfTimeMinutes: 5, transitionMinutes: 3, playoffMinutes: 30);
        await b.SaveAsync();

        var result = await svc.GetGameClockConfigAsync(job.JobId);

        result.Should().NotBeNull();
        result!.HalfMinutes.Should().Be(25);
        result.HalfTimeMinutes.Should().Be(5);
        result.TransitionMinutes.Should().Be(3);
        result.PlayoffMinutes.Should().Be(30);
    }
}
