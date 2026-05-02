using TSIC.Contracts.Dtos.NuveiUpload;

namespace TSIC.Contracts.Services;

public interface INuveiUploadService
{
    /// <summary>
    /// Parse a Nuvei monthly Funding CSV export and insert any new rows into adn.NuveiFunding.
    /// Dedupe is full-row equality on (RefNumber, FundingEvent, FundingType, FundingAmount, FundingDate),
    /// matching the legacy controller's WHERE clause exactly.
    /// </summary>
    Task<NuveiUploadResultDto> ProcessFundingAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse a Nuvei monthly Batches CSV export and insert any new rows into adn.NuveiBatches.
    /// Dedupe is full-row equality on (BatchId, BatchCloseDate, BatchNet, SaleAmt, ReturnAmt),
    /// matching the legacy controller's WHERE clause exactly.
    /// </summary>
    Task<NuveiUploadResultDto> ProcessBatchesAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default);
}
