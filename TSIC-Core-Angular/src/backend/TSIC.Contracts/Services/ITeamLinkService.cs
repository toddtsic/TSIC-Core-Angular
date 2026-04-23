using TSIC.Contracts.Dtos.TeamLink;

namespace TSIC.Contracts.Services;

/// <summary>
/// Admin-side CRUD for [mobile].[Team_Docs]. Preserves legacy write semantics
/// so the TSIC-Teams mobile app's read shape is unaffected.
/// </summary>
public interface ITeamLinkService
{
    Task<List<AdminTeamLinkDto>> GetForJobAsync(Guid jobId, CancellationToken ct = default);

    Task<List<TeamLinkTeamOptionDto>> GetAvailableTeamsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Insert a team link. Legacy behavior preserved:
    ///  - Any existing rows in the job sharing the same Label + DocUrl are deleted first.
    ///  - When TeamId == null, fan out one row per active team
    ///    (excluding agegroups "Dropped Teams" and "Registration").
    ///  - When TeamId is set, insert a single row.
    /// </summary>
    Task AddAsync(Guid jobId, string userId, CreateTeamLinkRequest request, CancellationToken ct = default);

    /// <summary>
    /// Update a team link by DocId.
    ///  - When TeamId is set, update that single row in place.
    ///  - When TeamId == null, delete the existing Label + DocUrl group in the
    ///    job and fan out a fresh group across all active teams.
    /// </summary>
    Task UpdateAsync(Guid jobId, string userId, Guid docId, UpdateTeamLinkRequest request, CancellationToken ct = default);

    /// <summary>
    /// Delete a team link group. Looks up the row by DocId, then deletes every
    /// row in the same job sharing its Label + DocUrl.
    /// </summary>
    Task DeleteAsync(Guid jobId, Guid docId, CancellationToken ct = default);
}
