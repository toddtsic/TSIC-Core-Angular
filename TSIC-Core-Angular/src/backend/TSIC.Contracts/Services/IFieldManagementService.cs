using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Manage Fields scheduling tool.
/// Handles field CRUD and league-season assignment/removal.
/// </summary>
public interface IFieldManagementService
{
    Task<FieldManagementResponse> GetFieldManagementDataAsync(
        Guid jobId, string userRole, CancellationToken ct = default);

    Task<FieldDto> CreateFieldAsync(
        Guid jobId, string userId, CreateFieldRequest request, CancellationToken ct = default);

    Task UpdateFieldAsync(
        string userId, UpdateFieldRequest request, CancellationToken ct = default);

    Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken ct = default);

    Task AssignFieldsAsync(
        Guid jobId, string userId, AssignFieldsRequest request, CancellationToken ct = default);

    Task RemoveFieldsAsync(
        Guid jobId, RemoveFieldsRequest request, CancellationToken ct = default);
}
