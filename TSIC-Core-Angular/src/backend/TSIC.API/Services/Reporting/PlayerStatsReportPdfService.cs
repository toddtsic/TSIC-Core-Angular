using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) E120 player-stats entry form — the EF replacement for the legacy
/// Crystal "PlayerStats_E120" (<c>reporting.PlayerStats_E120</c>). Active Players for the job, grouped
/// agegroup → team, with write-in cells for the four athletic-combine stats. Pre-fills any value
/// already recorded on <c>Registrations</c>; otherwise the cell is a blank box for hand-entry.
/// </summary>
public sealed class PlayerStatsReportPdfService : IPlayerStatsReportPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public PlayerStatsReportPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // Letter PORTRAIT.
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);          // 554.4
    private const float ContentBottom = PageH - MarginBottom;       // 763.2
    private const float FooterH = 18f;
    private const float MaxContentY = ContentBottom - MarginTop - FooterH - 2f;  // 714.4
    private const float CellPadX = 4f;
    private const float RowH = 16f;
    private const float ColHeaderH = 26f;

    // Columns (sum == ContentW). The four stat columns are write-in boxes.
    private static readonly (string Key, float W, PdfTextAlignment Align)[] Cols =
    {
        ("#",            34f,    PdfTextAlignment.Center),
        ("Player",       180.4f, PdfTextAlignment.Left),
        ("Fastest Shot", 85f,    PdfTextAlignment.Center),
        ("5-10-5",       85f,    PdfTextAlignment.Center),
        ("40 Yd Dash",   85f,    PdfTextAlignment.Center),
        ("300 Shuttle",  85f,    PdfTextAlignment.Center),
    };

    private static readonly PdfColor BandColor = new(222, 222, 222);
    private static readonly PdfColor TeamBand = new(238, 238, 238);
    private static readonly PdfColor TitleBlue = new(0, 0, 160);

    public async Task<ReportExportResult> GenerateE120Async(Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetPlayerStatsE120RowsAsync(jobId, cancellationToken);

        var doc = NewDocument();
        var fonts = new Fonts();
        var pens = new Pens();
        var printStamp = "Print Date: " + DateTime.Now.ToString("M/d/yyyy  h:mm tt", CultureInfo.InvariantCulture);

        var g = doc.Pages.Add().Graphics;
        var y = DrawTitle(g, printStamp, fonts);

        if (rows.Count == 0)
        {
            g.DrawString("No active players for this job.", fonts.Label, PdfBrushes.Gray,
                new RectangleF(0, y + 8f, ContentW, 18f), new PdfStringFormat(PdfTextAlignment.Left));
            return Save(doc);
        }

        y = DrawColumnHeader(g, y, fonts, pens);

        foreach (var ag in rows.GroupBy(r => r.AgegroupName ?? string.Empty))
        {
            if (y + RowH * 2 > MaxContentY)
            {
                g = doc.Pages.Add().Graphics;
                y = DrawColumnHeader(g, DrawTitle(g, printStamp, fonts), fonts, pens);
            }
            y = DrawBand(g, BandColor, ag.Key, y, fonts.CellBold);

            foreach (var team in ag.GroupBy(r => r.TeamName ?? string.Empty))
            {
                if (y + RowH * 2 > MaxContentY)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawColumnHeader(g, DrawTitle(g, printStamp, fonts), fonts, pens);
                }
                y = DrawBand(g, TeamBand, "   " + team.Key, y, fonts.Cell);

                foreach (var p in team)
                {
                    if (y + RowH > MaxContentY)
                    {
                        g = doc.Pages.Add().Graphics;
                        y = DrawColumnHeader(g, DrawTitle(g, printStamp, fonts), fonts, pens);
                    }
                    y = DrawPlayerRow(g, p, y, fonts, pens);
                }
            }
        }

        return Save(doc);
    }

    // ── Drawing ─────────────────────────────────────────────────────────

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

    private static float DrawTitle(PdfGraphics g, string printStamp, Fonts fonts)
    {
        g.DrawString("Player Stats — E120 Entry Form", fonts.Title, new PdfSolidBrush(TitleBlue),
            new RectangleF(0, 0, ContentW, 18f), new PdfStringFormat(PdfTextAlignment.Center));
        g.DrawString(printStamp, fonts.Small, new PdfSolidBrush(new PdfColor(110, 110, 110)),
            new RectangleF(0, 20f, ContentW, 12f), new PdfStringFormat(PdfTextAlignment.Center));
        return 38f;
    }

    private static float DrawColumnHeader(PdfGraphics g, float y, Fonts fonts, Pens pens)
    {
        g.DrawRectangle(new PdfSolidBrush(BandColor), new RectangleF(0, y, ContentW, ColHeaderH));
        var x = 0f;
        foreach (var (key, w, _) in Cols)
        {
            g.DrawString(key, fonts.ColHeader, PdfBrushes.Black,
                new RectangleF(x + CellPadX, y, w - (CellPadX * 2), ColHeaderH),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle) { WordWrap = PdfWordWrapType.Word });
            x += w;
        }
        g.DrawLine(pens.Header, new PointF(0, y + ColHeaderH), new PointF(ContentW, y + ColHeaderH));
        return y + ColHeaderH + 1f;
    }

    private static float DrawBand(PdfGraphics g, PdfColor color, string text, float y, PdfStandardFont font)
    {
        g.DrawRectangle(new PdfSolidBrush(color), new RectangleF(0, y, ContentW, RowH));
        g.DrawString(text, font, PdfBrushes.Black,
            new RectangleF(CellPadX, y, ContentW - (CellPadX * 2), RowH),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        return y + RowH;
    }

    private static float DrawPlayerRow(PdfGraphics g, PlayerStatsE120RowDto p, float y, Fonts fonts, Pens pens)
    {
        var name = $"{p.LastName}, {p.FirstName}".Trim().Trim(',').Trim();
        var values = new[]
        {
            p.UniformNo ?? string.Empty,
            name,
            Stat(p.Fastestshot),
            Stat(p.FiveTenFive),
            Stat(p.Fourtyyarddash),
            Stat(p.Threehundredshuttle),
        };

        var x = 0f;
        for (var i = 0; i < Cols.Length; i++)
        {
            var (_, w, align) = Cols[i];
            // Vertical separators on the four stat columns so each reads as a write-in box.
            if (i >= 2)
            {
                g.DrawLine(pens.Divider, new PointF(x, y), new PointF(x, y + RowH));
            }
            if (values[i].Length > 0)
            {
                g.DrawString(values[i], fonts.Cell, PdfBrushes.Black,
                    new RectangleF(x + CellPadX, y, w - (CellPadX * 2), RowH),
                    new PdfStringFormat(align, PdfVerticalAlignment.Middle));
            }
            x += w;
        }
        g.DrawLine(pens.Divider, new PointF(0, y + RowH), new PointF(ContentW, y + RowH));
        return y + RowH;
    }

    private static string Stat(double? v) => v.HasValue ? v.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

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

    private static ReportExportResult Save(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms);
        doc.Close(true);
        return new ReportExportResult { FileBytes = ms.ToArray(), ContentType = "application/pdf", FileName = "PlayerStats_E120.pdf" };
    }

    private sealed class Fonts
    {
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 9);
        public PdfStandardFont CellBold { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
        public PdfStandardFont Label { get; } = new(PdfFontFamily.Helvetica, 9);
        public PdfStandardFont Small { get; } = new(PdfFontFamily.Helvetica, 8);
    }

    private sealed class Pens
    {
        public PdfPen Header { get; } = new(new PdfColor(0, 0, 0), 0.75f);
        public PdfPen Divider { get; } = new(new PdfColor(200, 200, 200), 0.5f);
    }
}
