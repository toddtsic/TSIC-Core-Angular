using System.Globalization;
using System.Text.RegularExpressions;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.API.Utilities;
using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Renders the print-ready Master Schedule PDFs — a date×field grid on 18"×12" landscape pages,
/// ½" margins, the grid centered on the page and scaled down (never cropped) to fit.
/// <list type="bullet">
/// <item><b>Collegiate Coach</b> (no parking): one page per day while team names still print at
/// <see cref="MinTeamPt"/> or larger; a day too big for that breaks across pages at field-complex
/// boundaries (whole complexes packed per page, titled with the complexes it holds).</item>
/// <item><b>Operations</b> (parking supplied): always one page per field complex per day — parking is
/// managed per complex. The complex's CARS ON SITE column sits in B: cars on site at that complex at
/// that row's date/time, from the Tournament Parking report run with the director's on-screen
/// parameters, which print under the grid.</item>
/// </list>
/// A field complex is the field name up to the first "-" (the parking report's derivation). A complex
/// too big for one readable page splits by field. Field columns narrow (text wraps onto more lines)
/// when that lets the page use its height and print larger.
/// Common layout: the day's date (MM/dd/yyyy) in the corner cell, then field names; time column +
/// header row larger bold ALL CAPS, bordered like the game cells; game cell TEAM 1 / vs / TEAM 2,
/// ALL CAPS, centered both ways, on the agegroup color with the grid's black/white contrast pick;
/// no agegroup/pool line, no scores, no referees; the stored "club:team" colon prints as a space;
/// fields and time rows with no games on a page are omitted from it.
/// Additive to the Excel exports — those are unchanged.
/// </summary>
internal static class MasterSchedulePdfRenderer
{
    // 18in × 12in at 72pt/in, ½" margins.
    private const float PageW = 1296f, PageH = 864f, Margin = 36f;
    private const float ClientW = PageW - (Margin * 2);
    private const float ClientH = PageH - (Margin * 2);

    private const float TimeColW = 90f;
    private const float CarsColW = 80f;
    private const float HeaderRowH = 34f;
    private const float CellPad = 5f;
    private const float MinRowH = 44f;
    private const float MaxRowGrow = 1.5f;       // short days stretch rows to fill the page, up to this

    // Field-column widths tried when fitting a page; the fill-the-width width is always tried too.
    private const float MinCandidateColW = 50f, MaxCandidateColW = 200f, CandidateStep = 5f;

    // Readability floor: a page whose team names would print smaller than this gets split.
    private const float TeamPt = 9.5f, MinTeamPt = 6f;

    private const float MatchupGap = 1.5f;       // space above and below the VS line
    private const float TitleBandH = 26f;        // complex title above the grid (split / Operations pages)
    private const float NoteBandH = 20f;         // settings note under the grid (Operations only)
    private const int LoadLightest = 245;        // gray level of a complex's quietest CARS ON SITE cell
    private const int LoadDarkest = 90;          // gray level of a complex's peak CARS ON SITE cell

    private static readonly PdfStringFormat Centered = new(PdfTextAlignment.Center, PdfVerticalAlignment.Middle)
    {
        WordWrap = PdfWordWrapType.Word,
    };

    private static readonly PdfStringFormat TopCentered = new(PdfTextAlignment.Center, PdfVerticalAlignment.Top)
    {
        WordWrap = PdfWordWrapType.Word,
    };

    private static readonly PdfStringFormat MidLeft = new(PdfTextAlignment.Left, PdfVerticalAlignment.Middle);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <param name="parking">Null → Collegiate Coach layout; supplied → Operations layout, a page per complex.</param>
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
        var measure = new MatchupMeasurer(fonts);

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
            var pages = parkingSlots != null
                ? PlanOperationsPages(day, data.FieldColumns, measure)
                : PlanCoachPages(day, data.FieldColumns, measure);

            foreach (var spec in pages)
            {
                var layout = ComputeLayout(day, data.FieldColumns, spec.Fields, parkingSlots != null,
                    AvailableHeight(spec.Title != null, settingsNote != null), measure);
                DrawPage(doc.Pages.Add(), day, data.FieldColumns, layout, spec.Title,
                    parkingSlots, settingsNote, fonts, pens, measure);
            }
        }

        if (doc.Pages.Count == 0)
        {
            var g = doc.Pages.Add().Graphics;
            g.DrawString("NO SCHEDULED GAMES FOR THIS EVENT.", fonts.Header, PdfBrushes.Gray,
                new RectangleF(0, 0, ClientW, HeaderRowH), Centered);
        }

        return Save(doc);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Page planning
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>One page of a day: the field indices it shows, and its title (null = no title band).</summary>
    private sealed record PageSpec(List<int> Fields, string? Title);

    /// <summary>Same derivation as the parking report: the field name up to the first "-", trimmed.</summary>
    private static string FieldComplex(string fieldName) =>
        fieldName.Contains('-') ? fieldName[..fieldName.IndexOf('-')].Trim() : fieldName;

    /// <summary>Fields hosting a game this day, grouped by complex in column order (name-sorted, so contiguous).</summary>
    private static List<(string Complex, List<int> Fields)> ComplexGroups(MasterScheduleDay day, List<string> fieldColumns) =>
        Enumerable.Range(0, fieldColumns.Count)
            .Where(c => day.Rows.Any(r => c < r.Cells.Count && r.Cells[c] != null))
            .GroupBy(c => FieldComplex(fieldColumns[c]))
            .Select(g => (g.Key, g.ToList()))
            .ToList();

    /// <summary>Operations: always a page per complex (split by field only if one complex won't fit readably).</summary>
    private static List<PageSpec> PlanOperationsPages(MasterScheduleDay day, List<string> fieldColumns, MatchupMeasurer measure)
    {
        var availH = AvailableHeight(hasTitle: true, hasNote: true);
        var pages = new List<PageSpec>();
        foreach (var (complex, fields) in ComplexGroups(day, fieldColumns))
        {
            var pieces = Pack([fields], f => Fits(day, fieldColumns, f, withCars: true, availH, measure));
            for (var i = 0; i < pieces.Count; i++)
            {
                var title = pieces.Count == 1 ? Caps(complex) : $"{Caps(complex)}   ({i + 1} OF {pieces.Count})";
                pages.Add(new PageSpec(pieces[i], title));
            }
        }
        return pages;
    }

    /// <summary>Collegiate Coach: the whole day on one page when readable; else whole complexes packed per page.</summary>
    private static List<PageSpec> PlanCoachPages(MasterScheduleDay day, List<string> fieldColumns, MatchupMeasurer measure)
    {
        var groups = ComplexGroups(day, fieldColumns);
        var all = groups.SelectMany(g => g.Fields).ToList();
        if (all.Count == 0) return [];

        if (Fits(day, fieldColumns, all, withCars: false, AvailableHeight(hasTitle: false, hasNote: false), measure))
            return [new PageSpec(all, null)];

        var availH = AvailableHeight(hasTitle: true, hasNote: false);
        return Pack(groups.Select(g => g.Fields).ToList(),
                f => Fits(day, fieldColumns, f, withCars: false, availH, measure))
            .Select(fields => new PageSpec(fields, string.Join("   |   ",
                fields.Select(f => Caps(FieldComplex(fieldColumns[f]))).Distinct())))
            .ToList();
    }

    /// <summary>
    /// Packs units (a complex's fields) onto as few pages as fit: each unit joins the first page it
    /// still fits on, else starts a new one (first-fit — a small complex can share a page with an
    /// earlier one rather than only its neighbor). A unit that doesn't fit even alone is split field
    /// by field onto pages of its own. Pages keep column order, and come back ordered by first field.
    /// </summary>
    private static List<List<int>> Pack(List<List<int>> units, Func<List<int>, bool> fits)
    {
        var pages = new List<List<int>>();

        foreach (var unit in units)
        {
            if (fits(unit))
            {
                var home = pages.FindIndex(p => fits([.. p.Concat(unit).Order()]));
                if (home >= 0) pages[home] = [.. pages[home].Concat(unit).Order()];
                else pages.Add([.. unit]);
                continue;
            }

            // Too big alone — split by field, filling each page before starting the next.
            var piece = new List<int>();
            foreach (var field in unit)
            {
                if (piece.Count > 0 && !fits([.. piece, field]))
                {
                    pages.Add(piece);
                    piece = [];
                }
                piece.Add(field);
            }
            if (piece.Count > 0) pages.Add(piece);
        }

        return [.. pages.OrderBy(p => p[0])];
    }

    private static bool Fits(MasterScheduleDay day, List<string> fieldColumns, List<int> fields,
        bool withCars, float availH, MatchupMeasurer measure) =>
        ComputeLayout(day, fieldColumns, fields, withCars, availH, measure).Scale * TeamPt >= MinTeamPt;

    private static float AvailableHeight(bool hasTitle, bool hasNote) =>
        ClientH - (hasTitle ? TitleBandH : 0f) - (hasNote ? NoteBandH : 0f);

    // ══════════════════════════════════════════════════════════════════════
    // Layout
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>A page column: a field (index into FieldColumns), or a complex's CARS ON SITE.</summary>
    private sealed record Col(int? FieldIndex, string? Complex, float Width);

    private sealed record Layout(
        List<int> Rows, List<Col> Cols, float FieldColW, float HeaderH, float[] RowHeights, float GridW, float GridH, float Scale);

    /// <summary>
    /// Lays out one page: rows that have a game on these fields, a CARS ON SITE column ahead of each
    /// complex (Operations), and the field-column width — the fill-the-width width or a narrower /
    /// wider one — that prints largest once scaled to fit <see cref="ClientW"/> × <paramref name="availH"/>.
    /// </summary>
    private static Layout ComputeLayout(MasterScheduleDay day, List<string> fieldColumns, List<int> fields,
        bool withCars, float availH, MatchupMeasurer measure)
    {
        var rows = Enumerable.Range(0, day.Rows.Count)
            .Where(r => fields.Any(f => CellAt(day.Rows[r], f) != null))
            .ToList();

        var complexCount = withCars ? fields.Select(f => FieldComplex(fieldColumns[f])).Distinct().Count() : 0;
        var fixedW = TimeColW + (CarsColW * complexCount);
        var fillW = (ClientW - fixedW) / fields.Count;

        var candidates = new List<float> { fillW };
        for (var w = MinCandidateColW; w <= MaxCandidateColW; w += CandidateStep) candidates.Add(w);

        float[] RowHeightsAt(float colW)
        {
            var textW = colW - (CellPad * 2);
            return rows.Select(r =>
            {
                var h = MinRowH;
                foreach (var f in fields)
                {
                    var cell = CellAt(day.Rows[r], f);
                    if (cell != null) h = Math.Max(h, measure.Height(cell, textW) + (CellPad * 2));
                }
                return h;
            }).ToArray();
        }

        static float ScaleFor(float gridW, float gridH, float availH) =>
            Math.Min(1f, Math.Min(ClientW / gridW, availH / gridH));

        // Header row grows when a narrow column wraps a long field name.
        float HeaderHeightAt(float colW)
        {
            var textW = colW - (CellPad * 2);
            return fields.Aggregate(HeaderRowH, (h, f) =>
                Math.Max(h, measure.HeaderHeight(Caps(fieldColumns[f]), textW) + (CellPad * 2)));
        }

        // Largest printed scale wins; ties go to the larger printed area (fills the page).
        var best = candidates
            .Select(w =>
            {
                var heights = RowHeightsAt(w);
                var headerH = HeaderHeightAt(w);
                var gridW = fixedW + (w * fields.Count);
                var gridH = headerH + heights.Sum();
                var scale = ScaleFor(gridW, gridH, availH);
                return (W: w, Heights: heights, HeaderH: headerH, GridW: gridW, GridH: gridH, Scale: scale);
            })
            .OrderByDescending(c => Math.Round(c.Scale, 3))
            .ThenByDescending(c => c.GridW * c.Scale)
            .First();

        var rowHeights = best.Heights;
        var gridH = best.GridH;

        // Short page: stretch data rows toward the page height so the print is not a strip in the middle.
        var widthScale = Math.Min(1f, ClientW / best.GridW);
        var spare = (availH / widthScale) - gridH;
        if (spare > 0 && rowHeights.Length > 0)
        {
            var grow = Math.Min(MaxRowGrow, 1f + (spare / rowHeights.Sum()));
            rowHeights = rowHeights.Select(h => h * grow).ToArray();
            gridH = best.HeaderH + rowHeights.Sum();
        }

        var cols = new List<Col>();
        string? prevComplex = null;
        foreach (var f in fields)
        {
            if (withCars)
            {
                var complex = FieldComplex(fieldColumns[f]);
                if (complex != prevComplex) cols.Add(new Col(null, complex, CarsColW));
                prevComplex = complex;
            }
            cols.Add(new Col(f, null, best.W));
        }

        return new Layout(rows, cols, best.W, best.HeaderH, rowHeights, best.GridW, gridH, ScaleFor(best.GridW, gridH, availH));
    }

    private static MasterScheduleCell? CellAt(MasterScheduleRow row, int field) =>
        field < row.Cells.Count ? row.Cells[field] : null;

    // ══════════════════════════════════════════════════════════════════════
    // Drawing
    // ══════════════════════════════════════════════════════════════════════

    private static void DrawPage(
        PdfPage page, MasterScheduleDay day, List<string> fieldColumns, Layout layout, string? title,
        Dictionary<(string Complex, DateTime Date), List<ParkingTimeslotDto>>? parkingSlots,
        string? settingsNote, Fonts fonts, Pens pens, MatchupMeasurer measure)
    {
        // Draw at natural size into a template, then place it scaled-to-fit and centered.
        var tpl = new PdfTemplate(layout.GridW, layout.GridH);
        var g = tpl.Graphics;

        // Header row — date in the corner, then cars / field names.
        var dateLabel = day.Rows.Count > 0
            ? day.Rows[0].SortKey.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)
            : day.ShortLabel.ToUpperInvariant();
        DrawHeaderCell(g, dateLabel, new RectangleF(0, 0, TimeColW, layout.HeaderH), fonts, pens);
        var x = TimeColW;
        foreach (var col in layout.Cols)
        {
            var text = col.FieldIndex is int fi ? Caps(fieldColumns[fi]) : "CARS ON SITE";
            DrawHeaderCell(g, text, new RectangleF(x, 0, col.Width, layout.HeaderH), fonts, pens);
            x += col.Width;
        }

        // Cars per (complex, printed row) — the load shading ramps across each complex's printed min..max.
        var carsByComplex = layout.Cols
            .Where(c => c.Complex != null)
            .Select(c => c.Complex!)
            .Distinct()
            .ToDictionary(cx => cx, cx => layout.Rows.Select(r => CarsOnSite(parkingSlots!, cx, day.Rows[r].SortKey)).ToArray());

        // Data rows.
        var y = layout.HeaderH;
        for (var i = 0; i < layout.Rows.Count; i++)
        {
            var row = day.Rows[layout.Rows[i]];
            var h = layout.RowHeights[i];

            var timeRect = new RectangleF(0, y, TimeColW, h);
            g.DrawRectangle(pens.Grid, PdfBrushes.White, timeRect);
            g.DrawString(Caps(row.TimeLabel), fonts.Time, PdfBrushes.Black, timeRect, Centered);

            x = TimeColW;
            foreach (var col in layout.Cols)
            {
                var rect = new RectangleF(x, y, col.Width, h);
                x += col.Width;

                if (col.Complex != null)
                {
                    var series = carsByComplex[col.Complex];
                    var cars = series[i];
                    var (shade, text) = LoadShade(cars, series.Min(), series.Max());
                    g.DrawRectangle(pens.Grid, new PdfSolidBrush(shade), rect);
                    g.DrawString(cars.ToString("N0", CultureInfo.InvariantCulture), fonts.Time, new PdfSolidBrush(text), rect, Centered);
                    continue;
                }

                var cell = CellAt(row, col.FieldIndex!.Value);
                if (cell == null)
                {
                    g.DrawRectangle(pens.Grid, PdfBrushes.White, rect);
                    continue;
                }

                var fill = ParseColor(cell.Color) ?? new PdfColor(255, 255, 255);
                var ink = ParseColor(cell.ContrastColor) ?? new PdfColor(0, 0, 0);
                g.DrawRectangle(pens.Grid, new PdfSolidBrush(fill), rect);
                DrawMatchup(g, cell, rect, new PdfSolidBrush(ink), measure);
            }

            y += h;
        }

        var drawW = layout.GridW * layout.Scale;
        var drawH = layout.GridH * layout.Scale;

        // Title + grid + settings note center on the page as one unit.
        var titleH = title != null ? TitleBandH : 0f;
        var noteH = settingsNote != null ? NoteBandH : 0f;
        var left = (ClientW - drawW) / 2f;
        var top = (ClientH - (titleH + drawH + noteH)) / 2f;

        if (title != null)
        {
            page.Graphics.DrawString(title, fonts.Title, PdfBrushes.Black,
                new RectangleF(left, top, drawW, TitleBandH), MidLeft);
        }

        page.Graphics.DrawPdfTemplate(tpl, new PointF(left, top + titleH), new SizeF(drawW, drawH));

        if (settingsNote != null)
        {
            page.Graphics.DrawString(settingsNote, fonts.Note, new PdfSolidBrush(new PdfColor(64, 64, 64)),
                new RectangleF(left, top + titleH + drawH, drawW, NoteBandH), MidLeft);
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

    private static void DrawMatchup(PdfGraphics g, MasterScheduleCell cell, RectangleF rect, PdfBrush ink, MatchupMeasurer measure)
    {
        var textW = rect.Width - (CellPad * 2);
        var parts = measure.Parts(cell, textW);
        var y = rect.Y + ((rect.Height - (parts.Sum(p => p.Height) + (MatchupGap * 2))) / 2f);
        foreach (var (text, font, height) in parts)
        {
            g.DrawString(text, font, ink, new RectangleF(rect.X + CellPad, y, textW, height), TopCentered);
            y += height + MatchupGap;
        }
    }

    /// <summary>
    /// Matchup stack — TEAM 1 (bold) / vs (smaller, regular) / TEAM 2 (bold) — so the split between
    /// the two teams reads at a glance. Measured and drawn from the same pieces so row heights agree.
    /// Cached per (game, width): page planning re-measures the same cells at many trial widths.
    /// </summary>
    private sealed class MatchupMeasurer(Fonts fonts)
    {
        private readonly Dictionary<(int Gid, float TextW), (string Text, PdfFont Font, float Height)[]> _cache = [];

        public (string Text, PdfFont Font, float Height)[] Parts(MasterScheduleCell cell, float textW)
        {
            if (_cache.TryGetValue((cell.Gid, textW), out var hit)) return hit;

            (string, PdfFont)[] parts =
            [
                (TeamLabel(cell.T1Name), fonts.Team),
                ("VS", fonts.Vs),
                (TeamLabel(cell.T2Name), fonts.Team),
            ];
            var measured = parts
                .Select(p => (p.Item1, p.Item2, p.Item2.MeasureString(p.Item1, textW, TopCentered).Height))
                .ToArray();
            _cache[(cell.Gid, textW)] = measured;
            return measured;
        }

        public float Height(MasterScheduleCell cell, float textW) =>
            Parts(cell, textW).Sum(p => p.Height) + (MatchupGap * 2);

        private readonly Dictionary<(string Text, float TextW), float> _headerCache = [];

        public float HeaderHeight(string text, float textW)
        {
            if (_headerCache.TryGetValue((text, textW), out var hit)) return hit;
            var h = fonts.Header.MeasureString(text, textW, Centered).Height;
            _headerCache[(text, textW)] = h;
            return h;
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
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 15, PdfFontStyle.Bold);
        public PdfStandardFont Header { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        public PdfStandardFont Time { get; } = new(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        public PdfStandardFont Team { get; } = new(PdfFontFamily.Helvetica, TeamPt, PdfFontStyle.Bold);
        public PdfStandardFont Vs { get; } = new(PdfFontFamily.Helvetica, 7.5f);
        public PdfStandardFont Note { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
    }

    private sealed class Pens
    {
        public PdfPen Grid { get; } = new(new PdfColor(0, 0, 0), 0.75f);
    }
}
