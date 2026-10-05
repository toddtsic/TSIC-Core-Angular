using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Services.Shared.Devices;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.Shared.Devices;

/// <summary>
/// device/sync files one device against the one registration the login named, in one call.
///
/// The properties that matter: it files ONLY the registration in the bearer -- a TSIC-Teams
/// login is a pick of one player and one team, and the other registrations the family holds
/// are not filed (an earlier version walked all of them and put every phone on tournament
/// teams the user never signed into) -- it is idempotent (the client calls it on every
/// launch), it folds a rotated token in first so rows do not split across two device
/// records, and it takes nothing about job or team from the caller.
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
    private static async Task<(Guid regA, Guid teamA, Guid regB, Guid teamB)> TwoRegistrations(MobileDataBuilder b)
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

        var regA = b.AddRegistration(MobileDataBuilder.DefaultUserId, jobA.JobId, RoleConstants.Staff, teamA.TeamId);
        var regB = b.AddRegistration(MobileDataBuilder.DefaultUserId, jobB.JobId, RoleConstants.Staff, teamB.TeamId);
        await b.SaveAsync();

        return (regA.RegistrationId, teamA.TeamId, regB.RegistrationId, teamB.TeamId);
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

    [Fact(DisplayName = "Sync files only the registration the login named, not the user's others")]
    public async Task Sync_FilesOnlyTheNamedRegistration()
    {
        var (svc, b, ctx) = CreateService();
        var (regA, teamA, _, _) = await TwoRegistrations(b);

        var result = await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, regA, Req());

        result.Jobs.Should().Be(1);
        result.Teams.Should().Be(1);
        result.Registrations.Should().Be(1);

        var device = await ctx.Devices.AsNoTracking().SingleAsync(d => d.Token == Token);
        await NoEventsPoolRow(ctx, device.Id);
        (await ctx.DeviceRegistrationIds.AsNoTracking().Where(x => x.DeviceId == device.Id)
            .Select(x => x.RegistrationId).ToListAsync())
            .Should().BeEquivalentTo(new[] { regA });

        var teams = await ctx.DeviceTeams.AsNoTracking()
            .Where(x => x.DeviceId == device.Id).Select(x => x.TeamId).ToListAsync();
        teams.Should().BeEquivalentTo(new[] { teamA },
            "the login picked one player and one team; the family's other registrations are not filed");
    }

    [Fact(DisplayName = "Signing in as each registration in turn files one row per registration")]
    public async Task Sync_EachLoginAddsItsOwnRow()
    {
        var (svc, b, ctx) = CreateService();
        var (regA, teamA, regB, teamB) = await TwoRegistrations(b);

        await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, regA, Req());
        await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, regB, Req());

        var device = await ctx.Devices.AsNoTracking().SingleAsync(d => d.Token == Token);
        var teams = await ctx.DeviceTeams.AsNoTracking()
            .Where(x => x.DeviceId == device.Id).Select(x => x.TeamId).ToListAsync();
        teams.Should().BeEquivalentTo(new[] { teamA, teamB },
            "a parent who signs in as each child ends up filed on each child's team");
    }

    [Fact(DisplayName = "Sync is idempotent - a relaunch adds nothing")]
    public async Task Sync_Idempotent()
    {
        var (svc, b, ctx) = CreateService();
        var (regA, _, _, _) = await TwoRegistrations(b);

        await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, regA, Req());
        var second = await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, regA, Req());

        second.Teams.Should().Be(0, "nothing new to add");
        second.Registrations.Should().Be(0);

        var device = await ctx.Devices.AsNoTracking().SingleAsync(d => d.Token == Token);
        (await ctx.DeviceTeams.AsNoTracking().CountAsync(x => x.DeviceId == device.Id)).Should().Be(1);
        await NoEventsPoolRow(ctx, device.Id);
        (await ctx.Devices.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact(DisplayName = "Unplaced registration files the registration but no team")]
    public async Task Sync_UnplacedRegistration_NoTeamRow()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var reg = b.AddRegistration(MobileDataBuilder.DefaultUserId, job.JobId, RoleConstants.Staff, teamId: null);
        await b.SaveAsync();

        var result = await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, reg.RegistrationId, Req());

        result.Jobs.Should().Be(1);
        result.Teams.Should().Be(0);
        result.Registrations.Should().Be(1);
        (await ctx.DeviceTeams.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact(DisplayName = "Rotated token folds into one device, not two")]
    public async Task Sync_Rotation_FoldsOntoOneDevice()
    {
        var (svc, b, ctx) = CreateService();
        var (regA, _, _, _) = await TwoRegistrations(b);

        await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, regA, Req(OldToken));
        await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, regA, Req(Token, previous: OldToken));

        var device = await ctx.Devices.AsNoTracking().SingleAsync(d => d.Token == Token);
        (await ctx.DeviceTeams.AsNoTracking().CountAsync(x => x.DeviceId == device.Id))
            .Should().Be(1, "the swap runs before the rows are written");
        (await ctx.Devices.AsNoTracking().CountAsync(d => d.Active)).Should().Be(1);
    }

    [Fact(DisplayName = "A registration the bearer does not own files nothing")]
    public async Task Sync_IgnoresOtherUsersRegistration()
    {
        var (svc, b, ctx) = CreateService();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var theirs = b.AddTeam(div.DivId, "Theirs", ag.AgegroupId, job.JobId);

        var theirReg = b.AddRegistration("someone-else", job.JobId, RoleConstants.Staff, theirs.TeamId);
        await b.SaveAsync();

        var result = await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, theirReg.RegistrationId, Req());

        result.Jobs.Should().Be(0, "ownership is checked against the bearer, never trusted from the regId alone");
        (await ctx.DeviceTeams.AsNoTracking().CountAsync()).Should().Be(0);
        (await ctx.DeviceRegistrationIds.AsNoTracking().CountAsync()).Should().Be(0);
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
        var reg = b.AddRegistration(MobileDataBuilder.DefaultUserId, job.JobId, RoleConstants.Staff, team.TeamId, active: false);
        await b.SaveAsync();

        var result = await svc.SyncDeviceAsync(MobileDataBuilder.DefaultUserId, reg.RegistrationId, Req());

        result.Jobs.Should().Be(0);
        (await ctx.DeviceTeams.AsNoTracking().CountAsync()).Should().Be(0);
    }
}
