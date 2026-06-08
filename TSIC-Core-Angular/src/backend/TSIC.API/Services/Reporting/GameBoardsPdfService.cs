using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Renders the Game Boards report (Crystal "Schedule_ByAgegroup") as a hand-drawn Syncfusion PDF —
/// a blank game-day scoring board that officials fill in by hand. Layout reproduces the legacy report:
/// <list type="bullet">
/// <item>Grouped agegroup (name order) → division (alphabetical by divName within the agegroup).</item>
/// <item>Each division block: a centered "{agegroupName} Division:{divName}" title; a bordered
/// standings box with column headers Wins / Losses / Ties / Goals Against and one blank write-in row
/// per team (team name underlined, ordered by DivRank); then the division's games.</item>
/// <item>Each game row: Date / Time / Field, then the home team (right-aligned) and away team
/// (left-aligned) flanking two blank score boxes split by a heavy divider.</item>
/// <item>Per agegroup, after its division blocks, a "Championship Round" section (no standings) for
/// the bracket games — those with an unassigned seed-slot side. Seed types: Z = Round of 64,
/// Y = Round of 32, X = Round of 16, Q = Quarters, S = Semis, F = Finals; the slot label is the seed
/// annotation when present, else the seed name, else the raw type + number (e.g. "X7", "Q3", "F1").
/// Games sort by date, then field (so games at the same time read in field order); the rounds
/// still fall R64 → R32 → R16 → Q → S → F because earlier rounds play at earlier times.</item>
/// </list>
/// All score and standings cells print blank. EF data comes from
/// <see cref="IReportingRepository.GetScheduleListGamesAsync"/> (games, the
/// <c>reporting.Schedule_Get_AgegroupScorecard</c> replacement) and
/// <see cref="IReportingRepository.GetScheduleStandingsTeamsAsync"/> (per-division team roster, the
/// <c>reporting.Schedule_Get_DivTeamsAndStandings</c> replacement). Games group by <c>DivId</c> (the
/// primary division), never <c>Div2Id</c>.
/// </summary>
public sealed class GameBoardsPdfService : IGameBoardsPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public GameBoardsPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float FooterH = 18f;
    private const float ContentW = 612f - (MarginX * 2);                 // 554.4 (portrait Letter)
    private const float MaxY = 792f - MarginTop - MarginBottom - FooterH - 2f;

    private const float CellPadX = 4f;
    private const float TitleH = 22f;          // division / section title band
    private const float GameRowH = 17f;
    private const float StandHdrH = 14f;        // standings column-header band (above the box)
    private const float StandRowH = 17f;        // one team row inside the standings box
    private const float BlockGap = 12f;         // vertical gap after a block / section

    // Standings: team names fill the left, then four evenly-spaced blank write-in stat columns.
    private const float StandNameW = 250f;
    private const float StatBoxW = 44f, StatBoxH = 12f;
    private static readonly string[] StandStatHeaders = { "Wins", "Losses", "Ties", "Goals Against" };

    // Game row: Date | Time | Field on the left, then home (right-aligned) + two score boxes +
    // away (left-aligned) filling the remainder, symmetric about the score pair.
    private const float DateW = 88f, TimeW = 50f, FieldW = 72f;
    private const float LeftClusterW = DateW + TimeW + FieldW;            // 210
    private const float ScoreBoxW = 26f, ScoreBoxH = 13f;
    private const float ScorePairX = LeftClusterW + (((ContentW - LeftClusterW) - (ScoreBoxW * 2)) / 2f);
    private const float HomeX = LeftClusterW;
    private const float HomeW = ScorePairX - LeftClusterW - 6f;
    private const float AwayX = ScorePairX + (ScoreBoxW * 2) + 6f;
    private const float AwayW = ContentW - AwayX;

    private static readonly PdfStringFormat MidLeft = new(PdfTextAlignment.Left, PdfVerticalAlignment.Middle);
    private static readonly PdfStringFormat MidRight = new(PdfTextAlignment.Right, PdfVerticalAlignment.Middle);

    public async Task<ReportExportResult> GenerateAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // Sequential awaits — both reads share the scoped DbContext (never Task.WhenAll).
        var games = await _reportingRepository.GetScheduleListGamesAsync(jobId, cancellationToken);
        var standings = await _reportingRepository.GetScheduleStandingsTeamsAsync(jobId, cancellationToken);

        var teamsByDiv = standings
            .Where(t => t.DivId != null)
            .GroupBy(t => t.DivId!.Value)
            .ToDictionary(grp => grp.Key, grp => grp.OrderBy(t => t.DivRank).ToList());

        var doc = NewDocument();
        var fonts = new Fonts();
        var pens = new Pens();

        var g = doc.Pages.Add().Graphics;
        var y = 0f;

        if (games.Count == 0)
        {
            g.DrawString("No scheduled games for this job.", fonts.Label, PdfBrushes.Gray,
                new RectangleF(0, 0, ContentW, 18f), new PdfStringFormat(PdfTextAlignment.Left));
            return Save(doc);
        }

        // Agegroup (name order) → division blocks (real-team games, by divName) → championship round.
        foreach (var ag in games
            .GroupBy(x => x.AgegroupName ?? string.Empty)
            .OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
        {
            // Round-robin games (both sides a real team) form the division blocks, grouped by the
            // primary DivId (Team 1's division). Div2Id differs from DivId only for a cross-division
            // RR game (Team 2 drawn from another division); it is intentionally NOT the grouping key,
            // so such a game lists under Team 1's division. Championship games (a seed-slot side,
            // T1Id/T2Id null) use separate bracket mechanics and split out below, independent of Div2Id.
            var realGames = ag.Where(x => x.T1Id != null && x.T2Id != null).ToList();
            var bracketGames = ag.Where(x => x.T1Id == null || x.T2Id == null)
                .OrderBy(x => x.GDate).ThenBy(x => x.FieldName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Gid).ToList();

            foreach (var block in realGames
                .Where(x => x.DivId != null)
                .GroupBy(x => x.DivId!.Value)
                .OrderBy(b => b.First().DivName, StringComparer.OrdinalIgnoreCase))
            {
                var divName = block.First().DivName ?? string.Empty;
                var title = $"{ag.Key} Division:{divName}";
                var teams = teamsByDiv.TryGetValue(block.Key, out var t) ? t : new List<GameBoardStandingTeamDto>();
                var blockGames = block.OrderBy(x => x.GDate).ThenBy(x => x.FieldName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Gid).ToList();

                // Keep the title + standings + first game together on one page.
                var headNeed = TitleH + StandingsHeight(teams.Count) + GameRowH;
                if (y > 0 && y + headNeed > MaxY) { g = doc.Pages.Add().Graphics; y = 0f; }

                y = DrawSectionTitle(g, title, y, fonts);
                y = DrawStandings(g, teams, y, fonts, pens);

                foreach (var game in blockGames)
                {
                    if (y + GameRowH > MaxY)
                    {
                        g = doc.Pages.Add().Graphics; y = 0f;
                        y = DrawSectionTitle(g, title + "  (cont.)", y, fonts);
                    }
                    y = DrawGameRow(g, game, y, fonts, pens);
                }
                y += BlockGap;
            }

            if (bracketGames.Count > 0)
            {
                if (y > 0 && y + TitleH + GameRowH > MaxY) { g = doc.Pages.Add().Graphics; y = 0f; }
                y = DrawSectionTitle(g, "Championship Round", y, fonts);
                foreach (var game in bracketGames)
                {
                    if (y + GameRowH > MaxY)
                    {
                        g = doc.Pages.Add().Graphics; y = 0f;
                        y = DrawSectionTitle(g, "Championship Round  (cont.)", y, fonts);
                    }
                    y = DrawGameRow(g, game, y, fonts, pens);
                }
                y += BlockGap;
            }
        }

        return Save(doc);
    }

    private static float StandingsHeight(int teamCount) => StandHdrH + (teamCount * StandRowH) + 4f;

    private static float DrawSectionTitle(PdfGraphics g, string title, float y, Fonts fonts)
    {
        g.DrawString(title, fonts.Title, PdfBrushes.Black,
            new RectangleF(0, y, ContentW, TitleH),
            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        return y + TitleH;
    }

    private static float DrawStandings(PdfGraphics g, List<GameBoardStandingTeamDto> teams, float y, Fonts fonts, Pens pens)
    {
        var statColW = (ContentW - StandNameW) / StandStatHeaders.Length;

        // Column headers sit just above the box, centered over their write-in columns.
        for (var c = 0; c < StandStatHeaders.Length; c++)
        {
            g.DrawString(StandStatHeaders[c], fonts.StandHeader, PdfBrushes.Black,
                new RectangleF(StandNameW + (c * statColW), y, statColW, StandHdrH),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Bottom));
        }

        var boxTop = y + StandHdrH;
        var ty = boxTop;
        foreach (var team in teams)
        {
            g.DrawString(team.TeamFullName, fonts.TeamName, PdfBrushes.Black,
                new RectangleF(CellPadX, ty, StandNameW - (CellPadX * 2), StandRowH), MidLeft);
            for (var c = 0; c < StandStatHeaders.Length; c++)
            {
                var boxX = StandNameW + (c * statColW) + ((statColW - StatBoxW) / 2f);
                g.DrawRectangle(pens.Box, new RectangleF(boxX, ty + ((StandRowH - StatBoxH) / 2f), StatBoxW, StatBoxH));
            }
            ty += StandRowH;
        }

        // Enclosing border around the team rows.
        g.DrawRectangle(pens.Box, new RectangleF(0, boxTop, ContentW, ty - boxTop));
        return ty + 4f;
    }

    private static float DrawGameRow(PdfGraphics g, ScheduleListGameDto game, float y, Fonts fonts, Pens pens)
    {
        var date = game.GDate?.ToString("ddd dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        var time = game.GDate?.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant() ?? string.Empty;
        var home = SideLabel(game.T1Id, game.T1Name, game.T1Type, game.T1No, game.T1Ann);
        var away = SideLabel(game.T2Id, game.T2Name, game.T2Type, game.T2No, game.T2Ann);

        g.DrawString(date, fonts.Cell, PdfBrushes.Black, new RectangleF(CellPadX, y, DateW - CellPadX, GameRowH), MidLeft);
        g.DrawString(time, fonts.Cell, PdfBrushes.Black, new RectangleF(DateW, y, TimeW, GameRowH), MidLeft);
        g.DrawString(game.FieldName ?? string.Empty, fonts.Cell, PdfBrushes.Black,
            new RectangleF(DateW + TimeW, y, FieldW, GameRowH), MidLeft);

        g.DrawString(home, fonts.Cell, PdfBrushes.Black, new RectangleF(HomeX, y, HomeW, GameRowH), MidRight);
        g.DrawString(away, fonts.Cell, PdfBrushes.Black, new RectangleF(AwayX, y, AwayW, GameRowH), MidLeft);

        // Two blank score boxes split by a heavy divider (home | away).
        var boxY = y + ((GameRowH - ScoreBoxH) / 2f);
        g.DrawRectangle(pens.Box, new RectangleF(ScorePairX, boxY, ScoreBoxW, ScoreBoxH));
        g.DrawRectangle(pens.Box, new RectangleF(ScorePairX + ScoreBoxW, boxY, ScoreBoxW, ScoreBoxH));
        g.DrawLine(pens.Heavy, new PointF(ScorePairX + ScoreBoxW, boxY), new PointF(ScorePairX + ScoreBoxW, boxY + ScoreBoxH));

        g.DrawLine(pens.Divider, new PointF(0, y + GameRowH), new PointF(ContentW, y + GameRowH));
        return y + GameRowH;
    }

    // Real team → the schedule's denormalized "club:team" name. Bracket slot (no team id) →
    // the proc's coalesce: annotation, else the seed name, else the raw seed type + number.
    private static string SideLabel(Guid? id, string? name, string? type, int? no, string? ann)
    {
        if (id != null) return name ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(ann)) return ann!;
        if (!string.IsNullOrWhiteSpace(name)) return name!;
        return (type ?? string.Empty) + (no?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
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
        AddFooterTemplate(doc);
        return doc;
    }

    private static void AddFooterTemplate(PdfDocument document)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8);
        var bold = new PdfStandardFont(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        var gray = new PdfSolidBrush(new PdfColor(90, 90, 90));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, ContentW, FooterH));

        // Left: Page X of Y.
        var page = new PdfCompositeField(footerFont, gray, "Page {0} of {1}",
            new PdfPageNumberField(footerFont, gray), new PdfPageCountField(footerFont, gray))
        {
            Bounds = new RectangleF(0, 4, 150f, FooterH),
            StringFormat = new PdfStringFormat(PdfTextAlignment.Left),
        };
        page.Draw(footer.Graphics, new PointF(0, 4));

        // Center: brand.
        footer.Graphics.DrawString("Reports by TeamSportsInfo.com", bold, gray,
            new RectangleF(0, 4, ContentW, 12f), new PdfStringFormat(PdfTextAlignment.Center));

        // Right: print date + time (matches the legacy Crystal footer stamp).
        var stamp = DateTime.Now.ToString("M/d/yyyy    h:mm:sstt", CultureInfo.InvariantCulture);
        footer.Graphics.DrawString(stamp, footerFont, gray,
            new RectangleF(ContentW - 180f, 4, 180f, 12f), new PdfStringFormat(PdfTextAlignment.Right));

        document.Template.Bottom = footer;
    }

    private static ReportExportResult Save(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms);
        doc.Close(true);
        return new ReportExportResult { FileBytes = ms.ToArray(), ContentType = "application/pdf", FileName = "GameBoards.pdf" };
    }

    private sealed class Fonts
    {
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
        public PdfStandardFont StandHeader { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
        public PdfStandardFont TeamName { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Underline);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 8.5f);
        public PdfStandardFont Label { get; } = new(PdfFontFamily.Helvetica, 9);
    }

    private sealed class Pens
    {
        public PdfPen Box { get; } = new(new PdfColor(0, 0, 0), 0.75f);
        public PdfPen Divider { get; } = new(new PdfColor(0, 0, 0), 0.5f);
        public PdfPen Heavy { get; } = new(new PdfColor(0, 0, 0), 2f);
    }
}
