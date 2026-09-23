using TSIC.Contracts.Dtos.TeamSearch;

namespace TSIC.Contracts.Services;

/// <summary>
/// The director's read-only view of a job's club reps and their Club Team Libraries (CTL Phase 4).
/// Registration-driven so zero-team reps appear; library data is aggregate + names only, never a
/// write path (ruling: Todd 2026-09-22 - directors read a club's library, they never edit it).
/// </summary>
public interface IClubRepDirectoryService
{
    Task<List<DirectorClubRepDto>> GetClubRepsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Null when the registration is not a club rep on this job.</summary>
    Task<DirectorClubLibraryDto?> GetClubLibraryAsync(Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default);
}
