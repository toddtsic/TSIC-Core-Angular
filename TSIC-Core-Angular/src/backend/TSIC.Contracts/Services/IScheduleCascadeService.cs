using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Resolves the 3-level scheduling cascade (Event → Agegroup → Division)
/// for GamePlacement, BetweenRoundRows, and per-date Wave assignments.
/// </summary>
public interface IScheduleCascadeService
{
    /// <summary>
    /// Load and resolve the full cascade for a job. Every division gets
    /// effective values for GamePlacement, BetweenRoundRows, and per-date Wave.
    /// </summary>
    Task<ScheduleCascadeSnapshot> ResolveAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>Save event-level defaults (non-nullable — always has values).</summary>
    Task SaveEventDefaultsAsync(
        Guid jobId, string gamePlacement, byte betweenRoundRows,
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Save agegroup-level overrides. Null property values = inherit from event.
    /// If both are null AND no waves, deletes the profile row entirely.
    /// </summary>
    Task SaveAgegroupOverrideAsync(
        Guid agegroupId, string? gamePlacement, byte? betweenRoundRows,
        Dictionary<DateTime, byte>? wavesByDate,
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Save division-level overrides. Null property values = inherit from agegroup.
    /// If both are null AND no waves, deletes the profile row entirely.
    /// </summary>
    Task SaveDivisionOverrideAsync(
        Guid divisionId, string? gamePlacement, byte? betweenRoundRows,
        Dictionary<DateTime, byte>? wavesByDate,
        string userId, CancellationToken ct = default);
}
