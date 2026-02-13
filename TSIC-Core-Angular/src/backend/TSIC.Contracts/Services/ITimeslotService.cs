using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Manage Timeslots scheduling tool.
/// Handles date/field CRUD, cloning, cartesian product creation, and capacity preview.
/// </summary>
public interface ITimeslotService
{
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
}
