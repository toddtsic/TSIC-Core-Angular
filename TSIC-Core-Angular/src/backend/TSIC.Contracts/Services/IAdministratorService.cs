using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for managing administrator registrations within a job.
/// </summary>
public interface IAdministratorService
{
    /// <summary>
    /// Get all administrator registrations for a job.
    /// </summary>
    Task<List<AdministratorDto>> GetAdministratorsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new administrator registration to a job.
    /// </summary>
    Task<AdministratorDto> AddAdministratorAsync(Guid jobId, AddAdministratorRequest request, string currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing administrator registration.
    /// </summary>
    Task<AdministratorDto> UpdateAdministratorAsync(Guid registrationId, UpdateAdministratorRequest request, string currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an administrator registration.
    /// </summary>
    Task DeleteAdministratorAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch update active status for non-Superuser administrators.
    /// </summary>
    Task<int> BatchUpdateStatusAsync(Guid jobId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search users by username or name for typeahead.
    /// </summary>
    Task<List<UserSearchResultDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
}
