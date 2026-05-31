using System.Data;
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
/// instead of a per-report RDL. Data comes from the single
/// reporting_migrate.TournamentRosterPacked_Flat proc (the field superset).
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
    private const string ProcName = "reporting_migrate.TournamentRosterPacked_Flat";
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 18f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);   // 554.4
    private const float ContentH = PageH - MarginTop - MarginBottom; // 745.2
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
    };

    public async Task<ReportExportResult> GenerateAsync(
        PackedRosterRequestDto request,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var nUp = request.NUp is 2 or 3 ? request.NUp : 3;

        // Same SP → DataTable materialization the Bold path uses; the proc returns the
        // full superset and we project/filter at draw time.
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

        var rows = table.Rows.Cast<DataRow>().Select(RosterRow.From).ToList();

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
                .OrderBy(g => g.First().DivTeamRow)
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
            // CC RDL blanks GPA for committed players.
            "gpa" => row.IsStaff || row.IsCommitted ? "" : row.Gpa,
            "collegeCommit" => row.IsStaff ? "" : row.CollegeCommit,
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
        return showAsterisk ? "* " + row.Player : row.Player;
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

    /// <summary>
    /// One materialized proc row. <see cref="IsStaff"/> / <see cref="IsCommitted"/> wrap
    /// the proc's string flags (roleName, bCollegeCommit="yes").
    /// </summary>
    private sealed record RosterRow
    {
        public required Guid TeamId { get; init; }
        public required string AgDiv { get; init; }
        public required long DivTeamRow { get; init; }
        public required string ClubTeamName { get; init; }
        public required string ClubRepName { get; init; }
        public required string ClubRepEmail { get; init; }
        public required string ClubRepCellphone { get; init; }
        public required string Player { get; init; }
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

        public static RosterRow From(DataRow r) => new()
        {
            TeamId = r["teamID"] is Guid gid ? gid : Guid.Empty,
            AgDiv = Str(r, "agDiv"),
            DivTeamRow = r["divTeamRow"] is long l ? l : Convert.ToInt64(r["divTeamRow"] is DBNull ? 0 : r["divTeamRow"]),
            ClubTeamName = Str(r, "clubTeamName"),
            ClubRepName = Str(r, "clubRepName"),
            ClubRepEmail = Str(r, "clubRepEmail"),
            ClubRepCellphone = Str(r, "clubRepCellphone"),
            Player = Str(r, "player"),
            UniformNo = Str(r, "uniform_no"),
            Position = Str(r, "position"),
            SchoolName = Str(r, "school_name"),
            GradYear = Str(r, "gradYear"),
            Gpa = Str(r, "gpa"),
            CollegeCommit = Str(r, "collegeCommit"),
            RoleName = Str(r, "roleName"),
            BCollegeCommit = Str(r, "bCollegeCommit"),
        };

        private static string Str(DataRow r, string col)
            => r.Table.Columns.Contains(col) && r[col] is not DBNull ? Convert.ToString(r[col]) ?? "" : "";
    }
}
