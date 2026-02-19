using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Read-only repository for nav rendering. Returns merged platform defaults
/// + job overrides for a role.
/// </summary>
public interface INavRepository
{
    /// <summary>
    /// Get the platform default nav for a role (JobId = NULL).
    /// Returns null if no default exists.
    /// </summary>
    Task<NavDto?> GetPlatformDefaultAsync(
        string roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the job-specific override nav for a role.
    /// Returns null if no override exists.
    /// </summary>
    Task<NavDto?> GetJobOverrideAsync(
        string roleId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the merged nav for a role + job: platform default items
    /// plus any job override items appended.
    /// </summary>
    Task<NavDto?> GetMergedNavAsync(
        string roleId,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
