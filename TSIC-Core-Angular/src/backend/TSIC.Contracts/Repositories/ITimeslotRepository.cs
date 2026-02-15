using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for timeslot dates and field configurations.
/// </summary>
public interface ITimeslotRepository
{
    // ── Dates ──

    Task<List<TimeslotsLeagueSeasonDates>> GetDatesAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default);

    Task<TimeslotsLeagueSeasonDates?> GetDateByIdAsync(int ai, CancellationToken ct = default);

    void AddDate(TimeslotsLeagueSeasonDates date);

    void RemoveDate(TimeslotsLeagueSeasonDates date);

    Task DeleteAllDatesAsync(Guid agegroupId, string season, string year, CancellationToken ct = default);

    // ── Field timeslots ──

    Task<List<TimeslotsLeagueSeasonFields>> GetFieldTimeslotsAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default);

    Task<TimeslotsLeagueSeasonFields?> GetFieldTimeslotByIdAsync(int ai, CancellationToken ct = default);

    void AddFieldTimeslot(TimeslotsLeagueSeasonFields timeslot);

    Task AddFieldTimeslotsRangeAsync(List<TimeslotsLeagueSeasonFields> timeslots, CancellationToken ct = default);

    void RemoveFieldTimeslot(TimeslotsLeagueSeasonFields timeslot);

    Task DeleteAllFieldTimeslotsAsync(Guid agegroupId, string season, string year, CancellationToken ct = default);

    // ── Dashboard aggregates ──

    /// <summary>
    /// Get the set of agegroup IDs that have at least one date row for this season/year.
    /// Used by dashboard timeslot-readiness check.
    /// </summary>
    Task<HashSet<Guid>> GetAgegroupIdsWithDatesAsync(
        string season, string year, CancellationToken ct = default);

    /// <summary>
    /// Get the set of agegroup IDs that have at least one field-timeslot row for this season/year.
    /// Used by dashboard timeslot-readiness check.
    /// </summary>
    Task<HashSet<Guid>> GetAgegroupIdsWithFieldTimeslotsAsync(
        string season, string year, CancellationToken ct = default);

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
