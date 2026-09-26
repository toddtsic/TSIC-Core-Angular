using System.Globalization;
using System.Text.RegularExpressions;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.API.Utilities;
using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Renders the print-ready Master Schedule PDFs — a date×field grid, one 18"×12" landscape page per
/// day, ½" margins, the grid centered on the page and scaled down (never cropped) when a day is too
/// big to fit.
/// <list type="bullet">
/// <item><b>Collegiate Coach</b> (no parking): college coaches' handout.</item>
/// <item><b>Operations</b> (parking supplied): adds a CARS ON SITE column ahead of each field
/// complex's fields — the cars on site at that complex at that row's date/time, from the Tournament
/// Parking report run with the director's on-screen buffers.</item>
/// </list>
/// Common layout: the day's date (MM/dd/yyyy) in the corner cell, then field names; time column +
/// header row larger bold ALL CAPS, bordered like the game cells; game cell TEAM 1 / VS / TEAM 2,
/// ALL CAPS, centered both ways, on the agegroup color with the grid's black/white contrast pick;
/// no agegroup/pool line, no scores, no referees; the stored "club:team" colon prints as a space;
/// fields with no games that day are omitted from that day's page.
/// Additive to the Excel exports — those are unchanged.
/// </summary>
internal static class MasterSchedulePdfRenderer
{
    // 18in × 12in at 72pt/in, ½" margins.
    private const float PageW = 1296f, PageH = 864f, Margin = 36f;

    private const float TimeColW = 90f;
    private const float CarsColW = 80f;
    private const float MinFieldColW = 100f;     // below this the grid scales down instead
    private const float HeaderRowH = 34f;
    private const float CellPad = 5f;
    private const float MinRowH = 44f;
    private const float MaxRowGrow = 1.5f;       // short days stretch rows to fill the page, up to this

    private static readonly PdfStringFormat Centered = new(PdfTextAlignment.Center, PdfVerticalAlignment.Middle)
    {
        WordWrap = PdfWordWrapType.Word,
    };

    private static readonly PdfStringFormat TopCentered = new(PdfTextAlignment.Center, PdfVerticalAlignment.Top)
    {
        WordWrap = PdfWordWrapType.Word,
    };

    private const float MatchupGap = 1.5f;       // space above and below the VS line
    private const float NoteBandH = 20f;         // settings note under the grid (Operations only)
    private const int LoadLightest = 245;        // gray level of a complex's quietest CARS ON SITE cell
    private const int LoadDarkest = 90;          // gray level of a complex's peak CARS ON SITE cell

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <param name="parking">Null → Collegiate Coach layout; supplied → Operations layout with cars columns.</param>
    /// <param name="parkingRequest">The parameters the parking report ran with — printed under the grid so
    /// two sheets run with different inputs can be told apart.</param>
    public static byte[] Render(
        MasterScheduleResponse data,
        TournamentParkingResponse? parking = null,
        TournamentParkingRequest? parkingRequest = null)
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

        // (complex, day) → timeslots in time order, for the "as of" cars lookup.
        var parkingSlots = parking?.ComplexDays.ToDictionary(
            cd => (Complex: cd.FieldComplex, cd.Day.Date),
            cd => cd.Timeslots.OrderBy(t => t.Time).ToList());

        var settingsNote = parkingRequest == null
            ? null
            : $"CARS ON SITE ESTIMATE:   ARRIVAL BUFFER {parkingRequest.ArrivalBufferMinutes} MIN   |   " +
              $"DEPARTURE BUFFER {parkingRequest.DepartureBufferMinutes} MIN   |   " +
              $"{parkingRequest.CarMultiplier} CARS PER TEAM";

        foreach (var day in data.Days)
        {
            var page = doc.Pages.Add();
            DrawDay(page, day, data.FieldColumns, parkingSlots, settingsNote, fonts, pens);
        }

        return Save(doc);
    }

    /// <summary>Same derivation as the parking report: the field name up to the first "-", trimmed.</summary>
    private static string FieldComplex(string fieldName) =>
        fieldName.Contains('-') ? fieldName[..fieldName.IndexOf('-')].Trim() : fieldName;

    /// <summary>A page column: a field (index into FieldColumns), or a complex's CARS ON SITE.</summary>
    private sealed record Col(int? FieldIndex, string? Complex, float Width);

    private static void DrawDay(
        PdfPage page, MasterScheduleDay day, List<string> fieldColumns,
        Dictionary<(string Complex, DateTime Date), List<ParkingTimeslotDto>>? parkingSlots,
        string? settingsNote, Fonts fonts, Pens pens)
    {
        var clientW = PageW - (Margin * 2);
        var pageClientH = PageH - (Margin * 2);
        // The settings note sits under the grid at full size; the grid fits in what's left.
        var clientH = pageClientH - (settingsNote != null ? NoteBandH : 0f);

        // Only the fields that host a game this day.
        var fieldIdx = Enumerable.Range(0, fieldColumns.Count)
            .Where(c => day.Rows.Any(r => c < r.Cells.Count && r.Cells[c] != null))
            .ToList();
        if (fieldIdx.Count == 0) return;

        // Columns fill the page width, but never narrower than MinFieldColW (then the grid scales).
        // FieldColumns are name-sorted, so a complex's fields are contiguous.
        var complexCount = parkingSlots == null
            ? 0
            : fieldIdx.Select(c => FieldComplex(fieldColumns[c])).Distinct().Count();
        var fieldColW = Math.Max(MinFieldColW,
            (clientW - TimeColW - (CarsColW * complexCount)) / fieldIdx.Count);

        var cols = new List<Col>();
        string? prevComplex = null;
        foreach (var c in fieldIdx)
        {
            if (parkingSlots != null)
            {
                var complex = FieldComplex(fieldColumns[c]);
                if (complex != prevComplex) cols.Add(new Col(null, complex, CarsColW));
                prevComplex = complex;
            }
            cols.Add(new Col(c, null, fieldColW));
        }

        var gridW = TimeColW + cols.Sum(c => c.Width);
        var textW = fieldColW - (CellPad * 2);

        // Natural row heights: tallest cell text in the row, wrapped to the column width.
        var rowHeights = day.Rows.Select(row =>
        {
            var h = MinRowH;
            foreach (var c in fieldIdx)
            {
                var cell = c < row.Cells.Count ? row.Cells[c] : null;
                if (cell == null) continue;
                h = Math.Max(h, MatchupHeight(cell, textW, fonts) + (CellPad * 2));
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

        // Header row — date in the corner, then cars / field names.
        var dateLabel = day.Rows.Count > 0
            ? day.Rows[0].SortKey.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)
            : day.ShortLabel.ToUpperInvariant();
        DrawHeaderCell(g, dateLabel, new RectangleF(0, 0, TimeColW, HeaderRowH), fonts, pens);
        var x = TimeColW;
        foreach (var col in cols)
        {
            var text = col.FieldIndex is int fi ? Caps(fieldColumns[fi]) : "CARS ON SITE";
            DrawHeaderCell(g, text, new RectangleF(x, 0, col.Width, HeaderRowH), fonts, pens);
            x += col.Width;
        }

        // Cars per (complex, row) — the load shading ramps across each complex's printed min..max.
        var carsByComplex = cols
            .Where(c => c.Complex != null)
            .Select(c => c.Complex!)
            .Distinct()
            .ToDictionary(cx => cx, cx => day.Rows.Select(r => CarsOnSite(parkingSlots!, cx, r.SortKey)).ToArray());

        // Data rows.
        var y = HeaderRowH;
        for (var r = 0; r < day.Rows.Count; r++)
        {
            var row = day.Rows[r];
            var h = rowHeights[r];

            var timeRect = new RectangleF(0, y, TimeColW, h);
            g.DrawRectangle(pens.Grid, PdfBrushes.White, timeRect);
            g.DrawString(Caps(row.TimeLabel), fonts.Time, PdfBrushes.Black, timeRect, Centered);

            x = TimeColW;
            foreach (var col in cols)
            {
                var rect = new RectangleF(x, y, col.Width, h);
                x += col.Width;

                if (col.Complex != null)
                {
                    var series = carsByComplex[col.Complex];
                    var cars = series[r];
                    var (shade, text) = LoadShade(cars, series.Min(), series.Max());
                    g.DrawRectangle(pens.Grid, new PdfSolidBrush(shade), rect);
                    g.DrawString(cars.ToString("N0", CultureInfo.InvariantCulture), fonts.Time, new PdfSolidBrush(text), rect, Centered);
                    continue;
                }

                var fi = col.FieldIndex!.Value;
                var cell = fi < row.Cells.Count ? row.Cells[fi] : null;
                if (cell == null)
                {
                    g.DrawRectangle(pens.Grid, PdfBrushes.White, rect);
                    continue;
                }

                var fill = ParseColor(cell.Color) ?? new PdfColor(255, 255, 255);
                var ink = ParseColor(cell.ContrastColor) ?? new PdfColor(0, 0, 0);
                g.DrawRectangle(pens.Grid, new PdfSolidBrush(fill), rect);
                DrawMatchup(g, cell, rect, new PdfSolidBrush(ink), fonts);
            }

            y += h;
        }

        var scale = Math.Min(1f, Math.Min(clientW / gridW, clientH / gridH));
        var drawW = gridW * scale;
        var drawH = gridH * scale;

        // Grid + settings note center on the page as one unit.
        var blockH = drawH + (settingsNote != null ? NoteBandH : 0f);
        var left = (clientW - drawW) / 2f;
        var top = (pageClientH - blockH) / 2f;
        page.Graphics.DrawPdfTemplate(tpl, new PointF(left, top), new SizeF(drawW, drawH));

        if (settingsNote != null)
        {
            page.Graphics.DrawString(settingsNote, fonts.Note, new PdfSolidBrush(new PdfColor(64, 64, 64)),
                new RectangleF(left, top + drawH, drawW, NoteBandH),
                new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        }
    }

    /// <summary>
    /// Gray load ramp for a CARS ON SITE cell, stretched across the complex's printed range for the
    /// day: its quietest row near-white → its peak dark gray, so mid-range differences stay visible.
    /// A flat day (min == max) stays light. Text uses the grid's black/white pick. Gray, because every
    /// hue is already an agegroup color on the sheet.
    /// </summary>
    private static (PdfColor Fill, PdfColor Text) LoadShade(int cars, int min, int max)
    {
        var t = max > min ? Math.Clamp((float)(cars - min) / (max - min), 0f, 1f) : 0f;
        var v = (byte)Math.Round(LoadLightest - (t * (LoadLightest - LoadDarkest)));
        var text = ColorUtility.GetContrastColor($"#{v:X2}{v:X2}{v:X2}") == "#fff"
            ? new PdfColor(255, 255, 255)
            : new PdfColor(0, 0, 0);
        return (new PdfColor(v, v, v), text);
    }

    /// <summary>
    /// Cars on site at <paramref name="complex"/> as of <paramref name="at"/>: the running total of the
    /// latest parking timeslot at or before that moment (arrivals ahead of the game are counted).
    /// No timeslot yet that day → 0.
    /// </summary>
    private static int CarsOnSite(
        Dictionary<(string Complex, DateTime Date), List<ParkingTimeslotDto>> parkingSlots,
        string complex, DateTime at)
    {
        if (!parkingSlots.TryGetValue((complex, at.Date), out var slots)) return 0;
        var cars = 0;
        foreach (var s in slots)
        {
            if (s.Time > at) break;
            cars = s.CarsOnSite;
        }
        return cars;
    }

    private static void DrawHeaderCell(PdfGraphics g, string text, RectangleF rect, Fonts fonts, Pens pens)
    {
        g.DrawRectangle(pens.Grid, new PdfSolidBrush(new PdfColor(230, 230, 230)), rect);
        g.DrawString(text, fonts.Header, PdfBrushes.Black,
            new RectangleF(rect.X + CellPad, rect.Y, rect.Width - (CellPad * 2), rect.Height), Centered);
    }

    /// <summary>
    /// Matchup stack — TEAM 1 (bold) / vs (smaller, regular) / TEAM 2 (bold) — so the split between
    /// the two teams reads at a glance. Measured and drawn by the same pieces so row heights agree.
    /// </summary>
    private static (string Text, PdfFont Font, float Height)[] MatchupParts(
        MasterScheduleCell cell, float textW, Fonts fonts)
    {
        (string, PdfFont)[] parts =
        [
            (TeamLabel(cell.T1Name), fonts.Team),
            ("VS", fonts.Vs),
            (TeamLabel(cell.T2Name), fonts.Team),
        ];
        return parts
            .Select(p => (p.Item1, p.Item2, p.Item2.MeasureString(p.Item1, textW, TopCentered).Height))
            .ToArray();
    }

    private static float MatchupHeight(MasterScheduleCell cell, float textW, Fonts fonts) =>
        MatchupParts(cell, textW, fonts).Sum(p => p.Height) + (MatchupGap * 2);

    private static void DrawMatchup(PdfGraphics g, MasterScheduleCell cell, RectangleF rect, PdfBrush ink, Fonts fonts)
    {
        var textW = rect.Width - (CellPad * 2);
        var parts = MatchupParts(cell, textW, fonts);
        var y = rect.Y + ((rect.Height - (parts.Sum(p => p.Height) + (MatchupGap * 2))) / 2f);
        foreach (var (text, font, height) in parts)
        {
            g.DrawString(text, font, ink, new RectangleF(rect.X + CellPad, y, textW, height), TopCentered);
            y += height + MatchupGap;
        }
    }

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
        public PdfStandardFont Team { get; } = new(PdfFontFamily.Helvetica, 9.5f, PdfFontStyle.Bold);
        public PdfStandardFont Vs { get; } = new(PdfFontFamily.Helvetica, 7.5f);
        public PdfStandardFont Note { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
    }

    private sealed class Pens
    {
        public PdfPen Grid { get; } = new(new PdfColor(0, 0, 0), 0.75f);
    }
}
