using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) TSIC-fee year-to-date comparison reports — the EF replacement for the
/// legacy Crystal "tsicTSICFeesYTD" (by customer + job) and "tsicTSICFeesYTDByCustomer" (customer
/// rollup), both backed by <c>adn.tsicFeesYTDAndLastYear</c>. One flat EF row set
/// (<see cref="IReportingRepository.GetFeeYtdRowsAsync"/>) of month-grain fees for this year and last
/// year is rolled up to a this-year-YTD vs last-year-YTD comparison over the same months
/// (1..lastMonth), grouped by customer (and job), with a year-over-year change column. Runs across ALL
/// jobs; the period is the most recently completed month.
///
/// Fee math mirrors the proc exactly: each row's fee = NewPlayers×perPlayerCharge +
/// NewTeams×perTeamCharge (computed in the repository); this layer only sums and groups.
/// </summary>
public sealed class FeeYtdReportPdfService : IFeeYtdReportPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public FeeYtdReportPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // Letter PORTRAIT (the comparison is a narrow 4-column table).
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);          // 554.4
    private const float ContentBottom = PageH - MarginBottom;       // 763.2
    private const float FooterH = 18f;
    private const float MaxContentY = ContentBottom - MarginTop - FooterH - 2f;  // 714.4
    private const float CellPadX = 4f;
    private const float RowH = 15f;

    // Columns (sum == ContentW). The label column carries customer/job names; the three money
    // columns are last-year YTD, this-year YTD, and the year-over-year change.
    private static readonly (string Key, float W, PdfTextAlignment Align)[] Cols =
    {
        ("Customer / Job", 264.4f, PdfTextAlignment.Left),
        ("Last Year YTD",   90f,   PdfTextAlignment.Right),
        ("This Year YTD",   90f,   PdfTextAlignment.Right),
        ("Change",         110f,   PdfTextAlignment.Right),
    };

    private static readonly PdfColor BandColor = new(222, 222, 222);
    private static readonly PdfColor CustomerBand = new(238, 238, 238);
    private static readonly PdfColor TitleBlue = new(0, 0, 160);

    public Task<ReportExportResult> GenerateByCustomerAndJobAsync(CancellationToken cancellationToken = default)
        => GenerateAsync(includeJobRows: true, "By Customer and Job", "TSICFeesYTD_ByCustomerAndJob.pdf", cancellationToken);

    public Task<ReportExportResult> GenerateByCustomerAsync(CancellationToken cancellationToken = default)
        => GenerateAsync(includeJobRows: false, "By Customer", "TSICFeesYTD_ByCustomer.pdf", cancellationToken);

    private async Task<ReportExportResult> GenerateAsync(
        bool includeJobRows, string subtitle, string fileName, CancellationToken ct)
    {
        var (thisYear, lastYear, maxMonth, customers) = await BuildAsync(ct);

        var doc = NewDocument();
        var fonts = new Fonts();
        var pens = new Pens();

        var monthAbbr = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(maxMonth);
        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(maxMonth);
        var period = $"Year-to-date through {monthName} {thisYear}  ·  Jan–{monthAbbr} vs same period {lastYear}";
        var printStamp = "Print Date: " + DateTime.Now.ToString("M/d/yyyy  h:mm tt", CultureInfo.InvariantCulture);

        var g = doc.Pages.Add().Graphics;
        var y = DrawTitle(g, subtitle, period, printStamp, fonts);

        if (customers.Count == 0)
        {
            g.DrawString($"No TSIC-fee activity for {monthName} {thisYear} or {lastYear}.", fonts.Label,
                PdfBrushes.Gray, new RectangleF(0, y + 8f, ContentW, 18f),
                new PdfStringFormat(PdfTextAlignment.Left));
            return Save(doc, fileName);
        }

        y = DrawColumnHeader(g, y, fonts, pens);

        decimal grandLast = 0m, grandThis = 0m;
        foreach (var c in customers)
        {
            grandLast += c.LastYtd;
            grandThis += c.ThisYtd;

            // Keep the customer header with at least its total on the same page.
            var need = RowH + (includeJobRows ? c.Jobs.Count * RowH : 0f) + RowH;
            if (y + Math.Min(need, RowH * 3) > MaxContentY)
            {
                g = doc.Pages.Add().Graphics;
                y = DrawColumnHeader(g, DrawTitle(g, subtitle, period, printStamp, fonts), fonts, pens);
            }

            y = DrawCustomerBand(g, c.Name, y, fonts);

            if (includeJobRows)
            {
                foreach (var j in c.Jobs.OrderBy(j => j.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (y + RowH > MaxContentY)
                    {
                        g = doc.Pages.Add().Graphics;
                        y = DrawColumnHeader(g, DrawTitle(g, subtitle, period, printStamp, fonts), fonts, pens);
                    }
                    y = DrawRow(g, "    " + j.Name, j.LastYtd, j.ThisYtd, y, fonts, pens, bold: false, indent: true);
                }
            }

            if (y + RowH > MaxContentY)
            {
                g = doc.Pages.Add().Graphics;
                y = DrawColumnHeader(g, DrawTitle(g, subtitle, period, printStamp, fonts), fonts, pens);
            }
            y = DrawRow(g, includeJobRows ? $"{c.Name} — Total" : c.Name, c.LastYtd, c.ThisYtd, y, fonts, pens,
                bold: true, indent: false);
            y += 4f;
        }

        // Grand total.
        if (y + RowH + 6f > MaxContentY)
        {
            g = doc.Pages.Add().Graphics;
            y = DrawColumnHeader(g, DrawTitle(g, subtitle, period, printStamp, fonts), fonts, pens);
        }
        g.DrawLine(pens.Header, new PointF(0, y + 1f), new PointF(ContentW, y + 1f));
        DrawRow(g, "GRAND TOTAL", grandLast, grandThis, y + 2f, fonts, pens, bold: true, indent: false);

        return Save(doc, fileName);
    }

    // ── Data shaping ────────────────────────────────────────────────────

    private async Task<(int ThisYear, int LastYear, int MaxMonth, List<CustomerBlock> Customers)> BuildAsync(
        CancellationToken ct)
    {
        var now = DateTime.Now;
        var prev = now.AddMonths(-1);
        var thisYear = prev.Year;
        var lastYear = thisYear - 1;
        var maxMonth = prev.Month;

        var rows = await _reportingRepository.GetFeeYtdRowsAsync(now, ct);

        var customers = rows
            .GroupBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .Select(cg => new CustomerBlock
            {
                Name = cg.Key,
                Jobs = cg.GroupBy(r => r.JobName, StringComparer.OrdinalIgnoreCase)
                    .Select(jg => new JobRow
                    {
                        Name = jg.Key,
                        LastYtd = jg.Where(r => r.Year == lastYear).Sum(r => r.TsicFees),
                        ThisYtd = jg.Where(r => r.Year == thisYear).Sum(r => r.TsicFees),
                    })
                    .Where(j => j.LastYtd != 0m || j.ThisYtd != 0m)
                    .ToList(),
                LastYtd = cg.Where(r => r.Year == lastYear).Sum(r => r.TsicFees),
                ThisYtd = cg.Where(r => r.Year == thisYear).Sum(r => r.TsicFees),
            })
            .Where(c => c.LastYtd != 0m || c.ThisYtd != 0m)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (thisYear, lastYear, maxMonth, customers);
    }

    // ── Drawing ─────────────────────────────────────────────────────────

    private const float ColHeaderH = 15f;

    private static PdfDocument NewDocument()
    {
        var doc = new PdfDocument();
        doc.PageSettings.Size = PdfPageSize.Letter;
        doc.PageSettings.Orientation = PdfPageOrientation.Portrait;
        doc.PageSettings.Margins.Left = MarginX;
        doc.PageSettings.Margins.Right = MarginX;
        doc.PageSettings.Margins.Top = MarginTop;
        doc.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(doc);
        return doc;
    }

    private static float DrawTitle(PdfGraphics g, string subtitle, string period, string printStamp, Fonts fonts)
    {
        g.DrawString("TSIC Fees — Year-to-Date Comparison", fonts.Title, new PdfSolidBrush(TitleBlue),
            new RectangleF(0, 0, ContentW, 18f), new PdfStringFormat(PdfTextAlignment.Center));
        g.DrawString(subtitle, fonts.Subtitle, PdfBrushes.Black,
            new RectangleF(0, 19f, ContentW, 14f), new PdfStringFormat(PdfTextAlignment.Center));
        g.DrawString(period, fonts.Small, new PdfSolidBrush(new PdfColor(70, 70, 70)),
            new RectangleF(0, 34f, ContentW, 12f), new PdfStringFormat(PdfTextAlignment.Center));
        g.DrawString(printStamp, fonts.Small, new PdfSolidBrush(new PdfColor(110, 110, 110)),
            new RectangleF(0, 46f, ContentW, 12f), new PdfStringFormat(PdfTextAlignment.Center));
        return 64f;
    }

    private static float DrawColumnHeader(PdfGraphics g, float y, Fonts fonts, Pens pens)
    {
        g.DrawRectangle(new PdfSolidBrush(BandColor), new RectangleF(0, y, ContentW, ColHeaderH));
        var x = 0f;
        foreach (var (key, w, align) in Cols)
        {
            g.DrawString(key, fonts.ColHeader, PdfBrushes.Black,
                new RectangleF(x + CellPadX, y, w - (CellPadX * 2), ColHeaderH),
                new PdfStringFormat(align, PdfVerticalAlignment.Middle));
            x += w;
        }
        g.DrawLine(pens.Header, new PointF(0, y + ColHeaderH), new PointF(ContentW, y + ColHeaderH));
        return y + ColHeaderH + 1f;
    }

    private static float DrawCustomerBand(PdfGraphics g, string name, float y, Fonts fonts)
    {
        g.DrawRectangle(new PdfSolidBrush(CustomerBand), new RectangleF(0, y, ContentW, RowH));
        g.DrawString(name, fonts.CellBold, PdfBrushes.Black,
            new RectangleF(CellPadX, y, ContentW - (CellPadX * 2), RowH),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        return y + RowH;
    }

    private static float DrawRow(
        PdfGraphics g, string label, decimal last, decimal cur, float y, Fonts fonts, Pens pens,
        bool bold, bool indent)
    {
        var font = bold ? fonts.CellBold : fonts.Cell;
        var change = cur - last;
        var values = new[] { label, Money(last), Money(cur), ChangeText(change) };

        var x = 0f;
        for (var i = 0; i < Cols.Length; i++)
        {
            var (_, w, align) = Cols[i];
            var brush = i == 3 && !bold
                ? (change < 0 ? new PdfSolidBrush(new PdfColor(176, 0, 0)) : PdfBrushes.Black)
                : PdfBrushes.Black;
            g.DrawString(values[i], font, brush,
                new RectangleF(x + CellPadX, y, w - (CellPadX * 2), RowH),
                new PdfStringFormat(align, PdfVerticalAlignment.Middle));
            x += w;
        }
        g.DrawLine(pens.Divider, new PointF(0, y + RowH), new PointF(ContentW, y + RowH));
        return y + RowH;
    }

    private static string Money(decimal v) => v.ToString("$#,##0.00", CultureInfo.InvariantCulture);

    // Year-over-year delta with an explicit sign so a drop reads clearly (paired with red, not color-only).
    private static string ChangeText(decimal change)
    {
        if (change == 0m)
        {
            return "$0.00";
        }
        var sign = change < 0 ? "-" : "+";
        return sign + Math.Abs(change).ToString("$#,##0.00", CultureInfo.InvariantCulture);
    }

    private static void AddFooterTemplate(PdfDocument document)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var gray = new PdfSolidBrush(new PdfColor(102, 102, 102));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, ContentW, FooterH));
        footer.Graphics.DrawString("Reports By TeamSportsInfo.com", footerFont, gray, new PointF(2, 4));
        var composite = new PdfCompositeField(
            footerFont, gray, "Page {0} / {1}",
            new PdfPageNumberField(footerFont, gray),
            new PdfPageCountField(footerFont, gray))
        {
            Bounds = new RectangleF(0, 4, ContentW - 2, FooterH),
            StringFormat = new PdfStringFormat(PdfTextAlignment.Right),
        };
        composite.Draw(footer.Graphics, new PointF(0, 4));
        document.Template.Bottom = footer;
    }

    private static ReportExportResult Save(PdfDocument doc, string fileName)
    {
        using var ms = new MemoryStream();
        doc.Save(ms);
        doc.Close(true);
        return new ReportExportResult { FileBytes = ms.ToArray(), ContentType = "application/pdf", FileName = fileName };
    }

    // ── Render-time models ──────────────────────────────────────────────

    private sealed class CustomerBlock
    {
        public required string Name { get; init; }
        public required List<JobRow> Jobs { get; init; }
        public decimal LastYtd { get; init; }
        public decimal ThisYtd { get; init; }
    }

    private sealed class JobRow
    {
        public required string Name { get; init; }
        public decimal LastYtd { get; init; }
        public decimal ThisYtd { get; init; }
    }

    private sealed class Fonts
    {
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        public PdfStandardFont Subtitle { get; } = new(PdfFontFamily.Helvetica, 10, PdfFontStyle.Bold);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 8);
        public PdfStandardFont CellBold { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Label { get; } = new(PdfFontFamily.Helvetica, 9);
        public PdfStandardFont Small { get; } = new(PdfFontFamily.Helvetica, 8);
    }

    private sealed class Pens
    {
        public PdfPen Header { get; } = new(new PdfColor(0, 0, 0), 0.75f);
        public PdfPen Divider { get; } = new(new PdfColor(210, 210, 210), 0.5f);
    }
}
