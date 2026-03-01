using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Per-agegroup readiness data returned by the repository.
/// Service layer transforms this into AgegroupCanvasReadinessDto.
/// </summary>
/// <summary>Per-DOW field scheduling parameters from field-timeslot rows.</summary>
public record DowFieldData
{
    public required string Dow { get; init; }
    public required int FieldCount { get; init; }
    public required List<string> StartTimes { get; init; }
    public required List<int> GsiValues { get; init; }
    public required List<int> MaxGamesValues { get; init; }
    public required int TotalMaxGamesSum { get; init; }
}

public record AgegroupReadinessData
{
    public required int DateCount { get; init; }
    public required int FieldCount { get; init; }
    public required List<string> DaysOfWeek { get; init; }
    public required List<int> DistinctGsi { get; init; }
    public required List<string> DistinctStartTimes { get; init; }
    public required List<int> DistinctMaxGames { get; init; }
    public required int TotalMaxGamesSum { get; init; }

    /// <summary>Actual calendar dates from TimeslotsLeagueSeasonDates.</summary>
    public required List<DateTime> Dates { get; init; }

    /// <summary>Per-DOW field scheduling parameters for game day line construction.</summary>
    public required List<DowFieldData> PerDowFields { get; init; }
}

/// <summary>
/// Repository for timeslot dates and field configurations.
/// </summary>
public interface ITimeslotRepository
{
    // ── Dates ──

    Task<List<TimeslotDateDto>> GetDatesAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default);

    Task<TimeslotsLeagueSeasonDates?> GetDateByIdAsync(int ai, CancellationToken ct = default);

    void AddDate(TimeslotsLeagueSeasonDates date);

    void RemoveDate(TimeslotsLeagueSeasonDates date);

    Task DeleteAllDatesAsync(Guid agegroupId, string season, string year, CancellationToken ct = default);

    // ── Field timeslots ──

    Task<List<TimeslotFieldDto>> GetFieldTimeslotsAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default);

    Task<TimeslotsLeagueSeasonFields?> GetFieldTimeslotByIdAsync(int ai, CancellationToken ct = default);

    void AddFieldTimeslot(TimeslotsLeagueSeasonFields timeslot);

    Task AddFieldTimeslotsRangeAsync(List<TimeslotsLeagueSeasonFields> timeslots, CancellationToken ct = default);

    void RemoveFieldTimeslot(TimeslotsLeagueSeasonFields timeslot);

    Task DeleteAllFieldTimeslotsAsync(Guid agegroupId, string season, string year, CancellationToken ct = default);

    // ── Dashboard aggregates ──

    /// <summary>
    /// Get the set of agegroup IDs that have at least one date row for this season/year.
    /// Scoped to agegroups belonging to the specified league.
    /// </summary>
    Task<HashSet<Guid>> GetAgegroupIdsWithDatesAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default);

    /// <summary>
    /// Get the set of agegroup IDs that have at least one field-timeslot row for this season/year.
    /// Scoped to agegroups belonging to the specified league.
    /// </summary>
    Task<HashSet<Guid>> GetAgegroupIdsWithFieldTimeslotsAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default);

    /// <summary>
    /// Get per-agegroup readiness data for event dashboard display.
    /// Returns date counts, distinct field counts, and field scheduling parameters.
    /// Scoped to agegroups belonging to the specified league.
    /// </summary>
    Task<Dictionary<Guid, AgegroupReadinessData>> GetReadinessDataAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default);

    // ── Cloning support queries ──

    /// <summary>Get all field timeslots for a source agegroup (used by clone-fields, clone-by-field, etc.).</summary>
    Task<List<TimeslotsLeagueSeasonFields>> GetFieldTimeslotsByFilterAsync(
        Guid agegroupId, string season, string year,
        Guid? fieldId = null, Guid? divId = null, string? dow = null,
        CancellationToken ct = default);

    /// <summary>Get all dates for a source agegroup (used by clone-dates).</summary>
    Task<List<TimeslotsLeagueSeasonDates>> GetDatesByAgegroupAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default);

    // ── Division/field lists for cartesian product ──

    /// <summary>Get active divisions for an agegroup (excluding Dropped/Waitlist/Unassigned).</summary>
    Task<List<Guid>> GetActiveDivisionIdsAsync(Guid agegroupId, Guid jobId, CancellationToken ct = default);

    /// <summary>Get field IDs assigned to the league-season (excluding system fields).</summary>
    Task<List<Guid>> GetAssignedFieldIdsAsync(Guid leagueId, string season, CancellationToken ct = default);

    // ── Capacity ──

    /// <summary>Count pairings for team count (to compute games needed).</summary>
    Task<int> GetPairingCountAsync(Guid leagueId, string season, int teamCount, CancellationToken ct = default);

    // ── Persist ──

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
