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

	/// <summary>
	/// Emails the receipt PDF to the purchasing family — port of legacy
	/// <c>StoreFamilyController.SendEmailReceipt</c>.
	///
	/// <para>
	/// Recipients, in legacy's order: Mom's email, Dad's email, then every registrant a line was
	/// directed to, each skipped if blank or already on the list. From is forced to the
	/// SES-verified identity at the send chokepoint; Reply-To is the job's store contact, so a
	/// shopper's reply reaches the director rather than TSIC support.
	/// </para>
	///
	/// <para>
	/// Returns a result rather than throwing. Legacy called this inside a try/catch marked
	/// "Priority 3 - Send email (failure acceptable)": the money has already moved, and a mail
	/// failure must never surface as a failed checkout. Callers on the checkout path should log
	/// and carry on.
	/// </para>
	/// </summary>
	Task<StoreReceiptEmailResult> EmailReceiptAsync(Guid jobId, int storeCartBatchId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a receipt send: who it went to, and whether the transport accepted it.</summary>
public record StoreReceiptEmailResult
{
	public required bool Sent { get; init; }

	/// <summary>Addresses on the To line. Empty when the family has no email on file at all.</summary>
	public required List<string> Recipients { get; init; }

	/// <summary>Set when the send did not happen, for the log. Never shown to a shopper.</summary>
	public string? Reason { get; init; }
}
