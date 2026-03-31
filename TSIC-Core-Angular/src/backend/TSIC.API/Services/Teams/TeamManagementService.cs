using TSIC.API.Services.Shared.Firebase;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Teams;

public sealed class TeamManagementService : ITeamManagementService
{
    private readonly ITeamRepository _teamRepo;
    private readonly ITeamDocsRepository _teamDocsRepo;
    private readonly IPushNotificationRepository _pushRepo;
    private readonly IFirebasePushService _firebasePush;

    public TeamManagementService(
        ITeamRepository teamRepo,
        ITeamDocsRepository teamDocsRepo,
        IPushNotificationRepository pushRepo,
        IFirebasePushService firebasePush)
    {
        _teamRepo = teamRepo;
        _teamDocsRepo = teamDocsRepo;
        _pushRepo = pushRepo;
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

    public async Task<bool> DeleteLinkAsync(Guid docId, CancellationToken ct = default)
    {
        var deleted = await _teamDocsRepo.DeleteTeamLinkAsync(docId, ct);
        if (deleted) await _teamDocsRepo.SaveChangesAsync(ct);
        return deleted;
    }

    public async Task<List<TeamPushDto>> GetPushesAsync(Guid teamId, CancellationToken ct = default)
    {
        var detail = await _teamRepo.GetTeamDetailAsync(teamId, ct);
        var jobId = detail?.JobId ?? Guid.Empty;
        return await _teamDocsRepo.GetTeamPushesAsync(teamId, jobId, ct);
    }

    public async Task<TeamPushDto> SendPushAsync(
        Guid teamId, string userId, SendTeamPushRequest request, CancellationToken ct = default)
    {
        var detail = await _teamRepo.GetTeamDetailAsync(teamId, ct);
        var jobId = detail?.JobId ?? Guid.Empty;

        // Get device tokens for this team (or all job devices if AddAllTeams)
        var displayInfo = await _pushRepo.GetJobDisplayInfoAsync(jobId, ct);
        var deviceTokens = await _pushRepo.GetDeviceTokensForJobAsync(jobId, ct);
        // TODO: filter to team-specific devices when AddAllTeams is false

        var title = displayInfo?.JobName ?? "Team Notification";
        var sentCount = await _firebasePush.SendToDevicesAsync(deviceTokens, title, request.PushText, ct: ct);

        // Record in audit trail
        var record = new JobPushNotificationsToAll
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            TeamId = request.AddAllTeams ? null : teamId,
            LebUserId = userId,
            PushText = request.PushText,
            DeviceCount = sentCount,
            Modified = DateTime.UtcNow
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
