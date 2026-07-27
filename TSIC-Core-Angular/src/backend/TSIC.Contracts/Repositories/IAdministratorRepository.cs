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
    /// Get all non-superuser admin registrations for a job (tracked, for bulk mutation).
    /// </summary>
    Task<List<Registrations>> GetNonSuperuserByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current PrimaryContactRegistrationId for a job.
    /// </summary>
    Task<Guid?> GetPrimaryContactIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set (or clear) the PrimaryContactRegistrationId on a job.
    /// Pass null to clear.
    /// </summary>
    Task SetPrimaryContactAsync(Guid jobId, Guid? registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Earliest-registered active Director on the job — the default primary contact
    /// (same rule as the !DIRECTOR text-substitution fallback). Null when the job
    /// has no active Director.
    /// </summary>
    Task<Guid?> GetEarliestActiveDirectorIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the registration is an active Director on the given job — the shape a
    /// persisted primary contact must have to be considered valid.
    /// </summary>
    Task<bool> IsActiveDirectorOnJobAsync(Guid jobId, Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get every registration for a user (tracked — used to classify eligibility and to
    /// convert a pending Unassigned Adult registration into an admin registration; AM-004).
    /// </summary>
    Task<List<Registrations>> GetRegistrationsByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the user is the credential holder of any family registration
    /// (Registrations.FamilyUserId). Family logins are shared within a household and
    /// must never be elevated to admin roles (AM-004).
    /// </summary>
    Task<bool> IsFamilyCredentialHolderAsync(string userId, CancellationToken cancellationToken = default);
}
