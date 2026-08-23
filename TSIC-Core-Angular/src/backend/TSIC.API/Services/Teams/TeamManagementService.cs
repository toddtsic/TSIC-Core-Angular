using TSIC.API.Services.Shared.Firebase;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Domain.JobRules;

namespace TSIC.API.Services.Teams;

public sealed class TeamManagementService : ITeamManagementService
{
    private readonly ITeamRepository _teamRepo;
    private readonly ITeamDocsRepository _teamDocsRepo;
    private readonly IPushNotificationRepository _pushRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IFirebasePushService _firebasePush;

    public TeamManagementService(
        ITeamRepository teamRepo,
        ITeamDocsRepository teamDocsRepo,
        IPushNotificationRepository pushRepo,
        IDeviceRepository deviceRepo,
        IFirebasePushService firebasePush)
    {
        _teamRepo = teamRepo;
        _teamDocsRepo = teamDocsRepo;
        _pushRepo = pushRepo;
        _deviceRepo = deviceRepo;
        _firebasePush = firebasePush;
    }

    public async Task<TeamRosterDetailDto> GetRosterAsync(Guid teamId, CancellationToken ct = default)
    {
        return await _teamRepo.GetTeamRosterMobileAsync(teamId, ct);
    }

    public async Task<List<TeamLinkDto>> GetLinksAsync(Guid teamId, CancellationToken ct = default)
    {
        var detail = await _teamRepo.GetTeamDetailAsync(teamId, ct);
        var jobId = detail?.JobId ?? Guid.Empty;
        return await _teamDocsRepo.GetTeamLinksAsync(teamId, jobId, ct);
    }

    public async Task<TeamLinkDto> AddLinkAsync(
        Guid teamId, string userId, AddTeamLinkRequest request, CancellationToken ct = default)
    {
        var detail = await _teamRepo.GetTeamDetailAsync(teamId, ct);
        var jobId = detail?.JobId ?? Guid.Empty;

        Guid? docTeamId = request.AddAllTeams ? null : teamId;
        Guid? docJobId = request.AddAllTeams ? jobId : null;

        var doc = await _teamDocsRepo.AddTeamLinkAsync(docTeamId, docJobId, userId, request.Label, request.DocUrl, ct);
        await _teamDocsRepo.SaveChangesAsync(ct);

        return new TeamLinkDto
        {
            DocId = doc.DocId,
            TeamId = doc.TeamId,
            JobId = doc.JobId,
            Label = doc.Label ?? "",
            DocUrl = doc.DocUrl ?? "",
            UserId = userId,
            CreateDate = doc.CreateDate
        };
    }

    public async Task<bool> DeleteLinkAsync(Guid docId, Guid teamId, CancellationToken ct = default)
    {
        var detail = await _teamRepo.GetTeamDetailAsync(teamId, ct);
        var jobId = detail?.JobId ?? Guid.Empty;
        var deleted = await _teamDocsRepo.DeleteTeamLinkAsync(docId, teamId, jobId, ct);
        if (deleted) await _teamDocsRepo.SaveChangesAsync(ct);
        return deleted;
    }

    public async Task<List<TeamPushDto>> GetPushesAsync(Guid teamId, CancellationToken ct = default)
    {
        var detail = await _teamRepo.GetTeamDetailAsync(teamId, ct);
        var jobId = detail?.JobId ?? Guid.Empty;
        return await _teamDocsRepo.GetTeamPushesAsync(teamId, jobId, ct);
    }

    public async Task<TeamPushDto?> SendPushAsync(
        Guid teamId,
        string userId,
        Guid? callerJobId,
        bool callerIsSuperuser,
        SendTeamPushRequest request,
        CancellationToken ct = default)
    {
        var detail = await _teamRepo.GetTeamDetailAsync(teamId, ct);
        var jobId = detail?.JobId ?? Guid.Empty;

        // Job scope. teamId arrives off the route, so without this a director of one event
        // could push to any team in any other event -- an unrecallable blast to a club they
        // have no relationship with. Superuser is exempt, matching cross-job ops elsewhere.
        if (!callerIsSuperuser && (callerJobId == null || jobId == Guid.Empty || callerJobId != jobId))
            return null;

        // Which app this job feeds decides both the pool and the sender. One app, never both:
        // Device_Teams mixes TSIC-Events favourites (null RegistrationId) with TSIC-Teams app
        // rows (RegistrationId set), and sending that union through either project's credential
        // means half the batch comes back SenderIdMismatch.
        var flags = await _pushRepo.GetJobPushFlagsAsync(jobId, ct);
        var audience = flags == null
            ? PushAudience.None
            : PushAudienceResolver.Resolve(flags.Value.JobTypeId, flags.Value.TeamsEnabled);

        if (audience == PushAudience.None)
            throw new InvalidOperationException(
                "This job feeds no mobile app, so a push has no audience. Showcase jobs run "
                + "neither app; other non-scheduling jobs need TSIC-Teams enabled first.");

        var displayInfo = await _pushRepo.GetJobDisplayInfoAsync(jobId, ct);

        // AddAllTeams is the switch the product spec names: true is the job, false is this
        // team. Until this branch existed, false still sent club-wide while the audit row
        // below recorded it as team-scoped -- the record claimed a reach it did not have.
        //
        // The branch is HERE, at the call site, deliberately. The job-wide pools are shared
        // with the website push screen (PushNotificationService.SendPushToAllAsync); narrowing
        // them inside the repository would silently shrink the website audience with no error
        // anywhere.
        var deviceTokens = request.AddAllTeams
            ? audience == PushAudience.Teams
                ? await _pushRepo.GetTeamsDeviceTokensForJobAsync(jobId, ct)
                : await _pushRepo.GetDeviceTokensForJobAsync(jobId, ct)
            : await _deviceRepo.GetTokensSubscribedToTeamsAsync(audience, teamId, null, ct);

        var title = displayInfo?.JobName ?? "Team Notification";
        var sentCount = await _firebasePush.SendToDevicesAsync(
            audience, deviceTokens, title, request.PushText, ct: ct);

        // Record in audit trail
        var record = new JobPushNotificationsToAll
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            TeamId = request.AddAllTeams ? null : teamId,
            LebUserId = userId,
            PushText = request.PushText,
            DeviceCount = sentCount,
            Modified = DateTime.Now
        };
        _pushRepo.AddNotificationRecord(record);
        await _pushRepo.SaveChangesAsync(ct);

        return new TeamPushDto
        {
            Id = record.Id,
            JobId = jobId,
            TeamId = record.TeamId,
            UserId = userId,
            PushText = request.PushText,
            DeviceCount = sentCount,
            AddAllTeams = request.AddAllTeams,
            CreateDate = record.Modified
        };
    }
}
