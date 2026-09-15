using FluentAssertions;
using TSIC.API.Services.Admin;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.SearchRegistrations;

/// <summary>
/// SCHEDULE PREVIEW INVITE — SEND SIDE
///
/// The modal offers the preview invite only while the job's preview door is open and the search is Club Reps.
/// StartBatchEmailAsync re-checks all of it, so a hand-built request can't mail a preview link to a player, to a
/// deactivated rep, for another event, or while the door is shut. Filter options expose the door to the modal.
/// </summary>
public class SchedulePreviewInviteSendTests
{
    private const string PreviewBody = "View by !INVITE_EXPIRES: !SCHEDULE_PREVIEW_LINK";

    private sealed record Fixture(SqlDbContext Ctx, RegistrationSearchService Svc, Jobs Job, Registrations Rep, Registrations Player);

    private static async Task<Fixture> BuildAsync()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        job.JobName = "Spring Cup";
        job.BAllowClubRepSchedulePreview = true;
        job.BScheduleAllowPublicAccess = false;
        job.ExpiryUsers = DateTime.Now.AddDays(30);
        b.AddRole(RoleConstants.ClubRep, RoleConstants.Names.ClubRepName);
        b.AddRole(RoleConstants.Player, RoleConstants.Names.PlayerName);
        var rep = b.AddRegistration(job.JobId, b.AddUser("Cara", "Rep").Id, RoleConstants.ClubRep, feeTotal: 0m);
        var player = b.AddRegistration(job.JobId, b.AddUser("Pat", "Player").Id, RoleConstants.Player, feeTotal: 0m);
        await b.SaveAsync();

        return new Fixture(ctx, InviteExpiryCapTests.BuildService(ctx, new JobRepository(ctx)), job, rep, player);
    }

    private static BatchEmailRequest PreviewRequest(Fixture f, params Guid[] regIds) => new()
    {
        RegistrationIds = regIds.Length > 0 ? [.. regIds] : [f.Rep.RegistrationId],
        Subject = "Preview the schedule",
        BodyTemplate = PreviewBody,
        InviteLinkTargetJobId = f.Job.JobId,
        InviteExpiryHours = 24
    };

    private static async Task<string> RefusalAsync(Fixture f, BatchEmailRequest request)
    {
        // A send that passes the checks continues into mocked collaborators, which may fail for unrelated
        // reasons; only a preview refusal matters here.
        try { await f.Svc.StartBatchEmailAsync(f.Job.JobId, "admin-1", request); }
        catch (InvalidOperationException ex) { return ex.Message; }
        catch (Exception) { }
        return "";
    }

    [Fact(DisplayName = "Door open, active Club Reps, this event → not refused")]
    public async Task ValidSend_NotRefused()
    {
        var f = await BuildAsync();
        (await RefusalAsync(f, PreviewRequest(f))).Should().NotContainAny("preview", "Club Rep");
    }

    [Fact(DisplayName = "A player among the recipients → refused")]
    public async Task PlayerRecipient_Refused()
    {
        var f = await BuildAsync();
        (await RefusalAsync(f, PreviewRequest(f, f.Rep.RegistrationId, f.Player.RegistrationId)))
            .Should().Contain("only to active Club Reps");
    }

    [Fact(DisplayName = "A deactivated Club Rep → refused")]
    public async Task InactiveRep_Refused()
    {
        var f = await BuildAsync();
        f.Rep.BActive = false;
        await f.Ctx.SaveChangesAsync();

        (await RefusalAsync(f, PreviewRequest(f))).Should().Contain("only to active Club Reps");
    }

    [Fact(DisplayName = "Targeting another event → refused")]
    public async Task OtherEventTarget_Refused()
    {
        var f = await BuildAsync();
        (await RefusalAsync(f, PreviewRequest(f) with { InviteLinkTargetJobId = Guid.NewGuid() }))
            .Should().Contain("must be for this event");
    }

    [Fact(DisplayName = "No target at all → refused (no token-less preview link goes out)")]
    public async Task NoTarget_Refused()
    {
        var f = await BuildAsync();
        (await RefusalAsync(f, PreviewRequest(f) with { InviteLinkTargetJobId = null }))
            .Should().Contain("must be for this event");
    }

    [Theory(DisplayName = "Mixed with a registration invite link → refused")]
    [InlineData("!CLUBREP_INVITE_LINK")]
    [InlineData("!INVITE_LINK")]
    public async Task MixedWithRegistrationInvite_Refused(string registrationToken)
    {
        var f = await BuildAsync();
        (await RefusalAsync(f, PreviewRequest(f) with { BodyTemplate = PreviewBody + " " + registrationToken }))
            .Should().Contain("can't be combined");
    }

    [Theory(DisplayName = "Door shut — flag off, schedule public, or event expired → refused")]
    [InlineData("flag-off")]
    [InlineData("public")]
    [InlineData("expired")]
    public async Task DoorShut_Refused(string how)
    {
        var f = await BuildAsync();
        switch (how)
        {
            case "flag-off": f.Job.BAllowClubRepSchedulePreview = false; break;
            case "public": f.Job.BScheduleAllowPublicAccess = true; break;
            case "expired": f.Job.ExpiryUsers = DateTime.Now.AddDays(-1); break;
        }
        await f.Ctx.SaveChangesAsync();

        (await RefusalAsync(f, PreviewRequest(f))).Should().Contain("Club Rep schedule preview on");
    }

    [Fact(DisplayName = "The token in the subject line is checked the same as in the body")]
    public async Task TokenInSubject_AlsoChecked()
    {
        var f = await BuildAsync();
        var request = PreviewRequest(f, f.Player.RegistrationId) with { Subject = "!SCHEDULE_PREVIEW_LINK", BodyTemplate = "hi" };
        (await RefusalAsync(f, request)).Should().Contain("only to active Club Reps");
    }

    [Fact(DisplayName = "Filter options: the preview target is this job while the door is open, empty once shut")]
    public async Task FilterOptions_ExposeTheDoor()
    {
        var f = await BuildAsync();

        (await f.Svc.GetFilterOptionsAsync(f.Job.JobId)).EligibleSchedulePreviewInviteTargetJobs
            .Should().ContainSingle().Which.JobId.Should().Be(f.Job.JobId);

        f.Job.BAllowClubRepSchedulePreview = false;
        await f.Ctx.SaveChangesAsync();

        (await f.Svc.GetFilterOptionsAsync(f.Job.JobId)).EligibleSchedulePreviewInviteTargetJobs.Should().BeEmpty();
    }
}
