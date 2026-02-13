using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Fields and FieldsLeagueSeason data access.
/// Used by the Manage Fields scheduling tool (009-1).
/// </summary>
public interface IFieldRepository
{
    /// <summary>
    /// Get fields available for assignment (not assigned to this league-season).
    /// SuperUser: all non-system fields not assigned to this league-season.
    /// Director: fields historically used by any of their jobs, not assigned to this league-season.
    /// System fields (name starts with '*') are always excluded.
    /// </summary>
    Task<List<Fields>> GetAvailableFieldsAsync(
        Guid leagueId,
        string season,
        List<Guid> directorJobIds,
        bool isSuperUser,
        CancellationToken ct = default);

    /// <summary>
    /// Get fields assigned to a league-season, with Field nav prop included.
    /// </summary>
    Task<List<FieldsLeagueSeason>> GetLeagueSeasonFieldsAsync(
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
    /// Persist all changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
