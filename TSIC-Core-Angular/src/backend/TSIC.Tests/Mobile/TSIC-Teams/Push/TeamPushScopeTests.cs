using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TSIC.API.Services.Shared.Firebase;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;
using TSIC.Domain.JobRules;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.TSIC_Teams.Push;

/// <summary>
/// AddAllTeams is the switch the product spec names: true is the whole job, false is one
/// team. Before this branch existed, false still sent club-wide while the audit row
/// recorded it as team-scoped - the record claimed a reach it did not have.
///
/// The tests assert on the tokens actually handed to Firebase, because that is the only
/// thing that decides whose phone rings.
/// </summary>
public class TeamPushScopeTests
{
    private sealed class Harness
    {
        public required TeamManagementService Svc { get; init; }
        public required MobileDataBuilder B { get; init; }
        public required Mock<IFirebasePushService> Firebase { get; init; }
        public required Infrastructure.Data.SqlDbContext.SqlDbContext Ctx { get; init; }
        public required List<string> Sent { get; init; }
    }

    private static Harness CreateHarness()
    {
        var ctx = DbContextFactory.Create();
        var sent = new List<string>();
        var firebase = new Mock<IFirebasePushService>();
        firebase
            .Setup(f => f.SendToDevicesAsync(
                It.IsAny<PushAudience>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<PushAudience, IReadOnlyList<string>, string, string, string?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (_, tokens, _, _, _, _, _) => sent.AddRange(tokens))
            .ReturnsAsync((PushAudience _, IReadOnlyList<string> tokens, string _, string _, string? _,
                IReadOnlyDictionary<string, string>? _, CancellationToken _) => tokens.Count);

        return new Harness
        {
            Svc = new TeamManagementService(
                new TeamRepository(ctx), new TeamDocsRepository(ctx),
                new PushNotificationRepository(ctx), new DeviceRepository(ctx), firebase.Object),
            B = new MobileDataBuilder(ctx),
            Ctx = ctx,
            Firebase = firebase,
            Sent = sent
        };
    }

    private static Devices Device(string token) => new()
    {
        Id = token, Token = token, Type = "ios", Active = true, Modified = DateTime.Now
    };

    /// <summary>
    /// One job, two teams. A phone on each team, plus a phone registered to the job but on
    /// no team at all - the case that separates the two branches.
    /// </summary>
    private static async Task<(Guid jobId, Guid teamA, Guid teamB)> Fixture(MobileDataBuilder b,
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var teamA = b.AddTeam(div.DivId, "Team A", ag.AgegroupId, job.JobId);
        var teamB = b.AddTeam(div.DivId, "Team B", ag.AgegroupId, job.JobId);

        var phoneA = Device("phone-team-a");
        var phoneB = Device("phone-team-b");
        var phoneJobOnly = Device("phone-job-only");
        ctx.Devices.AddRange(phoneA, phoneB, phoneJobOnly);

        ctx.DeviceJobs.AddRange(
            new DeviceJobs { Id = Guid.NewGuid(), DeviceId = phoneA.Id, JobId = job.JobId, Modified = DateTime.Now },
            new DeviceJobs { Id = Guid.NewGuid(), DeviceId = phoneB.Id, JobId = job.JobId, Modified = DateTime.Now },
            new DeviceJobs { Id = Guid.NewGuid(), DeviceId = phoneJobOnly.Id, JobId = job.JobId, Modified = DateTime.Now });

        ctx.DeviceTeams.AddRange(
            new DeviceTeams { Id = Guid.NewGuid(), DeviceId = phoneA.Id, TeamId = teamA.TeamId, Modified = DateTime.Now },
            new DeviceTeams { Id = Guid.NewGuid(), DeviceId = phoneB.Id, TeamId = teamB.TeamId, Modified = DateTime.Now });

        await b.SaveAsync();
        return (job.JobId, teamA.TeamId, teamB.TeamId);
    }

    private static SendTeamPushRequest Push(bool allTeams) =>
        new() { PushText = "Practice moved to 6pm", AddAllTeams = allTeams };

    [Fact(DisplayName = "AddAllTeams false reaches only that team's subscribers")]
    public async Task TeamScoped_ReachesOnlyThatTeam()
    {
        var h = CreateHarness();
        var (jobId, teamA, _) = await Fixture(h.B, h.Ctx);

        var result = await h.Svc.SendPushAsync(teamA, "director-1", jobId, false, callerHasJobWideReach: true, callerTeamId: null, Push(false));

        result.Should().NotBeNull();
        h.Sent.Should().BeEquivalentTo(new[] { "phone-team-a" },
            "a team-scoped alert must not reach the rest of the club");
        result!.TeamId.Should().Be(teamA, "the audit row records the scope actually sent");
    }

    [Fact(DisplayName = "AddAllTeams true reaches every device in the job")]
    public async Task JobScoped_ReachesWholeJob()
    {
        var h = CreateHarness();
        var (jobId, teamA, _) = await Fixture(h.B, h.Ctx);

        var result = await h.Svc.SendPushAsync(teamA, "director-1", jobId, false, callerHasJobWideReach: true, callerTeamId: null, Push(true));

        result.Should().NotBeNull();
        h.Sent.Should().BeEquivalentTo(
            new[] { "phone-team-a", "phone-team-b", "phone-job-only" });
        result!.TeamId.Should().BeNull("a job-wide send is not team-scoped");
    }

    [Fact(DisplayName = "Team-scoped send skips a device with no subscription to that team")]
    public async Task TeamScoped_SkipsUnsubscribed()
    {
        var h = CreateHarness();
        var (jobId, _, teamB) = await Fixture(h.B, h.Ctx);

        await h.Svc.SendPushAsync(teamB, "director-1", jobId, false, callerHasJobWideReach: true, callerTeamId: null, Push(false));

        h.Sent.Should().NotContain("phone-job-only",
            "registered to the job is not the same as subscribed to the team");
        h.Sent.Should().NotContain("phone-team-a");
    }

    [Fact(DisplayName = "Cross-job caller is refused before any token is fetched")]
    public async Task CrossJob_SendsNothing()
    {
        var h = CreateHarness();
        var (_, teamA, _) = await Fixture(h.B, h.Ctx);

        var result = await h.Svc.SendPushAsync(teamA, "director-1", Guid.NewGuid(), false, callerHasJobWideReach: true, callerTeamId: null, Push(true));

        result.Should().BeNull();
        h.Sent.Should().BeEmpty();
    }
}
