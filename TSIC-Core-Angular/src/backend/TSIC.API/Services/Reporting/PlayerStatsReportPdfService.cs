using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) E120 player-stats entry form — the EF replacement for the legacy
/// Crystal "PlayerStats_E120" (<c>reporting.PlayerStats_E120</c>). Active Players for the job,
/// grouped agegroup → team; ONE page per team, titled "{agegroup}: {team}" (the teams are named by
/// position — e.g. "2026 Attack" — so each page IS the position group; the proc carries no position
/// column). Each player is a row of blank write-in boxes (a pair per combine stat) under the four
/// stat headers — it is a blank collection form printed at the combine, so values are never
/// pre-filled (matches the legacy).
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
    private const float ContentW = PageW - (MarginX * 2);                 // 554.4
    private const float MaxContentY = PageH - MarginTop - MarginBottom - 2f; // bottom of drawable area

    private const float TitleH = 48f;     // title + gap before the stat headers
    private const float HeaderH = 18f;
    private const float RowH = 30f;
    private const float BoxH = 22f;

    private const float NameW = 150f;     // "#{uniform} Last, First"
    private const float GroupW = (ContentW - NameW) / 4f;  // 101.1 per stat
    private const float BoxPairW = 92f;   // a pair of write-in boxes, centered in the group
    private const float BoxW = BoxPairW / 2f;              // 46

    // Four athletic-combine stats; labels match the legacy abbreviations. Each is a PAIR of blank
    // write-in boxes (the proc carries one value per stat, but the form is printed blank).
    private static readonly string[] StatLabels = { "F-Shot", "5-10-5", "40-YD", "300-YD" };

    public async Task<ReportExportResult> GenerateE120Async(Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetPlayerStatsE120RowsAsync(jobId, cancellationToken);

        var doc = NewDocument();
        var fonts = new Fonts();
        var pens = new Pens();

        if (rows.Count == 0)
        {
            var g0 = doc.Pages.Add().Graphics;
            g0.DrawString("No active players for this job.", fonts.Label, PdfBrushes.Gray,
                new RectangleF(0, 40f, ContentW, 18f), new PdfStringFormat(PdfTextAlignment.Left));
            return Save(doc);
        }

        // One page per team (agegroup → team), titled "{agegroup}: {team}". Order matches the proc
        // (agegroup, team, last, first).
        var teams = rows
            .GroupBy(r => new { Ag = r.AgegroupName ?? string.Empty, Tn = r.TeamName ?? string.Empty })
            .OrderBy(t => t.Key.Ag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Key.Tn, StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            var title = $"{team.Key.Ag}: {team.Key.Tn}".Trim().Trim(':').Trim();
            var g = doc.Pages.Add().Graphics;
            var y = DrawTitle(g, title, fonts);
            y = DrawStatHeaders(g, y, fonts);

            foreach (var p in team
                .OrderBy(x => x.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase))
            {
                if (y + RowH > MaxContentY)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawTitle(g, title, fonts);   // overflow → repeat title + headers
                    y = DrawStatHeaders(g, y, fonts);
                }
                y = DrawPlayerRow(g, p, y, fonts, pens);
            }
        }

        return Save(doc);
    }

    private static PdfDocument NewDocument()
    {
        var doc = new PdfDocument();
        doc.PageSettings.Size = PdfPageSize.Letter;
        doc.PageSettings.Orientation = PdfPageOrientation.Portrait;
        doc.PageSettings.Margins.Left = MarginX;
        doc.PageSettings.Margins.Right = MarginX;
        doc.PageSettings.Margins.Top = MarginTop;
        doc.PageSettings.Margins.Bottom = MarginBottom;
        return doc;
    }

    private static float DrawTitle(PdfGraphics g, string title, Fonts fonts)
    {
        g.DrawString(title, fonts.Title, PdfBrushes.Black,
            new RectangleF(0, 0, ContentW, 22f), new PdfStringFormat(PdfTextAlignment.Center));
        return TitleH;
    }

    // The four stat labels, underlined, centered over each pair of write-in boxes. No name header
    // and no band — matches the legacy.
    private static float DrawStatHeaders(PdfGraphics g, float y, Fonts fonts)
    {
        for (var s = 0; s < StatLabels.Length; s++)
        {
            var pairX = NameW + (s * GroupW) + ((GroupW - BoxPairW) / 2f);
            g.DrawString(StatLabels[s], fonts.Header, PdfBrushes.Black,
                new RectangleF(pairX, y, BoxPairW, HeaderH),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        }
        return y + HeaderH + 6f;
    }

    private static float DrawPlayerRow(PdfGraphics g, PlayerStatsE120RowDto p, float y, Fonts fonts, Pens pens)
    {
        var name = $"{p.LastName}, {p.FirstName}".Trim().Trim(',').Trim();
        // The legacy printed a bare "#" jersey marker, but for un-numbered showcase players the
        // uniform field is empty or the literal string "null" — suppress both so "#null" never shows.
        var uniform = p.UniformNo?.Trim();
        if (string.IsNullOrEmpty(uniform) || string.Equals(uniform, "null", StringComparison.OrdinalIgnoreCase))
        {
            uniform = null;
        }
        var label = uniform is null ? $"# {name}" : $"#{uniform} {name}";
        g.DrawString(label, fonts.Cell, PdfBrushes.Black,
            new RectangleF(0, y, NameW - 4f, RowH),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));

        var boxY = y + ((RowH - BoxH) / 2f);
        for (var s = 0; s < StatLabels.Length; s++)
        {
            var pairX = NameW + (s * GroupW) + ((GroupW - BoxPairW) / 2f);
            // A pair of adjacent blank write-in boxes per stat.
            g.DrawRectangle(pens.Box, new RectangleF(pairX, boxY, BoxW, BoxH));
            g.DrawRectangle(pens.Box, new RectangleF(pairX + BoxW, boxY, BoxW, BoxH));
        }
        return y + RowH;
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
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 15, PdfFontStyle.Bold);
        public PdfStandardFont Header { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Underline);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 9);
        public PdfStandardFont Label { get; } = new(PdfFontFamily.Helvetica, 9);
    }

    private sealed class Pens
    {
        public PdfPen Box { get; } = new(new PdfColor(0, 0, 0), 0.75f);
    }
}
