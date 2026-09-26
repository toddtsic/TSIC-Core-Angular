using System.Globalization;
using System.Text.RegularExpressions;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Renders the Collegiate Coach Master Schedule — a print-ready date×field grid for college coaches,
/// operations staff and parking staff. One 18"×12" landscape page per day, ½" margins, the grid
/// centered on the page and scaled down (never cropped) when a day is too big to fit.
/// <list type="bullet">
/// <item>Header row: the day's date (MM/dd/yyyy) in the corner cell, then field names.</item>
/// <item>Time column + header row: larger bold ALL CAPS, bordered like the game cells.</item>
/// <item>Game cell: TEAM 1 / VS / TEAM 2, ALL CAPS, centered both ways, on the agegroup color with
/// the same black/white contrast pick as the grid. No agegroup/pool line, no scores, no referees.</item>
/// <item>The stored "club:team" colon prints as a space.</item>
/// <item>Fields with no games that day are omitted from that day's page.</item>
/// </list>
/// Additive to the Excel exports — those are unchanged.
/// </summary>
internal static class CoachMasterSchedulePdfRenderer
{
    // 18in × 12in at 72pt/in, ½" margins.
    private const float PageW = 1296f, PageH = 864f, Margin = 36f;

    private const float TimeColW = 90f;
    private const float MinFieldColW = 100f;     // below this the grid scales down instead
    private const float HeaderRowH = 34f;
    private const float CellPad = 5f;
    private const float MinRowH = 44f;
    private const float MaxRowGrow = 1.5f;       // short days stretch rows to fill the page, up to this

    private static readonly PdfStringFormat Centered = new(PdfTextAlignment.Center, PdfVerticalAlignment.Middle)
    {
        WordWrap = PdfWordWrapType.Word,
    };

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static byte[] Render(MasterScheduleResponse data)
    {
        var doc = new PdfDocument();
        doc.PageSettings.Orientation = PdfPageOrientation.Landscape;
        doc.PageSettings.Size = new SizeF(PageW, PageH);
        doc.PageSettings.Margins.All = Margin;

        var fonts = new Fonts();
        var pens = new Pens();

        if (data.Days.Count == 0)
        {
            var g = doc.Pages.Add().Graphics;
            g.DrawString("NO SCHEDULED GAMES FOR THIS EVENT.", fonts.Header, PdfBrushes.Gray,
                new RectangleF(0, 0, PageW - (Margin * 2), HeaderRowH), Centered);
            return Save(doc);
        }

        foreach (var day in data.Days)
        {
            var page = doc.Pages.Add();
            DrawDay(page, day, data.FieldColumns, fonts, pens);
        }

        return Save(doc);
    }

    private static void DrawDay(PdfPage page, MasterScheduleDay day, List<string> fieldColumns, Fonts fonts, Pens pens)
    {
        var clientW = PageW - (Margin * 2);
        var clientH = PageH - (Margin * 2);

        // Only the fields that host a game this day.
        var cols = Enumerable.Range(0, fieldColumns.Count)
            .Where(c => day.Rows.Any(r => c < r.Cells.Count && r.Cells[c] != null))
            .ToList();
        if (cols.Count == 0) return;

        // Columns fill the page width, but never narrower than MinFieldColW (then the grid scales).
        var fieldColW = Math.Max(MinFieldColW, (clientW - TimeColW) / cols.Count);
        var gridW = TimeColW + (fieldColW * cols.Count);
        var textW = fieldColW - (CellPad * 2);

        // Natural row heights: tallest cell text in the row, wrapped to the column width.
        var rowHeights = day.Rows.Select(row =>
        {
            var h = MinRowH;
            foreach (var c in cols)
            {
                var cell = c < row.Cells.Count ? row.Cells[c] : null;
                if (cell == null) continue;
                var size = fonts.Cell.MeasureString(CellText(cell), textW, Centered);
                h = Math.Max(h, size.Height + (CellPad * 2));
            }
            return h;
        }).ToList();

        var gridH = HeaderRowH + rowHeights.Sum();

        // Short day: stretch data rows toward the page height so the print is not a strip in the middle.
        var fitScale = clientW / gridW;
        var spare = (clientH / Math.Min(1f, fitScale)) - gridH;
        if (spare > 0 && rowHeights.Count > 0)
        {
            var grow = Math.Min(MaxRowGrow, 1f + (spare / rowHeights.Sum()));
            rowHeights = rowHeights.Select(h => h * grow).ToList();
            gridH = HeaderRowH + rowHeights.Sum();
        }

        // Draw at natural size into a template, then place it scaled-to-fit and centered.
        var tpl = new PdfTemplate(gridW, gridH);
        var g = tpl.Graphics;

        // Header row — date in the corner, then field names.
        var dateLabel = day.Rows.Count > 0
            ? day.Rows[0].SortKey.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)
            : day.ShortLabel.ToUpperInvariant();
        DrawHeaderCell(g, dateLabel, new RectangleF(0, 0, TimeColW, HeaderRowH), fonts, pens);
        for (var i = 0; i < cols.Count; i++)
        {
            DrawHeaderCell(g, Caps(fieldColumns[cols[i]]),
                new RectangleF(TimeColW + (i * fieldColW), 0, fieldColW, HeaderRowH), fonts, pens);
        }

        // Data rows.
        var y = HeaderRowH;
        for (var r = 0; r < day.Rows.Count; r++)
        {
            var row = day.Rows[r];
            var h = rowHeights[r];

            var timeRect = new RectangleF(0, y, TimeColW, h);
            g.DrawRectangle(pens.Grid, PdfBrushes.White, timeRect);
            g.DrawString(Caps(row.TimeLabel), fonts.Time, PdfBrushes.Black, timeRect, Centered);

            for (var i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                var cell = c < row.Cells.Count ? row.Cells[c] : null;
                var rect = new RectangleF(TimeColW + (i * fieldColW), y, fieldColW, h);

                if (cell == null)
                {
                    g.DrawRectangle(pens.Grid, PdfBrushes.White, rect);
                    continue;
                }

                var fill = ParseColor(cell.Color) ?? new PdfColor(255, 255, 255);
                var ink = ParseColor(cell.ContrastColor) ?? new PdfColor(0, 0, 0);
                g.DrawRectangle(pens.Grid, new PdfSolidBrush(fill), rect);
                g.DrawString(CellText(cell), fonts.Cell, new PdfSolidBrush(ink),
                    new RectangleF(rect.X + CellPad, rect.Y + CellPad, rect.Width - (CellPad * 2), rect.Height - (CellPad * 2)),
                    Centered);
            }

            y += h;
        }

        var scale = Math.Min(1f, Math.Min(clientW / gridW, clientH / gridH));
        var drawW = gridW * scale;
        var drawH = gridH * scale;
        page.Graphics.DrawPdfTemplate(tpl,
            new PointF((clientW - drawW) / 2f, (clientH - drawH) / 2f),
            new SizeF(drawW, drawH));
    }

    private static void DrawHeaderCell(PdfGraphics g, string text, RectangleF rect, Fonts fonts, Pens pens)
    {
        g.DrawRectangle(pens.Grid, new PdfSolidBrush(new PdfColor(230, 230, 230)), rect);
        g.DrawString(text, fonts.Header, PdfBrushes.Black,
            new RectangleF(rect.X + CellPad, rect.Y, rect.Width - (CellPad * 2), rect.Height), Centered);
    }

    private static string CellText(MasterScheduleCell cell) =>
        $"{TeamLabel(cell.T1Name)}\nVS\n{TeamLabel(cell.T2Name)}";

    // Stored names are "club:team" — the colon prints as a space.
    private static string TeamLabel(string name) =>
        Caps(Whitespace.Replace(name.Replace(':', ' '), " ").Trim());

    private static string Caps(string s) => s.ToUpper(CultureInfo.InvariantCulture);

    private static PdfColor? ParseColor(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        try
        {
            var c = System.Drawing.ColorTranslator.FromHtml(html);
            return new PdfColor(c.R, c.G, c.B);
        }
        catch
        {
            return null; // invalid color string — fall back to white / black
        }
    }

    private static byte[] Save(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms);
        doc.Close(true);
        return ms.ToArray();
    }

    private sealed class Fonts
    {
        public PdfStandardFont Header { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        public PdfStandardFont Time { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 9.5f);
    }

    private sealed class Pens
    {
        public PdfPen Grid { get; } = new(new PdfColor(0, 0, 0), 0.75f);
    }
}
