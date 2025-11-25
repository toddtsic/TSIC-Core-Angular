using TSIC.API.DTOs;

namespace TSIC.API.Services;

public interface ITeamLookupService
{
    Task<IReadOnlyList<AvailableTeamDto>> GetAvailableTeamsForJobAsync(Guid jobId);
    Task<(decimal Fee, decimal Deposit)> ResolvePerRegistrantAsync(Guid teamId);
}
