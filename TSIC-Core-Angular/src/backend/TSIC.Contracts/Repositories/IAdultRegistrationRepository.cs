using TSIC.Contracts.Dtos.AdultRegistration;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for adult registration data access (Unassigned Adult, Referee, Recruiter).
/// </summary>
public interface IAdultRegistrationRepository
{
    /// <summary>
    /// Get job data needed for adult registration (metadata, waivers, confirmation templates).
    /// </summary>
    Task<AdultRegJobData?> GetJobAdultRegDataAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job data by jobPath (for anonymous endpoints).
    /// </summary>
    Task<AdultRegJobData?> GetJobAdultRegDataByPathAsync(string jobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a user already has an active registration with a given role in a job.
    /// </summary>
    Task<bool> HasExistingRegistrationAsync(string userId, Guid jobId, string roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a registration with its job for confirmation display (read-only).
    /// </summary>
    Task<Registrations?> GetRegistrationWithJobAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a tracked registration with its job for payment stamping (writable).
    /// </summary>
    Task<Registrations?> GetTrackedRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active, tracked registrations for a given (userId, jobId, roleId).
    /// For Staff this is the set of teams the user is coaching; for other roles
    /// it's a single row. Used at payment time to collect all pending-owed
    /// registrations that should be charged together.
    /// Includes Job navigation.
    /// </summary>
    Task<List<Registrations>> GetTrackedActiveByRoleAsync(string userId, Guid jobId, string roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes (sets BActive=false) all active registrations for a given
    /// (userId, jobId, roleId) tuple. Used by the edit flow to implement legacy's
    /// "delete all + recreate" pattern without losing audit history.
    /// Returns the number of rows affected.
    /// </summary>
    Task<int> DeactivateActiveByRoleAsync(string userId, Guid jobId, string roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get teams available for a Coach to select, grouped by club.
    /// Excludes Waitlist/Dropped agegroups. Matches legacy ListAvailableTeams query.
    /// </summary>
    Task<List<AdultTeamOptionDto>> GetAvailableTeamsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new registration entity.
    /// </summary>
    void Add(Registrations registration);

    /// <summary>
    /// Persist changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
