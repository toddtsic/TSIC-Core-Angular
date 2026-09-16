using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the American Select Evaluation report — a per-team
/// evaluator scoring sheet (identity + five blank write-in score boxes + mom contact, grouped by
/// tryout team then position). EF data comes from
/// <see cref="IReportingRepository.GetAmericanSelectEvaluationRowsAsync"/>.
/// (Main Event Rosters are served by the shared PackedRoster engine — the offer-team rosters are
/// just a packed roster — so there's no bespoke renderer for them here.)
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

    // ── Evaluation: per-team evaluator scoring sheet (portrait) ─────────
    // Grouped by tryout team (page break per team, team name = page subtitle) then by position,
    // with five blank write-in scoring boxes per player. Matches the legacy Crystal layout.

    private const float EvalContentW = 612f - (MarginX * 2);            // 554.4 (portrait)
    private const float EvalMaxY = 792f - MarginBottom - MarginTop - FooterH - 2f;
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

    // ── Tryout Check-In: per-team check-off sheet (portrait) ────────────
    // One section per tryout team (page break per team), "{Job}:{GradYear} Tryout Players" as the
    // title on every page, players by name, a blank check-off box leading each row. Same dataset
    // as the Evaluation sheet. Matches the legacy Crystal "AmericanSelectTournyCheckin" layout.

    private const float CheckinTitleH = 26f;
    private const float CheckinColHeaderH = 16f;
    private const float CheckinRowH = 31f;          // tall rows — room to tick; 21 fit a page
    private const float CheckinCellPadY = 4f;

    // Box, #, GradYr, Position, Player, Club, School, Mom. Sum == EvalContentW (portrait).
    private static readonly (string Label, float W, PdfTextAlignment Align)[] CheckinCols =
    {
        ("",         28f,    PdfTextAlignment.Left),
        ("#",        30f,    PdfTextAlignment.Center),
        ("GradYr",   40f,    PdfTextAlignment.Left),
        ("Position", 44f,    PdfTextAlignment.Left),
        ("Player",   92f,    PdfTextAlignment.Left),
        ("Club",     92f,    PdfTextAlignment.Left),
        ("School",   102f,   PdfTextAlignment.Left),
        ("Mom",      126.4f, PdfTextAlignment.Left),
    };

    public async Task<ReportExportResult> GenerateTournyCheckinAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetAmericanSelectEvaluationRowsAsync(jobId, cancellationToken);

        var doc = NewDocument(landscape: false);
        var fonts = new Fonts();
        var pens = new Pens();
        var jobName = rows.FirstOrDefault()?.JobName ?? string.Empty;

        if (rows.Count == 0)
        {
            var g0 = doc.Pages.Add().Graphics;
            g0.DrawString("No tryout players for this job.", fonts.Label, PdfBrushes.Gray,
                new RectangleF(0, 0, EvalContentW, 18f), new PdfStringFormat(PdfTextAlignment.Left));
            return Save(doc, "AmericanSelectTryoutCheckin.pdf");
        }

        // One section per tryout team. The title's grad year is the team's most common player grad
        // year (tryout teams are one grad year each; a stray mis-registered player can't retitle it).
        var teams = rows
            .GroupBy(r => r.TeamName ?? string.Empty)
            .Select(t => new
            {
                Rows = t.ToList(),
                GradYear = t.Where(r => !string.IsNullOrWhiteSpace(r.GradYear))
                    .GroupBy(r => r.GradYear!.Trim())
                    .OrderByDescending(gy => gy.Count())
                    .Select(gy => gy.Key)
                    .FirstOrDefault(),
                TeamName = t.Key,
            })
            .OrderBy(t => t.GradYear ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            var title = $"{jobName}:{team.GradYear ?? team.TeamName} Tryout Players";
            var g = doc.Pages.Add().Graphics;
            var y = DrawCheckinPageHeader(g, title, fonts);

            foreach (var p in team.Rows
                .OrderBy(x => x.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase))
            {
                var cells = CheckinCells(p);
                var rowH = MeasureCheckinRow(cells, fonts);
                if (y + rowH > EvalMaxY)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawCheckinPageHeader(g, title, fonts);
                }
                y = DrawCheckinRow(g, cells, y, rowH, fonts, pens);
            }
        }

        return Save(doc, "AmericanSelectTryoutCheckin.pdf");
    }

    // Repeating page header: centered underlined title + underlined column labels.
    private static float DrawCheckinPageHeader(PdfGraphics g, string title, Fonts fonts)
    {
        g.DrawString(title, fonts.CheckinTitle, PdfBrushes.Black,
            new RectangleF(0, 0, EvalContentW, CheckinTitleH),
            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Top));

        var y = CheckinTitleH;
        var x = 0f;
        foreach (var (label, w, align) in CheckinCols)
        {
            if (label.Length > 0)
            {
                g.DrawString(label, fonts.CheckinColHeader, PdfBrushes.Black,
                    new RectangleF(x + CellPadX, y, w - (CellPadX * 2), CheckinColHeaderH),
                    new PdfStringFormat(align, PdfVerticalAlignment.Middle));
            }
            x += w;
        }
        return y + CheckinColHeaderH;
    }

    // Cell text per column; index 0 (the check-off box) is always empty.
    private static string[] CheckinCells(AmericanSelectEvaluationRowDto p)
    {
        var name = $"{p.LastName?.Trim()}, {p.FirstName?.Trim()}".Trim().Trim(',').Trim();
        var mom = string.Join(" ", new[]
        {
            $"{p.MomFirstName?.Trim()} {p.MomLastName?.Trim()}".Trim(),
            FormatPhone(p.MomCellphone),
        }.Where(s => s.Length > 0));

        return new[]
        {
            string.Empty,
            FormatUniform(p.UniformNo?.Trim()),
            p.GradYear?.Trim() ?? string.Empty,
            p.Position?.Trim() ?? string.Empty,
            name,
            p.ClubTeamName?.Trim() ?? string.Empty,
            p.SchoolName?.Trim() ?? string.Empty,
            mom,
        };
    }

    private static float MeasureCheckinRow(string[] cells, Fonts fonts)
    {
        var rowH = CheckinRowH;
        for (var i = 1; i < CheckinCols.Length; i++)
        {
            if (cells[i].Length == 0)
            {
                continue;
            }
            var (_, w, align) = CheckinCols[i];
            var sz = fonts.EvalCell.MeasureString(cells[i], w - (CellPadX * 2), CheckinWrap(align));
            rowH = Math.Max(rowH, sz.Height + (CheckinCellPadY * 2));
        }
        return rowH;
    }

    private static float DrawCheckinRow(PdfGraphics g, string[] cells, float y, float rowH, Fonts fonts, Pens pens)
    {
        // Blank check-off box, aligned with the first text line.
        g.DrawRectangle(pens.Box, new RectangleF(2f, y + CheckinCellPadY - 1f, CheckinCols[0].W - 6f, 11f));

        var x = CheckinCols[0].W;
        for (var i = 1; i < CheckinCols.Length; i++)
        {
            var (_, w, align) = CheckinCols[i];
            if (cells[i].Length > 0)
            {
                g.DrawString(cells[i], fonts.EvalCell, PdfBrushes.Black,
                    new RectangleF(x + CellPadX, y + CheckinCellPadY, w - (CellPadX * 2), rowH - CheckinCellPadY),
                    CheckinWrap(align));
            }
            x += w;
        }
        g.DrawLine(pens.Header, new PointF(0, y + rowH), new PointF(EvalContentW, y + rowH));
        return y + rowH;
    }

    // LineLimit=false: with it on, a line whose measured height exceeds the rect is silently dropped.
    private static PdfStringFormat CheckinWrap(PdfTextAlignment align) =>
        new(align, PdfVerticalAlignment.Top) { WordWrap = PdfWordWrapType.Word, LineLimit = false };

    private static string FormatPhone(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length == 10
            ? $"{digits[..3]}-{digits.Substring(3, 3)}-{digits[6..]}"
            : (phone ?? string.Empty).Trim();
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
        AddFooterTemplate(doc, EvalContentW);
        return doc;
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
        public PdfStandardFont Label { get; } = new(PdfFontFamily.Helvetica, 9);

        // Evaluation scoring sheet.
        public PdfStandardFont EvalTitle { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        public PdfStandardFont EvalSubtitle { get; } = new(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold | PdfFontStyle.Italic | PdfFontStyle.Underline);
        public PdfStandardFont EvalEvaluator { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold | PdfFontStyle.Italic);
        public PdfStandardFont EvalColHeader { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold | PdfFontStyle.Underline);
        public PdfStandardFont EvalPos { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
        public PdfStandardFont EvalCell { get; } = new(PdfFontFamily.Helvetica, 9);

        // Tryout check-in sheet (cells reuse EvalCell).
        public PdfStandardFont CheckinTitle { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold | PdfFontStyle.Underline);
        public PdfStandardFont CheckinColHeader { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Underline);
    }

    private sealed class Pens
    {
        public PdfPen Header { get; } = new(new PdfColor(0, 0, 0), 0.75f);
        public PdfPen Divider { get; } = new(new PdfColor(200, 200, 200), 0.5f);
        public PdfPen Box { get; } = new(new PdfColor(0, 0, 0), 0.75f);
    }
}
