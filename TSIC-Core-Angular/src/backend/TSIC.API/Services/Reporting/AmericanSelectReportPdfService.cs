using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the two American Select reports. Evaluation is a landscape
/// tryout sheet (check-in box + identity + mom contact, grouped by agegroup); Main Event Rosters are
/// portrait per-team roster cards (grouped agegroup → team). EF data comes from
/// <see cref="IReportingRepository.GetAmericanSelectEvaluationRowsAsync"/> and
/// <see cref="IReportingRepository.GetAmericanSelectMainEventRosterRowsAsync"/> (the latter the flat
/// replacement for the master-detail proc pair).
/// </summary>
public sealed class AmericanSelectReportPdfService : IAmericanSelectReportPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public AmericanSelectReportPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float FooterH = 18f;
    private const float CellPadX = 4f;
    private const float RowH = 16f;

    private static readonly PdfColor BandColor = new(222, 222, 222);
    private static readonly PdfColor TeamBand = new(238, 238, 238);
    private static readonly PdfColor TitleBlue = new(0, 0, 160);

    // ── Evaluation (landscape) ──────────────────────────────────────────

    private const float EvalContentW = 792f - (MarginX * 2);            // 734.4
    private const float EvalMaxY = 612f - MarginBottom - MarginTop - FooterH - 2f;
    private const float EvalHeaderH = 24f;

    private static readonly (string Key, float W, PdfTextAlignment Align)[] EvalCols =
    {
        ("Check In",   48f,    PdfTextAlignment.Center),
        ("Tryout #",   42f,    PdfTextAlignment.Center),
        ("Player",     120f,   PdfTextAlignment.Left),
        ("Team",       96f,    PdfTextAlignment.Left),
        ("Grad",       34f,    PdfTextAlignment.Center),
        ("Position",   56f,    PdfTextAlignment.Left),
        ("Club",       100f,   PdfTextAlignment.Left),
        ("School",     100f,   PdfTextAlignment.Left),
        ("Mom Contact", 138.4f, PdfTextAlignment.Left),
    };

    public async Task<ReportExportResult> GenerateEvaluationAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetAmericanSelectEvaluationRowsAsync(jobId, cancellationToken);

        var doc = NewDocument(landscape: true);
        var fonts = new Fonts();
        var pens = new Pens();
        var jobName = rows.FirstOrDefault()?.JobName;
        var printStamp = "Print Date: " + DateTime.Now.ToString("M/d/yyyy  h:mm tt", CultureInfo.InvariantCulture);

        var g = doc.Pages.Add().Graphics;
        var y = DrawTitle(g, EvalContentW, "American Select — Player Evaluation", jobName, printStamp, fonts);

        if (rows.Count == 0)
        {
            g.DrawString("No tryout players for this job.", fonts.Label, PdfBrushes.Gray,
                new RectangleF(0, y + 8f, EvalContentW, 18f), new PdfStringFormat(PdfTextAlignment.Left));
            return Save(doc, "AmericanSelectEvaluation.pdf");
        }

        y = DrawEvalHeader(g, y, fonts, pens);

        foreach (var ag in rows.GroupBy(r => r.AgegroupName ?? string.Empty))
        {
            if (y + RowH * 2 > EvalMaxY)
            {
                g = doc.Pages.Add().Graphics;
                y = DrawEvalHeader(g, DrawTitle(g, EvalContentW, "American Select — Player Evaluation", jobName, printStamp, fonts), fonts, pens);
            }
            y = DrawBand(g, EvalContentW, BandColor, ag.Key, y, fonts.CellBold);

            foreach (var p in ag)
            {
                if (y + RowH > EvalMaxY)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawEvalHeader(g, DrawTitle(g, EvalContentW, "American Select — Player Evaluation", jobName, printStamp, fonts), fonts, pens);
                }
                y = DrawEvalRow(g, p, y, fonts, pens);
            }
        }

        return Save(doc, "AmericanSelectEvaluation.pdf");
    }

    private static float DrawEvalHeader(PdfGraphics g, float y, Fonts fonts, Pens pens)
    {
        g.DrawRectangle(new PdfSolidBrush(BandColor), new RectangleF(0, y, EvalContentW, EvalHeaderH));
        var x = 0f;
        foreach (var (key, w, align) in EvalCols)
        {
            g.DrawString(key, fonts.ColHeader, PdfBrushes.Black,
                new RectangleF(x + CellPadX, y, w - (CellPadX * 2), EvalHeaderH),
                new PdfStringFormat(align, PdfVerticalAlignment.Middle) { WordWrap = PdfWordWrapType.Word });
            x += w;
        }
        g.DrawLine(pens.Header, new PointF(0, y + EvalHeaderH), new PointF(EvalContentW, y + EvalHeaderH));
        return y + EvalHeaderH + 1f;
    }

    private static float DrawEvalRow(PdfGraphics g, AmericanSelectEvaluationRowDto p, float y, Fonts fonts, Pens pens)
    {
        var mom = string.Join(" ",
            new[] { $"{p.MomFirstName} {p.MomLastName}".Trim(), FormatPhone(p.MomCellphone) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var values = new[]
        {
            string.Empty,                                  // Check In — blank box
            p.UniformNo ?? string.Empty,
            $"{p.LastName}, {p.FirstName}".Trim().Trim(',').Trim(),
            p.TeamName ?? string.Empty,
            p.GradYear ?? string.Empty,
            p.Position ?? string.Empty,
            p.ClubTeamName ?? string.Empty,
            p.SchoolName ?? string.Empty,
            mom,
        };

        var x = 0f;
        for (var i = 0; i < EvalCols.Length; i++)
        {
            var (_, w, align) = EvalCols[i];
            if (i > 0)
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
        g.DrawLine(pens.Divider, new PointF(0, y + RowH), new PointF(EvalContentW, y + RowH));
        return y + RowH;
    }

    // ── Main Event Rosters (portrait, per-team cards) ───────────────────

    private const float MainContentW = 612f - (MarginX * 2);            // 554.4
    private const float MainMaxY = 792f - MarginBottom - MarginTop - FooterH - 2f;

    private static readonly (string Key, float W, PdfTextAlignment Align)[] MainCols =
    {
        ("#",        40f,    PdfTextAlignment.Center),
        ("Player",   160f,   PdfTextAlignment.Left),
        ("Position", 90f,    PdfTextAlignment.Left),
        ("School",   150f,   PdfTextAlignment.Left),
        ("Hometown", 114.4f, PdfTextAlignment.Left),
    };

    public async Task<ReportExportResult> GenerateMainEventRostersAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetAmericanSelectMainEventRosterRowsAsync(jobId, cancellationToken);

        var doc = NewDocument(landscape: false);
        var fonts = new Fonts();
        var pens = new Pens();
        var jobName = rows.FirstOrDefault()?.JobName;
        var printStamp = "Print Date: " + DateTime.Now.ToString("M/d/yyyy  h:mm tt", CultureInfo.InvariantCulture);

        var g = doc.Pages.Add().Graphics;
        var y = DrawTitle(g, MainContentW, "American Select — Main Event Rosters", jobName, printStamp, fonts);

        if (rows.Count == 0)
        {
            g.DrawString("No main-event players for this job.", fonts.Label, PdfBrushes.Gray,
                new RectangleF(0, y + 8f, MainContentW, 18f), new PdfStringFormat(PdfTextAlignment.Left));
            return Save(doc, "AmericanSelectMainEventRosters.pdf");
        }

        // Group agegroup → team; one roster card per team (kept together where it fits).
        foreach (var team in rows
            .GroupBy(r => new { Ag = r.AgegroupName ?? string.Empty, Tid = r.TeamId, Tn = r.TeamName ?? string.Empty })
            .OrderBy(t => t.Key.Ag, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Key.Tn, StringComparer.OrdinalIgnoreCase))
        {
            var players = team
                .OrderBy(p => UniformSort(p.UniformNo))
                .ThenBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cardH = RowH + EvalHeaderH + (players.Count * RowH) + 8f;
            if (y > MarginTop && y + Math.Min(cardH, RowH * 4) > MainMaxY)
            {
                g = doc.Pages.Add().Graphics;
                y = DrawTitle(g, MainContentW, "American Select — Main Event Rosters", jobName, printStamp, fonts);
            }

            y = DrawBand(g, MainContentW, TeamBand, $"{team.Key.Ag}  —  {team.Key.Tn}", y, fonts.CellBold);
            y = DrawMainHeader(g, y, fonts, pens);

            foreach (var p in players)
            {
                if (y + RowH > MainMaxY)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawTitle(g, MainContentW, "American Select — Main Event Rosters", jobName, printStamp, fonts);
                    y = DrawBand(g, MainContentW, TeamBand, $"{team.Key.Ag}  —  {team.Key.Tn} (cont.)", y, fonts.CellBold);
                    y = DrawMainHeader(g, y, fonts, pens);
                }
                y = DrawMainRow(g, p, y, fonts, pens);
            }
            y += 10f;
        }

        return Save(doc, "AmericanSelectMainEventRosters.pdf");
    }

    private static float DrawMainHeader(PdfGraphics g, float y, Fonts fonts, Pens pens)
    {
        var x = 0f;
        foreach (var (key, w, align) in MainCols)
        {
            g.DrawString(key, fonts.ColHeader, PdfBrushes.Black,
                new RectangleF(x + CellPadX, y, w - (CellPadX * 2), EvalHeaderH),
                new PdfStringFormat(align, PdfVerticalAlignment.Bottom));
            x += w;
        }
        g.DrawLine(pens.Header, new PointF(0, y + EvalHeaderH), new PointF(MainContentW, y + EvalHeaderH));
        return y + EvalHeaderH + 1f;
    }

    private static float DrawMainRow(PdfGraphics g, AmericanSelectMainEventRosterRowDto p, float y, Fonts fonts, Pens pens)
    {
        var values = new[]
        {
            FormatUniform(p.UniformNo),
            $"{p.LastName}, {p.FirstName}".Trim().Trim(',').Trim(),
            p.Position ?? string.Empty,
            p.SchoolName ?? string.Empty,
            p.Hometown ?? string.Empty,
        };
        var x = 0f;
        for (var i = 0; i < MainCols.Length; i++)
        {
            var (_, w, align) = MainCols[i];
            if (values[i].Length > 0)
            {
                g.DrawString(values[i], fonts.Cell, PdfBrushes.Black,
                    new RectangleF(x + CellPadX, y, w - (CellPadX * 2), RowH),
                    new PdfStringFormat(align, PdfVerticalAlignment.Middle));
            }
            x += w;
        }
        g.DrawLine(pens.Divider, new PointF(0, y + RowH), new PointF(MainContentW, y + RowH));
        return y + RowH;
    }

    // ── Shared helpers ──────────────────────────────────────────────────

    private static PdfDocument NewDocument(bool landscape)
    {
        var doc = new PdfDocument();
        doc.PageSettings.Size = PdfPageSize.Letter;
        doc.PageSettings.Orientation = landscape ? PdfPageOrientation.Landscape : PdfPageOrientation.Portrait;
        doc.PageSettings.Margins.Left = MarginX;
        doc.PageSettings.Margins.Right = MarginX;
        doc.PageSettings.Margins.Top = MarginTop;
        doc.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(doc, landscape ? EvalContentW : MainContentW);
        return doc;
    }

    private static float DrawTitle(PdfGraphics g, float contentW, string title, string? jobName, string printStamp, Fonts fonts)
    {
        g.DrawString(title, fonts.Title, new PdfSolidBrush(TitleBlue),
            new RectangleF(0, 0, contentW, 18f), new PdfStringFormat(PdfTextAlignment.Center));
        if (!string.IsNullOrWhiteSpace(jobName))
        {
            g.DrawString(jobName, fonts.Subtitle, PdfBrushes.Black,
                new RectangleF(0, 19f, contentW, 13f), new PdfStringFormat(PdfTextAlignment.Center));
        }
        g.DrawString(printStamp, fonts.Small, new PdfSolidBrush(new PdfColor(110, 110, 110)),
            new RectangleF(0, 33f, contentW, 12f), new PdfStringFormat(PdfTextAlignment.Center));
        return 50f;
    }

    private static float DrawBand(PdfGraphics g, float contentW, PdfColor color, string text, float y, PdfStandardFont font)
    {
        g.DrawRectangle(new PdfSolidBrush(color), new RectangleF(0, y, contentW, RowH));
        g.DrawString(text, font, PdfBrushes.Black,
            new RectangleF(CellPadX, y, contentW - (CellPadX * 2), RowH),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        return y + RowH;
    }

    // Numeric uniforms sort ahead of non-numeric/blank; matches the proc's IsNumeric ordering intent.
    private static int UniformSort(string? u) =>
        int.TryParse(u, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;

    private static string FormatUniform(string? u) =>
        int.TryParse(u, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n.ToString(CultureInfo.InvariantCulture) : (u ?? string.Empty);

    // Mirrors the proc's SUBSTRING formatting for a 10-digit cell; otherwise returns the raw value.
    private static string FormatPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 10
            ? $"{digits[..3]}-{digits.Substring(3, 3)}-{digits.Substring(6, 4)}"
            : raw.Trim();
    }

    private static void AddFooterTemplate(PdfDocument document, float contentW)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var gray = new PdfSolidBrush(new PdfColor(102, 102, 102));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, contentW, FooterH));
        footer.Graphics.DrawString("Reports By TeamSportsInfo.com", footerFont, gray, new PointF(2, 4));
        var composite = new PdfCompositeField(
            footerFont, gray, "Page {0} / {1}",
            new PdfPageNumberField(footerFont, gray),
            new PdfPageCountField(footerFont, gray))
        {
            Bounds = new RectangleF(0, 4, contentW - 2, FooterH),
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

    private sealed class Fonts
    {
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        public PdfStandardFont Subtitle { get; } = new(PdfFontFamily.Helvetica, 10, PdfFontStyle.Bold);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 8);
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
