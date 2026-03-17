using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Per-agegroup readiness data returned by the repository.
/// Service layer transforms this into AgegroupCanvasReadinessDto.
/// </summary>
/// <summary>Most common field schedule defaults from a league-season-year.</summary>
public record FieldScheduleDefaults
{
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
}

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

    /// <summary>Per-date round counts: date → number of TimeslotsLeagueSeasonDates rows.</summary>
    public required Dictionary<DateTime, int> RoundsPerDate { get; init; }

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

    /// <summary>Delete date entries for an agegroup on a specific date.</summary>
    Task DeleteDatesByDateAsync(Guid agegroupId, DateTime gDate, string season, string year, CancellationToken ct = default);

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

    /// <summary>
    /// Get per-agegroup total round counts from timeslot dates for a league-season-year.
    /// Keyed by agegroup name (for cross-year matching). Returns count of date rows per agegroup.
    /// </summary>
    Task<Dictionary<string, int>> GetRoundCountsByAgegroupNameAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default);

    // ── Bulk field config update ──

    /// <summary>
    /// Get ALL field timeslot rows for a league-season-year as TRACKED entities (for in-place update).
    /// Used by UpdateFieldConfig to modify GSI/StartTime/MaxGames without recreating rows.
    /// </summary>
    Task<List<TimeslotsLeagueSeasonFields>> GetAllFieldTimeslotsForUpdateAsync(
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

    /// <summary>
    /// Get the distinct field IDs used by each agegroup in field-timeslot rows.
    /// Returns agegroupId → list of distinct fieldIds.
    /// Scoped to agegroups belonging to the specified league.
    /// </summary>
    Task<Dictionary<Guid, List<Guid>>> GetFieldIdsPerAgegroupAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default);

    /// <summary>
    /// Get the distinct field IDs used by each division in field-timeslot rows.
    /// Only includes rows where DivId IS NOT NULL. Returns divisionId → list of distinct fieldIds.
    /// </summary>
    Task<Dictionary<Guid, List<Guid>>> GetFieldIdsPerDivisionAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default);

    /// <summary>
    /// Get event-level field summaries (FieldId + FieldName) for all fields assigned
    /// to a league-season. Excludes system fields (name starts with '*').
    /// </summary>
    Task<List<EventFieldSummaryDto>> GetEventFieldSummariesAsync(
        Guid leagueId, string season, CancellationToken ct = default);

    /// <summary>
    /// Delete all field-timeslot rows for a specific (agegroup, field) combination.
    /// Used when removing a field from an agegroup's field assignment.
    /// </summary>
    Task DeleteFieldTimeslotsByFieldAsync(
        Guid agegroupId, Guid fieldId, string season, string year, CancellationToken ct = default);

    /// <summary>
    /// Delete field-timeslot rows for a specific (agegroup, division, field) combination.
    /// Used when removing a field from a division-level override.
    /// </summary>
    Task DeleteFieldTimeslotsByDivFieldAsync(
        Guid agegroupId, Guid divId, Guid fieldId, string season, string year, CancellationToken ct = default);

    // ── Prior-year defaults ──

    /// <summary>
    /// Get the most common field schedule defaults (StartTime, GSI, MaxGamesPerField)
    /// from any league-season-year. Returns null if no field timeslots exist.
    /// </summary>
    Task<FieldScheduleDefaults?> GetDominantFieldDefaultsAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default);

    // ── Capacity ──

    /// <summary>Count pairings for team count (to compute games needed).</summary>
    Task<int> GetPairingCountAsync(Guid leagueId, string season, int teamCount, CancellationToken ct = default);

    // ── Cascade date support ──

    /// <summary>Get ALL date rows for a specific GDate across all agegroups in a league-season-year (tracked for update).</summary>
    Task<List<TimeslotsLeagueSeasonDates>> GetDatesByDateTrackedAsync(
        Guid leagueId, DateTime gDate, string season, string year, CancellationToken ct = default);

    /// <summary>Check if any date row exists for a specific GDate in a league-season-year.</summary>
    Task<bool> DateExistsAsync(
        Guid leagueId, DateTime gDate, string season, string year, CancellationToken ct = default);

    // ── Persist ──

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
