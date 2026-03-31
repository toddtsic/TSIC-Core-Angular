using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface ITeamManagementService
{
    Task<TeamRosterDetailDto> GetRosterAsync(Guid teamId, CancellationToken ct = default);
    Task<List<TeamLinkDto>> GetLinksAsync(Guid teamId, CancellationToken ct = default);
    Task<TeamLinkDto> AddLinkAsync(Guid teamId, string userId, AddTeamLinkRequest request, CancellationToken ct = default);
    Task<bool> DeleteLinkAsync(Guid docId, CancellationToken ct = default);
    Task<List<TeamPushDto>> GetPushesAsync(Guid teamId, CancellationToken ct = default);
    Task<TeamPushDto> SendPushAsync(Guid teamId, string userId, SendTeamPushRequest request, CancellationToken ct = default);
}
