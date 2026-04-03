using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Services.Shared.Devices;
using TSIC.Contracts.Dtos;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.Shared.Devices;

public class DeviceManagementTests
{
    private static (DeviceManagementService svc, MobileDataBuilder builder, Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
        CreateService()
    {
        var ctx = DbContextFactory.Create();
        var builder = new MobileDataBuilder(ctx);
        var deviceRepo = new DeviceRepository(ctx);
        var svc = new DeviceManagementService(deviceRepo);
        return (svc, builder, ctx);
    }

    [Fact(DisplayName = "Register new device → creates Devices + DeviceJobs")]
    public async Task Register_NewDevice_CreatesDeviceAndJob()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        await b.SaveAsync();

        await svc.RegisterDeviceAsync(new RegisterDeviceRequest
        {
            DeviceToken = "token-abc",
            JobId = job.JobId,
            DeviceType = "ios"
        });

        var device = await ctx.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Token == "token-abc");
        device.Should().NotBeNull();
        device!.Active.Should().BeTrue();

        var deviceJob = await ctx.DeviceJobs.AsNoTracking().FirstOrDefaultAsync(dj => dj.DeviceId == "token-abc" && dj.JobId == job.JobId);
        deviceJob.Should().NotBeNull();
    }

    [Fact(DisplayName = "Register existing device → updates Modified, no duplicate")]
    public async Task Register_ExistingDevice_Idempotent()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        b.AddDevice("token-abc", "android");
        await b.SaveAsync();

        await svc.RegisterDeviceAsync(new RegisterDeviceRequest
        {
            DeviceToken = "token-abc",
            JobId = job.JobId,
            DeviceType = "ios"
        });

        var devices = await ctx.Devices.AsNoTracking().Where(d => d.Token == "token-abc").ToListAsync();
        devices.Should().HaveCount(1, "no duplicate device created");
        devices[0].Type.Should().Be("ios", "type updated");
    }

    [Fact(DisplayName = "Register device for second job → creates second DeviceJobs")]
    public async Task Register_SecondJob_CreatesSecondLink()
    {
        var (svc, b, ctx) = CreateService();
        var job1 = b.AddJob("Job 1", "job-1");
        var job2 = b.AddJob("Job 2", "job-2");
        await b.SaveAsync();

        await svc.RegisterDeviceAsync(new RegisterDeviceRequest { DeviceToken = "token-abc", JobId = job1.JobId, DeviceType = "ios" });
        await svc.RegisterDeviceAsync(new RegisterDeviceRequest { DeviceToken = "token-abc", JobId = job2.JobId, DeviceType = "ios" });

        var deviceJobs = await ctx.DeviceJobs.AsNoTracking().Where(dj => dj.DeviceId == "token-abc").ToListAsync();
        deviceJobs.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Toggle subscribe (new) → creates DeviceTeams")]
    public async Task ToggleSubscribe_New_Subscribes()
    {
        var (svc, b, _) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        b.AddDevice("token-abc", "ios");
        await b.SaveAsync();

        var response = await svc.ToggleTeamSubscriptionAsync(
            new ToggleTeamSubscriptionRequest { DeviceToken = "token-abc", TeamId = team.TeamId, DeviceType = "ios" },
            job.JobId);

        response.SubscribedTeamIds.Should().Contain(team.TeamId);
    }

    [Fact(DisplayName = "Toggle subscribe (existing) → removes DeviceTeams")]
    public async Task ToggleSubscribe_Existing_Unsubscribes()
    {
        var (svc, b, _) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        b.AddDevice("token-abc", "ios");
        b.AddDeviceTeam("token-abc", team.TeamId);
        await b.SaveAsync();

        var response = await svc.ToggleTeamSubscriptionAsync(
            new ToggleTeamSubscriptionRequest { DeviceToken = "token-abc", TeamId = team.TeamId, DeviceType = "ios" },
            job.JobId);

        response.SubscribedTeamIds.Should().NotContain(team.TeamId);
    }

    [Fact(DisplayName = "Get subscribed teams → returns correct team IDs for job")]
    public async Task GetSubscribedTeams_ReturnsCorrectIds()
    {
        var (svc, b, _) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team1 = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var team2 = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        b.AddDevice("token-abc", "ios");
        b.AddDeviceTeam("token-abc", team1.TeamId);
        b.AddDeviceTeam("token-abc", team2.TeamId);
        await b.SaveAsync();

        var result = await svc.GetSubscribedTeamIdsAsync("token-abc", job.JobId);

        result.Should().HaveCount(2);
        result.Should().Contain(team1.TeamId);
        result.Should().Contain(team2.TeamId);
    }

    [Fact(DisplayName = "Swap token → old device deactivated, new device has all links")]
    public async Task SwapToken_MigratesAllLinks()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        b.AddDevice("old-token", "ios");
        b.AddDeviceJob("old-token", job.JobId);
        b.AddDeviceTeam("old-token", team.TeamId);
        await b.SaveAsync();

        await svc.SwapTokenAsync(new SwapDeviceTokenRequest
        {
            OldDeviceToken = "old-token",
            NewDeviceToken = "new-token"
        });

        var oldDevice = await ctx.Devices.AsNoTracking().FirstAsync(d => d.Token == "old-token");
        oldDevice.Active.Should().BeFalse("old device deactivated");

        var newDeviceJobs = await ctx.DeviceJobs.AsNoTracking().Where(dj => dj.DeviceId == "new-token").ToListAsync();
        newDeviceJobs.Should().HaveCount(1, "job link migrated");

        var newDeviceTeams = await ctx.DeviceTeams.AsNoTracking().Where(dt => dt.DeviceId == "new-token").ToListAsync();
        newDeviceTeams.Should().HaveCount(1, "team link migrated");
    }

    [Fact(DisplayName = "Swap token (old doesn't exist) → no-op")]
    public async Task SwapToken_OldNotFound_NoOp()
    {
        var (svc, b, ctx) = CreateService();
        await b.SaveAsync();

        // Should not throw
        await svc.SwapTokenAsync(new SwapDeviceTokenRequest
        {
            OldDeviceToken = "nonexistent",
            NewDeviceToken = "new-token"
        });

        var devices = await ctx.Devices.AsNoTracking().ToListAsync();
        devices.Should().BeEmpty("no device created when old doesn't exist");
    }
}
