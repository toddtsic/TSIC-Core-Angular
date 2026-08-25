using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface ITeamManagementService
{
    Task<TeamRosterDetailDto> GetRosterAsync(Guid teamId, CancellationToken ct = default);
    Task<List<TeamLinkDto>> GetLinksAsync(Guid teamId, CancellationToken ct = default);
    Task<TeamLinkDto> AddLinkAsync(Guid teamId, string userId, AddTeamLinkRequest request, CancellationToken ct = default);
    Task<bool> DeleteLinkAsync(Guid docId, Guid teamId, bool allowJobLevel, CancellationToken ct = default);
    Task<List<TeamPushDto>> GetPushesAsync(Guid teamId, CancellationToken ct = default);
    /// <summary>
    /// Sends a team push. Returns null when the caller is not scoped to the team's job
    /// (cross-job attempt); the controller maps that to 403.
    /// </summary>
    /// <summary>
    /// Returns null when the caller is out of reach — the controller maps that to 403.
    /// callerHasJobWideReach is Director/Superuser; when false, callerTeamId is the only
    /// team the caller may address and AddAllTeams is refused outright.
    /// </summary>
    Task<TeamPushDto?> SendPushAsync(
        Guid teamId,
        string userId,
        Guid? callerJobId,
        bool callerIsSuperuser,
        bool callerHasJobWideReach,
        Guid? callerTeamId,
        SendTeamPushRequest request,
        CancellationToken ct = default);
}
