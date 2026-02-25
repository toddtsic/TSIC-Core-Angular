using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Grid;
using Syncfusion.Drawing;
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
	private readonly IJobRepository _jobRepo;

	public StoreReceiptService(IStoreCartRepository cartRepo, IJobRepository jobRepo)
	{
		_cartRepo = cartRepo;
		_jobRepo = jobRepo;
	}

	public async Task<byte[]?> GenerateReceiptPdfAsync(
		Guid jobId, int storeCartBatchId, CancellationToken cancellationToken = default)
	{
		// Validate: batch must be paid
		var accounting = await _cartRepo.GetBatchAccountingAsync(storeCartBatchId, cancellationToken);
		if (accounting == null)
			return null;

		// Get line items
		var lineItems = await _cartRepo.GetBatchLineItemsAsync(storeCartBatchId, cancellationToken);
		if (lineItems.Count == 0)
			return null;

		// Get job name
		var jobName = await _jobRepo.GetJobNameAsync(jobId, cancellationToken) ?? "Store";

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
				$"${item.FeeProcessing + item.FeeProduct:N2}",
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
		var totalFees = lineItems.Sum(li => li.FeeProcessing + li.FeeProduct);
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

		// Serialize to byte array
		using var ms = new MemoryStream();
		document.Save(ms);
		return ms.ToArray();
	}
}
