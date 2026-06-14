using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn roster-table PDF (Syncfusion.Pdf). One broad EF dataset
/// (<see cref="IReportingRepository.GetRosterTableRowsAsync"/>) is grouped, sorted, and
/// projected to a full-width table entirely from a runtime config — the single designer that
/// retires the wide-roster Crystal family (Club Rosters, No-Medical, Coaches, WithClubRep,
/// STEPS, Recruiting roster). Letter, portrait or landscape; the director picks/orders columns.
/// Structurally a sibling of <see cref="ScheduleListReportService"/> (same table engine).
/// </summary>
public sealed class RosterTablePdfService : IRosterTablePdfService
{
    private readonly IReportingRepository _reportingRepository;

    public RosterTablePdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // ── Page + table geometry (points; Letter, 0.4in margins) ──
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float FooterH = 18f;
    private const float GroupHeaderH = 18f;
    private const float ColHeaderH = 15f;
    private const float BaseRowH = 13f;
    private const float CellPadX = 3f;

    public IReadOnlyList<RosterTableFieldDto> GetAvailableFields() => AvailableFields;

    private static readonly IReadOnlyList<RosterTableFieldDto> AvailableFields = new[]
    {
        F("player",       "Player",       95, "Left",   true),
        F("uniform",      "#",            22, "Center", false),
        F("position",     "Pos",          28, "Center", false),
        F("team",         "Team",         85, "Left",   true),
        F("agegroup",     "Age Group",    60, "Left",   false),
        F("division",     "Division",     46, "Center", false),
        F("league",       "League",       70, "Left",   true),
        F("club",         "Club",         75, "Left",   true),
        F("gender",       "Sex",          26, "Center", false),
        F("dob",          "DOB",          52, "Center", false),
        F("gradYear",     "Grad",         34, "Center", false),
        F("school",       "School",       95, "Left",   true),
        F("schoolGrade",  "Grade",        34, "Center", false),
        F("gpa",          "GPA",          28, "Center", false),
        F("sat",          "SAT",          30, "Center", false),
        F("act",          "ACT",          28, "Center", false),
        F("email",        "Email",        120, "Left",  true),
        F("phone",        "Phone",        62, "Left",   false),
        F("address",      "Address",      140, "Left",  true),
        F("momName",      "Mom",          70, "Left",   true),
        F("momPhone",     "Mom Phone",    62, "Left",   false),
        F("momEmail",     "Mom Email",    120, "Left",  true),
        F("momContact",   "Mom",          110, "Left",  true),
        F("dadName",      "Dad",          70, "Left",   true),
        F("dadPhone",     "Dad Phone",    62, "Left",   false),
        F("dadEmail",     "Dad Email",    120, "Left",  true),
        F("dadContact",   "Dad",          110, "Left",  true),
        F("medical",      "Medical Note", 130, "Left",  true),
        F("allergies",    "Allergies",    70, "Left",   true),
        F("paid",         "Paid",         42, "Right",  false),
        F("owed",         "Owed",         42, "Right",  false),
        F("jersey",       "Jersey",       34, "Center", false),
        F("shorts",       "Shorts",       34, "Center", false),
        F("kilt",         "Kilt",         28, "Center", false),
        F("tshirt",       "T-Shirt",      34, "Center", false),
        F("reversible",   "Rev",          28, "Center", false),
        F("gloves",       "Gloves",       34, "Center", false),
        F("shoes",        "Shoes",        30, "Center", false),
        F("uslax",        "Sport Assn #", 54, "Center", false),
        F("clubRep",      "Club Rep",     80, "Left",   true),
        F("clubRepEmail", "Rep Email",    120, "Left",  true),
        F("clubRepPhone", "Rep Phone",    62, "Left",   false),
        F("dayGroup",     "Day Group",    50, "Left",   false),
        F("nightGroup",   "Night Group",  50, "Left",   false),
        F("roommate",     "Roommate",     70, "Left",   true),
    };

    private static RosterTableFieldDto F(string key, string label, int w, string align, bool longText)
        => new() { Key = key, Label = label, DefaultWidthWeight = w, DefaultAlign = align, SupportsLongText = longText };

    public async Task<ReportExportResult> GenerateAsync(
        RosterTableRequestDto request,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetRosterTableRowsAsync(jobId, request.PlayersOnly, cancellationToken);

        var landscape = string.Equals(request.Orientation, "Landscape", StringComparison.OrdinalIgnoreCase);
        var pageW = landscape ? 792f : 612f;
        var pageH = landscape ? 612f : 792f;
        var contentW = pageW - (MarginX * 2);
        var contentBottom = pageH - MarginBottom;
        // Reserve the bottom footer template (Syncfusion draws it inside the client area) plus a
        // hair, so the last row on a page — and its tallest cell — never clips under the footer.
        var maxContentY = contentBottom - MarginTop - FooterH - 2f;

        var columns = request.Columns.Count > 0
            ? request.Columns.ToList()
            : AvailableFields.Take(5)
                .Select(f => new RosterTableColumnDto { Key = f.Key, WidthWeight = f.DefaultWidthWeight, Align = f.DefaultAlign, LongText = "Truncate", TruncateAt = 24 })
                .ToList();

        var sumW = columns.Sum(c => Math.Max(1, c.WidthWeight));
        var widths = columns.Select(c => Math.Max(1, c.WidthWeight) / (float)sumW * contentW).ToArray();

        using var document = new PdfDocument();
        // Explicit SizeF (not Orientation auto-swap) so the page dimensions match the manual
        // contentW/contentBottom geometry exactly. Letter portrait 612×792, landscape 792×612.
        document.PageSettings.Size = new SizeF(pageW, pageH);
        document.PageSettings.Margins.Left = MarginX;
        document.PageSettings.Margins.Right = MarginX;
        document.PageSettings.Margins.Top = MarginTop;
        document.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(document, contentW);

        var fonts = new Fonts();
        var pens = new Pens();

        var groups = GroupRows(rows, request.SortBy, request.GroupBy);

        PdfGraphics? g = null;
        var y = 0f;
        var firstGroup = true;

        foreach (var group in groups)
        {
            if (g == null || (request.PageBreakPerGroup && !firstGroup))
            {
                g = document.Pages.Add().Graphics;
                y = 0f;
            }
            firstGroup = false;

            if (y + GroupHeaderH + ColHeaderH + BaseRowH > maxContentY)
            {
                g = document.Pages.Add().Graphics;
                y = 0f;
            }

            if (group.Label.Length > 0)
            {
                y = DrawGroupHeader(g, group, y, contentW, request.ColorAccent, fonts);
            }
            y = DrawColumnHeader(g, columns, widths, y, contentW, fonts, pens);

            foreach (var row in group.Rows)
            {
                var rowH = MeasureRowHeight(row, columns, widths, fonts);
                if (y + rowH > maxContentY)
                {
                    g = document.Pages.Add().Graphics;
                    y = 0f;
                    if (group.Label.Length > 0)
                    {
                        y = DrawGroupHeader(g, group, y, contentW, request.ColorAccent, fonts, continued: true);
                    }
                    y = DrawColumnHeader(g, columns, widths, y, contentW, fonts, pens);
                }
                y = DrawRow(g, row, columns, widths, y, rowH, contentW, fonts, pens);
            }
        }

        if (g == null)
        {
            g = document.Pages.Add().Graphics;
            g.DrawString("No registrants.", fonts.GroupHeader, PdfBrushes.Gray,
                new RectangleF(0, 0, contentW, 20),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = "Roster.pdf",
        };
    }

    // ── Grouping + sorting ──

    private static IReadOnlyList<RowGroup> GroupRows(
        List<RosterTableRowDto> rows, string sortBy, string groupBy)
    {
        // Camp groupings (Day/Night/Roommate) are assignment buckets — a registrant with no value
        // for the selected field isn't in any bucket, so drop them rather than show a "(No …)" group.
        rows = groupBy switch
        {
            "DayGroup"   => rows.Where(r => !string.IsNullOrWhiteSpace(r.DayGroup)).ToList(),
            "NightGroup" => rows.Where(r => !string.IsNullOrWhiteSpace(r.NightGroup)).ToList(),
            "Roommate"   => rows.Where(r => !string.IsNullOrWhiteSpace(r.Roommate)).ToList(),
            _ => rows,
        };

        Func<RosterTableRowDto, IComparable> sortKey = sortBy switch
        {
            "Uniform" => r => UniformSort(r.UniformNo),
            "School" => r => (r.SchoolName ?? "").ToUpperInvariant(),
            "GradYear" => r => r.GradYear ?? "",
            _ => r => ((r.LastName ?? "") + "|" + (r.FirstName ?? "")).ToUpperInvariant(),
        };

        IEnumerable<IGrouping<string, RosterTableRowDto>> grouped = groupBy switch
        {
            "AgeGroup" => rows.GroupBy(r => r.AgegroupName ?? ""),
            "Division" => rows.GroupBy(r => (r.AgegroupName ?? "") + "|" + (r.DivName ?? "")),
            "Team" => rows.GroupBy(r => (r.AgegroupName ?? "") + "|" + (r.DivName ?? "") + "|" + (r.TeamName ?? "")),
            "Club" => rows.GroupBy(r => ComposeClub(r)),
            "School" => rows.GroupBy(r => r.SchoolName ?? ""),
            "DayGroup" => rows.GroupBy(r => r.DayGroup ?? ""),
            "NightGroup" => rows.GroupBy(r => r.NightGroup ?? ""),
            "Roommate" => rows.GroupBy(r => r.Roommate ?? ""),
            _ => rows.GroupBy(_ => ""),
        };

        return grouped
            .Select(grp =>
            {
                var ordered = grp.OrderBy(sortKey)
                    .ThenBy(r => (r.LastName ?? "").ToUpperInvariant())
                    .ThenBy(r => (r.FirstName ?? "").ToUpperInvariant())
                    .ToList();
                return new RowGroup
                {
                    Label = GroupLabel(groupBy, ordered[0]),
                    Color = ordered[0].Color ?? "",
                    Rows = ordered,
                };
            })
            .OrderBy(grp => grp.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GroupLabel(string groupBy, RosterTableRowDto r) => groupBy switch
    {
        "AgeGroup" => r.AgegroupName ?? "",
        "Division" => $"{r.AgegroupName} — Div {r.DivName}".Trim(),
        "Team" => $"{r.AgegroupName} {(string.IsNullOrWhiteSpace(r.DivName) ? "" : "Div " + r.DivName + " ")}{r.TeamName}".Trim(),
        "Club" => ComposeClub(r),
        "School" => string.IsNullOrWhiteSpace(r.SchoolName) ? "(No School)" : r.SchoolName,
        "DayGroup" => string.IsNullOrWhiteSpace(r.DayGroup) ? "(No Day Group)" : "Day Group: " + r.DayGroup,
        "NightGroup" => string.IsNullOrWhiteSpace(r.NightGroup) ? "(No Night Group)" : "Night Group: " + r.NightGroup,
        "Roommate" => string.IsNullOrWhiteSpace(r.Roommate) ? "(No Roommate Pref)" : "Roommate: " + r.Roommate,
        _ => "",
    };

    // ── Drawing ──

    private static float DrawGroupHeader(
        PdfGraphics g, RowGroup group, float y, float contentW, bool colorAccent,
        Fonts fonts, bool continued = false)
    {
        var rect = new RectangleF(0, y, contentW, GroupHeaderH);
        var fill = colorAccent && TryParseColor(group.Color, out var c)
            ? new PdfSolidBrush(c)
            : new PdfSolidBrush(new PdfColor(232, 232, 232));
        g.DrawRectangle(fill, rect);

        var textColor = colorAccent && TryParseColor(group.Color, out var cc) && IsDark(cc)
            ? PdfBrushes.White
            : PdfBrushes.Black;
        var label = continued ? group.Label + " (cont.)" : group.Label;
        g.DrawString(label, fonts.GroupHeader, textColor,
            new RectangleF(CellPadX, y, contentW - (CellPadX * 2), GroupHeaderH),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        return y + GroupHeaderH;
    }

    private static float DrawColumnHeader(
        PdfGraphics g, List<RosterTableColumnDto> cols, float[] widths, float y, float contentW,
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
        g.DrawLine(pens.Header, new PointF(0, y + ColHeaderH), new PointF(contentW, y + ColHeaderH));
        return y + ColHeaderH;
    }

    private static float DrawRow(
        PdfGraphics g, RosterTableRowDto row, List<RosterTableColumnDto> cols, float[] widths,
        float y, float rowH, float contentW, Fonts fonts, Pens pens)
    {
        var x = 0f;
        for (var i = 0; i < cols.Count; i++)
        {
            var col = cols[i];
            var text = ResolveCell(row, col);
            if (text.Length > 0)
            {
                g.DrawString(text, fonts.Cell, PdfBrushes.Black,
                    new RectangleF(x + CellPadX, y + 1f, widths[i] - (CellPadX * 2), rowH - 2f),
                    WrapFormat(col));
            }
            x += widths[i];
        }
        g.DrawLine(pens.Divider, new PointF(0, y + rowH), new PointF(contentW, y + rowH));
        return y + rowH;
    }

    private static float MeasureRowHeight(
        RosterTableRowDto row, List<RosterTableColumnDto> cols, float[] widths, Fonts fonts)
    {
        var rowH = BaseRowH;
        for (var i = 0; i < cols.Count; i++)
        {
            var col = cols[i];
            if (!string.Equals(col.LongText, "Wrap", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var text = ResolveCell(row, col);
            if (text.Length == 0)
            {
                continue;
            }
            var sz = fonts.Cell.MeasureString(text, widths[i] - (CellPadX * 2), WrapFormat(col));
            rowH = Math.Max(rowH, sz.Height + 4f);
        }
        return rowH;
    }

    // ── Cell resolution (the C# home of the legacy procs' shaping) ──

    private static string ResolveCell(RosterTableRowDto r, RosterTableColumnDto col)
    {
        var value = col.Key switch
        {
            "player" => ComposeName(r.LastName, r.FirstName),
            "uniform" => CleanUniform(r.UniformNo),
            "position" => (r.Position ?? "").Trim(),
            "team" => (r.TeamName ?? "").Trim(),
            "agegroup" => (r.AgegroupName ?? "").Trim(),
            "division" => (r.DivName ?? "").Trim(),
            "league" => (r.LeagueName ?? "").Trim(),
            "club" => ComposeClub(r),
            "gender" => (r.Gender ?? "").Trim(),
            "dob" => FormatDate(r.Dob),
            "gradYear" => (r.GradYear ?? "").Trim(),
            "school" => (r.SchoolName ?? "").Trim(),
            "schoolGrade" => (r.SchoolGrade ?? "").Trim(),
            "gpa" => (r.Gpa ?? "").Trim(),
            "sat" => ComposeSat(r),
            "act" => (r.Act ?? "").Trim(),
            "email" => (r.Email ?? "").Trim(),
            "phone" => FormatPhone(r.Cellphone),
            "address" => ComposeAddress(r),
            "momName" => ComposeName2(r.MomFirstName, r.MomLastName),
            "momPhone" => FormatPhone(r.MomCellphone),
            "momEmail" => (r.MomEmail ?? "").Trim(),
            "momContact" => ComposeContact(r.MomFirstName, r.MomLastName, r.MomCellphone),
            "dadName" => ComposeName2(r.DadFirstName, r.DadLastName),
            "dadPhone" => FormatPhone(r.DadCellphone),
            "dadEmail" => (r.DadEmail ?? "").Trim(),
            "dadContact" => ComposeContact(r.DadFirstName, r.DadLastName, r.DadCellphone),
            // "allergies" is the medical_note field relabeled — the legacy camp roster's
            // "Allergies" column is just MedicalNote (no dedicated allergies field exists).
            "medical" or "allergies" => (r.MedicalNote ?? "").Trim(),
            "paid" => FormatMoney(r.PaidTotal),
            "owed" => FormatMoney(r.OwedTotal),
            "jersey" => (r.JerseySize ?? "").Trim(),
            "shorts" => (r.ShortsSize ?? "").Trim(),
            "kilt" => (r.Kilt ?? "").Trim(),
            "tshirt" => (r.TShirt ?? "").Trim(),
            "reversible" => (r.Reversible ?? "").Trim(),
            "gloves" => (r.Gloves ?? "").Trim(),
            "shoes" => (r.Shoes ?? "").Trim(),
            "uslax" => (r.SportAssnId ?? "").Trim(),
            "clubRep" => ComposeRep(r.ClubRepFirstName, r.ClubRepLastName),
            "clubRepEmail" => IsDummyRep(r) ? "" : (r.ClubRepEmail ?? "").Trim(),
            "clubRepPhone" => IsDummyRep(r) ? "" : FormatPhone(r.ClubRepCellphone),
            "dayGroup" => (r.DayGroup ?? "").Trim(),
            "nightGroup" => (r.NightGroup ?? "").Trim(),
            "roommate" => (r.Roommate ?? "").Trim(),
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

    // ── Shaping helpers ──

    private static string ComposeName(string? last, string? first)
    {
        var l = (last ?? "").Trim();
        var f = (first ?? "").Trim();
        if (l.Length == 0 && f.Length == 0) return "";
        return l.Length == 0 ? f : f.Length == 0 ? l : $"{l}, {f}";
    }

    private static string ComposeName2(string? first, string? last)
        => $"{(first ?? "").Trim()} {(last ?? "").Trim()}".Trim();

    // Parent "Name  phone" in one cell — the legacy camp roster's single Mom/Dad column.
    private static string ComposeContact(string? first, string? last, string? phone)
    {
        var name = ComposeName2(first, last);
        var ph = FormatPhone(phone);
        return string.Join("  ", new[] { name, ph }.Where(s => s.Length > 0));
    }

    private static bool IsDummyRep(RosterTableRowDto r)
        => string.Equals(r.ClubRepFirstName?.Trim(), "Club", StringComparison.OrdinalIgnoreCase)
           && string.Equals(r.ClubRepLastName?.Trim(), "Rep", StringComparison.OrdinalIgnoreCase);

    private static string ComposeRep(string? first, string? last)
    {
        var isDummy = string.Equals(first?.Trim(), "Club", StringComparison.OrdinalIgnoreCase)
                      && string.Equals(last?.Trim(), "Rep", StringComparison.OrdinalIgnoreCase);
        return isDummy ? "" : $"{(first ?? "").Trim()} {(last ?? "").Trim()}".Trim();
    }

    private static string ComposeClub(RosterTableRowDto r)
    {
        var club = (r.ClubName ?? "").Trim();
        return club.Length > 0 ? club : (r.ClubTeamName ?? "").Trim();
    }

    private static string ComposeSat(RosterTableRowDto r)
        => int.TryParse(r.SatMath, out var m) && int.TryParse(r.SatVerbal, out var v) && int.TryParse(r.SatWriting, out var w)
            ? (m + v + w).ToString(CultureInfo.InvariantCulture)
            : "";

    private static string ComposeAddress(RosterTableRowDto r)
    {
        var street = (r.StreetAddress ?? "").Trim();
        var city = (r.City ?? "").Trim();
        var state = (r.State ?? "").Trim();
        var zip = (r.PostalCode ?? "").Trim();
        var cityStateZip = $"{city}, {state} {zip}".Trim().Trim(',').Trim();
        return $"{street} {cityStateZip}".Trim();
    }

    private static string FormatMoney(decimal v)
        => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime? d)
        => d.HasValue ? d.Value.ToString("M/d/yyyy", CultureInfo.InvariantCulture) : "";

    private static string CleanUniform(string? u)
        => string.IsNullOrWhiteSpace(u) ? "" : u.Replace("#", "").Trim();

    private static int UniformSort(string? u)
        => int.TryParse(CleanUniform(u), out var n) ? n : int.MaxValue;

    private static string FormatPhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 10
            ? $"{digits[..3]}-{digits.Substring(3, 3)}-{digits[6..]}"
            : (phone ?? "").Trim();
    }

    private static PdfStringFormat WrapFormat(RosterTableColumnDto col) =>
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

    private static void AddFooterTemplate(PdfDocument document, float contentW)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var gray = new PdfSolidBrush(new PdfColor(102, 102, 102));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, contentW, FooterH));

        footer.Graphics.DrawString("Reports By TeamSportsInfo.com", footerFont, gray, new PointF(2, 4));

        // Right-aligned page number. A PdfCompositeField only honors its StringFormat
        // alignment within its Bounds — drawn at a bare point it renders left-aligned, so
        // "Page X / Y" overflows the template's right edge and the total gets clipped.
        var composite = new PdfCompositeField(
            footerFont, gray, "Page {0} / {1}",
            new PdfPageNumberField(footerFont, gray),
            new PdfPageCountField(footerFont, gray))
        {
            Bounds = new RectangleF(0, 4, contentW - 2, FooterH),
            StringFormat = new PdfStringFormat(PdfTextAlignment.Right),
        };
        composite.Draw(footer.Graphics, new PointF(0, 4));

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
    }

    private sealed class RowGroup
    {
        public required string Label { get; init; }
        public required string Color { get; init; }
        public required List<RosterTableRowDto> Rows { get; init; }
    }
}
