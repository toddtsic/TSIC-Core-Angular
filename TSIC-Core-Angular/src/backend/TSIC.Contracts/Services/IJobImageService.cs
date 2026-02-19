namespace TSIC.Contracts.Services;

/// <summary>
/// Service for validating, resizing, and storing job branding images.
/// Uses Stream parameters to keep Contracts framework-agnostic (no IFormFile dependency).
/// </summary>
public interface IJobImageService
{
    /// <summary>
    /// Validate, resize, and save a job image. Deletes previous file matching the convention.
    /// </summary>
    /// <param name="fileStream">The uploaded file stream.</param>
    /// <param name="originalFileName">Original file name (for extension detection).</param>
    /// <param name="jobId">Job GUID — used in the filename.</param>
    /// <param name="conventionName">Lowercase convention name (e.g. "paralaxbackgroundimage").</param>
    /// <param name="maxWidth">Maximum width in pixels — images wider than this are resized.</param>
    /// <param name="quality">JPEG/WebP encode quality (1–100). Ignored for PNG.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The saved filename (e.g. "{jobId}_{conventionName}.{ext}").</returns>
    Task<string> SaveJobImageAsync(
        Stream fileStream,
        string originalFileName,
        Guid jobId,
        string conventionName,
        int maxWidth,
        int quality,
        CancellationToken ct = default);

    /// <summary>
    /// Delete all files matching {jobId}_{conventionName}.* from the banner files path.
    /// </summary>
    Task DeleteJobImageAsync(Guid jobId, string conventionName, CancellationToken ct = default);
}
