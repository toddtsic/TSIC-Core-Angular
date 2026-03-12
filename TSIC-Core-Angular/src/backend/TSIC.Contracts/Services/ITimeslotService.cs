using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Manage Timeslots scheduling tool.
/// Handles date/field CRUD, cloning, cartesian product creation, and capacity preview.
/// </summary>
public interface ITimeslotService
{
    // ── Readiness ──

    Task<CanvasReadinessResponse> GetReadinessAsync(
        Guid jobId, CancellationToken ct = default);

    // ── Configuration ──

    Task<TimeslotConfigurationResponse> GetConfigurationAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default);

    Task<List<CapacityPreviewDto>> GetCapacityPreviewAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default);

    // ── Dates CRUD ──

    Task<TimeslotDateDto> AddDateAsync(
        Guid jobId, string userId, AddTimeslotDateRequest request, CancellationToken ct = default);

    Task EditDateAsync(
        string userId, EditTimeslotDateRequest request, CancellationToken ct = default);

    Task DeleteDateAsync(int ai, CancellationToken ct = default);

    Task DeleteAllDatesAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default);

    // ── Cascade date operations ──

    /// <summary>Change a game date for all agegroups, cascading to waves and games.</summary>
    Task<CascadeDateChangeResponse> CascadeEditDateAsync(
        Guid jobId, string userId, CascadeDateChangeRequest request, CancellationToken ct = default);

    /// <summary>Delete a game date for all agegroups, cascading to waves and games.</summary>
    Task<CascadeDateDeleteResponse> CascadeDeleteDateAsync(
        Guid jobId, CascadeDateDeleteRequest request, CancellationToken ct = default);

    // ── Date cloning ──

    Task<TimeslotDateDto> CloneDateRecordAsync(
        string userId, CloneDateRecordRequest request, CancellationToken ct = default);

    // ── Field timeslots CRUD ──

    Task<List<TimeslotFieldDto>> AddFieldTimeslotAsync(
        Guid jobId, string userId, AddTimeslotFieldRequest request, CancellationToken ct = default);

    Task EditFieldTimeslotAsync(
        string userId, EditTimeslotFieldRequest request, CancellationToken ct = default);

    Task DeleteFieldTimeslotAsync(int ai, CancellationToken ct = default);

    Task DeleteAllFieldTimeslotsAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default);

    // ── Cloning operations ──

    Task CloneDatesAsync(
        Guid jobId, string userId, CloneDatesRequest request, CancellationToken ct = default);

    Task CloneFieldsAsync(
        Guid jobId, string userId, CloneFieldsRequest request, CancellationToken ct = default);

    Task CloneByFieldAsync(
        Guid jobId, string userId, CloneByFieldRequest request, CancellationToken ct = default);

    Task CloneByDivisionAsync(
        Guid jobId, string userId, CloneByDivisionRequest request, CancellationToken ct = default);

    Task CloneByDowAsync(
        Guid jobId, string userId, CloneByDowRequest request, CancellationToken ct = default);

    Task<TimeslotFieldDto> CloneFieldDowAsync(
        string userId, CloneFieldDowRequest request, CancellationToken ct = default);

    // ── Bulk operations ──

    Task<BulkDateAssignResponse> BulkAssignDateAsync(
        Guid jobId, string userId, BulkDateAssignRequest request, CancellationToken ct = default);

    // ── Field config update ──

    /// <summary>
    /// Bulk-update GSI, StartTime, and/or MaxGamesPerField on existing field timeslot rows.
    /// Handles wave-adjusted start time recalculation in uniform mode.
    /// Does NOT touch TimeslotsLeagueSeasonDates — preserves R/day and wave assignments.
    /// </summary>
    Task<UpdateFieldConfigResponse> UpdateFieldConfigAsync(
        Guid jobId, string userId, UpdateFieldConfigRequest request, CancellationToken ct = default);

    // ── Field assignment management ──

    /// <summary>
    /// Reconcile field assignments for agegroups. For each agegroup entry:
    /// removes field-timeslot rows for fields NOT in the desired list, and
    /// adds field-timeslot rows for new fields (cloned from existing row timing).
    /// </summary>
    Task<SaveFieldAssignmentsResponse> SaveFieldAssignmentsAsync(
        Guid jobId, string userId, SaveFieldAssignmentsRequest request, CancellationToken ct = default);
}
