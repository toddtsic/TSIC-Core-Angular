using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Schedule entity data access.
/// </summary>
public interface IScheduleRepository
{
    /// <summary>
    /// Single point of truth for schedule team-name sync.
    /// Re-composes T1Name/T2Name from source entities (Teams.TeamName, Registrations.ClubName,
    /// Jobs.BShowTeamNameOnlyInSchedules) for every round-robin game where this team appears.
    /// Idempotent — safe to call from any admin operation that changes team name, club, or the flag.
    /// Only touches games where T1Type/T2Type == "T" (round-robin); championship/bracket games
    /// are updated by score-entry logic, not here.
    /// </summary>
    Task SynchronizeScheduleNamesForTeamAsync(Guid teamId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Update denormalized AgegroupId/AgegroupName/DivId/DivName on Schedule records
    /// for a team that has been moved to a different division/agegroup.
    /// Only touches round-robin games (T1Type/T2Type == "T").
    /// </summary>
    Task<int> SynchronizeScheduleDivisionForTeamAsync(
        Guid teamId, Guid jobId, Guid newAgegroupId, string newAgegroupName,
        Guid newDivId, string newDivName, CancellationToken ct = default);

    /// <summary>
    /// Update denormalized AgegroupName on all Schedule records where AgegroupId matches.
    /// Called when an agegroup is renamed in the LADT editor.
    /// </summary>
    Task SynchronizeScheduleAgegroupNameAsync(Guid agegroupId, Guid jobId, string newName, CancellationToken ct = default);

    /// <summary>
    /// Update denormalized DivName (and Div2Name) on all Schedule records where
    /// DivId (or Div2Id) matches. Called when a division is renamed in the LADT editor.
    /// </summary>
    Task SynchronizeScheduleDivisionNameAsync(Guid divId, Guid jobId, string newName, CancellationToken ct = default);

    /// <summary>
    /// Re-resolve T1Id/T1Name and T2Id/T2Name for every round-robin schedule record
    /// in a division based on current DivRank assignments. Called after a DivRank swap
    /// or team rename to keep denormalized fields in sync.
    /// Builds a rank → (teamId, displayName) map from active teams, then updates
    /// every game where DivId matches and T1Type/T2Type == "T".
    /// </summary>
    Task SynchronizeScheduleTeamAssignmentsForDivisionAsync(Guid divId, Guid jobId, CancellationToken ct = default);
}
