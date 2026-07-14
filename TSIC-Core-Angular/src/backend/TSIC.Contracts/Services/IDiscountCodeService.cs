using TSIC.Contracts.Dtos.DiscountCode;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for managing discount codes within a job.
/// </summary>
public interface IDiscountCodeService
{
    /// <summary>
    /// Get all discount codes for a job with computed status.
    /// </summary>
    Task<List<DiscountCodeDto>> GetDiscountCodesAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new discount code.
    /// </summary>
    Task<DiscountCodeDto> AddDiscountCodeAsync(Guid jobId, string userId, AddDiscountCodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk generate discount codes with sequential pattern.
    /// </summary>
    Task<List<DiscountCodeDto>> BulkAddDiscountCodesAsync(Guid jobId, string userId, BulkAddDiscountCodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing discount code owned by <paramref name="jobId"/>.
    /// Once the code has been redeemed its amount, type and dates are locked; only Active may change.
    /// </summary>
    Task<DiscountCodeDto> UpdateDiscountCodeAsync(Guid jobId, int ai, string userId, UpdateDiscountCodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a discount code owned by <paramref name="jobId"/> (only if not used).
    /// </summary>
    Task<bool> DeleteDiscountCodeAsync(Guid jobId, int ai, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch update status (activate/deactivate) for multiple codes.
    /// </summary>
    Task<int> BatchUpdateStatusAsync(Guid jobId, List<int> codeIds, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a discount code already exists for a job.
    /// </summary>
    Task<bool> CheckCodeExistsAsync(Guid jobId, string codeName, CancellationToken cancellationToken = default);
}
