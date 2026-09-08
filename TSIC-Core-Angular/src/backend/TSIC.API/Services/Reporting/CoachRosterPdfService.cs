using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn "Rosters for Coaches" PDF (Syncfusion.Pdf) — the coach's field copy, faithful to the
/// legacy Crystal <c>clubrostersNoMedicalII</c> output.
///
/// This is deliberately NOT the "Coaches Eyes Only" club roster (<see cref="ClubRosterPdfService"/>).
/// The original migration folded four legacy <c>.rpt</c> files onto that one render behind two
/// booleans; they were never one layout. Differences that matter here, all verified against a
/// legacy render of this report:
///   * lead column is <b>UNo</b> (the uniform number), not a sequential row index
///   * Player column carries the <b>position</b> under the name — not the email
///   * <b>School/HomeTown</b> is its own column (school, then the registrant's city)
///   * no DOB column, no contact emails, no medical note
///   * <b>no financial column</b> and no red pay-status banner — a coach's copy carries no money
///   * <b>one team per page</b> — every team starts on a fresh page
/// Team header is <c>TEAM ROSTER: {agegroup}: {team}</c>; league, division and job do not appear.
/// </summary>
public sealed class CoachRosterPdfService : ICoachRosterPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public CoachRosterPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // ── Page geometry (points; Letter portrait, 0.4in margins) ──
    private const float PageW = 612f, PageH = 792f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);   // 554.4
    private const float FooterH = 16f;
    private const float TeamBoxPad = 5f;
    private const float ColHeaderH = 16f;
    private const float LineH = 11f;       // one text line within a player row
    private const float RowPad = 7f;
    private const float MaxContentY = (PageH - MarginTop - MarginBottom) - FooterH - 2f;

    // ── Column x-offsets within ContentW (scaled off the legacy render) ──
    private const float UNoX = 0f;   // UNo is drawn at a point; the 36pt gap to PlayerX is its width
    private const float PlayerX = 36f, PlayerW = 110f;
    private const float SchoolX = 148f, SchoolW = 155f;
    private const float PhoneX = 305f, PhoneW = 66f;
    private const float ContactLabelX = 373f, ContactLabelW = 48f;
    private const float ContactNameX = 421f, ContactNameW = ContentW - ContactNameX;

    public async Task<ReportExportResult> GenerateAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Same source rows as the club-roster family (per-job scope); this render simply draws a
        // different, smaller subset of them. The repo already orders by team then last/first name.
        var rows = await _reportingRepository.GetClubRosterRowsAsync(jobId, allCustomerJobs: false, cancellationToken);

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

        foreach (var team in teams)
        {
            var header = ComposeTeamHeader(team[0]);

            // One team per page: every team opens a page, and a team that overflows continues on
            // the next page under a "(cont.)" header — it never shares a page with another team.
            var g = NewPage(document);
            var y = 0f;
            y = DrawTeamBox(g, header, y, fonts, pens);
            y = DrawColumnHeader(g, y, fonts);

            foreach (var row in team)
            {
                var rowH = (LineH * 2) + RowPad;
                if (y + rowH > MaxContentY)
                {
                    g = NewPage(document);
                    y = 0f;
                    y = DrawTeamBox(g, header, y, fonts, pens, continued: true);
                    y = DrawColumnHeader(g, y, fonts);
                }
                y = DrawPlayerRow(g, row, y, rowH, fonts, pens);
            }
        }

        if (teams.Count == 0)
        {
            var g = NewPage(document);
            g.DrawString("No active registrants.", fonts.TeamHeader, PdfBrushes.Gray,
                new RectangleF(0, 12f, ContentW, 20),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = "Rosters_For_Coaches.pdf",
        };
    }

    private static PdfGraphics NewPage(PdfDocument document) => document.Pages.Add().Graphics;

    // ── Team header box ──

    private static string ComposeTeamHeader(ClubRosterRowDto r)
    {
        // Legacy header is exactly "TEAM ROSTER: {agegroup}: {team}" — league/division/job are
        // deliberately absent (verified against a legacy render: div "A" and the league name that
        // the club-roster layout prints do not appear on this report).
        var parts = new[] { Trim(r.AgegroupName), Trim(r.TeamName) }.Where(s => s.Length > 0);
        return "TEAM ROSTER: " + string.Join(": ", parts);
    }

    private static float DrawTeamBox(
        PdfGraphics g, string header, float y, Fonts fonts, Pens pens, bool continued = false)
    {
        var text = continued ? header + " (cont.)" : header;
        var innerW = ContentW - (TeamBoxPad * 2);
        var sz = fonts.TeamHeader.MeasureString(text, innerW);
        var boxH = Math.Max(sz.Height, 12f) + (TeamBoxPad * 2);

        g.DrawRectangle(pens.TeamBox, new RectangleF(0, y, ContentW, boxH));
        g.DrawString(text, fonts.TeamHeader, PdfBrushes.Black,
            new RectangleF(TeamBoxPad, y + TeamBoxPad, innerW, sz.Height),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top) { WordWrap = PdfWordWrapType.Word });

        return y + boxH + 4f;
    }

    // ── Column header ──

    private static float DrawColumnHeader(PdfGraphics g, float y, Fonts fonts)
    {
        DrawHeaderCell(g, "UNo", UNoX, y, fonts);
        DrawHeaderCell(g, "Player", PlayerX, y, fonts);
        DrawHeaderCell(g, "School/HomeTown", SchoolX, y, fonts);
        DrawHeaderCell(g, "Phone", PhoneX, y, fonts);
        DrawHeaderCell(g, "Contacts", ContactLabelX, y, fonts);
        return y + ColHeaderH;
    }

    private static void DrawHeaderCell(PdfGraphics g, string label, float x, float y, Fonts fonts)
    {
        var sz = fonts.ColHeader.MeasureString(label);
        g.DrawString(label, fonts.ColHeader, PdfBrushes.Black,
            new RectangleF(x, y, sz.Width + 2f, ColHeaderH - 2f),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Bottom));
        // Legacy underlines each column head, sized to the label rather than the column.
        g.DrawLine(HeaderRule,
            new PointF(x, y + ColHeaderH - 1f),
            new PointF(x + sz.Width, y + ColHeaderH - 1f));
    }

    // ── Player row ──

    private static float DrawPlayerRow(
        PdfGraphics g, ClubRosterRowDto r, float yTop, float rowH, Fonts fonts, Pens pens)
    {
        var line1Y = yTop + 2f;
        var line2Y = line1Y + LineH;

        // UNo — the coach's identifier on the sideline; blank when the player has no number yet.
        // Drawn at a POINT, not into a layout rectangle: PdfStringFormat.LineLimit defaults to true,
        // so an 11pt line measured against a 13pt-tall cell is dropped whole rather than clipped —
        // which silently blanked this entire column. The point overload has no such bound.
        var uNo = Trim(r.UniformNo);
        if (uNo.Length > 0)
        {
            g.DrawString(uNo, fonts.UNo, PdfBrushes.Black, new PointF(UNoX, line1Y));
        }

        // Player — name over position.
        DrawClip(g, ComposeFirstLast(r.FirstName, r.LastName), fonts.Cell, NameBrush, PlayerX, line1Y, PlayerW);
        DrawClip(g, Trim(r.Position), fonts.Small, PdfBrushes.Black, PlayerX, line2Y, PlayerW);

        // School / HomeTown.
        DrawClip(g, Trim(r.SchoolName), fonts.Small, NameBrush, SchoolX, line1Y, SchoolW);
        DrawClip(g, Trim(r.City), fonts.Small, NameBrush, SchoolX, line2Y, SchoolW);

        // Phone.
        DrawClip(g, FormatPhone(r.Cellphone), fonts.Cell, PdfBrushes.Black, PhoneX, line1Y, PhoneW);

        // Contacts — Primary = Mom, Secondary = Dad. Name + phone only; no emails on this report.
        DrawContact(g, "Primary:", r.MomFirstName, r.MomLastName, r.MomCellphone, line1Y, fonts);
        DrawContact(g, "Secondary:", r.DadFirstName, r.DadLastName, r.DadCellphone, line2Y, fonts);

        g.DrawLine(pens.Divider, new PointF(0, yTop + rowH), new PointF(ContentW, yTop + rowH));
        return yTop + rowH;
    }

    private static void DrawContact(
        PdfGraphics g, string label, string? first, string? last, string? phone, float y, Fonts fonts)
    {
        var namePhone = string.Join(" ",
            new[] { ComposeFirstLast(first, last), FormatPhone(phone) }.Where(s => s.Length > 0));

        // The label prints even with no contact on file — legacy shows the empty Primary/Secondary
        // pair so a coach can see at a glance that a parent contact is missing.
        g.DrawString(label, fonts.Small, PdfBrushes.Black,
            new RectangleF(ContactLabelX, y, ContactLabelW, LineH), LeftTop);
        DrawClip(g, namePhone, fonts.Small, PdfBrushes.Black, ContactNameX, y, ContactNameW);
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

    private static readonly PdfSolidBrush NameBrush = new(new PdfColor(0, 0, 153));
    private static readonly PdfPen HeaderRule = new(new PdfColor(0, 0, 0), 0.5f);

    private sealed class Fonts
    {
        public PdfStandardFont TeamHeader { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont UNo { get; } = new(PdfFontFamily.Helvetica, 11, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 8);
        public PdfStandardFont Small { get; } = new(PdfFontFamily.Helvetica, 7);
    }

    private sealed class Pens
    {
        public PdfPen TeamBox { get; } = new(new PdfColor(0, 0, 0), 1f);
        public PdfPen Divider { get; } = new(new PdfColor(190, 190, 190), 0.5f);
    }
}
