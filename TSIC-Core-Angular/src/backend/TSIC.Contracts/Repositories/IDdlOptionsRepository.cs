namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for reading/writing the Jobs.JsonOptions column
/// which stores per-job dropdown values for registration forms.
/// </summary>
public interface IDdlOptionsRepository
{
    /// <summary>
    /// Read the raw JsonOptions string for a job (AsNoTracking).
    /// </summary>
    Task<string?> GetJsonOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Update the JsonOptions column for a job (single-column UPDATE).
    /// </summary>
    Task UpdateJsonOptionsAsync(Guid jobId, string jsonOptions, CancellationToken ct = default);
}
