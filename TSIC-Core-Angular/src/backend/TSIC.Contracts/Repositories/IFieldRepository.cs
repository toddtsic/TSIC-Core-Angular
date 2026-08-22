using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Fields and FieldsLeagueSeason data access.
/// Used by the Manage Fields scheduling tool (009-1).
/// </summary>
public interface IFieldRepository
{
    /// <summary>
    /// Every pseudo-field row attached to this job's league-season -- the candidates for the
    /// event address sent to Vertical Insure. Returns them all; which one counts is decided by
    /// EventLocationFieldNaming.SelectEventLocation, never here, so the readiness flag and the
    /// VI payload cannot pick differently.
    /// </summary>
    Task<List<EventLocationCandidateDto>> GetEventLocationCandidatesAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>
    /// Fields available for assignment (not already assigned to this league-season).
    /// SuperUser: all of them. Director: those historically used by any of their jobs, plus
    /// this job's own event-location row by name even when it has no history.
    /// Pseudo-fields ("*" rows) are NOT excluded -- they are addresses, and a customer reuses
    /// one address across their events, so they belong in the reusable bank.
    /// </summary>
    Task<List<Fields>> GetAvailableFieldsAsync(
        Guid leagueId,
        string season,
        List<Guid> directorJobIds,
        bool isSuperUser,
        string? eventLocationFieldName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get fields assigned to a league-season as projected DTOs (AsNoTracking).
    /// </summary>
    Task<List<LeagueSeasonFieldDto>> GetLeagueSeasonFieldsAsync(
        Guid leagueId,
        string season,
        CancellationToken ct = default);

    /// <summary>
    /// Count active fields assigned to a league-season (FieldsLeagueSeason rows with
    /// BActive true, system '*' fields excluded). Feeds the scheduling checklist's
    /// step-0 field-setup signal.
    /// </summary>
    Task<int> CountActiveLeagueSeasonFieldsAsync(
        Guid leagueId,
        string season,
        CancellationToken ct = default);

    /// <summary>
    /// Get a single field by ID (read-only).
    /// </summary>
    Task<Fields?> GetFieldByIdAsync(Guid fieldId, CancellationToken ct = default);

    /// <summary>
    /// Get a single field by ID (tracked, for mutation).
    /// </summary>
    Task<Fields?> GetFieldTrackedAsync(Guid fieldId, CancellationToken ct = default);

    /// <summary>
    /// Add a new field (does NOT call SaveChanges).
    /// </summary>
    void Add(Fields field);

    /// <summary>
    /// Remove a field (does NOT call SaveChanges).
    /// </summary>
    void Remove(Fields field);

    /// <summary>
    /// Check if a field is referenced in FieldsLeagueSeason, Schedule, or TimeslotsLeagueSeasonFields.
    /// If true, the field cannot be deleted from the global library.
    /// </summary>
    Task<bool> IsFieldReferencedAsync(Guid fieldId, CancellationToken ct = default);

    /// <summary>
    /// Assign fields to a league-season by creating FieldsLeagueSeason junction records.
    /// </summary>
    Task AssignFieldsToLeagueSeasonAsync(
        Guid leagueId,
        string season,
        List<Guid> fieldIds,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Remove fields from a league-season by deleting FieldsLeagueSeason junction records.
    /// </summary>
    Task RemoveFieldsFromLeagueSeasonAsync(
        Guid leagueId,
        string season,
        List<Guid> fieldIds,
        CancellationToken ct = default);

    /// <summary>
    /// Remove ALL field assignments from a league-season (bulk clear for dev reset).
    /// Returns the number of records deleted.
    /// </summary>
    Task<int> RemoveAllFieldsFromLeagueSeasonAsync(
        Guid leagueId,
        string season,
        CancellationToken ct = default);

    /// <summary>
    /// Get field preferences (Normal/Preferred/Avoid) for all fields in a league-season.
    /// Returns FieldId → FieldPreference (0=Normal, 1=Preferred, 2=Avoid).
    /// </summary>
    Task<Dictionary<Guid, int>> GetFieldPreferencesAsync(
        Guid leagueId,
        string season,
        CancellationToken ct = default);

    /// <summary>
    /// Update the field preference for a single FieldsLeagueSeason record.
    /// </summary>
    Task UpdateFieldPreferenceAsync(
        Guid flsId,
        byte fieldPreference,
        CancellationToken ct = default);

    /// <summary>
    /// Get field names for a list of field IDs. Returns FieldId → FName.
    /// </summary>
    Task<Dictionary<Guid, string>> GetFieldNamesByIdsAsync(
        List<Guid> fieldIds,
        CancellationToken ct = default);

    /// <summary>
    /// Persist all changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
