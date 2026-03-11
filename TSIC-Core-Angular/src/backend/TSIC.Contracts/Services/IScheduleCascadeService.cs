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
        Guid jobId, string gamePlacement, byte betweenRoundRows, int gameGuarantee,
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Save agegroup-level overrides. Null property values = inherit from event.
    /// If all profile properties are null AND no waves, deletes the profile row entirely.
    /// </summary>
    Task SaveAgegroupOverrideAsync(
        Guid agegroupId, string? gamePlacement, byte? betweenRoundRows, int? gameGuarantee,
        Dictionary<DateTime, byte>? wavesByDate,
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Save division-level overrides. Null property values = inherit from agegroup.
    /// If all profile properties are null AND no waves, deletes the profile row entirely.
    /// </summary>
    Task SaveDivisionOverrideAsync(
        Guid divisionId, string? gamePlacement, byte? betweenRoundRows, int? gameGuarantee,
        Dictionary<DateTime, byte>? wavesByDate,
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Bulk-seed division wave assignments from projected config.
    /// For each (divisionId, wave) pair, creates wave assignment rows
    /// for all dates the division's agegroup plays on.
    /// Skips divisions that already have wave assignments.
    /// </summary>
    Task SeedDivisionWavesAsync(
        Guid jobId,
        Dictionary<Guid, int> divisionWaves,
        Dictionary<Guid, List<DateTime>> agegroupDates,
        string userId, CancellationToken ct = default);
}
