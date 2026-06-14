using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn "Coaches Eyes Only" club roster PDF (Syncfusion.Pdf). One fixed team-grouped layout —
/// a red page banner, a boxed "TEAM ROSTER:" header per team, then numbered player rows with the
/// player (name + email), DOB/position, phone/school, the sensitive Amt Due, and both parents'
/// contact rows — replaces the legacy Crystal family <c>Job_Club_Rosters</c> (per-job, w/ medical),
/// <c>Job_Rosters_NoMedical</c> / <c>clubrostersNoMedicalII</c> (per-job, no medical), and
/// <c>Club_AllJobs_Rosters_NoMedical</c> (every job of the requesting job's customer). The data scope
/// and the medical note are the only things that vary across the four; both are caller flags.
/// </summary>
public sealed class ClubRosterPdfService : IClubRosterPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public ClubRosterPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // ── Page geometry (points; Letter portrait, 0.4in margins) ──
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);   // 554.4
    private const float FooterH = 16f;
    private const float BannerH = 20f;     // red "coaches eyes only" page banner
    private const float TeamBoxPad = 4f;
    private const float ColHeaderH = 14f;
    private const float LineH = 10f;       // one text line within a player row
    private const float RowPad = 4f;
    private const float MaxContentY = (PageH - MarginTop - MarginBottom) - FooterH - 2f;

    // ── Column x-offsets within ContentW ──
    private const float NumX = 0f, NumW = 14f;
    private const float PlayerX = 14f, PlayerW = 116f;
    private const float DobX = 130f, DobW = 56f;
    private const float PhoneX = 186f, PhoneW = 96f;
    private const float AmtX = 282f, AmtW = 44f;
    private const float ContactLabelX = 330f, ContactLabelW = 46f;
    private const float ContactNameX = 376f, ContactNameW = 116f;
    private const float ContactEmailX = 492f, ContactEmailW = 62f;

    public async Task<ReportExportResult> GenerateAsync(
        Guid jobId,
        bool allCustomerJobs,
        bool includeMedical,
        CancellationToken cancellationToken = default)
    {
        var rows = await _reportingRepository.GetClubRosterRowsAsync(jobId, allCustomerJobs, cancellationToken);

        // Group one boxed block per team (the repo already orders by job/league/agegroup/div/team/name).
        var teams = rows
            .GroupBy(r => r.TeamId)
            .Select(grp => grp.ToList())
            .ToList();

        using var document = new PdfDocument();
        document.PageSettings.Size = new SizeF(PageW, PageH);
        document.PageSettings.Margins.Left = MarginX;
        document.PageSettings.Margins.Right = MarginX;
        document.PageSettings.Margins.Top = MarginTop;
        document.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(document);

        var fonts = new Fonts();
        var pens = new Pens();

        PdfGraphics? g = null;
        var y = 0f;

        foreach (var team in teams)
        {
            var first = team[0];
            var headerLines = ComposeTeamHeader(first);
            var teamBoxH = MeasureTeamBoxHeight(headerLines, fonts);

            // Keep a team's box header + column header + first row together on a page.
            if (g == null || y + teamBoxH + ColHeaderH + RowHeight(team[0], includeMedical) > MaxContentY)
            {
                g = NewPage(document, fonts);
                y = BannerH + 6f;
            }

            y = DrawTeamBox(g, headerLines, y, teamBoxH, fonts, pens);
            y = DrawColumnHeader(g, y, fonts, pens);

            var index = 1;
            foreach (var row in team)
            {
                var rowH = RowHeight(row, includeMedical);
                if (y + rowH > MaxContentY)
                {
                    g = NewPage(document, fonts);
                    y = BannerH + 6f;
                    y = DrawTeamBox(g, headerLines, y, teamBoxH, fonts, pens, continued: true);
                    y = DrawColumnHeader(g, y, fonts, pens);
                }
                y = DrawPlayerRow(g, row, index, y, rowH, includeMedical, fonts, pens);
                index++;
            }

            y += 8f; // gap before the next team box
        }

        if (g == null)
        {
            g = NewPage(document, fonts);
            g.DrawString("No active registrants.", fonts.TeamHeader, PdfBrushes.Gray,
                new RectangleF(0, BannerH + 12f, ContentW, 20),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = allCustomerJobs ? "Club_AllJobs_Rosters.pdf" : "Club_Rosters.pdf",
        };
    }

    // ── Page scaffolding ──

    private static PdfGraphics NewPage(PdfDocument document, Fonts fonts)
    {
        var g = document.Pages.Add().Graphics;
        g.DrawString("FOR COACHES EYES ONLY (SENSITIVE PAY STATUS INCLUDED)",
            fonts.Banner, RedBrush,
            new RectangleF(0, 0, ContentW, BannerH),
            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        return g;
    }

    // ── Team header box ──

    private static string[] ComposeTeamHeader(ClubRosterRowDto r)
    {
        // Faithful to the legacy Crystal group header: team : job : agegroup : div, then a second
        // line with league. The club name (when present) leads so the coach sees the club affiliation.
        var team = Trim(r.TeamName);
        var club = Trim(r.ClubName);
        var teamLabel = club.Length > 0 && !team.StartsWith(club, StringComparison.OrdinalIgnoreCase)
            ? $"{club}:{team}"
            : team;

        var line1Parts = new[] { teamLabel, Trim(r.JobName), Trim(r.AgegroupName), Trim(r.DivName) }
            .Where(s => s.Length > 0);
        var line1 = "TEAM ROSTER: " + string.Join(": ", line1Parts);

        var line2Parts = new[] { Trim(r.LeagueName), Trim(r.DivName).Length > 0 ? "division" : "" }
            .Where(s => s.Length > 0);
        var line2 = string.Join(" ", line2Parts);

        return line2.Length > 0 ? new[] { line1, line2 } : new[] { line1 };
    }

    private static float MeasureTeamBoxHeight(string[] lines, Fonts fonts)
    {
        // line1 may wrap; measure against the box's inner width.
        var innerW = ContentW - (TeamBoxPad * 2);
        var h = TeamBoxPad;
        var sz1 = fonts.TeamHeader.MeasureString(lines[0], innerW);
        h += Math.Max(sz1.Height, 11f);
        if (lines.Length > 1)
        {
            h += fonts.TeamSub.MeasureString(lines[1], innerW).Height;
        }
        return h + TeamBoxPad;
    }

    private static float DrawTeamBox(
        PdfGraphics g, string[] lines, float y, float boxH, Fonts fonts, Pens pens, bool continued = false)
    {
        g.DrawRectangle(pens.TeamBox, new RectangleF(0, y, ContentW, boxH));

        var innerW = ContentW - (TeamBoxPad * 2);
        var ty = y + TeamBoxPad;
        var line1 = continued ? lines[0] + " (cont.)" : lines[0];
        var sz1 = fonts.TeamHeader.MeasureString(line1, innerW);
        g.DrawString(line1, fonts.TeamHeader, PdfBrushes.Black,
            new RectangleF(TeamBoxPad, ty, innerW, sz1.Height),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top) { WordWrap = PdfWordWrapType.Word });
        ty += Math.Max(sz1.Height, 11f);
        if (lines.Length > 1)
        {
            g.DrawString(lines[1], fonts.TeamSub, PdfBrushes.Black,
                new RectangleF(TeamBoxPad, ty, innerW, fonts.TeamSub.Height),
                new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top));
        }
        return y + boxH + 2f;
    }

    // ── Column header ──

    private static float DrawColumnHeader(PdfGraphics g, float y, Fonts fonts, Pens pens)
    {
        DrawHeaderCell(g, "Player", PlayerX, PlayerW, y, fonts, PdfTextAlignment.Left);
        DrawHeaderCell(g, "Dob/Psn", DobX, DobW, y, fonts, PdfTextAlignment.Left);
        DrawHeaderCell(g, "Phone/Sch", PhoneX, PhoneW, y, fonts, PdfTextAlignment.Left);
        DrawHeaderCell(g, "Amt Due", AmtX, AmtW, y, fonts, PdfTextAlignment.Right);
        DrawHeaderCell(g, "Contacts", ContactLabelX, ContactEmailX + ContactEmailW - ContactLabelX, y, fonts, PdfTextAlignment.Left);
        return y + ColHeaderH;
    }

    private static void DrawHeaderCell(
        PdfGraphics g, string label, float x, float w, float y, Fonts fonts, PdfTextAlignment align)
    {
        g.DrawString(label, fonts.ColHeader, PdfBrushes.Black,
            new RectangleF(x, y, w, ColHeaderH - 2f),
            new PdfStringFormat(align, PdfVerticalAlignment.Bottom));
        // Underline the header label (legacy reports underline each column head).
        var sz = fonts.ColHeader.MeasureString(label);
        var ux = align == PdfTextAlignment.Right ? x + w - sz.Width : x;
        g.DrawLine(new PdfPen(new PdfColor(0, 0, 0), 0.5f),
            new PointF(ux, y + ColHeaderH - 1f), new PointF(ux + sz.Width, y + ColHeaderH - 1f));
    }

    // ── Player row ──

    private static float RowHeight(ClubRosterRowDto r, bool includeMedical)
    {
        // Two stacked lines (player/dob/phone) and two contact rows → 2 lines tall, plus an optional
        // medical line when the note is present and medical is included.
        var h = (LineH * 2) + RowPad;
        if (includeMedical && Trim(r.MedicalNote).Length > 0)
        {
            h += LineH;
        }
        return h;
    }

    private static float DrawPlayerRow(
        PdfGraphics g, ClubRosterRowDto r, int index, float yTop, float rowH,
        bool includeMedical, Fonts fonts, Pens pens)
    {
        var line1Y = yTop + 1f;
        var line2Y = line1Y + LineH;

        // # (row number) + Player name / email
        g.DrawString(index.ToString(CultureInfo.InvariantCulture), fonts.Cell, PdfBrushes.Black,
            new RectangleF(NumX, line1Y, NumW, LineH),
            new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Top));
        g.DrawString(ComposeFirstLast(r.FirstName, r.LastName), fonts.Cell, PdfBrushes.Black,
            new RectangleF(PlayerX, line1Y, PlayerW, LineH), LeftTop);
        DrawClip(g, Trim(r.Email), fonts.Small, LinkBrush, PlayerX, line2Y, PlayerW);

        // Dob / Position
        g.DrawString(FormatDate(r.Dob), fonts.Cell, PdfBrushes.Black,
            new RectangleF(DobX, line1Y, DobW, LineH), LeftTop);
        DrawClip(g, Trim(r.Position), fonts.Small, PdfBrushes.Black, DobX, line2Y, DobW);

        // Phone / School
        g.DrawString(FormatPhone(r.Cellphone), fonts.Cell, PdfBrushes.Black,
            new RectangleF(PhoneX, line1Y, PhoneW, LineH), LeftTop);
        DrawClip(g, Trim(r.SchoolName), fonts.Small, PdfBrushes.Black, PhoneX, line2Y, PhoneW);

        // Amt Due (right-aligned, sensitive pay status)
        g.DrawString(FormatMoney(r.OwedTotal), fonts.Cell, PdfBrushes.Black,
            new RectangleF(AmtX, line1Y, AmtW, LineH),
            new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Top));

        // Contacts — Primary = Mom (line 1), Secondary = Dad (line 2)
        DrawContact(g, "Primary:", r.MomFirstName, r.MomLastName, r.MomCellphone, r.MomEmail, line1Y, fonts);
        DrawContact(g, "Secondary:", r.DadFirstName, r.DadLastName, r.DadCellphone, r.DadEmail, line2Y, fonts);

        // Optional medical line
        if (includeMedical && Trim(r.MedicalNote).Length > 0)
        {
            var medY = line2Y + LineH;
            g.DrawString("Medical: " + Trim(r.MedicalNote), fonts.Small, RedBrush,
                new RectangleF(PlayerX, medY, ContentW - PlayerX, LineH),
                new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top));
        }

        g.DrawLine(pens.Divider, new PointF(0, yTop + rowH), new PointF(ContentW, yTop + rowH));
        return yTop + rowH;
    }

    private static void DrawContact(
        PdfGraphics g, string label, string? first, string? last, string? phone, string? email,
        float y, Fonts fonts)
    {
        var name = ComposeFirstLast(first, last);
        var ph = FormatPhone(phone);
        var namePhone = string.Join(" ", new[] { name, ph }.Where(s => s.Length > 0));
        if (namePhone.Length == 0 && Trim(email).Length == 0)
        {
            return;
        }

        g.DrawString(label, fonts.Small, PdfBrushes.Black,
            new RectangleF(ContactLabelX, y, ContactLabelW, LineH), LeftTop);
        DrawClip(g, namePhone, fonts.Small, PdfBrushes.Black, ContactNameX, y, ContactNameW);
        DrawClip(g, Trim(email), fonts.Small, LinkBrush, ContactEmailX, y, ContactEmailW);
    }

    // Draw a single line of text clipped to a cell width (no wrap, truncates by clip rectangle).
    private static void DrawClip(
        PdfGraphics g, string text, PdfFont font, PdfBrush brush, float x, float y, float w)
    {
        if (text.Length == 0)
        {
            return;
        }
        g.DrawString(text, font, brush, new RectangleF(x, y, w, LineH),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top)
            {
                WordWrap = PdfWordWrapType.None,
                LineLimit = true,
            });
    }

    private static readonly PdfStringFormat LeftTop =
        new(PdfTextAlignment.Left, PdfVerticalAlignment.Top);

    // ── Shaping helpers ──

    private static string ComposeFirstLast(string? first, string? last)
        => $"{Trim(first)} {Trim(last)}".Trim();

    private static string FormatMoney(decimal v)
        => "$" + v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime? d)
        => d.HasValue ? d.Value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) : "";

    private static string FormatPhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 10
            ? $"{digits[..3]}-{digits.Substring(3, 3)}-{digits[6..]}"
            : Trim(phone);
    }

    private static string Trim(string? s) => (s ?? "").Trim();

    // ── Footer ──

    private static void AddFooterTemplate(PdfDocument document)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var gray = new PdfSolidBrush(new PdfColor(102, 102, 102));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, ContentW, FooterH));
        footer.Graphics.DrawString("Rosters by TeamSportsInfo.com   email: support@TeamSportsInfo.com",
            footerFont, gray, new PointF(2, 4));

        var composite = new PdfCompositeField(
            footerFont, gray, "Page {0} of {1}",
            new PdfPageNumberField(footerFont, gray),
            new PdfPageCountField(footerFont, gray))
        {
            Bounds = new RectangleF(0, 4, ContentW, FooterH),
            StringFormat = new PdfStringFormat(PdfTextAlignment.Center),
        };
        composite.Draw(footer.Graphics, new PointF(0, 4));
        document.Template.Bottom = footer;
    }

    // ── Render-time resources ──

    private static readonly PdfSolidBrush RedBrush = new(new PdfColor(204, 0, 0));
    private static readonly PdfSolidBrush LinkBrush = new(new PdfColor(0, 0, 170));

    private sealed class Fonts
    {
        public PdfStandardFont Banner { get; } = new(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
        public PdfStandardFont TeamHeader { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
        public PdfStandardFont TeamSub { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 8);
        public PdfStandardFont Small { get; } = new(PdfFontFamily.Helvetica, 7);
    }

    private sealed class Pens
    {
        public PdfPen TeamBox { get; } = new(new PdfColor(0, 0, 0), 1f);
        public PdfPen Divider { get; } = new(new PdfColor(190, 190, 190), 0.5f);
    }
}
