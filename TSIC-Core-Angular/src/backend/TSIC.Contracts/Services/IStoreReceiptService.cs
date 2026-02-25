namespace TSIC.Contracts.Services;

/// <summary>
/// Generates PDF receipts for completed store purchases.
/// </summary>
public interface IStoreReceiptService
{
	/// <summary>
	/// Generate a PDF receipt for a completed store batch.
	/// Returns the PDF as a byte array, or null if batch not found / not paid.
	/// </summary>
	Task<byte[]?> GenerateReceiptPdfAsync(Guid jobId, int storeCartBatchId, CancellationToken cancellationToken = default);
}
