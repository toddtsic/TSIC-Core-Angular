using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) one-off renders for two American Select showcase schedule
/// reports the shared Schedule List Designer doesn't cover. Both run off the same EF game
/// query (<see cref="IReportingRepository.GetScheduleListGamesAsync"/>):
///   • FieldUtilizationWithNominations — games grouped by date+field, each game printed with a
///     boxed score AND a blank "Player Nominations" write-in grid (the nominated-players area is
///     filled by hand at the field; the data is not stored).
///   • ScheduleByClubAgTPerPage — one page per team ("Age Group / Team" header) listing that
///     team's games; a game appears on BOTH teams' pages (the schedule stores it once).
/// </summary>
public sealed class ShowcaseScheduleReportService : IShowcaseScheduleReportService
{
    private readonly IReportingRepository _reportingRepository;

    public ShowcaseScheduleReportService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // ── Page geometry (points; Letter portrait, 0.4in margins) ──
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);          // 554.4
    private const float ContentBottom = PageH - MarginBottom;
    private const float FooterH = 18f;
    private const float MaxContentY = ContentBottom - MarginTop - FooterH - 2f;

    private static readonly PdfBrush ShadowBrush = new PdfSolidBrush(new PdfColor(208, 208, 208));
    private static readonly PdfStringFormat CenterMiddle = new(PdfTextAlignment.Center, PdfVerticalAlignment.Middle);
    private static readonly PdfStringFormat LeftMiddle = new(PdfTextAlignment.Left, PdfVerticalAlignment.Middle) { LineLimit = false };
    private static readonly PdfStringFormat RightMiddle = new(PdfTextAlignment.Right, PdfVerticalAlignment.Middle) { LineLimit = false };

    // Bracket-slot types (NOT "T" = round-robin). A real seeded team prints "name (X4)".
    private static readonly HashSet<string> BracketTypes = new(StringComparer.OrdinalIgnoreCase) { "Z", "Y", "X", "Q", "S", "F" };

    // ════════════════════════════════════════════════════════════════════════════
    //  FieldUtilizationWithNominations
    // ════════════════════════════════════════════════════════════════════════════

    private const float FuTitleH = 22f;
    private const float FuBlockH = 62f;     // time/matchup line + Player Nominations + 2 box rows

    public async Task<ReportExportResult> GenerateFieldUtilizationNominationsAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var games = (await _reportingRepository.GetScheduleListGamesAsync(jobId, cancellationToken))
            .Select(GameVm.From)
            .ToList();

        // Group by (date, field); page-break per group (one field-day per page run).
        var groups = games
            .GroupBy(g => new { Day = g.When.Date, g.FieldName })
            .Select(grp => new
            {
                grp.Key.Day,
                grp.Key.FieldName,
                Games = grp.OrderBy(g => g.When).ToList(),
            })
            .OrderBy(grp => grp.Day).ThenBy(grp => grp.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var document = NewDocument();
        var pens = new Pens();
        var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 11, PdfFontStyle.Bold);
        var fieldFont = new PdfStandardFont(PdfFontFamily.Helvetica, 11, PdfFontStyle.Bold | PdfFontStyle.Underline);
        var timeFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        var teamFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8);
        var scoreFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9);
        var smallFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var labelFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.5f, PdfFontStyle.Bold);

        foreach (var group in groups)
        {
            PdfGraphics g = document.Pages.Add().Graphics;
            var y = DrawFuHeader(g, group.Day, group.FieldName, titleFont, fieldFont);

            foreach (var game in group.Games)
            {
                if (y + FuBlockH > MaxContentY)
                {
                    g = document.Pages.Add().Graphics;
                    y = DrawFuHeader(g, group.Day, group.FieldName, titleFont, fieldFont);
                }
                y = DrawFuGameBlock(g, game, y, timeFont, teamFont, scoreFont, smallFont, labelFont, pens);
            }
        }

        return Save(document, "FieldUtilizationWithNominations.pdf");
    }

    private static float DrawFuHeader(
        PdfGraphics g, DateTime day, string field, PdfStandardFont titleFont, PdfStandardFont fieldFont)
    {
        g.DrawString(day.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), titleFont, PdfBrushes.Black,
            new RectangleF(0, 0, ContentW * 0.4f, FuTitleH), LeftMiddle);
        g.DrawString(field, fieldFont, PdfBrushes.Black,
            new RectangleF(ContentW * 0.3f, 0, ContentW * 0.7f, FuTitleH), CenterMiddle);
        return FuTitleH + 4f;
    }

    private static float DrawFuGameBlock(
        PdfGraphics g, GameVm game, float y, PdfStandardFont timeFont, PdfStandardFont teamFont,
        PdfStandardFont scoreFont, PdfStandardFont smallFont, PdfStandardFont labelFont, Pens pens)
    {
        // Row 1 — time | team1 [score][score] team2
        g.DrawString(FormatWhen(game.When, withDow: true), timeFont, PdfBrushes.Black,
            new RectangleF(0, y, 120, 14), LeftMiddle);

        const float boxW = 26f, boxH = 15f;
        var box1X = (ContentW / 2f) - boxW - 1f;
        var box2X = (ContentW / 2f) + 1f;
        g.DrawString(game.T1Name, teamFont, PdfBrushes.Black, new RectangleF(125, y, box1X - 128, 14), RightMiddle);
        DrawScoreBox(g, box1X, y, boxW, boxH, game.T1Score, scoreFont, pens);
        DrawScoreBox(g, box2X, y, boxW, boxH, game.T2Score, scoreFont, pens);
        g.DrawString(game.T2Name, teamFont, PdfBrushes.Black, new RectangleF(box2X + boxW + 3, y, ContentW - (box2X + boxW + 3), 14), LeftMiddle);

        // Row 2 — agegroup:div + Player Nominations label
        var ny = y + 16f;
        g.DrawString(ComposeAgDiv(game), smallFont, PdfBrushes.Black, new RectangleF(0, ny, 120, 11), LeftMiddle);
        g.DrawString("Player Nominations", labelFont, PdfBrushes.Black, new RectangleF(0, ny + 11, 120, 11), LeftMiddle);

        // Blank nomination grid — 2 stacked write-in boxes under each team's half.
        const float rowH = 15f;
        var leftX = 130f; var rightX = (ContentW / 2f) + 8f;
        var boxColW = (ContentW / 2f) - 138f;
        for (var r = 0; r < 2; r++)
        {
            var by = ny + (r * rowH);
            g.DrawRectangle(pens.Box, new RectangleF(leftX, by, boxColW, rowH));
            g.DrawRectangle(pens.Box, new RectangleF(rightX, by, boxColW, rowH));
        }

        var bottom = y + FuBlockH;
        g.DrawLine(pens.Divider, new PointF(0, bottom - 2), new PointF(ContentW, bottom - 2));
        return bottom;
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  ScheduleByClubAgTPerPage — one page per team
    // ════════════════════════════════════════════════════════════════════════════

    private const float SbtTitleH = 30f;
    private const float SbtSubHeaderH = 26f;
    private const float SbtRowH = 24f;

    public async Task<ReportExportResult> GenerateScheduleByTeamAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var games = (await _reportingRepository.GetScheduleListGamesAsync(jobId, cancellationToken))
            .Select(GameVm.From)
            .ToList();

        // Explode each game onto BOTH teams (a team's schedule = games where it is home OR away);
        // TBD bracket slots (null id) carry no team page.
        var byTeam = new Dictionary<Guid, TeamSchedule>();
        foreach (var game in games)
        {
            if (game.T1Id is Guid t1) AddToTeam(byTeam, t1, game.AgegroupName, game.T1Name, game);
            if (game.T2Id is Guid t2) AddToTeam(byTeam, t2, game.AgegroupName, game.T2Name, game);
        }

        var teams = byTeam.Values
            .OrderBy(t => t.AgegroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var document = NewDocument();
        var pens = new Pens();
        var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
        var subFont = new PdfStandardFont(PdfFontFamily.Helvetica, 10, PdfFontStyle.Bold | PdfFontStyle.Italic);
        var cellFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8);
        var teamFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        var scoreFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9);

        foreach (var team in teams)
        {
            PdfGraphics g = document.Pages.Add().Graphics;
            var y = DrawSbtHeader(g, team, titleFont, subFont, continued: false);

            foreach (var game in team.Games.OrderBy(x => x.When))
            {
                if (y + SbtRowH > MaxContentY)
                {
                    g = document.Pages.Add().Graphics;
                    y = DrawSbtHeader(g, team, titleFont, subFont, continued: true);
                }
                y = DrawSbtRow(g, game, y, cellFont, teamFont, scoreFont, pens);
            }
        }

        if (teams.Count == 0)
        {
            var g = document.Pages.Add().Graphics;
            DrawSbtTitle(g, titleFont);
        }

        return Save(document, "ScheduleByClubAgTPerPage.pdf");
    }

    private static void AddToTeam(
        Dictionary<Guid, TeamSchedule> map, Guid teamId, string agegroup, string teamName, GameVm game)
    {
        if (!map.TryGetValue(teamId, out var sched))
        {
            sched = new TeamSchedule { AgegroupName = agegroup, TeamName = teamName, Games = new List<GameVm>() };
            map[teamId] = sched;
        }
        sched.Games.Add(game);
    }

    private static void DrawSbtTitle(PdfGraphics g, PdfStandardFont titleFont)
        => g.DrawString("Schedules by Age Group and Team", titleFont, PdfBrushes.Black,
            new RectangleF(0, 0, ContentW, SbtTitleH), CenterMiddle);

    private static float DrawSbtHeader(
        PdfGraphics g, TeamSchedule team, PdfStandardFont titleFont, PdfStandardFont subFont, bool continued)
    {
        DrawSbtTitle(g, titleFont);
        var label = $"Age Group: {team.AgegroupName}  Team: {team.TeamName}".Trim()
            + (continued ? "  (cont.)" : "");
        g.DrawString(label, subFont, PdfBrushes.Black,
            new RectangleF(0, SbtTitleH, ContentW, SbtSubHeaderH), LeftMiddle);
        return SbtTitleH + SbtSubHeaderH;
    }

    private static float DrawSbtRow(
        PdfGraphics g, GameVm game, float y, PdfStandardFont cellFont, PdfStandardFont teamFont,
        PdfStandardFont scoreFont, Pens pens)
    {
        g.DrawString(FormatWhen(game.When, withDow: false), cellFont, PdfBrushes.Black, new RectangleF(0, y, 110, SbtRowH), LeftMiddle);
        g.DrawString(game.FieldName, cellFont, PdfBrushes.Black, new RectangleF(118, y, 95, SbtRowH), LeftMiddle);

        const float boxW = 26f, boxH = 15f;
        const float box1X = 368f, box2X = 396f;
        var boxY = y + ((SbtRowH - boxH) / 2f);
        g.DrawString(game.T1Name, teamFont, PdfBrushes.Black, new RectangleF(215, y, box1X - 218, SbtRowH), RightMiddle);
        DrawScoreBox(g, box1X, boxY, boxW, boxH, game.T1Score, scoreFont, pens);
        DrawScoreBox(g, box2X, boxY, boxW, boxH, game.T2Score, scoreFont, pens);
        g.DrawString(game.T2Name, teamFont, PdfBrushes.Black, new RectangleF(box2X + boxW + 4, y, ContentW - (box2X + boxW + 4), SbtRowH), LeftMiddle);

        var bottom = y + SbtRowH;
        g.DrawLine(pens.Divider, new PointF(0, bottom), new PointF(ContentW, bottom));
        return bottom;
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Schedule_Gamecards — 2-up blank score cards grouped by field
    // ════════════════════════════════════════════════════════════════════════════

    private const float GcTitleH = 22f;
    private const float GcCardH = 212f;
    private const float GcRowPitch = 224f;

    public async Task<ReportExportResult> GenerateGameCardsAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var games = (await _reportingRepository.GetScheduleListGamesAsync(jobId, cancellationToken))
            .Select(GameVm.From)
            .ToList();

        var groups = games
            .GroupBy(g => g.FieldName)
            .Select(grp => new { Field = grp.Key, Games = grp.OrderBy(g => g.When).ToList() })
            .OrderBy(grp => grp.Field, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var document = NewDocument();
        var pens = new Pens();
        var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold | PdfFontStyle.Italic | PdfFontStyle.Underline);
        var lineFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8.5f, PdfFontStyle.Bold);
        var teamFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8.5f, PdfFontStyle.Bold);
        var labelFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        var scoreFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9);

        var rowsPerPage = Math.Max(1, (int)((MaxContentY - GcTitleH) / GcRowPitch));
        var cardsPerPage = rowsPerPage * 2;
        var cardColW = ContentW / 2f;

        foreach (var group in groups)
        {
            for (var start = 0; start < group.Games.Count; start += cardsPerPage)
            {
                var g = document.Pages.Add().Graphics;
                g.DrawString(group.Field, titleFont, PdfBrushes.Black,
                    new RectangleF(0, 0, ContentW, GcTitleH), CenterMiddle);

                var slice = group.Games.Skip(start).Take(cardsPerPage).ToList();
                for (var i = 0; i < slice.Count; i++)
                {
                    var col = i % 2;
                    var rowIdx = i / 2;
                    var x = (col * cardColW) + 2f;
                    var y = GcTitleH + (rowIdx * GcRowPitch);
                    DrawGameCard(g, slice[i], x, y, cardColW - 4f, lineFont, teamFont, labelFont, scoreFont, pens);
                }
            }
        }

        return Save(document, "Schedule_Gamecards.pdf");
    }

    private static void DrawGameCard(
        PdfGraphics g, GameVm game, float x, float y, float w, PdfStandardFont lineFont,
        PdfStandardFont teamFont, PdfStandardFont labelFont, PdfStandardFont scoreFont, Pens pens)
    {
        g.DrawRectangle(ShadowBrush, new RectangleF(x + 2.5f, y + 2.5f, w, GcCardH));
        g.DrawRectangle(pens.CardBorder, PdfBrushes.White, new RectangleF(x, y, w, GcCardH));
        var centerX = x + (w / 2f);

        var head = $"{game.FieldName}   {FormatGameCardWhen(game.When)}".Trim();
        g.DrawString(head, lineFont, PdfBrushes.Black, new RectangleF(x, y + 10, w, 14), CenterMiddle);
        g.DrawString(ComposeAgDiv(game), lineFont, PdfBrushes.Black, new RectangleF(x, y + 32, w, 14), CenterMiddle);

        g.DrawString(game.T1Name, teamFont, PdfBrushes.Black, new RectangleF(x + 6, y + 74, w - 12, 14), CenterMiddle);
        DrawScoreLine(g, centerX, y + 100, labelFont, scoreFont, pens);

        // "── VS ──" divider
        g.DrawString("VS", labelFont, PdfBrushes.Black, new RectangleF(x, y + 128, w, 12), CenterMiddle);
        g.DrawLine(pens.Divider, new PointF(x + 12, y + 134), new PointF(centerX - 14, y + 134));
        g.DrawLine(pens.Divider, new PointF(centerX + 14, y + 134), new PointF(x + w - 12, y + 134));

        g.DrawString(game.T2Name, teamFont, PdfBrushes.Black, new RectangleF(x + 6, y + 158, w - 12, 14), CenterMiddle);
        DrawScoreLine(g, centerX, y + 184, labelFont, scoreFont, pens);
    }

    private static void DrawScoreLine(
        PdfGraphics g, float centerX, float y, PdfStandardFont labelFont, PdfStandardFont scoreFont, Pens pens)
    {
        g.DrawString("Score:", labelFont, PdfBrushes.Black, new RectangleF(centerX - 48, y, 40, 16), RightMiddle);
        DrawScoreBox(g, centerX - 4, y, 44, 16, "", scoreFont, pens);
    }

    // "26-Oct-2025   8:45 am" — meridiem lowercased to match legacy.
    private static string FormatGameCardWhen(DateTime when)
        => when.ToString("d-MMM-yyyy   h:mm tt", CultureInfo.InvariantCulture).Replace(" AM", " am").Replace(" PM", " pm");

    // ── Shared ──

    private static PdfDocument NewDocument()
    {
        var document = new PdfDocument();
        document.PageSettings.Size = PdfPageSize.Letter;
        document.PageSettings.Margins.Left = MarginX;
        document.PageSettings.Margins.Right = MarginX;
        document.PageSettings.Margins.Top = MarginTop;
        document.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(document);
        return document;
    }

    private static ReportExportResult Save(PdfDocument document, string fileName)
    {
        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = fileName,
        };
    }

    private static void DrawScoreBox(
        PdfGraphics g, float x, float y, float w, float h, string score, PdfStandardFont font, Pens pens)
    {
        g.DrawRectangle(ShadowBrush, new RectangleF(x + 1.1f, y + 1.1f, w, h));
        g.DrawRectangle(pens.Box, PdfBrushes.White, new RectangleF(x, y, w, h));
        if (!string.IsNullOrWhiteSpace(score))
        {
            g.DrawString(score, font, PdfBrushes.Black, new RectangleF(x, y, w, h), CenterMiddle);
        }
    }

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

    private static string ComposeAgDiv(GameVm g)
        => string.IsNullOrWhiteSpace(g.DivName) ? g.AgegroupName : $"{g.AgegroupName}:{g.DivName}";

    // "Fri 7/25/25  8:00 am" (withDow) or "7/25/25  8:00 am" — meridiem lowercased to match legacy.
    private static string FormatWhen(DateTime when, bool withDow)
    {
        var fmt = withDow ? "ddd M/d/yy  h:mm tt" : "M/d/yy  h:mm tt";
        return when.ToString(fmt, CultureInfo.InvariantCulture).Replace(" AM", " am").Replace(" PM", " pm");
    }

    private sealed class Pens
    {
        public PdfPen Divider { get; } = new(new PdfColor(150, 150, 150), 0.5f);
        public PdfPen Box { get; } = new(new PdfColor(80, 80, 80), 0.75f);
        public PdfPen CardBorder { get; } = new(new PdfColor(0, 0, 0), 1f);
    }

    private sealed class TeamSchedule
    {
        public required string AgegroupName { get; init; }
        public required string TeamName { get; init; }
        public required List<GameVm> Games { get; init; }
    }

    private sealed record GameVm
    {
        public required int Gid { get; init; }
        public required DateTime When { get; init; }
        public required string FieldName { get; init; }
        public required string AgegroupName { get; init; }
        public required string DivName { get; init; }
        public required Guid? T1Id { get; init; }
        public required string T1Name { get; init; }
        public required string T1Score { get; init; }
        public required Guid? T2Id { get; init; }
        public required string T2Name { get; init; }
        public required string T2Score { get; init; }

        public static GameVm From(ScheduleListGameDto d) => new()
        {
            Gid = d.Gid,
            When = d.GDate ?? DateTime.MinValue,
            FieldName = (d.FieldName ?? "").Trim(),
            AgegroupName = (d.AgegroupName ?? "").Trim(),
            DivName = (d.DivName ?? "").Trim(),
            T1Id = d.T1Id,
            T1Name = ComposeTeamName(d.T1Id, d.T1Name, d.T1Type, d.T1No, d.T1Ann),
            T1Score = d.T1Score?.ToString(CultureInfo.InvariantCulture) ?? "",
            T2Id = d.T2Id,
            T2Name = ComposeTeamName(d.T2Id, d.T2Name, d.T2Type, d.T2No, d.T2Ann),
            T2Score = d.T2Score?.ToString(CultureInfo.InvariantCulture) ?? "",
        };

        private static string ComposeTeamName(Guid? id, string? name, string? type, int? no, string? ann)
        {
            if (id != null && !string.IsNullOrWhiteSpace(name))
            {
                var t = (type ?? "").Trim();
                return BracketTypes.Contains(t) && no != null ? $"{name!.Trim()} ({t}{no})" : name!.Trim();
            }
            var round = type switch
            {
                "F" => "Finals",
                "S" => "Semis",
                "Q" => "Quarters",
                "X" => "R16",
                "Y" => "R32",
                "Z" => "R64",
                _ => type ?? "",
            };
            return $"{round} {ann}".Trim();
        }
    }
}
