using System.Data;
using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn schedule-list PDF (Syncfusion.Pdf). One flat game dataset
/// (reporting_migrate.ScheduleList_Flat) is grouped, sorted, and projected to a
/// full-width table entirely from a runtime config — the single designer that retires
/// the Schedule_ExportExcel report family. Score columns render the recorded score, a
/// blank write-in box (officials' sheet), or nothing, per the request's ScoreMode.
/// </summary>
public sealed class ScheduleListReportService : IScheduleListReportService
{
    private readonly IReportingRepository _reportingRepository;

    public ScheduleListReportService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    private const string ProcName = "reporting_migrate.ScheduleList_Flat";

    // ── Page + table geometry (points; Letter portrait, 0.4in margins) ──
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);          // 554.4
    private const float ContentBottom = PageH - MarginBottom;       // 763.2
    private const float GroupHeaderH = 18f;
    private const float ColHeaderH = 15f;
    private const float BaseRowH = 14f;
    private const float CellPadX = 3f;

    public IReadOnlyList<ScheduleListFieldDto> GetAvailableFields() => AvailableFields;

    private static readonly IReadOnlyList<ScheduleListFieldDto> AvailableFields = new[]
    {
        new ScheduleListFieldDto { Key = "date",      Label = "Date",      DefaultWidthWeight = 52, DefaultAlign = "Left",   SupportsLongText = false, IsScore = false },
        new ScheduleListFieldDto { Key = "time",      Label = "Time",      DefaultWidthWeight = 40, DefaultAlign = "Left",   SupportsLongText = false, IsScore = false },
        new ScheduleListFieldDto { Key = "field",     Label = "Field",     DefaultWidthWeight = 120, DefaultAlign = "Left",  SupportsLongText = true,  IsScore = false },
        new ScheduleListFieldDto { Key = "agegroup",  Label = "Age Group", DefaultWidthWeight = 72, DefaultAlign = "Left",   SupportsLongText = false, IsScore = false },
        new ScheduleListFieldDto { Key = "division",  Label = "Division",  DefaultWidthWeight = 38, DefaultAlign = "Center", SupportsLongText = false, IsScore = false },
        new ScheduleListFieldDto { Key = "league",    Label = "League",    DefaultWidthWeight = 80, DefaultAlign = "Left",   SupportsLongText = true,  IsScore = false },
        new ScheduleListFieldDto { Key = "home",      Label = "Home",      DefaultWidthWeight = 130, DefaultAlign = "Left",  SupportsLongText = true,  IsScore = false },
        new ScheduleListFieldDto { Key = "homeScore", Label = "Home Score", DefaultWidthWeight = 30, DefaultAlign = "Center", SupportsLongText = false, IsScore = true },
        new ScheduleListFieldDto { Key = "away",      Label = "Away",      DefaultWidthWeight = 130, DefaultAlign = "Left",  SupportsLongText = true,  IsScore = false },
        new ScheduleListFieldDto { Key = "awayScore", Label = "Away Score", DefaultWidthWeight = 30, DefaultAlign = "Center", SupportsLongText = false, IsScore = true },
        new ScheduleListFieldDto { Key = "homeRep",   Label = "Home Rep",  DefaultWidthWeight = 80, DefaultAlign = "Left",   SupportsLongText = true,  IsScore = false },
        new ScheduleListFieldDto { Key = "awayRep",   Label = "Away Rep",  DefaultWidthWeight = 80, DefaultAlign = "Left",   SupportsLongText = true,  IsScore = false },
    };

    private static bool IsScoreKey(string key) => key is "homeScore" or "awayScore";

    public async Task<ReportExportResult> GenerateAsync(
        ScheduleListRequestDto request,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var (reader, connection) = await _reportingRepository.ExecuteStoredProcedureAsync(
            ProcName, jobId, useJobId: true, cancellationToken: cancellationToken);

        var table = new DataTable("MainReportData");
        try
        {
            table.Load(reader);
        }
        finally
        {
            await reader.CloseAsync();
            await connection.CloseAsync();
        }

        var games = table.Rows.Cast<DataRow>().Select(GameRow.From).ToList();

        // Drop the score columns entirely when scores are hidden; everything else is
        // normalized full-width across whatever the director chose.
        var hidden = string.Equals(request.ScoreMode, "Hidden", StringComparison.OrdinalIgnoreCase);
        var columns = request.Columns
            .Where(c => !(hidden && IsScoreKey(c.Key)))
            .ToList();
        if (columns.Count == 0)
        {
            columns = AvailableFields.Where(f => !f.IsScore)
                .Take(4)
                .Select(f => new ScheduleListColumnDto { Key = f.Key, WidthWeight = f.DefaultWidthWeight, Align = f.DefaultAlign, LongText = "Truncate", TruncateAt = 28 })
                .ToList();
        }

        var sumW = columns.Sum(c => Math.Max(1, c.WidthWeight));
        var widths = columns.Select(c => Math.Max(1, c.WidthWeight) / (float)sumW * ContentW).ToArray();

        using var document = new PdfDocument();
        document.PageSettings.Size = PdfPageSize.Letter;
        document.PageSettings.Margins.Left = MarginX;
        document.PageSettings.Margins.Right = MarginX;
        document.PageSettings.Margins.Top = MarginTop;
        document.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(document);

        var fonts = new Fonts();
        var pens = new Pens();

        var groups = GroupGames(games, request.SortBy, request.GroupBy);

        PdfGraphics? g = null;
        var y = 0f;
        var firstGroup = true;

        foreach (var group in groups)
        {
            if (g == null || request.PageBreakPerGroup && !firstGroup)
            {
                g = document.Pages.Add().Graphics;
                y = 0f;
            }
            firstGroup = false;

            // Keep the group header with at least its column header + one row.
            if (y + GroupHeaderH + ColHeaderH + BaseRowH > ContentBottom - MarginTop)
            {
                g = document.Pages.Add().Graphics;
                y = 0f;
            }

            if (group.Label.Length > 0)
            {
                y = DrawGroupHeader(g, group, y, request.ColorAccent, fonts, pens);
            }
            y = DrawColumnHeader(g, columns, widths, y, fonts, pens);

            foreach (var game in group.Games)
            {
                var rowH = MeasureRowHeight(game, columns, widths, fonts);
                if (y + rowH > ContentBottom - MarginTop)
                {
                    g = document.Pages.Add().Graphics;
                    y = 0f;
                    if (group.Label.Length > 0)
                    {
                        y = DrawGroupHeader(g, group, y, request.ColorAccent, fonts, pens, continued: true);
                    }
                    y = DrawColumnHeader(g, columns, widths, y, fonts, pens);
                }
                y = DrawGameRow(g, game, columns, widths, y, rowH, request.ScoreMode, fonts, pens);
            }
        }

        if (g == null)
        {
            // No games — still emit a one-page report rather than a corrupt empty doc.
            g = document.Pages.Add().Graphics;
            g.DrawString("No scheduled games.", fonts.GroupHeader, PdfBrushes.Gray,
                new RectangleF(0, 0, ContentW, 20),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = "Schedule.pdf",
        };
    }

    // ── Grouping + sorting ──

    private static IReadOnlyList<GameGroup> GroupGames(
        List<GameRow> games, string sortBy, string groupBy)
    {
        Func<GameRow, IComparable> sortKey = sortBy switch
        {
            "Field" => g => g.FieldName,
            "Team" => g => g.T1Name,
            _ => g => g.GDateTime,
        };

        IEnumerable<IGrouping<string, GameRow>> grouped = groupBy switch
        {
            "Day" => games.GroupBy(g => g.GDay),
            "Field" => games.GroupBy(g => g.FieldName),
            "AgeGroup" => games.GroupBy(g => g.AgegroupName),
            "Division" => games.GroupBy(g => g.AgegroupName + "|" + g.DivName),
            _ => games.GroupBy(_ => ""),
        };

        return grouped
            .Select(grp =>
            {
                var ordered = grp.OrderBy(sortKey).ThenBy(g => g.GDateTime).ToList();
                return new GameGroup
                {
                    Label = GroupLabel(groupBy, ordered[0]),
                    Color = ordered[0].Color,
                    Games = ordered,
                };
            })
            .OrderBy(grp => grp.Games[0], GroupOrderComparer(groupBy))
            .ToList();
    }

    private static IComparer<GameRow> GroupOrderComparer(string groupBy) => groupBy switch
    {
        "Field" => Comparer<GameRow>.Create((a, b) => string.Compare(a.FieldName, b.FieldName, StringComparison.OrdinalIgnoreCase)),
        "AgeGroup" => Comparer<GameRow>.Create((a, b) => string.Compare(a.AgegroupName, b.AgegroupName, StringComparison.OrdinalIgnoreCase)),
        "Division" => Comparer<GameRow>.Create((a, b) =>
        {
            var ag = string.Compare(a.AgegroupName, b.AgegroupName, StringComparison.OrdinalIgnoreCase);
            return ag != 0 ? ag : string.Compare(a.DivName, b.DivName, StringComparison.OrdinalIgnoreCase);
        }),
        "Day" => Comparer<GameRow>.Create((a, b) => a.GDateTime.CompareTo(b.GDateTime)),
        _ => Comparer<GameRow>.Create((_, _) => 0),
    };

    private static string GroupLabel(string groupBy, GameRow g) => groupBy switch
    {
        "Day" => g.GDateTime.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture),
        "Field" => g.FieldName,
        "AgeGroup" => g.AgegroupName,
        "Division" => $"{g.AgegroupName} — Div {g.DivName}",
        _ => "",
    };

    // ── Drawing ──

    private static float DrawGroupHeader(
        PdfGraphics g, GameGroup group, float y, bool colorAccent,
        Fonts fonts, Pens pens, bool continued = false)
    {
        var rect = new RectangleF(0, y, ContentW, GroupHeaderH);
        var fill = colorAccent && TryParseColor(group.Color, out var c)
            ? new PdfSolidBrush(c)
            : new PdfSolidBrush(new PdfColor(232, 232, 232));
        g.DrawRectangle(fill, rect);

        var textColor = colorAccent && TryParseColor(group.Color, out var cc) && IsDark(cc)
            ? PdfBrushes.White
            : PdfBrushes.Black;
        var label = continued ? group.Label + " (cont.)" : group.Label;
        g.DrawString(label, fonts.GroupHeader, textColor,
            new RectangleF(CellPadX, y, ContentW - (CellPadX * 2), GroupHeaderH),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        return y + GroupHeaderH;
    }

    private static float DrawColumnHeader(
        PdfGraphics g, List<ScheduleListColumnDto> cols, float[] widths, float y,
        Fonts fonts, Pens pens)
    {
        var x = 0f;
        for (var i = 0; i < cols.Count; i++)
        {
            var label = AvailableFields.FirstOrDefault(f => f.Key == cols[i].Key)?.Label ?? cols[i].Key;
            g.DrawString(label, fonts.ColHeader, PdfBrushes.Black,
                new RectangleF(x + CellPadX, y, widths[i] - (CellPadX * 2), ColHeaderH),
                new PdfStringFormat(ParseAlign(cols[i].Align), PdfVerticalAlignment.Middle));
            x += widths[i];
        }
        g.DrawLine(pens.Header, new PointF(0, y + ColHeaderH), new PointF(ContentW, y + ColHeaderH));
        return y + ColHeaderH;
    }

    private static float DrawGameRow(
        PdfGraphics g, GameRow game, List<ScheduleListColumnDto> cols, float[] widths,
        float y, float rowH, string scoreMode, Fonts fonts, Pens pens)
    {
        var blank = string.Equals(scoreMode, "Blank", StringComparison.OrdinalIgnoreCase);
        var x = 0f;
        for (var i = 0; i < cols.Count; i++)
        {
            var col = cols[i];
            if (IsScoreKey(col.Key) && blank)
            {
                // Write-in box: a small centered rectangle the official fills by hand.
                var boxW = Math.Min(widths[i] - (CellPadX * 2), 22f);
                var boxH = Math.Min(rowH - 4f, 11f);
                var bx = x + (widths[i] - boxW) / 2f;
                var by = y + (rowH - boxH) / 2f;
                g.DrawRectangle(pens.Box, new RectangleF(bx, by, boxW, boxH));
            }
            else
            {
                var text = ResolveCell(game, col, scoreMode);
                if (text.Length > 0)
                {
                    g.DrawString(text, fonts.Cell, PdfBrushes.Black,
                        new RectangleF(x + CellPadX, y + 1f, widths[i] - (CellPadX * 2), rowH - 2f),
                        WrapFormat(col));
                }
            }
            x += widths[i];
        }
        g.DrawLine(pens.Divider, new PointF(0, y + rowH), new PointF(ContentW, y + rowH));
        return y + rowH;
    }

    private static float MeasureRowHeight(
        GameRow game, List<ScheduleListColumnDto> cols, float[] widths, Fonts fonts)
    {
        var rowH = BaseRowH;
        for (var i = 0; i < cols.Count; i++)
        {
            var col = cols[i];
            if (!string.Equals(col.LongText, "Wrap", StringComparison.OrdinalIgnoreCase) || IsScoreKey(col.Key))
            {
                continue;
            }
            var text = ResolveCell(game, col, "Printed");
            if (text.Length == 0)
            {
                continue;
            }
            var sz = fonts.Cell.MeasureString(text, widths[i] - (CellPadX * 2), WrapFormat(col));
            rowH = Math.Max(rowH, sz.Height + 4f);
        }
        return rowH;
    }

    private static string ResolveCell(GameRow g, ScheduleListColumnDto col, string scoreMode)
    {
        var value = col.Key switch
        {
            "date" => g.GDateTime.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
            "time" => g.GDateTime.ToString("h:mm tt", CultureInfo.InvariantCulture),
            "field" => g.FieldName,
            "agegroup" => g.AgegroupName,
            "division" => g.DivName,
            "league" => g.LeagueName,
            "home" => g.T1Name,
            "away" => g.T2Name,
            "homeScore" => string.Equals(scoreMode, "Printed", StringComparison.OrdinalIgnoreCase) ? g.T1Score : "",
            "awayScore" => string.Equals(scoreMode, "Printed", StringComparison.OrdinalIgnoreCase) ? g.T2Score : "",
            "homeRep" => g.ClubRep1,
            "awayRep" => g.ClubRep2,
            _ => "",
        };

        if (value.Length > 0 && string.Equals(col.LongText, "Truncate", StringComparison.OrdinalIgnoreCase))
        {
            var at = col.TruncateAt ?? 28;
            if (at > 0 && value.Length > at)
            {
                value = value[..at];
            }
        }
        return value;
    }

    private static PdfStringFormat WrapFormat(ScheduleListColumnDto col) =>
        new(ParseAlign(col.Align), PdfVerticalAlignment.Top)
        {
            WordWrap = string.Equals(col.LongText, "Wrap", StringComparison.OrdinalIgnoreCase)
                ? PdfWordWrapType.Word
                : PdfWordWrapType.None,
            LineLimit = false,
        };

    private static PdfTextAlignment ParseAlign(string align) => align switch
    {
        "Right" => PdfTextAlignment.Right,
        "Center" => PdfTextAlignment.Center,
        _ => PdfTextAlignment.Left,
    };

    private static bool TryParseColor(string hex, out PdfColor color)
    {
        color = new PdfColor(0, 0, 0);
        if (string.IsNullOrWhiteSpace(hex) || hex[0] != '#' || hex.Length != 7)
        {
            return false;
        }
        try
        {
            var r = Convert.ToByte(hex.Substring(1, 2), 16);
            var gg = Convert.ToByte(hex.Substring(3, 2), 16);
            var b = Convert.ToByte(hex.Substring(5, 2), 16);
            color = new PdfColor(r, gg, b);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsDark(PdfColor c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 140;

    private static void AddFooterTemplate(PdfDocument document)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var gray = new PdfSolidBrush(new PdfColor(102, 102, 102));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, ContentW, 18));

        footer.Graphics.DrawString("Reports By TeamSportsInfo.com", footerFont, gray, new PointF(2, 4));

        var composite = new PdfCompositeField(
            footerFont, gray, "{0} of {1}",
            new PdfPageNumberField(footerFont, gray),
            new PdfPageCountField(footerFont, gray))
        {
            StringFormat = new PdfStringFormat(PdfTextAlignment.Right),
        };
        composite.Draw(footer.Graphics, new PointF(ContentW - 2, 4));

        document.Template.Bottom = footer;
    }

    // ── Render-time helpers ──

    private sealed class Fonts
    {
        public PdfStandardFont GroupHeader { get; } = new(PdfFontFamily.Helvetica, 11, PdfFontStyle.Bold);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 8);
    }

    private sealed class Pens
    {
        public PdfPen Header { get; } = new(new PdfColor(0, 0, 0), 0.75f);
        public PdfPen Divider { get; } = new(new PdfColor(200, 200, 200), 0.5f);
        public PdfPen Box { get; } = new(new PdfColor(120, 120, 120), 0.75f);
    }

    private sealed class GameGroup
    {
        public required string Label { get; init; }
        public required string Color { get; init; }
        public required List<GameRow> Games { get; init; }
    }

    /// <summary>One materialized proc row (one scheduled game).</summary>
    private sealed record GameRow
    {
        public required int Gid { get; init; }
        public required string AgegroupName { get; init; }
        public required string DivName { get; init; }
        public required string LeagueName { get; init; }
        public required string FieldName { get; init; }
        public required string Color { get; init; }
        public required string GDay { get; init; }
        public required DateTime GDateTime { get; init; }
        public required string T1Name { get; init; }
        public required string T1Score { get; init; }
        public required string T2Name { get; init; }
        public required string T2Score { get; init; }
        public required string ClubRep1 { get; init; }
        public required string ClubRep2 { get; init; }

        public static GameRow From(DataRow r) => new()
        {
            Gid = r["GID"] is int i ? i : 0,
            AgegroupName = Str(r, "agegroupName"),
            DivName = Str(r, "divName"),
            LeagueName = Str(r, "leagueName"),
            FieldName = Str(r, "fieldName"),
            Color = Str(r, "color"),
            GDay = Str(r, "gDay").Trim(),
            GDateTime = r["gDateTime"] is DateTime dt ? dt : DateTime.MinValue,
            T1Name = Str(r, "t1Name"),
            T1Score = Str(r, "t1Score"),
            T2Name = Str(r, "t2Name"),
            T2Score = Str(r, "t2Score"),
            ClubRep1 = Str(r, "clubRep1"),
            ClubRep2 = Str(r, "clubRep2"),
        };

        private static string Str(DataRow r, string col)
            => r.Table.Columns.Contains(col) && r[col] is not DBNull ? Convert.ToString(r[col]) ?? "" : "";
    }
}
