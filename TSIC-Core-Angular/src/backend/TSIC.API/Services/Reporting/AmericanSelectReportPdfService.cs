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

    private static readonly PdfColor TeamBand = new(238, 238, 238);
    private static readonly PdfColor TitleBlue = new(0, 0, 160);

    // ── Evaluation: per-team evaluator scoring sheet (portrait) ─────────
    // Grouped by tryout team (page break per team, team name = page subtitle) then by position,
    // with five blank write-in scoring boxes per player. Matches the legacy Crystal layout.

    private const float EvalContentW = 612f - (MarginX * 2);            // 554.4 (portrait)
    private const float EvalMaxY = 792f - MarginBottom - MarginTop - FooterH - 2f;
    private const float EvalHeaderH = 24f;          // column-header height (also reused by Main Event)
    private const float EvalRowH = 33f;             // tall rows — score columns are write-in boxes
    private const float EvalPosH = 17f;             // position group-header row

    // #, Position, Player, then five blank scoring boxes (cols 3..7). Sum == EvalContentW.
    private const int EvalFirstBoxCol = 3;
    private static readonly (string Key, float W, PdfTextAlignment Align)[] EvalCols =
    {
        ("#",           30f,    PdfTextAlignment.Right),
        ("Position",    58f,    PdfTextAlignment.Left),
        ("Player",      104f,   PdfTextAlignment.Left),
        ("Physical",    60f,    PdfTextAlignment.Center),
        ("PsnSpecific", 66f,    PdfTextAlignment.Center),
        ("StickSkills", 62f,    PdfTextAlignment.Center),
        ("Notes",       110f,   PdfTextAlignment.Center),
        ("Total",       64.4f,  PdfTextAlignment.Center),
    };

    public async Task<ReportExportResult> GenerateEvaluationAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetAmericanSelectEvaluationRowsAsync(jobId, cancellationToken);

        var doc = NewDocument(landscape: false);
        var fonts = new Fonts();
        var pens = new Pens();
        var jobName = rows.FirstOrDefault()?.JobName ?? string.Empty;

        if (rows.Count == 0)
        {
            var g0 = doc.Pages.Add().Graphics;
            DrawEvalPageHeader(g0, jobName, "—", fonts, pens);
            g0.DrawString("No tryout players for this job.", fonts.Label, PdfBrushes.Gray,
                new RectangleF(0, 80f, EvalContentW, 18f), new PdfStringFormat(PdfTextAlignment.Left));
            return Save(doc, "AmericanSelectEvaluation.pdf");
        }

        // One section per tryout team — page break per team, team name is the page subtitle.
        var teams = rows
            .GroupBy(r => r.TeamName ?? string.Empty)
            .OrderBy(t => t.First().AgegroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            var g = doc.Pages.Add().Graphics;
            var y = DrawEvalPageHeader(g, jobName, team.Key, fonts, pens);

            // Group by position (alphabetical: attack, defense, goalie, midfield).
            foreach (var pos in team
                .GroupBy(r => r.Position ?? string.Empty)
                .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (y + EvalPosH + EvalRowH > EvalMaxY)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawEvalPageHeader(g, jobName, team.Key, fonts, pens);
                }
                y = DrawEvalPositionHeader(g, pos.Key, y, fonts);

                foreach (var p in pos
                    .OrderBy(x => UniformSort(x.UniformNo))
                    .ThenBy(x => x.LastName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase))
                {
                    if (y + EvalRowH > EvalMaxY)
                    {
                        g = doc.Pages.Add().Graphics;
                        y = DrawEvalPageHeader(g, jobName, team.Key, fonts, pens);
                    }
                    y = DrawEvalScoreRow(g, p, y, fonts, pens);
                }
            }
        }

        return Save(doc, "AmericanSelectEvaluation.pdf");
    }

    // Repeating page header: "{Job} Evaluations" (left) + "Evaluator:" write-in (right) +
    // centered team subtitle + the underlined column-header row. Returns y after the headers.
    private static float DrawEvalPageHeader(PdfGraphics g, string jobName, string teamName, Fonts fonts, Pens pens)
    {
        g.DrawString($"{jobName} Evaluations", fonts.EvalTitle, PdfBrushes.Black,
            new RectangleF(0, 0, EvalContentW - 150f, 20f),
            new PdfStringFormat(PdfTextAlignment.Left));
        g.DrawString("Evaluator:", fonts.EvalEvaluator, PdfBrushes.Black,
            new RectangleF(EvalContentW - 150f, 0, 150f, 12f),
            new PdfStringFormat(PdfTextAlignment.Right));
        g.DrawLine(pens.Header, new PointF(EvalContentW - 150f, 22f), new PointF(EvalContentW, 22f));
        g.DrawString(teamName, fonts.EvalSubtitle, PdfBrushes.Black,
            new RectangleF(0, 22f, EvalContentW, 16f),
            new PdfStringFormat(PdfTextAlignment.Center));

        var y = 44f;
        var x = 0f;
        foreach (var (key, w, align) in EvalCols)
        {
            g.DrawString(key, fonts.EvalColHeader, PdfBrushes.Black,
                new RectangleF(x + CellPadX, y, w - (CellPadX * 2), 14f),
                new PdfStringFormat(align, PdfVerticalAlignment.Middle));
            x += w;
        }
        return y + 16f;
    }

    private static float DrawEvalPositionHeader(PdfGraphics g, string position, float y, Fonts fonts)
    {
        g.DrawString(position, fonts.EvalPos, PdfBrushes.Black,
            new RectangleF(CellPadX, y, EvalContentW - (CellPadX * 2), EvalPosH),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        return y + EvalPosH;
    }

    private static float DrawEvalScoreRow(PdfGraphics g, AmericanSelectEvaluationRowDto p, float y, Fonts fonts, Pens pens)
    {
        var name = $"{p.LastName}, {p.FirstName}".Trim().Trim(',').Trim();
        var text = new[] { FormatUniform(p.UniformNo), p.Position ?? string.Empty, name };

        var x = 0f;
        for (var i = 0; i < EvalCols.Length; i++)
        {
            var (_, w, align) = EvalCols[i];
            if (i < EvalFirstBoxCol)
            {
                if (text[i].Length > 0)
                {
                    g.DrawString(text[i], fonts.EvalCell, PdfBrushes.Black,
                        new RectangleF(x + CellPadX, y, w - (CellPadX * 2), EvalRowH),
                        new PdfStringFormat(align, PdfVerticalAlignment.Middle));
                }
            }
            else
            {
                // Blank write-in scoring box, vertically centered in the row.
                const float boxH = 22f;
                g.DrawRectangle(pens.Box, new RectangleF(x + 3f, y + ((EvalRowH - boxH) / 2f), w - 6f, boxH));
            }
            x += w;
        }
        g.DrawLine(pens.Divider, new PointF(0, y + EvalRowH), new PointF(EvalContentW, y + EvalRowH));
        return y + EvalRowH;
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

    private static void AddFooterTemplate(PdfDocument document, float contentW)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8);
        var bold = new PdfStandardFont(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        var gray = new PdfSolidBrush(new PdfColor(90, 90, 90));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, contentW, FooterH));

        // Left: Page X of Y.
        var page = new PdfCompositeField(
            footerFont, gray, "Page {0} of {1}",
            new PdfPageNumberField(footerFont, gray),
            new PdfPageCountField(footerFont, gray))
        {
            Bounds = new RectangleF(0, 4, 150f, FooterH),
            StringFormat = new PdfStringFormat(PdfTextAlignment.Left),
        };
        page.Draw(footer.Graphics, new PointF(0, 4));

        // Center: brand.
        footer.Graphics.DrawString("Reports by TeamSportsInfo.com", bold, gray,
            new RectangleF(0, 4, contentW, 12f), new PdfStringFormat(PdfTextAlignment.Center));

        // Right: print date + time (matches the legacy Crystal footer stamp).
        var stamp = DateTime.Now.ToString("M/d/yyyy    h:mm:sstt", CultureInfo.InvariantCulture);
        footer.Graphics.DrawString(stamp, footerFont, gray,
            new RectangleF(contentW - 180f, 4, 180f, 12f), new PdfStringFormat(PdfTextAlignment.Right));

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

        // Evaluation scoring sheet.
        public PdfStandardFont EvalTitle { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        public PdfStandardFont EvalSubtitle { get; } = new(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold | PdfFontStyle.Italic | PdfFontStyle.Underline);
        public PdfStandardFont EvalEvaluator { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold | PdfFontStyle.Italic);
        public PdfStandardFont EvalColHeader { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold | PdfFontStyle.Underline);
        public PdfStandardFont EvalPos { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
        public PdfStandardFont EvalCell { get; } = new(PdfFontFamily.Helvetica, 9);
    }

    private sealed class Pens
    {
        public PdfPen Header { get; } = new(new PdfColor(0, 0, 0), 0.75f);
        public PdfPen Divider { get; } = new(new PdfColor(200, 200, 200), 0.5f);
        public PdfPen Box { get; } = new(new PdfColor(0, 0, 0), 0.75f);
    }
}
