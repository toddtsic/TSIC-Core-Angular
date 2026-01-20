using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing JobDiscountCodes entity data access.
/// Encapsulates all EF Core queries related to discount codes.
/// </summary>
public interface IJobDiscountCodeRepository
{
    /// <summary>
    /// Get an active, non-expired discount code for a job
    /// </summary>
    Task<(bool? BAsPercent, decimal? CodeAmount)?> GetActiveCodeAsync(
        Guid jobId,
        string codeNameLower,
        DateTime currentTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get discount code by AI (auto-increment ID).
    /// </summary>
    Task<(bool? BAsPercent, decimal? CodeAmount)?> GetByAiAsync(
        int discountCodeAi,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active discount codes for a job.
    /// Used for checking if any active discount codes exist.
    /// </summary>
    Task<List<JobDiscountCodes>> GetActiveCodesForJobAsync(
        Guid jobId,
        DateTime currentTime,
        CancellationToken cancellationToken = default);
}
