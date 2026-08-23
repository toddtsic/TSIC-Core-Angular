using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface ITeamManagementService
{
    Task<TeamRosterDetailDto> GetRosterAsync(Guid teamId, CancellationToken ct = default);
    Task<List<TeamLinkDto>> GetLinksAsync(Guid teamId, CancellationToken ct = default);
    Task<TeamLinkDto> AddLinkAsync(Guid teamId, string userId, AddTeamLinkRequest request, CancellationToken ct = default);
    Task<bool> DeleteLinkAsync(Guid docId, Guid teamId, CancellationToken ct = default);
    Task<List<TeamPushDto>> GetPushesAsync(Guid teamId, CancellationToken ct = default);
    /// <summary>
    /// Sends a team push. Returns null when the caller is not scoped to the team's job
    /// (cross-job attempt); the controller maps that to 403.
    /// </summary>
    Task<TeamPushDto?> SendPushAsync(
        Guid teamId,
        string userId,
        Guid? callerJobId,
        bool callerIsSuperuser,
        SendTeamPushRequest request,
        CancellationToken ct = default);
}
