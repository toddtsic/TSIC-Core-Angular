using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Teams;

public interface ITeamLookupService
{
    Task<IReadOnlyList<AvailableTeamDto>> GetAvailableTeamsForJobAsync(Guid jobId);
    Task<(decimal Fee, decimal Deposit)> ResolvePerRegistrantAsync(Guid teamId);
}
