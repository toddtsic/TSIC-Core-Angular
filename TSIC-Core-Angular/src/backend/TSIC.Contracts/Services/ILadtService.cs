using TSIC.Contracts.Dtos.Ladt;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the LADT (League/Age-Group/Division/Team) admin hierarchy.
/// Single service handles all 4 entity levels for simpler DI and shared validation.
/// </summary>
public interface ILadtService
{
    // ── Tree ──

    Task<LadtTreeRootDto> GetLadtTreeAsync(Guid jobId, CancellationToken cancellationToken = default);

    // ── League ──

    Task<LeagueDetailDto> GetLeagueDetailAsync(Guid leagueId, Guid jobId, CancellationToken cancellationToken = default);
    Task<LeagueDetailDto> UpdateLeagueAsync(Guid leagueId, UpdateLeagueRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default);

    // ── Agegroup ──

    Task<AgegroupDetailDto> GetAgegroupDetailAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default);
    Task<AgegroupDetailDto> CreateAgegroupAsync(CreateAgegroupRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task<AgegroupDetailDto> UpdateAgegroupAsync(Guid agegroupId, UpdateAgegroupRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task DeleteAgegroupAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default);
    Task<Guid> AddStubAgegroupAsync(Guid leagueId, Guid jobId, string userId, CancellationToken cancellationToken = default);

    // ── Division ──

    Task<DivisionDetailDto> GetDivisionDetailAsync(Guid divId, Guid jobId, CancellationToken cancellationToken = default);
    Task<DivisionDetailDto> CreateDivisionAsync(CreateDivisionRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task<DivisionDetailDto> UpdateDivisionAsync(Guid divId, UpdateDivisionRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task DeleteDivisionAsync(Guid divId, Guid jobId, CancellationToken cancellationToken = default);
    Task<Guid> AddStubDivisionAsync(Guid agegroupId, Guid jobId, string userId, CancellationToken cancellationToken = default);

    // ── Team ──

    Task<TeamDetailDto> GetTeamDetailAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default);
    Task<TeamDetailDto> CreateTeamAsync(CreateTeamRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task<TeamDetailDto> UpdateTeamAsync(Guid teamId, UpdateTeamRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task<DeleteTeamResultDto> DeleteTeamAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default);
    Task<DropTeamResultDto> DropTeamAsync(Guid teamId, Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task<TeamDetailDto> CloneTeamAsync(Guid teamId, CloneTeamRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task<Guid> AddStubTeamAsync(Guid divId, Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task<List<ClubRegistrationDto>> GetClubRegistrationsForJobAsync(Guid jobId, CancellationToken ct = default);
    Task<MoveTeamToClubResultDto> MoveTeamToClubAsync(Guid teamId, MoveTeamToClubRequest request, Guid jobId, string userId, CancellationToken ct = default);

    // ── Sibling batch queries ──

    Task<List<LeagueDetailDto>> GetLeagueSiblingsAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<List<AgegroupDetailDto>> GetAgegroupsByLeagueAsync(Guid leagueId, Guid jobId, CancellationToken cancellationToken = default);
    Task<List<DivisionDetailDto>> GetDivisionsByAgegroupAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default);
    Task<List<TeamDetailDto>> GetTeamsByDivisionAsync(Guid divId, Guid jobId, CancellationToken cancellationToken = default);

    // ── Batch operations ──

    Task<int> AddWaitlistAgegroupsAsync(Guid jobId, string userId, CancellationToken cancellationToken = default);
    Task<int> UpdatePlayerFeesToAgegroupFeesAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default);
}
