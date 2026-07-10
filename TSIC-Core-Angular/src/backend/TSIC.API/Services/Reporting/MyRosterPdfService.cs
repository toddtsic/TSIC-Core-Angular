using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.MyRoster;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Team-scoped roster listing PDF (Syncfusion.Pdf) — the printable companion to the on-screen
/// roster cards. Renders only what the cards show: player identity + contact and the two family
/// contacts. Deliberately NOT a slice of the director's <see cref="RosterTablePdfService"/>: that
/// query is job-wide and carries money / medical / academics, which must never be pulled into a
/// player- or staff-facing request. Fed the already-gated team list, it does no data access and no
/// authorization of its own. Shares the Reporting family's page geometry, fonts, and footer.
/// </summary>
public sealed class MyRosterPdfService : IMyRosterPdfService
{
    // Landscape Letter, 0.4in margins — the parent-contact columns need the extra width.
    private const float PageW = 792f, PageH = 612f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float FooterH = 18f, ColHeaderH = 15f, BaseRowH = 14f, CellPadX = 3f;
    private const float TitleH = 22f, SubtitleH = 14f;

    private static readonly PdfStringFormat LeftMiddle = new(PdfTextAlignment.Left, PdfVerticalAlignment.Middle);
    private static readonly PdfStringFormat LeftTop = new(PdfTextAlignment.Left, PdfVerticalAlignment.Top);

    public ReportExportResult Generate(IReadOnlyList<MyRosterPlayerDto> players, string teamName)
    {
        var contentW = PageW - (MarginX * 2);
        var contentBottom = PageH - MarginBottom;
        // Reserve the bottom footer template so the last row on a page never clips under it.
        var maxContentY = contentBottom - MarginTop - FooterH - 2f;

        var cols = BuildColumns();
        var sumW = cols.Sum(c => c.Width);
        var widths = cols.Select(c => c.Width / sumW * contentW).ToArray();

        var ordered = players
            .OrderBy(p => (p.LastName ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => (p.FirstName ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var document = new PdfDocument();
        document.PageSettings.Size = new SizeF(PageW, PageH);
        document.PageSettings.Margins.Left = MarginX;
        document.PageSettings.Margins.Right = MarginX;
        document.PageSettings.Margins.Top = MarginTop;
        document.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(document, contentW);

        var fonts = new Fonts();
        var pens = new Pens();

        var g = document.Pages.Add().Graphics;
        var y = DrawTitle(g, teamName, ordered.Count, contentW, fonts, pens);
        y = DrawColumnHeader(g, cols, widths, y, contentW, fonts, pens);

        if (ordered.Count == 0)
        {
            g.DrawString("No teammates on this roster yet.", fonts.Cell, PdfBrushes.Gray,
                new RectangleF(CellPadX, y + 4f, contentW, 16f), LeftTop);
        }

        foreach (var p in ordered)
        {
            var rowH = MeasureRowHeight(p, cols, widths, fonts);
            if (y + rowH > maxContentY)
            {
                g = document.Pages.Add().Graphics;
                y = DrawColumnHeader(g, cols, widths, 0f, contentW, fonts, pens);
            }
            y = DrawRow(g, p, cols, widths, y, rowH, contentW, fonts, pens);
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return new ReportExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = "Team-Roster.pdf",
        };
    }

    // ── Columns (fixed, safe preset — the card's fields only) ──

    private sealed record Col(string Header, float Width, PdfTextAlignment Align, bool Wrap, Func<MyRosterPlayerDto, string> Cell);

    private static IReadOnlyList<Col> BuildColumns() => new[]
    {
        new Col("#",         26f,  PdfTextAlignment.Center, false, p => CleanUniform(p.UniformNo)),
        new Col("Player",    120f, PdfTextAlignment.Left,   true,  NameCell),
        new Col("Pos",       30f,  PdfTextAlignment.Center, false, p => (p.Position ?? "").Trim()),
        new Col("Grad",      34f,  PdfTextAlignment.Center, false, p => p.GradYear?.ToString(CultureInfo.InvariantCulture) ?? ""),
        new Col("Email",     138f, PdfTextAlignment.Left,   true,  p => (p.Email ?? "").Trim()),
        new Col("Phone",     72f,  PdfTextAlignment.Left,   false, p => FormatPhone(p.Cellphone)),
        new Col("Contact 1", 157f, PdfTextAlignment.Left,   true,  p => ContactCell(p.MomFirstName, p.MomLastName, p.MomCellphone, p.MomEmail)),
        new Col("Contact 2", 157f, PdfTextAlignment.Left,   true,  p => ContactCell(p.DadFirstName, p.DadLastName, p.DadCellphone, p.DadEmail)),
    };

    // ── Drawing ──

    private static float DrawTitle(PdfGraphics g, string teamName, int count, float contentW, Fonts fonts, Pens pens)
    {
        var y = 0f;
        g.DrawString(string.IsNullOrWhiteSpace(teamName) ? "Team Roster" : teamName,
            fonts.Title, PdfBrushes.Black, new RectangleF(0, y, contentW, TitleH), LeftMiddle);
        y += TitleH;

        var sub = $"Team Roster · {count} member{(count == 1 ? "" : "s")}";
        g.DrawString(sub, fonts.Subtitle, new PdfSolidBrush(new PdfColor(102, 102, 102)),
            new RectangleF(0, y, contentW, SubtitleH), LeftMiddle);
        y += SubtitleH + 2f;

        g.DrawLine(pens.Header, new PointF(0, y), new PointF(contentW, y));
        return y + 4f;
    }

    private static float DrawColumnHeader(
        PdfGraphics g, IReadOnlyList<Col> cols, float[] widths, float y, float contentW, Fonts fonts, Pens pens)
    {
        var x = 0f;
        for (var i = 0; i < cols.Count; i++)
        {
            g.DrawString(cols[i].Header, fonts.ColHeader, PdfBrushes.Black,
                new RectangleF(x + CellPadX, y, widths[i] - (CellPadX * 2), ColHeaderH),
                new PdfStringFormat(cols[i].Align, PdfVerticalAlignment.Middle));
            x += widths[i];
        }
        g.DrawLine(pens.Header, new PointF(0, y + ColHeaderH), new PointF(contentW, y + ColHeaderH));
        return y + ColHeaderH;
    }

    private static float DrawRow(
        PdfGraphics g, MyRosterPlayerDto p, IReadOnlyList<Col> cols, float[] widths,
        float y, float rowH, float contentW, Fonts fonts, Pens pens)
    {
        var x = 0f;
        for (var i = 0; i < cols.Count; i++)
        {
            var text = cols[i].Cell(p);
            if (text.Length > 0)
            {
                g.DrawString(text, fonts.Cell, PdfBrushes.Black,
                    new RectangleF(x + CellPadX, y + 1f, widths[i] - (CellPadX * 2), rowH - 2f),
                    WrapFormat(cols[i]));
            }
            x += widths[i];
        }
        g.DrawLine(pens.Divider, new PointF(0, y + rowH), new PointF(contentW, y + rowH));
        return y + rowH;
    }

    private static float MeasureRowHeight(MyRosterPlayerDto p, IReadOnlyList<Col> cols, float[] widths, Fonts fonts)
    {
        var rowH = BaseRowH;
        for (var i = 0; i < cols.Count; i++)
        {
            if (!cols[i].Wrap)
            {
                continue;
            }
            var text = cols[i].Cell(p);
            if (text.Length == 0)
            {
                continue;
            }
            var sz = fonts.Cell.MeasureString(text, widths[i] - (CellPadX * 2), WrapFormat(cols[i]));
            rowH = Math.Max(rowH, sz.Height + 4f);
        }
        return rowH;
    }

    private static PdfStringFormat WrapFormat(Col col) => new(col.Align, PdfVerticalAlignment.Top)
    {
        WordWrap = col.Wrap ? PdfWordWrapType.Word : PdfWordWrapType.None,
        LineLimit = false,
    };

    // ── Cell shaping ──

    private static string NameCell(MyRosterPlayerDto p)
    {
        var name = ComposeName(p.LastName, p.FirstName);
        if (name.Length == 0)
        {
            name = (p.PlayerName ?? "").Trim();
        }
        if (IsStaff(p))
        {
            var role = (p.RoleName ?? "").Trim();
            name = role.Length > 0 ? $"{name} ({role})" : name;
        }
        return name;
    }

    private static bool IsStaff(MyRosterPlayerDto p)
        => !string.Equals((p.RoleName ?? "").Trim(), "player", StringComparison.OrdinalIgnoreCase);

    private static string ComposeName(string? last, string? first)
    {
        var l = (last ?? "").Trim();
        var f = (first ?? "").Trim();
        if (l.Length == 0 && f.Length == 0) return "";
        return l.Length == 0 ? f : f.Length == 0 ? l : $"{l}, {f}";
    }

    // Family contact in one cell — name, then phone, then email, each on its own line.
    private static string ContactCell(string? first, string? last, string? phone, string? email)
    {
        var name = $"{(first ?? "").Trim()} {(last ?? "").Trim()}".Trim();
        var ph = FormatPhone(phone);
        var em = (email ?? "").Trim();
        return string.Join("\n", new[] { name, ph, em }.Where(s => s.Length > 0));
    }

    private static string CleanUniform(string? u)
        => string.IsNullOrWhiteSpace(u) ? "" : u.Replace("#", "").Trim();

    private static string FormatPhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 10
            ? $"{digits[..3]}-{digits.Substring(3, 3)}-{digits[6..]}"
            : (phone ?? "").Trim();
    }

    private static void AddFooterTemplate(PdfDocument document, float contentW)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var gray = new PdfSolidBrush(new PdfColor(102, 102, 102));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, contentW, FooterH));

        footer.Graphics.DrawString("Reports By TeamSportsInfo.com", footerFont, gray, new PointF(2, 4));

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

    private sealed class Fonts
    {
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 14, PdfFontStyle.Bold);
        public PdfStandardFont Subtitle { get; } = new(PdfFontFamily.Helvetica, 9);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 8);
    }

    private sealed class Pens
    {
        public PdfPen Header { get; } = new(new PdfColor(0, 0, 0), 0.75f);
        public PdfPen Divider { get; } = new(new PdfColor(210, 210, 210), 0.5f);
    }
}
