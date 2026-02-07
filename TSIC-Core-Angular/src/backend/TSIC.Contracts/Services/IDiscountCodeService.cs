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
    /// Update an existing discount code.
    /// </summary>
    Task<DiscountCodeDto> UpdateDiscountCodeAsync(int ai, UpdateDiscountCodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a discount code (only if not used).
    /// </summary>
    Task<bool> DeleteDiscountCodeAsync(int ai, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch update status (activate/deactivate) for multiple codes.
    /// </summary>
    Task<int> BatchUpdateStatusAsync(Guid jobId, List<int> codeIds, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a discount code already exists for a job.
    /// </summary>
    Task<bool> CheckCodeExistsAsync(Guid jobId, string codeName, CancellationToken cancellationToken = default);
}
