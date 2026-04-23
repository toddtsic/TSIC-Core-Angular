using TSIC.Contracts.Dtos.TeamLink;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for [mobile].[Team_Docs] — team-scoped labeled URLs that the
/// TSIC-Teams mobile app surfaces to players.
///
/// Read shape collapses the legacy "all teams" fan-out (multiple rows sharing
/// the same Job + Label + DocUrl, each pinned to a different TeamId) into a
/// single display row with TeamId == null.
/// </summary>
public interface ITeamLinkRepository
{
    /// <summary>
    /// All team links for a job, with the fan-out group collapsed for display.
    /// </summary>
    Task<List<AdminTeamLinkDto>> GetByJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Active team IDs in a job, excluding agegroups "Dropped Teams" and "Registration".
    /// Used by the service layer when fanning out an "all teams" link.
    /// </summary>
    Task<List<Guid>> GetActiveTeamIdsForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Active teams in a job with display labels ("AgegroupName - TeamName"),
    /// excluding agegroups "Dropped Teams" and "Registration". Used by the
    /// admin form's team dropdown.
    /// </summary>
    Task<List<TeamLinkTeamOptionDto>> GetActiveTeamOptionsForJobAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Single tracked TeamDoc by its primary key, for in-place edit of one row.
    /// </summary>
    Task<TeamDocs?> GetByDocIdAsync(Guid docId, CancellationToken ct = default);

    /// <summary>
    /// All TeamDocs in a job that share the same Label + DocUrl. This is the
    /// "group" the legacy delete and edit-as-fan-out paths operate on.
    /// </summary>
    Task<List<TeamDocs>> GetGroupByLabelAndUrlAsync(
        Guid jobId, string label, string docUrl, CancellationToken ct = default);

    void AddRange(IEnumerable<TeamDocs> records);

    void RemoveRange(IEnumerable<TeamDocs> records);

    Task SaveChangesAsync(CancellationToken ct = default);
}
