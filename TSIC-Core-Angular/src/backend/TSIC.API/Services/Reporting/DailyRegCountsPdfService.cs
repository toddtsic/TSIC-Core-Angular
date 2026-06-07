using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) daily registration-counts report — the EF replacement for the
/// legacy Crystal "JobPlayers_TSICDaily" (proc <c>reporting.Get_Registrations_TSIC_Today</c>).
/// One flat EF dataset (<see cref="IReportingRepository.GetDailyRegCountsAsync"/>) is grouped
/// Customer → Job → role and drawn as a running-total table: today's count and the to-date total
/// per role, with per-job and grand totals. Cross-job and public (the daily ops report), no
/// job scoping.
/// </summary>
public sealed class DailyRegCountsPdfService : IDailyRegCountsPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public DailyRegCountsPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // ── Page + table geometry (points; Letter portrait, 0.4in margins) ──
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);            // 554.4
    private const float ContentBottom = PageH - MarginBottom;        // 763.2
    private const float FooterH = 18f;
    // Reserve the bottom footer template (Syncfusion draws it inside the client area) so the last
    // row never clips — same accounting as the schedule/roster services.
    private const float MaxContentY = ContentBottom - MarginTop - FooterH - 2f;

    private const float NumColW = 78f;                               // Today / To-Date columns
    private const float RoleColW = ContentW - (NumColW * 2);
    private const float RoleIndent = 16f;

    private const float CustomerHeaderH = 20f;
    private const float JobHeaderH = 16f;
    private const float ColHeaderH = 14f;
    private const float RowH = 14f;
    private const float JobTotalH = 16f;
    private const float GrandTotalH = 24f;
    private const float CellPadX = 3f;

    private static readonly PdfStringFormat LeftMid = new(PdfTextAlignment.Left, PdfVerticalAlignment.Middle);
    private static readonly PdfStringFormat RightMid = new(PdfTextAlignment.Right, PdfVerticalAlignment.Middle);

    public async Task<ReportExportResult> GenerateAsync(CancellationToken cancellationToken = default)
    {
        // Server-local "today", mirroring the proc's getdate() (the codebase is local-AZ, not UTC).
        var asOf = DateTime.Now;
        var rows = await _reportingRepository.GetDailyRegCountsAsync(asOf, cancellationToken);

        using var document = new PdfDocument();
        document.PageSettings.Size = PdfPageSize.Letter;
        document.PageSettings.Margins.Left = MarginX;
        document.PageSettings.Margins.Right = MarginX;
        document.PageSettings.Margins.Top = MarginTop;
        document.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(document);

        var fonts = new Fonts();
        var pens = new Pens();

        var g = document.Pages.Add().Graphics;
        var y = DrawTitle(g, asOf, rows, fonts, pens);

        if (rows.Count == 0)
        {
            g.DrawString("No registrations recorded today.", fonts.JobHeader, PdfBrushes.Gray,
                new RectangleF(0, y + 8f, ContentW, 20f), LeftMid);
        }
        else
        {
            var grandToday = 0;
            var grandToDate = 0;

            // rows arrive ordered Customer → Job → Role, so GroupBy preserves display order.
            foreach (var customer in rows.GroupBy(r => r.CustomerName))
            {
                if (y + CustomerHeaderH + JobHeaderH + ColHeaderH + RowH > MaxContentY)
                {
                    g = document.Pages.Add().Graphics;
                    y = 0f;
                }
                y = DrawCustomerHeader(g, customer.Key, y, fonts);

                foreach (var job in customer.GroupBy(r => r.JobName))
                {
                    if (y + JobHeaderH + ColHeaderH + RowH > MaxContentY)
                    {
                        g = document.Pages.Add().Graphics;
                        y = 0f;
                        y = DrawCustomerHeader(g, customer.Key, y, fonts, continued: true);
                    }
                    y = DrawJobHeader(g, job.Key, y, fonts);
                    y = DrawColumnHeader(g, y, fonts, pens);

                    var jobToday = 0;
                    var jobToDate = 0;
                    foreach (var role in job)
                    {
                        if (y + RowH + JobTotalH > MaxContentY)
                        {
                            g = document.Pages.Add().Graphics;
                            y = 0f;
                            y = DrawJobHeader(g, job.Key, y, fonts, continued: true);
                            y = DrawColumnHeader(g, y, fonts, pens);
                        }
                        y = DrawRoleRow(g, role, y, fonts);
                        jobToday += role.CountDaily;
                        jobToDate += role.CountToDate;
                    }

                    y = DrawJobTotal(g, jobToday, jobToDate, y, fonts, pens);
                    grandToday += jobToday;
                    grandToDate += jobToDate;
                }

                y += 4f;   // breathing room after each customer block
            }

            if (y + GrandTotalH > MaxContentY)
            {
                g = document.Pages.Add().Graphics;
                y = 0f;
            }
            DrawGrandTotal(g, grandToday, grandToDate, y, fonts, pens);
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = "DailyRegistrationCounts.pdf",
        };
    }

    // ── Drawing ──

    private static float DrawTitle(
        PdfGraphics g, DateTime asOf, List<DailyRegCountRowDto> rows, Fonts fonts, Pens pens)
    {
        g.DrawString("Daily Registration Counts", fonts.Title, PdfBrushes.Black, new PointF(0, 0));

        var gray = new PdfSolidBrush(new PdfColor(90, 90, 90));
        g.DrawString(asOf.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture),
            fonts.Subtitle, gray, new PointF(0, 21f));

        var jobsToday = rows.Select(r => (r.CustomerName, r.JobName)).Distinct().Count();
        var totalToday = rows.Sum(r => r.CountDaily);
        var summary = rows.Count == 0
            ? "No activity today."
            : $"{jobsToday} job(s) with activity today  ·  {totalToday} registration(s) today";
        g.DrawString(summary, fonts.Subtitle, gray, new PointF(0, 34f));

        g.DrawLine(pens.Header, new PointF(0, 50f), new PointF(ContentW, 50f));
        return 56f;
    }

    private static float DrawCustomerHeader(
        PdfGraphics g, string customer, float y, Fonts fonts, bool continued = false)
    {
        g.DrawRectangle(new PdfSolidBrush(new PdfColor(60, 72, 88)),
            new RectangleF(0, y, ContentW, CustomerHeaderH));
        var label = customer.Length == 0 ? "(Unspecified customer)" : customer;
        if (continued)
        {
            label += " (cont.)";
        }
        g.DrawString(label, fonts.CustomerHeader, PdfBrushes.White,
            new RectangleF(CellPadX + 2f, y, ContentW - (CellPadX * 2) - 2f, CustomerHeaderH), LeftMid);
        return y + CustomerHeaderH;
    }

    private static float DrawJobHeader(
        PdfGraphics g, string job, float y, Fonts fonts, bool continued = false)
    {
        var label = job.Length == 0 ? "(Unspecified job)" : job;
        if (continued)
        {
            label += " (cont.)";
        }
        g.DrawString(label, fonts.JobHeader, PdfBrushes.Black,
            new RectangleF(RoleIndent - 6f, y + 1f, ContentW - RoleIndent, JobHeaderH - 1f), LeftMid);
        return y + JobHeaderH;
    }

    private static float DrawColumnHeader(PdfGraphics g, float y, Fonts fonts, Pens pens)
    {
        g.DrawString("Role", fonts.ColHeader, PdfBrushes.Black,
            new RectangleF(RoleIndent, y, RoleColW - RoleIndent, ColHeaderH), LeftMid);
        g.DrawString("Today", fonts.ColHeader, PdfBrushes.Black,
            new RectangleF(RoleColW, y, NumColW - CellPadX, ColHeaderH), RightMid);
        g.DrawString("To-Date", fonts.ColHeader, PdfBrushes.Black,
            new RectangleF(RoleColW + NumColW, y, NumColW - CellPadX, ColHeaderH), RightMid);
        g.DrawLine(pens.Divider, new PointF(RoleIndent, y + ColHeaderH), new PointF(ContentW, y + ColHeaderH));
        return y + ColHeaderH;
    }

    private static float DrawRoleRow(PdfGraphics g, DailyRegCountRowDto row, float y, Fonts fonts)
    {
        g.DrawString(row.RoleName.Length == 0 ? "(No role)" : row.RoleName, fonts.Cell, PdfBrushes.Black,
            new RectangleF(RoleIndent, y, RoleColW - RoleIndent, RowH), LeftMid);
        g.DrawString(row.CountDaily.ToString(CultureInfo.InvariantCulture), fonts.Cell, PdfBrushes.Black,
            new RectangleF(RoleColW, y, NumColW - CellPadX, RowH), RightMid);
        g.DrawString(row.CountToDate.ToString(CultureInfo.InvariantCulture), fonts.Cell, PdfBrushes.Black,
            new RectangleF(RoleColW + NumColW, y, NumColW - CellPadX, RowH), RightMid);
        return y + RowH;
    }

    private static float DrawJobTotal(
        PdfGraphics g, int today, int toDate, float y, Fonts fonts, Pens pens)
    {
        g.DrawLine(pens.Total, new PointF(RoleColW, y + 1f), new PointF(ContentW, y + 1f));
        g.DrawString("Job total", fonts.CellBold, PdfBrushes.Black,
            new RectangleF(RoleIndent, y, RoleColW - RoleIndent, JobTotalH), RightMid);
        g.DrawString(today.ToString(CultureInfo.InvariantCulture), fonts.CellBold, PdfBrushes.Black,
            new RectangleF(RoleColW, y, NumColW - CellPadX, JobTotalH), RightMid);
        g.DrawString(toDate.ToString(CultureInfo.InvariantCulture), fonts.CellBold, PdfBrushes.Black,
            new RectangleF(RoleColW + NumColW, y, NumColW - CellPadX, JobTotalH), RightMid);
        return y + JobTotalH;
    }

    private static void DrawGrandTotal(
        PdfGraphics g, int today, int toDate, float y, Fonts fonts, Pens pens)
    {
        g.DrawLine(pens.Header, new PointF(0, y + 2f), new PointF(ContentW, y + 2f));
        g.DrawString("Grand total", fonts.JobHeader, PdfBrushes.Black,
            new RectangleF(0, y + 5f, RoleColW, 18f), LeftMid);
        g.DrawString(today.ToString(CultureInfo.InvariantCulture), fonts.JobHeader, PdfBrushes.Black,
            new RectangleF(RoleColW, y + 5f, NumColW - CellPadX, 18f), RightMid);
        g.DrawString(toDate.ToString(CultureInfo.InvariantCulture), fonts.JobHeader, PdfBrushes.Black,
            new RectangleF(RoleColW + NumColW, y + 5f, NumColW - CellPadX, 18f), RightMid);
    }

    private static void AddFooterTemplate(PdfDocument document)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var gray = new PdfSolidBrush(new PdfColor(102, 102, 102));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, ContentW, FooterH));

        footer.Graphics.DrawString("Reports By TeamSportsInfo.com", footerFont, gray, new PointF(2, 4));

        // Right-aligned page number. A PdfCompositeField only honors its StringFormat alignment
        // within its Bounds — drawn at a bare point it renders left-aligned and the total clips.
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

    // ── Render-time helpers ──

    private sealed class Fonts
    {
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 15, PdfFontStyle.Bold);
        public PdfStandardFont Subtitle { get; } = new(PdfFontFamily.Helvetica, 9);
        public PdfStandardFont CustomerHeader { get; } = new(PdfFontFamily.Helvetica, 11, PdfFontStyle.Bold);
        public PdfStandardFont JobHeader { get; } = new(PdfFontFamily.Helvetica, 10, PdfFontStyle.Bold);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 9);
        public PdfStandardFont CellBold { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
    }

    private sealed class Pens
    {
        public PdfPen Header { get; } = new(new PdfColor(0, 0, 0), 0.75f);
        public PdfPen Divider { get; } = new(new PdfColor(200, 200, 200), 0.5f);
        public PdfPen Total { get; } = new(new PdfColor(120, 120, 120), 0.6f);
    }
}
