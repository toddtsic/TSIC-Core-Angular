using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Services.Shared.Devices;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.Shared.Devices;

/// <summary>
/// device/sync files one device against everything the bearer holds, in one call.
///
/// The properties that matter: it is idempotent (the client calls it on every launch), it
/// covers ALL of a user's registrations rather than whichever one is active, it folds a
/// rotated token in first so rows do not split across two device records, and it takes
/// nothing about job or team from the caller.
/// </summary>
public class DeviceSyncTests
{
    private const string Token = "device-token-aaa";
    private const string OldToken = "device-token-old";

    private static (DeviceManagementService svc, MobileDataBuilder b, Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
        CreateService()
    {
        var ctx = DbContextFactory.Create();
        return (new DeviceManagementService(new DeviceRepository(ctx), new RegistrationRepository(ctx)),
                new MobileDataBuilder(ctx), ctx);
    }

    private static SyncDeviceRequest Req(string token = Token, string? previous = null) =>
        new() { DeviceToken = token, DeviceType = "ios", PreviousDeviceToken = previous };

    /// <summary>Two jobs, two teams, one user - the multi-registration parent.</summary>
    private static async Task<(Guid teamA, Guid teamB)> TwoRegistrations(MobileDataBuilder b)
    {
        var jobA = b.AddJob(name: "Job A", jobPath: "job-a");
        var lA = b.AddLeague(jobA.JobId);
        var agA = b.AddAgegroup(lA.LeagueId);
        var dA = b.AddDivision(agA.AgegroupId);
        var teamA = b.AddTeam(dA.DivId, "Team A", agA.AgegroupId, jobA.JobId);

        var jobB = b.AddJob(name: "Job B", jobPath: "job-b");
        var lB = b.AddLeague(jobB.JobId);
        var agB = b.AddAgegroup(lB.LeagueId);
        var dB = b.AddDivision(agB.AgegroupId);
        var teamB = b.AddTeam(dB.DivId, "Team B", agB.AgegroupId, jobB.JobId);

        b.AddRegistration(MobileDataBuilder.DefaultUserId, jobA.JobId, RoleConstants.Staff, teamA.TeamId);
        b.AddRegistration(MobileDataBuilder.DefaultUserId, jobB.JobId, RoleConstants.Staff, teamB.TeamId);
        await b.SaveAsync();

        return (teamA.TeamId, teamB.TeamId);
    }

    /// <summary>
    /// Device_Jobs is the TSIC-Events broadcast pool -- its only two readers are the Events
    /// send paths, so a row in it means "blast this phone the Events push". device/sync is the
    /// authenticated TSIC-Teams path and its tokens belong to the tsic-teams Firebase project,
    /// which the Events credential rejects with SenderIdMismatch. Filing here therefore did not
    /// widen reach, it padded the pool with tokens that could never be delivered to.
    ///
    /// TSIC-Teams devices are reached through Device_Teams. The anonymous register endpoint is
    /// what fills Device_Jobs, and that endpoint is the TSIC-Events app.
    /// </summary>
    private static async Task NoEventsPoolRow(
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx, string deviceId)
    {
        (await ctx.DeviceJobs.AsNoTracking().CountAsync(x => x.DeviceId == deviceId))
            .Should().Be(0, "sync is TSIC-Teams and must never write the TSIC-Events pool");
    }

    [Fact(DisplayName = "Sync files the device against every registration, not just one")]
    public async Task Sync_CoversAllRegistrations()
    {
        var (svc, b, ctx) = CreateService();
        var (teamA, teamB) = await TwoRegistrations(b);

        var result = await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, Req());

        result.Jobs.Should().Be(2);
        result.Teams.Should().Be(2);
        result.Registrations.Should().Be(2);

        var device = await ctx.Devices.AsNoTracking().SingleAsync(d => d.Token == Token);
        await NoEventsPoolRow(ctx, device.Id);
        (await ctx.DeviceRegistrationIds.AsNoTracking().CountAsync(x => x.DeviceId == device.Id)).Should().Be(2);

        var teams = await ctx.DeviceTeams.AsNoTracking()
            .Where(x => x.DeviceId == device.Id).Select(x => x.TeamId).ToListAsync();
        teams.Should().BeEquivalentTo(new[] { teamA, teamB },
            "a parent with two children on two teams must hear about both");
    }

    [Fact(DisplayName = "Sync is idempotent - a relaunch adds nothing")]
    public async Task Sync_Idempotent()
    {
        var (svc, b, ctx) = CreateService();
        await TwoRegistrations(b);

        await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, Req());
        var second = await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, Req());

        second.Teams.Should().Be(0, "nothing new to add");
        second.Registrations.Should().Be(0);

        var device = await ctx.Devices.AsNoTracking().SingleAsync(d => d.Token == Token);
        (await ctx.DeviceTeams.AsNoTracking().CountAsync(x => x.DeviceId == device.Id)).Should().Be(2);
        await NoEventsPoolRow(ctx, device.Id);
        (await ctx.Devices.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact(DisplayName = "Unplaced registration files job and registration but no team")]
    public async Task Sync_UnplacedRegistration_NoTeamRow()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        b.AddRegistration(MobileDataBuilder.DefaultUserId, job.JobId, RoleConstants.Staff, teamId: null);
        await b.SaveAsync();

        var result = await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, Req());

        result.Jobs.Should().Be(1);
        result.Teams.Should().Be(0);
        result.Registrations.Should().Be(1);
        (await ctx.DeviceTeams.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact(DisplayName = "Rotated token folds into one device, not two")]
    public async Task Sync_Rotation_FoldsOntoOneDevice()
    {
        var (svc, b, ctx) = CreateService();
        await TwoRegistrations(b);

        await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, Req(OldToken));
        await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, Req(Token, previous: OldToken));

        var device = await ctx.Devices.AsNoTracking().SingleAsync(d => d.Token == Token);
        (await ctx.DeviceTeams.AsNoTracking().CountAsync(x => x.DeviceId == device.Id))
            .Should().Be(2, "the swap runs before the rows are written");
    }

    [Fact(DisplayName = "Sync files only the caller registrations")]
    public async Task Sync_IgnoresOtherUsers()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var mine = b.AddTeam(div.DivId, "Mine", ag.AgegroupId, job.JobId);
        var theirs = b.AddTeam(div.DivId, "Theirs", ag.AgegroupId, job.JobId);

        b.AddRegistration(MobileDataBuilder.DefaultUserId, job.JobId, RoleConstants.Staff, mine.TeamId);
        b.AddRegistration("someone-else", job.JobId, RoleConstants.Staff, theirs.TeamId);
        await b.SaveAsync();

        await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, Req());

        var device = await ctx.Devices.AsNoTracking().SingleAsync(d => d.Token == Token);
        var teams = await ctx.DeviceTeams.AsNoTracking()
            .Where(x => x.DeviceId == device.Id).Select(x => x.TeamId).ToListAsync();
        teams.Should().BeEquivalentTo(new[] { mine.TeamId },
            "job and team come from the bearer, never from the caller");
    }

    [Fact(DisplayName = "Inactive registration is not filed")]
    public async Task Sync_SkipsInactiveRegistration()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, "Dropped", ag.AgegroupId, job.JobId);
        b.AddRegistration(MobileDataBuilder.DefaultUserId, job.JobId, RoleConstants.Staff, team.TeamId, active: false);
        await b.SaveAsync();

        var result = await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, Req());

        result.Jobs.Should().Be(0);
        (await ctx.DeviceTeams.AsNoTracking().CountAsync()).Should().Be(0);
    }
}
