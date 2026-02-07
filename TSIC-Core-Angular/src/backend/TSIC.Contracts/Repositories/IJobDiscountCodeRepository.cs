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

    // === ADMIN MANAGEMENT METHODS (for discount code CRUD operations) ===

    /// <summary>
    /// Get all discount codes for a job with usage counts.
    /// </summary>
    Task<List<JobDiscountCodes>> GetAllByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single discount code by Ai (tracked for updates).
    /// </summary>
    Task<JobDiscountCodes?> GetByIdAsync(
        int ai,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a discount code already exists for a job (case-insensitive).
    /// </summary>
    Task<bool> CodeExistsAsync(
        Guid jobId,
        string codeName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the count of registrations using a specific discount code.
    /// </summary>
    Task<int> GetUsageCountAsync(
        int ai,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new discount code.
    /// </summary>
    void Add(JobDiscountCodes code);

    /// <summary>
    /// Remove a discount code.
    /// </summary>
    void Remove(JobDiscountCodes code);

    /// <summary>
    /// Save all pending changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
