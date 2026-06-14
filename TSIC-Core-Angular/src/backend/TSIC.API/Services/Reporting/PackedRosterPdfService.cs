using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn packed-roster PDF (Syncfusion.Pdf). Reproduces the Pattern A geometry the
/// legacy Bold RDLs encode in nested Tablix groups — full-width division title, then an
/// N-up newspaper grid of fixed team cards — but driven entirely by a runtime config
/// instead of a per-report RDL. Data comes from a single EF query
/// (IReportingRepository.GetTournamentRosterRowsAsync) — the unshaped field superset,
/// shaped in C# at map time (RosterRow.FromDto).
/// </summary>
public sealed class PackedRosterPdfService : IPackedRosterPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public PackedRosterPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // ── Page + card geometry (points; mirrors the RDL: Letter, 0.4in L/R margins,
    //    554pt body, 320pt card rows, 314pt card height) ──────────────────────────
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 18f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);   // 554.4
    private const float ContentH = PageH - MarginTop - MarginBottom; // 745.2
    private const float FooterH = 18f;       // bottom page-template band; eats into the drawable client area
    private const float TitleH = 22f;
    private const float CardGap = 3f;        // L/R/T gap of cardOuter inside its grid cell
    private const float CardOuterH = 314f;
    private const float CardRowH = 320f;     // cardOuter + vertical gap
    private const float HeaderH = 14f, RepLineH = 12f, UnderlineY = 26f, RosterTop = 28f;
    private const float RosterH = 282f;      // inner roster max height inside the card
    private const float BaseRowH = 11f;
    private const float InnerInsetL = 4f, InnerInsetR = 2f; // innerRoster inset within cardOuter

    public IReadOnlyList<PackedRosterFieldDto> GetAvailableFields() => AvailableFields;

    // Defaults seeded from the two legacy RDL column sets (base 3-up + CC 2-up).
    private static readonly IReadOnlyList<PackedRosterFieldDto> AvailableFields = new[]
    {
        new PackedRosterFieldDto { Key = "uniform_no",    Label = "Uniform #",       DefaultWidthWeight = 20, DefaultAlign = "Right",  SupportsLongText = false },
        new PackedRosterFieldDto { Key = "player",        Label = "Name",            DefaultWidthWeight = 90, DefaultAlign = "Left",   SupportsLongText = false },
        new PackedRosterFieldDto { Key = "position",      Label = "Position",        DefaultWidthWeight = 32, DefaultAlign = "Left",   SupportsLongText = false },
        new PackedRosterFieldDto { Key = "school_name",   Label = "School",          DefaultWidthWeight = 42, DefaultAlign = "Left",   SupportsLongText = true  },
        new PackedRosterFieldDto { Key = "gradYear",      Label = "Grad Yr",         DefaultWidthWeight = 26, DefaultAlign = "Center", SupportsLongText = false },
        new PackedRosterFieldDto { Key = "gpa",           Label = "GPA",             DefaultWidthWeight = 22, DefaultAlign = "Center", SupportsLongText = false },
        new PackedRosterFieldDto { Key = "collegeCommit", Label = "College Commit",  DefaultWidthWeight = 57, DefaultAlign = "Right",  SupportsLongText = true  },
        new PackedRosterFieldDto { Key = "dayGroup",      Label = "Day Group",       DefaultWidthWeight = 26, DefaultAlign = "Center", SupportsLongText = false },
    };

    public async Task<ReportExportResult> GenerateAsync(
        PackedRosterRequestDto request,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var nUp = request.NUp is 2 or 3 ? request.NUp : 3;

        // Single EF query returns the unshaped superset; we sort it to the legacy proc's
        // ORDER BY (agegroup → div → club:team → teamId, then Staff-first, uniform#, name)
        // and shape each row in C#. Replaces reporting_migrate.TournamentRosterPacked_Flat.
        var ordered = (await _reportingRepository.GetTournamentRosterRowsAsync(jobId, request.RequiresSchedule, cancellationToken))
            .OrderBy(d => d.AgegroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.DivName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => ComposeClubTeam(d), StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.TeamId)
            .ThenBy(d => string.Equals(d.RoleName, "Staff", StringComparison.OrdinalIgnoreCase) ? 0 : 1);

        // Staff sort first (above); players then order by the chosen key. "Position" reproduces
        // the by-position grouping of the PackedByPosition family; default is uniform # (proc order).
        ordered = request.SortBy switch
        {
            "Position" => ordered
                .ThenBy(d => (d.Position ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => UniformSort(d.UniformNo))
                .ThenBy(d => d.LastName, StringComparer.OrdinalIgnoreCase),
            "Name" => ordered
                .ThenBy(d => d.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.FirstName, StringComparer.OrdinalIgnoreCase),
            _ => ordered
                .ThenBy(d => UniformSort(d.UniformNo))
                .ThenBy(d => d.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.FirstName, StringComparer.OrdinalIgnoreCase),
        };

        var rows = ordered.Select(RosterRow.FromDto).ToList();

        using var document = new PdfDocument();
        document.PageSettings.Size = PdfPageSize.Letter;
        document.PageSettings.Margins.Left = MarginX;
        document.PageSettings.Margins.Right = MarginX;
        document.PageSettings.Margins.Top = MarginTop;
        document.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(document);

        var blackPen = new PdfPen(new PdfColor(0, 0, 0), 0.75f);
        var dividerPen = new PdfPen(new PdfColor(136, 136, 136), 0.5f);
        var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
        var headerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        var repFont = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);
        var keyFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7, PdfFontStyle.Bold); // uniform + name
        var cellFont = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);

        var rowsPerPage = Math.Max(1, (int)((ContentH - TitleH) / CardRowH));
        var cardsPerPage = nUp * rowsPerPage;
        var cardColW = ContentW / nUp;
        var cardOuterW = cardColW - (CardGap * 2);

        // Divisions print in proc order (proc sorts agegroupName, divName); each division
        // starts on a fresh page (RDL PageBreak=Between) and repeats its title when it
        // overflows (RDL RepeatOnNewPage).
        foreach (var division in rows.GroupBy(r => r.AgDiv))
        {
            var cards = division
                .GroupBy(r => r.TeamId)
                .OrderBy(g => g.First().ClubTeamName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key)
                .Select(g => g.Where(r => request.ShowCoaches || !r.IsStaff).ToList())
                .Where(card => card.Count > 0)
                .ToList();

            if (cards.Count == 0)
            {
                continue;
            }

            for (var start = 0; start < cards.Count; start += cardsPerPage)
            {
                var page = document.Pages.Add();
                var g = page.Graphics;

                g.DrawString(
                    division.Key,
                    titleFont,
                    PdfBrushes.Black,
                    new RectangleF(0, 0, ContentW, TitleH),
                    new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));

                var slice = cards.Skip(start).Take(cardsPerPage).ToList();
                for (var i = 0; i < slice.Count; i++)
                {
                    var col = i % nUp;
                    var rowIdx = i / nUp;
                    var x = (col * cardColW) + CardGap;
                    var y = TitleH + (rowIdx * CardRowH) + CardGap;
                    DrawCard(g, slice[i], x, y, cardOuterW, request,
                        blackPen, dividerPen, headerFont, repFont, keyFont, cellFont);
                }
            }
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = "PackedRoster.pdf",
        };
    }

    private void DrawCard(
        PdfGraphics g,
        List<RosterRow> cardRows,
        float x,
        float y,
        float cardOuterW,
        PackedRosterRequestDto request,
        PdfPen blackPen,
        PdfPen dividerPen,
        PdfStandardFont headerFont,
        PdfStandardFont repFont,
        PdfStandardFont keyFont,
        PdfStandardFont cellFont)
    {
        var first = cardRows[0];

        // Card border
        g.DrawRectangle(blackPen, new RectangleF(x, y, cardOuterW, CardOuterH));

        // Team header
        g.DrawString(
            first.ClubTeamName,
            headerFont,
            PdfBrushes.Black,
            new RectangleF(x, y, cardOuterW, HeaderH),
            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));

        // Rep line (toggle-controlled)
        var repLine = BuildRepLine(first, request);
        if (repLine.Length > 0)
        {
            g.DrawString(
                repLine,
                repFont,
                PdfBrushes.Black,
                new RectangleF(x + 2, y + HeaderH, cardOuterW - 4, RepLineH),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        }

        // Underline under the header/rep block
        g.DrawLine(blackPen, new PointF(x, y + UnderlineY), new PointF(x + cardOuterW, y + UnderlineY));

        // Inner roster grid
        var ix = x + InnerInsetL;
        var iy = y + RosterTop;
        var iw = cardOuterW - InnerInsetL - InnerInsetR;
        var sumW = request.Columns.Sum(c => Math.Max(1, c.WidthWeight));

        var cursorY = iy;
        for (var r = 0; r < cardRows.Count; r++)
        {
            var row = cardRows[r];
            var isLast = r == cardRows.Count - 1;

            // Row height — grow for any wrapping column whose text needs >1 line.
            var rowH = BaseRowH;
            foreach (var col in request.Columns)
            {
                if (!string.Equals(col.LongText, "Wrap", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var cw = Math.Max(1, col.WidthWeight) / (float)sumW * iw;
                var text = ResolveCell(row, col, request);
                if (text.Length == 0)
                {
                    continue;
                }
                var font = FontFor(col, keyFont, cellFont);
                var sz = font.MeasureString(text, cw, WrapFormat(col, row));
                rowH = Math.Max(rowH, sz.Height + 2f);
            }

            // Fixed card: stop drawing rows that would spill past the roster box.
            if (cursorY - iy + rowH > RosterH)
            {
                break;
            }

            var cx = ix;
            foreach (var col in request.Columns)
            {
                var cw = Math.Max(1, col.WidthWeight) / (float)sumW * iw;
                var text = ResolveCell(row, col, request);
                if (text.Length > 0)
                {
                    var font = FontFor(col, keyFont, cellFont);
                    g.DrawString(text, font, PdfBrushes.Black,
                        new RectangleF(cx + 1, cursorY + 1, cw - 2, rowH - 1),
                        WrapFormat(col, row));
                }
                cx += cw;
            }

            if (!isLast)
            {
                g.DrawLine(dividerPen, new PointF(ix, cursorY + rowH), new PointF(ix + iw, cursorY + rowH));
            }

            cursorY += rowH;
        }
    }

    private static PdfStandardFont FontFor(PackedRosterColumnDto col, PdfStandardFont keyFont, PdfStandardFont cellFont)
        => col.Key is "uniform_no" or "player" ? keyFont : cellFont;

    private static PdfStringFormat WrapFormat(PackedRosterColumnDto col, RosterRow row)
    {
        var align = col.Align;
        // Staff phone in the school column is right-aligned (legacy base RDL).
        if (col.Key == "school_name" && row.IsStaff)
        {
            align = "Right";
        }

        return new PdfStringFormat(ParseAlign(align), PdfVerticalAlignment.Top)
        {
            WordWrap = string.Equals(col.LongText, "Wrap", StringComparison.OrdinalIgnoreCase)
                ? PdfWordWrapType.Word
                : PdfWordWrapType.None,
            LineLimit = false,
        };
    }

    private static PdfTextAlignment ParseAlign(string align) => align switch
    {
        "Right" => PdfTextAlignment.Right,
        "Center" => PdfTextAlignment.Center,
        _ => PdfTextAlignment.Left,
    };

    /// <summary>
    /// Resolves a cell's text for the given row + column, honoring the role overloads
    /// the proc bakes in (Staff rows carry phone in school_name; grad/gpa/commit are
    /// player-only) and the Designer's commit-display toggle.
    /// </summary>
    private static string ResolveCell(RosterRow row, PackedRosterColumnDto col, PackedRosterRequestDto request)
    {
        string value = col.Key switch
        {
            "uniform_no" => row.IsStaff ? "" : row.UniformNo,
            "player" => ResolveName(row, request),
            "position" => row.IsStaff ? "" : row.Position,
            "school_name" => ResolveSchool(row, request),
            "gradYear" => row.IsStaff ? "" : row.GradYear,
            // GPA shows for every player, committed included (verified vs the by-School legacy
            // export — e.g. "… 4.7 Shenandoah"); only staff rows blank it.
            "gpa" => row.IsStaff ? "" : row.Gpa,
            "collegeCommit" => row.IsStaff ? "" : row.CollegeCommit,
            "dayGroup" => row.IsStaff ? "" : row.DayGroup,
            _ => "",
        };

        if (value.Length > 0 && string.Equals(col.LongText, "Truncate", StringComparison.OrdinalIgnoreCase))
        {
            var at = col.TruncateAt ?? 14;
            if (at > 0 && value.Length > at)
            {
                value = value[..at];
            }
        }

        return value;
    }

    private static string ResolveName(RosterRow row, PackedRosterRequestDto request)
    {
        if (row.IsStaff)
        {
            return "Coach " + row.Player;
        }
        // Asterisk marks committed players only when the school column ISN'T already
        // showing their commit (legacy base behavior). SchoolShowsCommit retires it.
        var showAsterisk = row.IsCommitted && !request.SchoolShowsCommit;
        var name = showAsterisk ? "* " + row.Player : row.Player;
        // PackedByPosition appends the player's own club ("NAME / CLUB"); the No-Club sibling
        // (toggle off) prints plain names. Staff are handled above, so never affiliated here.
        if (request.ShowClubAffiliation && row.ClubAffiliation.Length > 0)
        {
            name = $"{name} / {row.ClubAffiliation}";
        }
        return name;
    }

    private static string ResolveSchool(RosterRow row, PackedRosterRequestDto request)
    {
        if (row.IsStaff)
        {
            return row.SchoolName; // proc overloads school_name with the staff phone
        }
        if (request.SchoolShowsCommit && row.IsCommitted)
        {
            return row.CollegeCommit;
        }
        return row.SchoolName;
    }

    private static string BuildRepLine(RosterRow row, PackedRosterRequestDto request)
    {
        var parts = new List<string>(3);
        if (request.ShowRepName && row.ClubRepName.Length > 0)
        {
            parts.Add(row.ClubRepName);
        }
        if (request.ShowRepEmail && row.ClubRepEmail.Length > 0)
        {
            parts.Add(row.ClubRepEmail);
        }
        if (request.ShowRepPhone && row.ClubRepCellphone.Length > 0)
        {
            parts.Add(row.ClubRepCellphone);
        }
        return string.Join(" ", parts);
    }

    private static void AddFooterTemplate(PdfDocument document)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var gray = new PdfSolidBrush(new PdfColor(102, 102, 102));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, ContentW, FooterH));

        footer.Graphics.DrawString("Reports By TeamSportsInfo.com", footerFont, gray, new PointF(2, 4));

        // Right-aligned page number. A PdfCompositeField only honors its StringFormat
        // alignment within its Bounds — drawn at a bare point it renders left-aligned, so
        // "Page X / Y" overflows the template's right edge and the total gets clipped.
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

    // ════════════════════════════════════════════════════════════════════════════
    //  Recruiter report (player-as-card) — reproduces the legacy LFTC Recruiters PDF
    //  off the SAME EF roster query (IReportingRepository.GetTournamentRosterRowsAsync).
    //  Topology differs from the packed roster: page = TEAM (header = "agegroup div
    //  club:team" + a coach contact line); each card = ONE player, 2-up, with the
    //  recruiting field set (name+grad / GPA+SAT, email, address, phone, club/HS, and
    //  an italic right-aligned college commit for committed players).
    // ════════════════════════════════════════════════════════════════════════════

    private const float RecTitleH = 34f;       // team title line + coach contact line
    private const float RecCardRowH = 76f;     // card pitch — sized so 9 rows + the footer band fit one page
    private const float RecCardOuterH = 70f;   // card height (holds the 5 recruiting lines: 3pt pad + 5×12.5)
    private const float UslCardOuterH = 64f;   // USL stat card (4 left lines + 3-row blank G/A·GB/DC·S grid)

    public async Task<ReportExportResult> GenerateRecruiterAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetTournamentRosterRowsAsync(jobId, cancellationToken: cancellationToken);

        using var document = new PdfDocument();
        document.PageSettings.Size = PdfPageSize.Letter;
        document.PageSettings.Margins.Left = MarginX;
        document.PageSettings.Margins.Right = MarginX;
        document.PageSettings.Margins.Top = MarginTop;
        document.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(document);

        var blackPen = new PdfPen(new PdfColor(0, 0, 0), 0.75f);
        var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 10, PdfFontStyle.Bold);
        var coachFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.5f);
        var nameFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.5f, PdfFontStyle.Bold);
        var acadFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var cellFont = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);
        var commitFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.5f, PdfFontStyle.Bold | PdfFontStyle.Italic);

        // Reserve the footer band so the last card row never renders under the footer
        // (Syncfusion's bottom page template eats into the page's drawable client area).
        var rowsPerPage = Math.Max(1, (int)((ContentH - FooterH - RecTitleH) / RecCardRowH));
        var cardsPerPage = rowsPerPage * 2;
        var cardColW = ContentW / 2f;
        var cardOuterW = cardColW - (CardGap * 2);

        // Group by team; cards are PLAYER rows only (Staff carry no recruiting data). Order
        // teams agegroup→div→club:team→teamId, players by uniform# then name (matches the
        // legacy proc's ORDER BY). One team's overflow repeats the header on each new page.
        var teams = rows
            .GroupBy(r => r.TeamId)
            .Select(grp => new
            {
                Header = grp.First(),
                Players = grp
                    .Where(r => string.Equals(r.RoleName, "Player", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => UniformSort(r.UniformNo))
                    .ThenBy(r => r.LastName)
                    .ThenBy(r => r.FirstName)
                    .ToList(),
            })
            .Where(t => t.Players.Count > 0)
            .OrderBy(t => t.Header.AgegroupName)
            .ThenBy(t => t.Header.DivName)
            .ThenBy(t => ComposeClubTeam(t.Header))
            .ThenBy(t => t.Header.TeamId)
            .ToList();

        foreach (var team in teams)
        {
            for (var start = 0; start < team.Players.Count; start += cardsPerPage)
            {
                var page = document.Pages.Add();
                var g = page.Graphics;
                DrawRecruiterHeader(g, team.Header, titleFont, coachFont);

                var slice = team.Players.Skip(start).Take(cardsPerPage).ToList();
                for (var i = 0; i < slice.Count; i++)
                {
                    var col = i % 2;
                    var rowIdx = i / 2;
                    var x = (col * cardColW) + CardGap;
                    var y = RecTitleH + (rowIdx * RecCardRowH) + CardGap;
                    DrawRecruiterCard(g, slice[i], x, y, cardOuterW,
                        blackPen, nameFont, acadFont, cellFont, commitFont);
                }
            }
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = "RecruiterReport.pdf",
        };
    }

    private static void DrawRecruiterHeader(
        PdfGraphics g, TournamentRosterRowDto h, PdfStandardFont titleFont, PdfStandardFont coachFont)
    {
        // Legacy header is agegroup + club:team (no division — agegroupName already carries
        // the "2027 A"-style label; the division is an internal bracket qualifier).
        var title = $"{h.AgegroupName}  {ComposeClubTeam(h)}".Trim();
        g.DrawString(title, titleFont, PdfBrushes.Black,
            new RectangleF(0, 0, ContentW, 16),
            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));

        var coach = ComposeCoachLine(h);
        if (coach.Length > 0)
        {
            g.DrawString(coach, coachFont, PdfBrushes.Black,
                new RectangleF(0, 16, ContentW, 14),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        }
    }

    private static void DrawRecruiterCard(
        PdfGraphics g, TournamentRosterRowDto row, float x, float y, float w,
        PdfPen blackPen, PdfStandardFont nameFont, PdfStandardFont acadFont,
        PdfStandardFont cellFont, PdfStandardFont commitFont)
    {
        g.DrawRectangle(blackPen, new RectangleF(x, y, w, RecCardOuterH));

        var leftTop = new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top) { LineLimit = false };
        var rightTop = new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Top) { LineLimit = false };

        const float pad = 4f;
        const float lineH = 12.5f;
        var lx = x + pad;
        var innerW = w - (pad * 2);
        var cy = y + 3f;

        // Line 1 — "# {uni}   {NAME} ({grad})" | "GPA: x    SAT: y"
        var uni = CleanUniform(row.UniformNo);
        var name = $"{row.FirstName} {row.LastName}".Trim().ToUpperInvariant();
        var grad = string.IsNullOrWhiteSpace(row.GradYear) ? "" : $" ({row.GradYear})";
        g.DrawString($"# {uni}   {name}{grad}".Trim(), nameFont, PdfBrushes.Black,
            new RectangleF(lx, cy, innerW * 0.60f, lineH), leftTop);

        var gpa = string.IsNullOrWhiteSpace(row.Gpa) ? "" : $"GPA: {row.Gpa}";
        var acad = $"{gpa}    SAT: {ComputeSat(row)}".Trim();
        g.DrawString(acad, acadFont, PdfBrushes.Black,
            new RectangleF(x + (innerW * 0.58f), cy, innerW * 0.42f, lineH), rightTop);
        cy += lineH;

        // Line 2 — email
        g.DrawString((FirstNonBlank(row.PlayerEmail, row.FamilyEmail) ?? "").Trim(), cellFont,
            PdfBrushes.Black, new RectangleF(lx, cy, innerW, lineH), leftTop);
        cy += lineH;

        // Line 3 — address
        g.DrawString(ComposeAddress(row), cellFont, PdfBrushes.Black,
            new RectangleF(lx, cy, innerW, lineH), leftTop);
        cy += lineH;

        // Line 4 — phone
        g.DrawString(FormatPhone(FirstNonBlank(row.Cellphone, row.MomCellphone)), cellFont,
            PdfBrushes.Black, new RectangleF(lx, cy, innerW, lineH), leftTop);
        cy += lineH;

        // Line 5 — "CLUB / SCHOOL" | italic college commit (committed players only)
        g.DrawString(ComposeClubSchool(row), cellFont, PdfBrushes.Black,
            new RectangleF(lx, cy, innerW * 0.62f, lineH), leftTop);
        if (row.BCollegeCommit == true && !string.IsNullOrWhiteSpace(row.CollegeCommit))
        {
            g.DrawString(row.CollegeCommit, commitFont, PdfBrushes.Black,
                new RectangleF(x + (innerW * 0.42f), cy - (lineH * 0.4f), innerW * 0.58f, lineH * 1.4f),
                new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle) { LineLimit = false });
        }
    }

    // ── Recruiter shaping helpers (the C# home of what the proc used to bake in) ──

    private static string CleanUniform(string? u)
        => string.IsNullOrWhiteSpace(u) ? "" : u.Replace("#", "").Trim();

    private static int UniformSort(string? u)
        => int.TryParse(CleanUniform(u), out var n) ? n : int.MaxValue;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// SAT = SatMath + SatVerbal + SatWriting when all three parse. NOTE: the legacy proc's
    /// condition was inverted (required satVerbal IS NULL), so the CR report showed SAT blank
    /// almost everywhere. SUM is the decided behavior — user confirmed "SAT should be sum"
    /// 2026-06-05 (faithful-to-legacy blank was the alternative, declined).
    /// </summary>
    private static string ComputeSat(TournamentRosterRowDto r)
        => int.TryParse(r.SatMath, out var m) && int.TryParse(r.SatVerbal, out var v) && int.TryParse(r.SatWriting, out var w)
            ? (m + v + w).ToString()
            : "";

    private static string FormatPhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 10
            ? $"{digits[..3]}-{digits.Substring(3, 3)}-{digits[6..]}"
            : (phone ?? "").Trim();
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  American Select recruiting reports (ASL / USL) — two card layouts over the
    //  SAME EF roster query, grouped by AGEGROUP (not team): page title = agegroup,
    //  STAFF first then players (2-up newspaper grid), players sorted by uniform#
    //  across every team in the agegroup. Showcase scope (requiresSchedule:false),
    //  same as the American Select main-event rosters.
    //    • ASL = college-recruiter CONTACT sheet (boxed staff contact cards; player
    //      cards = name+grad / GPA+SAT, email, address, phone+position-club, school).
    //    • USL = blank STAT-CAPTURE sheet (unboxed "Coach …" lines; player cards =
    //      name+grad / POSITION-CLUB / CITY,ST / SCHOOL on the left + a blank
    //      G:/A: · GB:/DC: · S: hand-entry grid on the right — the stats are not in
    //      the DB; coaches record them by hand on the printed sheet).
    //  NOTE: the agegroup title composition is inferred from the legacy sample PDFs
    //  (the source job is not on this restore) — verify it on the first live render.
    // ════════════════════════════════════════════════════════════════════════════

    private sealed record RecruitGroup(TournamentRosterRowDto Header, List<TournamentRosterRowDto> Entries, int PlayerCount);

    private static bool IsStaffRow(TournamentRosterRowDto r)
        => string.Equals(r.RoleName, "Staff", StringComparison.OrdinalIgnoreCase);

    private static List<RecruitGroup> GroupRecruitByAgegroup(List<TournamentRosterRowDto> rows)
    {
        var oic = StringComparer.OrdinalIgnoreCase;
        return rows
            .GroupBy(r => new { Ag = r.AgegroupName ?? "", Dv = r.DivName ?? "" })
            .Select(grp =>
            {
                var staff = grp.Where(IsStaffRow)
                    .OrderBy(r => r.LastName, oic).ThenBy(r => r.FirstName, oic).ToList();
                var players = grp.Where(r => !IsStaffRow(r))
                    .OrderBy(r => UniformSort(r.UniformNo))
                    .ThenBy(r => r.LastName, oic).ThenBy(r => r.FirstName, oic).ToList();
                return new RecruitGroup(grp.First(), staff.Concat(players).ToList(), players.Count);
            })
            .Where(g => g.PlayerCount > 0)
            .OrderBy(g => g.Header.AgegroupName, oic)
            .ThenBy(g => g.Header.DivName, oic)
            .ToList();
    }

    private static void DrawRecruitTitle(PdfGraphics g, TournamentRosterRowDto h, PdfStandardFont titleFont)
    {
        var ag = (h.AgegroupName ?? "").Trim();
        var dv = (h.DivName ?? "").Trim();
        var title = dv.Length > 0 && !ag.Contains(dv, StringComparison.OrdinalIgnoreCase)
            ? $"{ag}  {dv}"
            : ag;
        g.DrawString(title, titleFont, PdfBrushes.Black,
            new RectangleF(0, 0, ContentW, 16),
            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
    }

    public async Task<ReportExportResult> GenerateRecruiterAslAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetTournamentRosterRowsAsync(jobId, requiresSchedule: false, cancellationToken);
        var groups = GroupRecruitByAgegroup(rows);

        using var document = NewRecruitDocument();
        var blackPen = new PdfPen(new PdfColor(0, 0, 0), 0.75f);
        var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 10, PdfFontStyle.Bold);
        var nameFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.5f, PdfFontStyle.Bold);
        var acadFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var cellFont = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);

        RenderRecruitGrid(document, groups, titleFont, RecCardRowH, (g, row, x, y, w) =>
        {
            if (IsStaffRow(row))
                DrawAslStaffCard(g, row, x, y, w, blackPen, nameFont, cellFont);
            else
                DrawAslPlayerCard(g, row, x, y, w, blackPen, nameFont, acadFont, cellFont);
        });

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = "TournamentRecruitingReportASL.pdf",
        };
    }

    public async Task<ReportExportResult> GenerateRecruiterUslAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetTournamentRosterRowsAsync(jobId, requiresSchedule: false, cancellationToken);
        var groups = GroupRecruitByAgegroup(rows);

        using var document = NewRecruitDocument();
        var blackPen = new PdfPen(new PdfColor(0, 0, 0), 0.75f);
        var dotPen = new PdfPen(new PdfColor(0, 0, 0), 0.75f) { DashStyle = PdfDashStyle.Dot };
        var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 10, PdfFontStyle.Bold);
        var nameFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.5f, PdfFontStyle.Bold);
        var cellFont = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);
        var statFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.5f, PdfFontStyle.Bold);

        RenderRecruitGrid(document, groups, titleFont, RecCardRowH, (g, row, x, y, w) =>
        {
            if (IsStaffRow(row))
                DrawUslCoachCell(g, row, x, y, cellFont);
            else
                DrawUslPlayerCard(g, row, x, y, w, blackPen, dotPen, nameFont, cellFont, statFont);
        });

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = "TournamentRecruitingReportUSL.pdf",
        };
    }

    private static PdfDocument NewRecruitDocument()
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

    // Shared agegroup-grouped 2-up grid: title per page (repeated on overflow), then
    // staff+player entries flow left→right / top→bottom. The per-entry draw delegate
    // decides staff-vs-player rendering (the two reports differ only in the cell art).
    private static void RenderRecruitGrid(
        PdfDocument document, List<RecruitGroup> groups, PdfStandardFont titleFont,
        float rowH, Action<PdfGraphics, TournamentRosterRowDto, float, float, float> drawCell)
    {
        var rowsPerPage = Math.Max(1, (int)((ContentH - FooterH - RecTitleH) / rowH));
        var cardsPerPage = rowsPerPage * 2;
        var cardColW = ContentW / 2f;
        var cardOuterW = cardColW - (CardGap * 2);

        foreach (var group in groups)
        {
            for (var start = 0; start < group.Entries.Count; start += cardsPerPage)
            {
                var page = document.Pages.Add();
                var g = page.Graphics;
                DrawRecruitTitle(g, group.Header, titleFont);

                var slice = group.Entries.Skip(start).Take(cardsPerPage).ToList();
                for (var i = 0; i < slice.Count; i++)
                {
                    var col = i % 2;
                    var rowIdx = i / 2;
                    var x = (col * cardColW) + CardGap;
                    var y = RecTitleH + (rowIdx * rowH) + CardGap;
                    drawCell(g, slice[i], x, y, cardOuterW);
                }
            }
        }
    }

    // ── ASL (contact sheet) cells ────────────────────────────────────────────────

    private static void DrawAslStaffCard(
        PdfGraphics g, TournamentRosterRowDto row, float x, float y, float w,
        PdfPen blackPen, PdfStandardFont nameFont, PdfStandardFont cellFont)
    {
        g.DrawRectangle(blackPen, new RectangleF(x, y, w, RecCardOuterH));
        var leftTop = new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top) { LineLimit = false };
        const float pad = 4f, lineH = 12.5f;
        var lx = x + pad;
        var innerW = w - (pad * 2);
        var cy = y + 3f;

        var name = $"{row.FirstName} {row.LastName}".Trim().ToUpperInvariant();
        g.DrawString($"Staff:  {name}", nameFont, PdfBrushes.Black, new RectangleF(lx, cy, innerW, lineH), leftTop);
        cy += lineH;
        g.DrawString((FirstNonBlank(row.PlayerEmail, row.FamilyEmail) ?? "").Trim(), cellFont,
            PdfBrushes.Black, new RectangleF(lx, cy, innerW, lineH), leftTop);
        cy += lineH;
        g.DrawString(ComposeAddress(row), cellFont, PdfBrushes.Black, new RectangleF(lx, cy, innerW, lineH), leftTop);
        cy += lineH;
        g.DrawString(FormatPhone(FirstNonBlank(row.Cellphone, row.MomCellphone)), cellFont,
            PdfBrushes.Black, new RectangleF(lx, cy, innerW, lineH), leftTop);
    }

    private static void DrawAslPlayerCard(
        PdfGraphics g, TournamentRosterRowDto row, float x, float y, float w,
        PdfPen blackPen, PdfStandardFont nameFont, PdfStandardFont acadFont, PdfStandardFont cellFont)
    {
        g.DrawRectangle(blackPen, new RectangleF(x, y, w, RecCardOuterH));
        var leftTop = new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top) { LineLimit = false };
        var rightTop = new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Top) { LineLimit = false };
        const float pad = 4f, lineH = 12.5f;
        var lx = x + pad;
        var innerW = w - (pad * 2);
        var cy = y + 3f;

        // L1 — "# uni   NAME (grad)" | "GPA: x    SAT: y"
        var uni = CleanUniform(row.UniformNo);
        var name = $"{row.FirstName} {row.LastName}".Trim().ToUpperInvariant();
        var grad = string.IsNullOrWhiteSpace(row.GradYear) ? "" : $" ({row.GradYear})";
        g.DrawString($"# {uni}   {name}{grad}".Trim(), nameFont, PdfBrushes.Black,
            new RectangleF(lx, cy, innerW * 0.60f, lineH), leftTop);
        var gpa = string.IsNullOrWhiteSpace(row.Gpa) ? "" : $"GPA: {row.Gpa}";
        g.DrawString($"{gpa}    SAT: {ComputeSat(row)}".Trim(), acadFont, PdfBrushes.Black,
            new RectangleF(x + (innerW * 0.58f), cy, innerW * 0.42f, lineH), rightTop);
        cy += lineH;

        // L2 — email
        g.DrawString((FirstNonBlank(row.PlayerEmail, row.FamilyEmail) ?? "").Trim(), cellFont,
            PdfBrushes.Black, new RectangleF(lx, cy, innerW, lineH), leftTop);
        cy += lineH;

        // L3 — address
        g.DrawString(ComposeAddress(row), cellFont, PdfBrushes.Black, new RectangleF(lx, cy, innerW, lineH), leftTop);
        cy += lineH;

        // L4 — phone | "position - club" (player's own club affiliation)
        g.DrawString(FormatPhone(FirstNonBlank(row.Cellphone, row.MomCellphone)), cellFont,
            PdfBrushes.Black, new RectangleF(lx, cy, innerW * 0.50f, lineH), leftTop);
        g.DrawString(ComposePositionClub(row, upper: false), cellFont, PdfBrushes.Black,
            new RectangleF(x + (innerW * 0.42f), cy, innerW * 0.58f, lineH), rightTop);
        cy += lineH;

        // L5 — school
        g.DrawString((row.SchoolName ?? "").Trim().ToUpperInvariant(), cellFont, PdfBrushes.Black,
            new RectangleF(lx, cy, innerW, lineH), leftTop);
    }

    // ── USL (blank stat-capture sheet) cells ─────────────────────────────────────

    private static void DrawUslCoachCell(
        PdfGraphics g, TournamentRosterRowDto row, float x, float y, PdfStandardFont cellFont)
    {
        var name = $"{row.FirstName} {row.LastName}".Trim().ToUpperInvariant();
        var phone = FormatPhone(FirstNonBlank(row.Cellphone, row.MomCellphone));
        var line = $"Coach {name}   {phone}".Trim();
        g.DrawString(line, cellFont, PdfBrushes.Black, new PointF(x + 4f, y + 3f));
    }

    private static void DrawUslPlayerCard(
        PdfGraphics g, TournamentRosterRowDto row, float x, float y, float w,
        PdfPen blackPen, PdfPen dotPen, PdfStandardFont nameFont, PdfStandardFont cellFont, PdfStandardFont statFont)
    {
        g.DrawRectangle(blackPen, new RectangleF(x, y, w, UslCardOuterH));
        var leftTop = new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top) { LineLimit = false };
        const float pad = 4f, lineH = 12.5f;
        var lx = x + pad;
        var leftW = (w * 0.55f) - pad;
        var cy = y + 3f;

        // Left column — name+grad / POSITION-CLUB / CITY,ST / SCHOOL
        var uni = CleanUniform(row.UniformNo);
        var name = $"{row.FirstName} {row.LastName}".Trim().ToUpperInvariant();
        var grad = string.IsNullOrWhiteSpace(row.GradYear) ? "" : $" ({row.GradYear})";
        g.DrawString($"# {uni}   {name}{grad}".Trim(), nameFont, PdfBrushes.Black, new RectangleF(lx, cy, leftW, lineH), leftTop);
        cy += lineH;
        g.DrawString(ComposePositionClub(row, upper: true), cellFont, PdfBrushes.Black, new RectangleF(lx, cy, leftW, lineH), leftTop);
        cy += lineH;
        g.DrawString(ComposeCityState(row), cellFont, PdfBrushes.Black, new RectangleF(lx, cy, leftW, lineH), leftTop);
        cy += lineH;
        g.DrawString((row.SchoolName ?? "").Trim().ToUpperInvariant(), cellFont, PdfBrushes.Black, new RectangleF(lx, cy, leftW, lineH), leftTop);

        // Right block — blank hand-entry grid: G:/A: · GB:/DC: · S:
        var rx = x + (w * 0.56f);
        var colW = (w - (w * 0.56f) - pad) / 2f;
        var sy = y + 5f;
        const float statRowH = 17f;
        DrawStatField(g, "G:", rx, sy, colW, statFont, dotPen);
        DrawStatField(g, "A:", rx + colW, sy, colW, statFont, dotPen);
        DrawStatField(g, "GB:", rx, sy + statRowH, colW, statFont, dotPen);
        DrawStatField(g, "DC:", rx + colW, sy + statRowH, colW, statFont, dotPen);
        DrawStatField(g, "S:", rx, sy + (statRowH * 2), colW, statFont, dotPen);
    }

    private static void DrawStatField(
        PdfGraphics g, string label, float fx, float fy, float fw, PdfStandardFont font, PdfPen dotPen)
    {
        g.DrawString(label, font, PdfBrushes.Black, new PointF(fx, fy));
        var labelW = font.MeasureString(label).Width;
        var lineY = fy + font.Height - 2f;
        g.DrawLine(dotPen, fx + labelW + 3f, lineY, fx + fw - 4f, lineY);
    }

    private static string ComposePositionClub(TournamentRosterRowDto r, bool upper)
    {
        var pos = (r.Position ?? "").Trim();
        var club = (FirstNonBlank(r.PlayerClubName, r.PlayerClubTeamName) ?? "").Trim();
        var combined = pos.Length > 0 && club.Length > 0 ? $"{pos} - {club}"
            : pos.Length > 0 ? pos : club;
        return upper ? combined.ToUpperInvariant() : combined;
    }

    private static string ComposeCityState(TournamentRosterRowDto r)
    {
        var city = (FirstNonBlank(r.PlayerCity, r.FamilyCity) ?? "").Trim();
        var st = (FirstNonBlank(r.PlayerState, r.FamilyState) ?? "").Trim();
        return string.Join(", ", new[] { city, st }.Where(s => s.Length > 0)).ToUpperInvariant();
    }

    private static string ComposeClubTeam(TournamentRosterRowDto r)
    {
        var team = (r.TeamName ?? "").Trim();
        return string.IsNullOrWhiteSpace(r.ClubName)
            ? team.ToUpperInvariant()
            : $"{r.ClubName!.Trim()}:{team}".ToUpperInvariant();
    }

    private static string ComposeClubSchool(TournamentRosterRowDto r)
    {
        var club = (r.ClubName ?? "").Trim();
        var school = (r.SchoolName ?? "").Trim();
        return club.Length == 0 && school.Length == 0 ? "" : $"{club} / {school}".ToUpperInvariant();
    }

    private static string ComposeAddress(TournamentRosterRowDto r)
    {
        var street = FirstNonBlank(r.PlayerStreet, r.FamilyStreet) ?? "";
        var city = FirstNonBlank(r.PlayerCity, r.FamilyCity) ?? "";
        var state = FirstNonBlank(r.PlayerState, r.FamilyState) ?? "";
        var zip = FirstNonBlank(r.PlayerZip, r.FamilyZip) ?? "";
        var cityStateZip = $"{city}, {state}  {zip}".Trim().Trim(',').Trim();
        return $"{street}  {cityStateZip}".Trim().ToUpperInvariant();
    }

    private static string ComposeCoachLine(TournamentRosterRowDto h)
    {
        var first = (h.ClubRepFirstName ?? "").Trim();
        var last = (h.ClubRepLastName ?? "").Trim();
        var isDummy = string.Equals(first, "Club", StringComparison.OrdinalIgnoreCase)
            && string.Equals(last, "Rep", StringComparison.OrdinalIgnoreCase);
        var name = isDummy ? "" : $"{first} {last}".Trim();
        var parts = new[] { name, (h.ClubRepEmail ?? "").Trim(), FormatPhone(h.ClubRepCellphone) }
            .Where(s => s.Length > 0);
        return string.Join("   ", parts);
    }

    /// <summary>
    /// One materialized proc row. <see cref="IsStaff"/> / <see cref="IsCommitted"/> wrap
    /// the proc's string flags (roleName, bCollegeCommit="yes").
    /// </summary>
    private sealed record RosterRow
    {
        public required Guid TeamId { get; init; }
        public required string AgDiv { get; init; }
        public required string ClubTeamName { get; init; }
        public required string ClubRepName { get; init; }
        public required string ClubRepEmail { get; init; }
        public required string ClubRepCellphone { get; init; }
        public required string Player { get; init; }
        public required string ClubAffiliation { get; init; }
        public required string DayGroup { get; init; }
        public required string UniformNo { get; init; }
        public required string Position { get; init; }
        public required string SchoolName { get; init; }
        public required string GradYear { get; init; }
        public required string Gpa { get; init; }
        public required string CollegeCommit { get; init; }
        public required string RoleName { get; init; }
        public required string BCollegeCommit { get; init; }

        public bool IsStaff => string.Equals(RoleName, "Staff", StringComparison.OrdinalIgnoreCase);
        public bool IsCommitted => string.Equals(BCollegeCommit, "yes", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Maps the raw EF row to the draw-time model, applying the shaping the legacy proc
        /// used to bake in: UPPER club:team, dummy "Club Rep" suppression, club-rep phone
        /// formatting, the Staff→school_name phone overload, uniform "#" strip, and the
        /// commit-gated collegeCommit / bCollegeCommit values.
        /// </summary>
        public static RosterRow FromDto(TournamentRosterRowDto d)
        {
            var isStaff = string.Equals(d.RoleName, "Staff", StringComparison.OrdinalIgnoreCase);
            var committed = d.BCollegeCommit == true;
            var repIsDummy =
                string.Equals(d.ClubRepFirstName?.Trim(), "Club", StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.ClubRepLastName?.Trim(), "Rep", StringComparison.OrdinalIgnoreCase);

            return new RosterRow
            {
                TeamId = d.TeamId,
                AgDiv = $"{d.AgegroupName}:{d.DivName}",
                ClubTeamName = ComposeClubTeam(d),
                ClubRepName = repIsDummy ? "" : $"{d.ClubRepFirstName} {d.ClubRepLastName}".Trim().ToUpperInvariant(),
                ClubRepEmail = repIsDummy ? "" : (d.ClubRepEmail ?? "").Trim(),
                ClubRepCellphone = repIsDummy ? "" : FormatPhone(d.ClubRepCellphone),
                Player = $"{d.FirstName} {d.LastName}".Trim(),
                ClubAffiliation = isStaff
                    ? ""
                    : (FirstNonBlank(d.PlayerClubName, d.PlayerClubTeamName) ?? "").Trim().ToUpperInvariant(),
                DayGroup = (d.DayGroup ?? "").Trim(),
                UniformNo = CleanUniform(d.UniformNo),
                Position = (d.Position ?? "").Trim(),
                SchoolName = isStaff ? FormatPhone(d.Cellphone) : (d.SchoolName ?? "").Trim(),
                GradYear = (d.GradYear ?? "").Trim(),
                Gpa = (d.Gpa ?? "").Trim(),
                CollegeCommit = committed ? (d.CollegeCommit ?? "").Trim() : "",
                RoleName = d.RoleName ?? "",
                BCollegeCommit = committed ? "yes" : "",
            };
        }
    }
}
