using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Grid;
using Syncfusion.Drawing;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Store;

/// <summary>
/// Generates PDF receipts for completed store purchases (Syncfusion PDF).
/// Port of legacy GenerateInvoicePdf from TSIC-Unify-Services.
/// </summary>
public sealed class StoreReceiptService : IStoreReceiptService
{
	private readonly IStoreCartRepository _cartRepo;
	private readonly IStoreRepository _storeRepo;

	// IJobRepository is gone: the job name now comes from GetReceiptContextAsync, which resolves it
	// from the BATCH rather than from the caller's claims. Reading it from the caller is what let a
	// foreign receipt render under the reader's own job name.
	private readonly IEmailService _emailService;

	public StoreReceiptService(
		IStoreCartRepository cartRepo,
		IStoreRepository storeRepo,
		IEmailService emailService)
	{
		_cartRepo = cartRepo;
		_storeRepo = storeRepo;
		_emailService = emailService;
	}

	public async Task<StoreReceiptEmailResult> EmailReceiptAsync(
		Guid jobId, int storeCartBatchId, CancellationToken cancellationToken = default)
	{
		var context = await _cartRepo.GetReceiptContextAsync(storeCartBatchId, cancellationToken);
		if (context == null || context.JobId != jobId)
			return new StoreReceiptEmailResult
			{
				Sent = false,
				Recipients = [],
				Reason = "Batch not found in this job."
			};

		var recipients = BuildRecipients(context);
		if (recipients.Count == 0)
			return new StoreReceiptEmailResult
			{
				Sent = false,
				Recipients = [],
				// Not an error. A walk-up buyer registered at the counter often has no address on
				// file, and legacy mailed nobody in that case too — silently.
				Reason = "No email address on file for this family."
			};

		var pdf = await GenerateReceiptPdfAsync(jobId, storeCartBatchId, cancellationToken);
		if (pdf == null)
			return new StoreReceiptEmailResult
			{
				Sent = false,
				Recipients = recipients,
				Reason = "Receipt could not be generated (batch unpaid or empty)."
			};

		var storeName = string.IsNullOrWhiteSpace(context.DisplayName)
			? context.JobName
			: context.DisplayName;

		var message = new EmailMessageDto
		{
			FromName = storeName,
			// Replies go to the DIRECTOR's store contact, not TSIC support — it is their store,
			// their merchandise, and their pickup table. Falls back to the verified From identity
			// when the job has not set one.
			ReplyToName = $"{storeName} Merchandise",
			ReplyToAddress = context.StoreContactEmail,
			ToAddresses = recipients,
			Subject = $"Your {storeName} merchandise receipt",
			HtmlBody = BuildReceiptEmailBody(storeName, storeCartBatchId, context.StoreContactEmail),
			Attachments =
			{
				new EmailAttachmentDto
				{
					FileName = $"receipt-{storeCartBatchId}.pdf",
					Content = pdf,
					ContentType = "application/pdf"
				}
			}
		};

		// sendInDevelopment stays FALSE: a local or staging checkout must not mail a real family.
		// Production is the only environment that reaches a shopper (SANDBOX-RULE).
		var sent = await _emailService.SendAsync(message, sendInDevelopment: false, cancellationToken);

		return new StoreReceiptEmailResult
		{
			Sent = sent,
			Recipients = recipients,
			Reason = sent ? null : "Transport reported a failure."
		};
	}

	/// <summary>
	/// LEGACY order and dedup (<c>SendEmailReceipt</c>): Mom, then Dad, then each directed
	/// registrant, skipping blanks and anything already on the list. Case-insensitive here —
	/// legacy's <c>List.Contains</c> was ordinal, so a parent whose player registration carried
	/// the same address in different case got the receipt twice.
	/// </summary>
	private static List<string> BuildRecipients(StoreReceiptContextDto context)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var recipients = new List<string>();

		void Add(string? address)
		{
			var trimmed = address?.Trim();
			if (string.IsNullOrEmpty(trimmed)) return;
			if (seen.Add(trimmed)) recipients.Add(trimmed);
		}

		Add(context.MomEmail);
		Add(context.DadEmail);
		foreach (var email in context.DirectedEmails) Add(email);

		return recipients;
	}

	private static string BuildReceiptEmailBody(string storeName, int batchId, string? contactEmail)
	{
		var contactLine = string.IsNullOrWhiteSpace(contactEmail)
			? ""
			: $"<p style=\"margin:0 0 12px\">Questions about your order? Reply to this email or write to "
				+ $"<a href=\"mailto:{contactEmail}\">{contactEmail}</a>.</p>";

		return $"""
			<div style="font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#1f2937">
			  <p style="margin:0 0 12px">Thank you for your order from <strong>{storeName}</strong>.</p>
			  <p style="margin:0 0 12px">Your receipt is attached as a PDF. Order #{batchId}.</p>
			  {contactLine}
			</div>
			""";
	}

	public async Task<byte[]?> GenerateReceiptPdfAsync(
		Guid jobId, int storeCartBatchId, CancellationToken cancellationToken = default)
	{
		// SECURITY: the batch must belong to the caller's job. `jobId` used to be accepted here
		// and never applied — every read below keys on storeCartBatchId alone — so any
		// authenticated user could walk receipt/1, receipt/2, … and pull another family's PDF:
		// buyer name, the registrants goods were directed to, the amounts, and the card's last
		// four. Worse, the document was TITLED with the caller's job, so it did not even look
		// foreign. Closed here, at the one place every receipt path passes through. See D-11.
		var context = await _cartRepo.GetReceiptContextAsync(storeCartBatchId, cancellationToken);
		if (context == null || context.JobId != jobId)
			return null;

		// Validate: batch must be paid
		var accounting = await _cartRepo.GetBatchAccountingAsync(storeCartBatchId, cancellationToken);
		if (accounting == null)
			return null;

		// Get line items
		var lineItems = await _cartRepo.GetBatchLineItemsAsync(storeCartBatchId, cancellationToken);
		if (lineItems.Count == 0)
			return null;

		var jobName = context.JobName;

		// Build PDF
		using var document = new PdfDocument();
		document.PageSettings.Orientation = PdfPageOrientation.Landscape;
		document.PageSettings.Margins.All = 50;

		var page = document.Pages.Add();
		var graphics = page.Graphics;
		var clientWidth = graphics.ClientSize.Width;

		// ── Header ──
		var titleFont = new PdfStandardFont(PdfFontFamily.TimesRoman, 14, PdfFontStyle.Bold);
		var headingFont = new PdfStandardFont(PdfFontFamily.TimesRoman, 14);
		var bodyFont = new PdfStandardFont(PdfFontFamily.TimesRoman, 10);
		var headerColor = new PdfColor(126, 151, 173);

		// Store name
		var titleElement = new PdfTextElement($"Store: {jobName}", titleFont, PdfBrushes.Black);
		var result = titleElement.Draw(page, new RectangleF(0, 0, clientWidth / 2, 200));

		// Receipt banner (dark blue bar)
		float bannerY = result.Bounds.Bottom + 40;
		graphics.DrawRectangle(new PdfSolidBrush(headerColor), new RectangleF(0, bannerY, clientWidth, 30));

		var receiptElement = new PdfTextElement("RECEIPT", headingFont, PdfBrushes.White);
		receiptElement.Draw(page, new PointF(10, bannerY + 8));

		// Purchase date and invoice on the right
		var purchaseDate = accounting.CreateDate.ToString("MM/dd/yyyy");
		var invoiceNo = accounting.AdnInvoiceNo ?? $"STORE-{storeCartBatchId}";
		var dateText = $"Purchase Date: {purchaseDate}  Invoice#: {invoiceNo}";
		var dateSize = headingFont.MeasureString(dateText);
		graphics.DrawString(dateText, headingFont, PdfBrushes.White,
			new PointF(clientWidth - dateSize.Width - 10, bannerY + 8));

		// ── Info section ──
		float infoY = bannerY + 50;
		var transactionInfo = accounting.AdnTransactionId != null
			? $"\nTransaction ID: {accounting.AdnTransactionId}"
			: "";
		var ccInfo = accounting.Cclast4 != null
			? $"\nCard ending in: ****{accounting.Cclast4}"
			: "";

		var infoText = $"Order #{storeCartBatchId}{transactionInfo}{ccInfo}";
		var infoElement = new PdfTextElement(infoText, bodyFont, PdfBrushes.Black);
		result = infoElement.Draw(page, new PointF(10, infoY));

		// Divider line
		float dividerY = result.Bounds.Bottom + 15;
		graphics.DrawLine(
			new PdfPen(headerColor, 0.70f),
			new PointF(0, dividerY),
			new PointF(clientWidth, dividerY));

		// ── Line items grid ──
		var grid = new PdfGrid();

		// Build data table manually (Syncfusion PdfGrid needs IEnumerable of objects or DataTable)
		var dataTable = new System.Data.DataTable();
		dataTable.Columns.Add("Item", typeof(string));
		dataTable.Columns.Add("Variant", typeof(string));
		dataTable.Columns.Add("Qty", typeof(string));
		dataTable.Columns.Add("Unit Price", typeof(string));
		dataTable.Columns.Add("Fees", typeof(string));
		dataTable.Columns.Add("Tax", typeof(string));
		dataTable.Columns.Add("Line Total", typeof(string));

		foreach (var item in lineItems)
		{
			var variant = "";
			if (!string.IsNullOrEmpty(item.ColorName)) variant = item.ColorName;
			if (!string.IsNullOrEmpty(item.SizeName))
				variant = string.IsNullOrEmpty(variant) ? item.SizeName : $"{variant} / {item.SizeName}";
			if (!string.IsNullOrEmpty(item.DirectToPlayerName))
				variant = string.IsNullOrEmpty(variant)
					? $"For {item.DirectToPlayerName}"
					: $"{variant} — For {item.DirectToPlayerName}";

			dataTable.Rows.Add(
				item.ItemName,
				variant,
				item.Quantity.ToString(),
				$"${item.UnitPrice:N2}",
				$"${item.FeeProcessing:N2}",
				$"${item.SalesTax:N2}",
				$"${item.LineTotal:N2}"
			);
		}

		grid.DataSource = dataTable;

		// Header style
		var headerStyle = new PdfGridCellStyle
		{
			BackgroundBrush = new PdfSolidBrush(headerColor),
			TextBrush = PdfBrushes.White,
			Font = new PdfStandardFont(PdfFontFamily.TimesRoman, 11, PdfFontStyle.Regular)
		};
		headerStyle.Borders.All = new PdfPen(headerColor);

		var header = grid.Headers[0];
		for (int i = 0; i < header.Cells.Count; i++)
		{
			header.Cells[i].Style = headerStyle;
			header.Cells[i].StringFormat = i >= 3
				? new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle)
				: new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle);
		}

		// Cell style
		var cellStyle = new PdfGridCellStyle
		{
			Font = new PdfStandardFont(PdfFontFamily.TimesRoman, 10),
			TextBrush = PdfBrushes.Black
		};
		cellStyle.Borders.All = PdfPens.White;
		cellStyle.Borders.Bottom = new PdfPen(new PdfColor(217, 217, 217), 0.70f);

		foreach (PdfGridRow row in grid.Rows)
		{
			row.Height = 35;
			row.ApplyStyle(cellStyle);
			for (int i = 0; i < row.Cells.Count; i++)
			{
				row.Cells[i].StringFormat = i >= 3
					? new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle)
					: new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle);
			}
		}

		// Column widths
		grid.Columns[0].Width = 160; // Item
		grid.Columns[1].Width = 100; // Variant
		grid.Columns[2].Width = 40;  // Qty

		var layoutFormat = new PdfGridLayoutFormat { Layout = PdfLayoutType.Paginate };
		var gridResult = grid.Draw(page,
			new RectangleF(
				new PointF(0, dividerY + 15),
				new SizeF(clientWidth, graphics.ClientSize.Height - 100)),
			layoutFormat);

		// ── Totals footer ──
		var subtotal = lineItems.Sum(li => li.UnitPrice * li.Quantity);
		var totalFees = lineItems.Sum(li => li.FeeProcessing); // FeeProduct IS the subtotal, not a fee
		var totalTax = lineItems.Sum(li => li.SalesTax);

		float footerY = gridResult.Bounds.Bottom + 20;
		var footerPage = gridResult.Page;
		float labelX = clientWidth - 250;

		// Subtotal
		footerPage.Graphics.DrawString("Subtotal:", bodyFont, new PdfSolidBrush(headerColor),
			new PointF(labelX, footerY));
		footerPage.Graphics.DrawString($"${subtotal:N2}", bodyFont, PdfBrushes.Black,
			new PointF(labelX + 130, footerY));

		if (totalFees > 0)
		{
			footerY += 18;
			footerPage.Graphics.DrawString("Fees:", bodyFont, new PdfSolidBrush(headerColor),
				new PointF(labelX, footerY));
			footerPage.Graphics.DrawString($"${totalFees:N2}", bodyFont, PdfBrushes.Black,
				new PointF(labelX + 130, footerY));
		}

		if (totalTax > 0)
		{
			footerY += 18;
			footerPage.Graphics.DrawString("Tax:", bodyFont, new PdfSolidBrush(headerColor),
				new PointF(labelX, footerY));
			footerPage.Graphics.DrawString($"${totalTax:N2}", bodyFont, PdfBrushes.Black,
				new PointF(labelX + 130, footerY));
		}

		// Divider before total
		footerY += 22;
		footerPage.Graphics.DrawLine(
			new PdfPen(headerColor, 0.70f),
			new PointF(labelX, footerY),
			new PointF(clientWidth, footerY));

		// Total Paid
		footerY += 10;
		footerPage.Graphics.DrawString("Total Paid:", headingFont, new PdfSolidBrush(headerColor),
			new PointF(labelX, footerY));
		footerPage.Graphics.DrawString($"${accounting.Paid:N2}", headingFont,
			new PdfSolidBrush(new PdfColor(131, 130, 136)),
			new PointF(labelX + 130, footerY));

		// Thank you
		footerY += 35;
		footerPage.Graphics.DrawString("Thank you for your business!", headingFont,
			new PdfSolidBrush(headerColor),
			new PointF(labelX, footerY));

		// ── Policy footer ──
		// LEGACY (GenerateInvoicePdf): the receipt ends with Refund Policy / Pickup Instructions /
		// Merch Contact, drawn on the LEFT under the grid. Ours omitted it entirely, so the one
		// document a shopper keeps said nothing about where to collect their goods or how to reach
		// anyone. Same three job-level strings as the storefront panel — see StoreFrontInfoDto.
		var storeInfo = await _storeRepo.GetStoreFrontInfoAsync(jobId, cancellationToken);
		if (storeInfo.HasAny)
		{
			var lines = new List<string>();
			if (storeInfo.RefundPolicy is not null)
				lines.Add($"Refund Policy: {storeInfo.RefundPolicy}");
			if (storeInfo.PickupDetails is not null)
				lines.Add($"Pickup Instructions: {storeInfo.PickupDetails}");
			if (storeInfo.ContactEmail is not null)
				lines.Add($"Merch Contact: For questions regarding merchandise, please reach out to {storeInfo.ContactEmail}.");

			// Drawn from the left margin across the full width, NOT in the right-hand totals
			// column — this is prose and needs the room to wrap.
			var policyElement = new PdfTextElement(
				string.Join("\n\n", lines), bodyFont, PdfBrushes.Black);
			policyElement.Draw(
				footerPage,
				new RectangleF(0, footerY + 30, clientWidth, 200));
		}

		// Serialize to byte array
		using var ms = new MemoryStream();
		document.Save(ms);
		return ms.ToArray();
	}
}
