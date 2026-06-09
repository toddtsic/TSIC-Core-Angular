using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) TSIC-fee year-to-date reports — the EF replacement for the legacy
/// Crystal "tsicTSICFeesYTDByCustomer" and "tsicTSICFeesYTD" (by customer + job), both backed by
/// <c>adn.tsicFeesYTDAndLastYear</c>. These reproduce the legacy <b>cross-tab</b>: rows are
/// Customer (→ Job) → calendar-month number, columns are the two years (last year, this year), and
/// each cell is that month's fee. Per-group <c>Total</c> rows and a final grand <c>Total</c> close
/// the tab. The flat EF row set (<see cref="IReportingRepository.GetFeeYtdRowsAsync"/>) supplies
/// month-grain fees; this layer only pivots, sums, and draws.
///
/// Fee math mirrors the proc exactly: each row's fee = NewPlayers×perPlayerCharge +
/// NewTeams×perTeamCharge (computed in the repository). Months with no activity are suppressed per
/// group; zero-valued cells/rows are NOT suppressed (the legacy prints them). Runs across ALL jobs;
/// the window is months 1..lastCompletedMonth for both years.
/// </summary>
public sealed class FeeYtdReportPdfService : IFeeYtdReportPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public FeeYtdReportPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // Letter PORTRAIT (the cross-tab is a narrow, left-aligned table).
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);                       // 554.4
    private const float FooterH = 18f;
    private const float MaxContentY = PageH - MarginTop - MarginBottom - FooterH - 2f;  // 714.4
    private const float CellPadX = 4f;
    private const float RowH = 15f;

    public async Task<ReportExportResult> GenerateByCustomerAsync(CancellationToken cancellationToken = default)
    {
        var (lastYear, thisYear, customers, g1, g2) = await BuildByCustomerAsync(cancellationToken);
        return RenderByCustomer(customers, g1, g2, lastYear, thisYear, "TSICFeesYTD_ByCustomer.pdf");
    }

    public async Task<ReportExportResult> GenerateByCustomerAndJobAsync(CancellationToken cancellationToken = default)
    {
        var (lastYear, thisYear, customers, g1, g2) = await BuildByCustomerAndJobAsync(cancellationToken);
        return RenderByCustomerAndJob(customers, g1, g2, lastYear, thisYear, "TSICFeesYTD_ByCustomerAndJob.pdf");
    }

    // ── Data shaping (pivot the flat rows into the cross-tab) ────────────

    private async Task<(int LastYear, int ThisYear, List<CustBlock> Customers, decimal G1, decimal G2)>
        BuildByCustomerAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var thisYear = now.AddMonths(-1).Year;
        var lastYear = thisYear - 1;

        var rows = await _reportingRepository.GetFeeYtdRowsAsync(now, ct);

        var customers = rows
            .GroupBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(cg => cg.Key, StringComparer.OrdinalIgnoreCase)
            .Select(cg => new CustBlock
            {
                Name = cg.Key,
                Months = MonthCells(cg, lastYear, thisYear),
                T1 = cg.Where(r => r.Year == lastYear).Sum(r => r.TsicFees),
                T2 = cg.Where(r => r.Year == thisYear).Sum(r => r.TsicFees),
            })
            .ToList();

        return (lastYear, thisYear, customers,
            rows.Where(r => r.Year == lastYear).Sum(r => r.TsicFees),
            rows.Where(r => r.Year == thisYear).Sum(r => r.TsicFees));
    }

    private async Task<(int LastYear, int ThisYear, List<CustJobBlock> Customers, decimal G1, decimal G2)>
        BuildByCustomerAndJobAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var thisYear = now.AddMonths(-1).Year;
        var lastYear = thisYear - 1;

        var rows = await _reportingRepository.GetFeeYtdRowsAsync(now, ct);

        var customers = rows
            .GroupBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(cg => cg.Key, StringComparer.OrdinalIgnoreCase)
            .Select(cg => new CustJobBlock
            {
                Name = cg.Key,
                Jobs = cg.GroupBy(r => r.JobName, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(jg => jg.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(jg => new JobBlock
                    {
                        Label = JobLabel(cg.Key, jg.Key),
                        Months = MonthCells(jg, lastYear, thisYear),
                        T1 = jg.Where(r => r.Year == lastYear).Sum(r => r.TsicFees),
                        T2 = jg.Where(r => r.Year == thisYear).Sum(r => r.TsicFees),
                    })
                    .ToList(),
                T1 = cg.Where(r => r.Year == lastYear).Sum(r => r.TsicFees),
                T2 = cg.Where(r => r.Year == thisYear).Sum(r => r.TsicFees),
            })
            .ToList();

        return (lastYear, thisYear, customers,
            rows.Where(r => r.Year == lastYear).Sum(r => r.TsicFees),
            rows.Where(r => r.Year == thisYear).Sum(r => r.TsicFees));
    }

    // One MonthCell per calendar month present in the group (either year); cell value = sum of fees
    // for that month/year (0 if none). Empty months are suppressed; zero cells are kept.
    private static List<MonthCell> MonthCells(
        IEnumerable<FeeYtdRowDto> group, int lastYear, int thisYear)
    {
        var rows = group as ICollection<FeeYtdRowDto> ?? group.ToList();
        return rows
            .Select(r => r.Month).Distinct().OrderBy(m => m)
            .Select(m => new MonthCell
            {
                Month = m,
                V1 = rows.Where(r => r.Month == m && r.Year == lastYear).Sum(r => r.TsicFees),
                V2 = rows.Where(r => r.Month == m && r.Year == thisYear).Sum(r => r.TsicFees),
            })
            .ToList();
    }

    // Legacy job label = "Customer:JobName". Guard against doubling if JobName already carries it.
    private static string JobLabel(string customer, string job)
    {
        var prefix = customer + ":";
        return job.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? job : prefix + job;
    }

    // ── Render: By Customer (Customer | Month | Year1 | Year2) ───────────

    private ReportExportResult RenderByCustomer(
        List<CustBlock> customers, decimal g1, decimal g2, int lastYear, int thisYear, string fileName)
    {
        float[] xs = { 0f, 175f, 230f, 305f, 380f };
        const int Cust = 0, Mon = 1, Y1 = 2, Y2 = 3;
        const string title = "TSIC Fees Per Month YTD By Customer";

        var doc = NewDocument();
        var f = new Fonts();
        var p = new Pens();

        var g = doc.Pages.Add().Graphics;
        var y = DrawHead(g, title, xs, lastYear, thisYear, f, p);

        if (customers.Count == 0)
        {
            DrawCellText(g, "No TSIC-fee activity.", f.Cell, xs[0], y + 6f, ContentW, PdfTextAlignment.Left);
            return Save(doc, fileName);
        }

        foreach (var c in customers)
        {
            var rows = new List<CrossRow>();
            foreach (var mc in c.Months)
            {
                rows.Add(new CrossRow { Label = mc.Month.ToString(CultureInfo.InvariantCulture), V1 = mc.V1, V2 = mc.V2, Kind = RowKind.Detail });
            }
            rows.Add(new CrossRow { Label = "Total", V1 = c.T1, V2 = c.T2, Kind = RowKind.GroupTotal });

            var i = 0;
            while (i < rows.Count)
            {
                if (y + RowH > MaxContentY)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawHead(g, title, xs, lastYear, thisYear, f, p);
                }
                var fit = Math.Min(rows.Count - i, (int)((MaxContentY - y) / RowH));
                if (fit <= 0)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawHead(g, title, xs, lastYear, thisYear, f, p);
                    continue;
                }

                DrawLabel(g, c.Name, f.Cell, xs[Cust], y, xs[Cust + 1] - xs[Cust], fit * RowH);

                for (var k = 0; k < fit; k++)
                {
                    var r = rows[i + k];
                    DrawCellText(g, r.Label, f.Cell, xs[Mon], y, xs[Mon + 1] - xs[Mon], PdfTextAlignment.Left);
                    DrawCellText(g, Money(r.V1), f.CellBold, xs[Y1], y, xs[Y1 + 1] - xs[Y1], PdfTextAlignment.Right);
                    DrawCellText(g, Money(r.V2), f.CellBold, xs[Y2], y, xs[Y2 + 1] - xs[Y2], PdfTextAlignment.Right);
                    DrawVerticals(g, xs, y, p);
                    var lx = r.Kind == RowKind.GroupTotal ? xs[0] : xs[Mon];
                    g.DrawLine(p.Grid, new PointF(lx, y + RowH), new PointF(xs[^1], y + RowH));
                    y += RowH;
                }
                i += fit;
            }
        }

        DrawGrandTotal(ref g, ref y, doc, xs, Cust, Y1, Y2, g1, g2, title, lastYear, thisYear, f, p);
        return Save(doc, fileName);
    }

    // ── Render: By Customer and Job (Customer | Job | Month | Y1 | Y2) ───

    private ReportExportResult RenderByCustomerAndJob(
        List<CustJobBlock> customers, decimal g1, decimal g2, int lastYear, int thisYear, string fileName)
    {
        float[] xs = { 0f, 120f, 235f, 283f, 363f, 443f };
        const int Cust = 0, Job = 1, Mon = 2, Y1 = 3, Y2 = 4;
        const string title = "TSIC Fees Per Month YTD By Customer and Job";

        var doc = NewDocument();
        var f = new Fonts();
        var p = new Pens();

        var g = doc.Pages.Add().Graphics;
        var y = DrawHead(g, title, xs, lastYear, thisYear, f, p);

        if (customers.Count == 0)
        {
            DrawCellText(g, "No TSIC-fee activity.", f.Cell, xs[0], y + 6f, ContentW, PdfTextAlignment.Left);
            return Save(doc, fileName);
        }

        foreach (var c in customers)
        {
            var rows = new List<CrossRow>();
            for (var ji = 0; ji < c.Jobs.Count; ji++)
            {
                var job = c.Jobs[ji];
                foreach (var mc in job.Months)
                {
                    rows.Add(new CrossRow { JobIndex = ji, Label = mc.Month.ToString(CultureInfo.InvariantCulture), V1 = mc.V1, V2 = mc.V2, Kind = RowKind.Detail });
                }
                rows.Add(new CrossRow { JobIndex = ji, Label = "Total", V1 = job.T1, V2 = job.T2, Kind = RowKind.JobTotal });
            }
            rows.Add(new CrossRow { Label = "Total", V1 = c.T1, V2 = c.T2, Kind = RowKind.CustTotal });

            var i = 0;
            while (i < rows.Count)
            {
                if (y + RowH > MaxContentY)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawHead(g, title, xs, lastYear, thisYear, f, p);
                }
                var fit = Math.Min(rows.Count - i, (int)((MaxContentY - y) / RowH));
                if (fit <= 0)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawHead(g, title, xs, lastYear, thisYear, f, p);
                    continue;
                }

                // Customer label spans the whole page-segment (all its jobs visible here).
                DrawLabel(g, c.Name, f.Cell, xs[Cust], y, xs[Cust + 1] - xs[Cust], fit * RowH);

                var k = 0;
                while (k < fit)
                {
                    var r = rows[i + k];
                    if (r.Kind == RowKind.CustTotal)
                    {
                        DrawCellText(g, "Total", f.Cell, xs[Job], y, xs[Job + 1] - xs[Job], PdfTextAlignment.Left);
                        DrawCellText(g, Money(r.V1), f.CellBold, xs[Y1], y, xs[Y1 + 1] - xs[Y1], PdfTextAlignment.Right);
                        DrawCellText(g, Money(r.V2), f.CellBold, xs[Y2], y, xs[Y2 + 1] - xs[Y2], PdfTextAlignment.Right);
                        DrawVerticals(g, xs, y, p);
                        g.DrawLine(p.Grid, new PointF(xs[0], y + RowH), new PointF(xs[^1], y + RowH));
                        y += RowH;
                        k++;
                        continue;
                    }

                    // Run of consecutive rows for one job (its months + job Total) within this segment.
                    var ji = r.JobIndex;
                    var run = 0;
                    while (k + run < fit && rows[i + k + run].Kind != RowKind.CustTotal && rows[i + k + run].JobIndex == ji)
                    {
                        run++;
                    }

                    DrawLabel(g, c.Jobs[ji].Label, f.CellSmall, xs[Job], y, xs[Job + 1] - xs[Job], run * RowH);

                    for (var q = 0; q < run; q++)
                    {
                        var rr = rows[i + k + q];
                        DrawCellText(g, rr.Label, f.Cell, xs[Mon], y, xs[Mon + 1] - xs[Mon], PdfTextAlignment.Left);
                        DrawCellText(g, Money(rr.V1), f.CellBold, xs[Y1], y, xs[Y1 + 1] - xs[Y1], PdfTextAlignment.Right);
                        DrawCellText(g, Money(rr.V2), f.CellBold, xs[Y2], y, xs[Y2 + 1] - xs[Y2], PdfTextAlignment.Right);
                        DrawVerticals(g, xs, y, p);
                        // Job Total closes the job (rule from the Job column); a month row's rule starts at Month.
                        var lx = rr.Kind == RowKind.JobTotal ? xs[Job] : xs[Mon];
                        g.DrawLine(p.Grid, new PointF(lx, y + RowH), new PointF(xs[^1], y + RowH));
                        y += RowH;
                    }
                    k += run;
                }
                i += fit;
            }
        }

        DrawGrandTotal(ref g, ref y, doc, xs, Cust, Y1, Y2, g1, g2, title, lastYear, thisYear, f, p);
        return Save(doc, fileName);
    }

    // Final grand-total row: "Total" in the Customer column, full-width rules above and below.
    private static void DrawGrandTotal(
        ref PdfGraphics g, ref float y, PdfDocument doc, float[] xs, int cust, int y1, int y2,
        decimal g1, decimal g2, string title, int lastYear, int thisYear, Fonts f, Pens p)
    {
        if (y + RowH > MaxContentY)
        {
            g = doc.Pages.Add().Graphics;
            y = DrawHead(g, title, xs, lastYear, thisYear, f, p);
        }
        DrawCellText(g, "Total", f.Cell, xs[cust], y, xs[cust + 1] - xs[cust], PdfTextAlignment.Left);
        DrawCellText(g, Money(g1), f.CellBold, xs[y1], y, xs[y1 + 1] - xs[y1], PdfTextAlignment.Right);
        DrawCellText(g, Money(g2), f.CellBold, xs[y2], y, xs[y2 + 1] - xs[y2], PdfTextAlignment.Right);
        DrawVerticals(g, xs, y, p);
        g.DrawLine(p.Grid, new PointF(xs[0], y), new PointF(xs[^1], y));
        g.DrawLine(p.Grid, new PointF(xs[0], y + RowH), new PointF(xs[^1], y + RowH));
        y += RowH;
    }

    // ── Drawing primitives ──────────────────────────────────────────────

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

    // Title (bold, underlined, top-left) + the two boxed year-column headers; returns the body top y.
    private static float DrawHead(PdfGraphics g, string title, float[] xs, int lastYear, int thisYear, Fonts f, Pens p)
    {
        g.DrawString(title, f.Title, PdfBrushes.Black,
            new RectangleF(0, 0, ContentW, 18f), new PdfStringFormat(PdfTextAlignment.Left));

        const float headTop = 26f, headH = 16f;
        var n = xs.Length;
        int y1 = n - 3, y2 = n - 2, right = n - 1;
        var gray = new PdfSolidBrush(new PdfColor(80, 80, 80));

        g.DrawString(lastYear.ToString(CultureInfo.InvariantCulture), f.Cell, gray,
            new RectangleF(xs[y1] + CellPadX, headTop, (xs[y2] - xs[y1]) - (CellPadX * 2), headH),
            new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle));
        g.DrawString(thisYear.ToString(CultureInfo.InvariantCulture), f.Cell, gray,
            new RectangleF(xs[y2] + CellPadX, headTop, (xs[right] - xs[y2]) - (CellPadX * 2), headH),
            new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle));

        g.DrawLine(p.Grid, new PointF(xs[y1], headTop), new PointF(xs[right], headTop));
        g.DrawLine(p.Grid, new PointF(xs[y1], headTop + headH), new PointF(xs[right], headTop + headH));
        g.DrawLine(p.Grid, new PointF(xs[y1], headTop), new PointF(xs[y1], headTop + headH));
        g.DrawLine(p.Grid, new PointF(xs[y2], headTop), new PointF(xs[y2], headTop + headH));
        g.DrawLine(p.Grid, new PointF(xs[right], headTop), new PointF(xs[right], headTop + headH));

        var bodyTop = headTop + headH;
        g.DrawLine(p.Grid, new PointF(xs[0], bodyTop), new PointF(xs[^1], bodyTop));
        return bodyTop;
    }

    private static void DrawLabel(PdfGraphics g, string text, PdfFont font, float x, float y, float w, float h)
        => g.DrawString(text, font, PdfBrushes.Black,
            new RectangleF(x + CellPadX, y + 1f, w - (CellPadX * 2), h - 2f),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top) { WordWrap = PdfWordWrapType.Word });

    private static void DrawCellText(PdfGraphics g, string text, PdfFont font, float x, float y, float w, PdfTextAlignment align)
        => g.DrawString(text, font, PdfBrushes.Black,
            new RectangleF(x + CellPadX, y, w - (CellPadX * 2), RowH),
            new PdfStringFormat(align, PdfVerticalAlignment.Middle));

    private static void DrawVerticals(PdfGraphics g, float[] xs, float y, Pens p)
    {
        foreach (var x in xs)
        {
            g.DrawLine(p.Grid, new PointF(x, y), new PointF(x, y + RowH));
        }
    }

    // Legacy cells are plain "#,##0.00" — no currency symbol; negatives carry a leading minus.
    private static string Money(decimal v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static void AddFooterTemplate(PdfDocument document)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8);
        var gray = new PdfSolidBrush(new PdfColor(90, 90, 90));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, ContentW, FooterH));
        footer.Graphics.DrawString(
            DateTime.Now.ToString("M/d/yyyy", CultureInfo.InvariantCulture), footerFont, gray, new PointF(2, 4));
        var composite = new PdfCompositeField(
            footerFont, gray, "Page {0} of {1}",
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

    private enum RowKind { Detail, JobTotal, GroupTotal, CustTotal }

    private sealed class CrossRow
    {
        public int JobIndex { get; init; } = -1;
        public required string Label { get; init; }
        public decimal V1 { get; init; }
        public decimal V2 { get; init; }
        public RowKind Kind { get; init; }
    }

    private sealed class MonthCell
    {
        public int Month { get; init; }
        public decimal V1 { get; init; }
        public decimal V2 { get; init; }
    }

    private sealed class CustBlock
    {
        public required string Name { get; init; }
        public required List<MonthCell> Months { get; init; }
        public decimal T1 { get; init; }
        public decimal T2 { get; init; }
    }

    private sealed class JobBlock
    {
        public required string Label { get; init; }
        public required List<MonthCell> Months { get; init; }
        public decimal T1 { get; init; }
        public decimal T2 { get; init; }
    }

    private sealed class CustJobBlock
    {
        public required string Name { get; init; }
        public required List<JobBlock> Jobs { get; init; }
        public decimal T1 { get; init; }
        public decimal T2 { get; init; }
    }

    private sealed class Fonts
    {
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold | PdfFontStyle.Underline);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 8);
        public PdfStandardFont CellBold { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont CellSmall { get; } = new(PdfFontFamily.Helvetica, 7.5f);
    }

    private sealed class Pens
    {
        public PdfPen Grid { get; } = new(new PdfColor(0, 0, 0), 0.5f);
    }
}
