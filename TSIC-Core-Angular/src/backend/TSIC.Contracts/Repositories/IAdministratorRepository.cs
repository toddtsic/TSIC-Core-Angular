using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing administrator registrations (Director, SuperDirector, etc.) within a job.
/// </summary>
public interface IAdministratorRepository
{
    /// <summary>
    /// Get all administrator registrations for a job as projected DTOs (AsNoTracking).
    /// </summary>
    Task<List<AdministratorDto>> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single administrator registration by ID (tracked for mutation, no navigation includes).
    /// </summary>
    Task<Registrations?> GetByIdAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single admin as a projected DTO (AsNoTracking). Used for display after add/update.
    /// </summary>
    Task<AdministratorDto?> GetAdminProjectionByIdAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new administrator registration.
    /// </summary>
    void Add(Registrations registration);

    /// <summary>
    /// Remove an administrator registration.
    /// </summary>
    void Remove(Registrations registration);

    /// <summary>
    /// Persist all changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current PrimaryContactRegistrationId for a job.
    /// </summary>
    Task<Guid?> GetPrimaryContactIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set (or clear) the PrimaryContactRegistrationId on a job.
    /// Pass null to clear.
    /// </summary>
    Task SetPrimaryContactAsync(Guid jobId, Guid? registrationId, CancellationToken cancellationToken = default);
}
