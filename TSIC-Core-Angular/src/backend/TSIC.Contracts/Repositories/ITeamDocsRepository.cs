using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface ITeamDocsRepository
{
    Task<List<TeamLinkDto>> GetTeamLinksAsync(Guid teamId, Guid jobId, CancellationToken ct = default);
    Task<TeamDocs> AddTeamLinkAsync(Guid? teamId, Guid? jobId, string userId, string label, string docUrl, CancellationToken ct = default);
    Task<bool> DeleteTeamLinkAsync(Guid docId, CancellationToken ct = default);
    Task<List<TeamPushDto>> GetTeamPushesAsync(Guid teamId, Guid jobId, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
