using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) monthly client-invoice reports — the EF replacement for the legacy
/// Crystal "invoices2015" (itemized) and "invoices2015SummariesOnly", both backed by
/// <c>adn.rpt_invoice</c>. One flat EF line dataset (<see cref="IReportingRepository.GetInvoiceLinesAsync"/>)
/// is shaped (text→decimal, credit negation, raw-text date parse), grouped Venue → Payment Category,
/// and drawn as the itemized payment tables + a per-venue Accounting Summary. Runs for the most
/// recently completed month across ALL jobs.
///
/// All money math mirrors the proc: CC line amount = signed settlement (credits negated); the CC
/// processing fee = ProcessingFeePercent% (default 3.5) of |amount| over Credit-Card lines; the
/// per-venue summary's TSIC Registrant Charges = NewThisMonth × per-registrant charge; Balance Due
/// Client = Net CC − (Registrant Charges + Processing Fee).
/// </summary>
public sealed class InvoiceReportPdfService : IInvoiceReportPdfService
{
    private readonly IReportingRepository _reportingRepository;

    public InvoiceReportPdfService(IReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    // Letter LANDSCAPE (the legacy layout is wide: 7-column itemized table + two-column summary).
    private const float PageW = 792f, PageH = 612f;
    private const float MarginX = 28.8f, MarginTop = 28.8f, MarginBottom = 28.8f;
    private const float ContentW = PageW - (MarginX * 2);          // 734.4
    private const float ContentBottom = PageH - MarginBottom;       // 583.2
    private const float FooterH = 18f;
    private const float MaxContentY = ContentBottom - MarginTop - FooterH - 2f;
    private const float CellPadX = 3f;

    // Itemized table columns (sum == ContentW).
    private static readonly (string Key, float W, PdfTextAlignment Align)[] Cols =
    {
        ("Payment Date", 110f, PdfTextAlignment.Left),
        ("Member",       200f, PdfTextAlignment.Left),
        ("CC Tx# or Check#", 130f, PdfTextAlignment.Left),
        ("Amount",       75f,  PdfTextAlignment.Right),
        ("Count",        50f,  PdfTextAlignment.Center),
        ("Online Reg'n Date", 90f, PdfTextAlignment.Center),
        ("Id",           79.4f, PdfTextAlignment.Left),
    };

    private static readonly PdfColor BandColor = new(222, 222, 222);
    private static readonly PdfColor TitleBlue = new(0, 0, 160);

    public async Task<ReportExportResult> GenerateItemizedAsync(CancellationToken cancellationToken = default)
    {
        var (year, month, venues) = await BuildVenuesAsync(cancellationToken);
        var doc = NewDocument();
        var fonts = new Fonts();
        var pens = new Pens();
        var period = $"{year:D4}-{month:D2}";
        var printStamp = "Print Date: " + DateTime.Now.ToString("M/d/yyyy  h:mm tt", CultureInfo.InvariantCulture);

        if (venues.Count == 0)
        {
            var g0 = doc.Pages.Add().Graphics;
            g0.DrawString($"No invoiced activity for {period}.", fonts.Title, PdfBrushes.Gray,
                new RectangleF(0, 0, ContentW, 20f), new PdfStringFormat(PdfTextAlignment.Left));
        }

        foreach (var v in venues)
        {
            var g = doc.Pages.Add().Graphics;          // page break per venue
            var y = DrawVenueBand(g, v.Title + " --  Itemized Accounting", printStamp, 0f, fonts);

            foreach (var cat in v.Categories)
            {
                if (y + 16f + ColHeaderH + 24f > MaxContentY)
                {
                    g = doc.Pages.Add().Graphics;
                    y = DrawVenueBand(g, v.Title + " --  Itemized Accounting", printStamp, 0f, fonts);
                }
                y = DrawSectionHeader(g, $"Invoice Period: {period}    Payment Category: {cat.Category}", y, fonts);
                y = DrawColumnHeader(g, y, fonts, pens);

                foreach (var line in cat.Lines)
                {
                    var rowH = MeasureMemberHeight(line, fonts);
                    if (y + rowH > MaxContentY)
                    {
                        g = doc.Pages.Add().Graphics;
                        y = DrawVenueBand(g, v.Title + " --  Itemized Accounting", printStamp, 0f, fonts);
                        y = DrawColumnHeader(g, y, fonts, pens);
                    }
                    y = DrawLineRow(g, line, y, rowH, fonts, pens);
                }

                y = DrawSubtotal(g, cat, y, fonts, pens);
                y += 6f;
            }

            // Accounting Summary for the venue (same block the summary-only report draws).
            if (y + SummaryBlockH > MaxContentY)
            {
                g = doc.Pages.Add().Graphics;
                y = DrawVenueBand(g, v.Title + " --  Itemized Accounting", printStamp, 0f, fonts);
            }
            DrawSummaryBlock(g, v, y + 8f, period, fonts, pens, drawTitle: false);
        }

        return Save(doc, "Invoices.pdf");
    }

    public async Task<ReportExportResult> GenerateSummaryOnlyAsync(CancellationToken cancellationToken = default)
    {
        var (year, month, venues) = await BuildVenuesAsync(cancellationToken);
        var doc = NewDocument();
        var fonts = new Fonts();
        var pens = new Pens();
        var period = $"{year:D4}-{month:D2}";

        if (venues.Count == 0)
        {
            var g0 = doc.Pages.Add().Graphics;
            g0.DrawString($"No invoiced activity for {period}.", fonts.Title, PdfBrushes.Gray,
                new RectangleF(0, 0, ContentW, 20f), new PdfStringFormat(PdfTextAlignment.Left));
        }

        foreach (var v in venues)
        {
            var g = doc.Pages.Add().Graphics;          // one page per venue
            DrawSummaryBlock(g, v, 0f, period, fonts, pens, drawTitle: true);
        }

        return Save(doc, "InvoicesSummary.pdf");
    }

    // ── Data shaping ────────────────────────────────────────────────────

    private async Task<(int Year, int Month, List<VenueData> Venues)> BuildVenuesAsync(CancellationToken ct)
    {
        var lastMonth = DateTime.Now.AddMonths(-1);
        var year = lastMonth.Year;
        var month = lastMonth.Month;

        var raw = await _reportingRepository.GetInvoiceLinesAsync(year, month, ct);

        var venues = new List<VenueData>();
        foreach (var jobGroup in raw.GroupBy(r => r.JobId))
        {
            var first = jobGroup.First();
            var lines = jobGroup.Select(Shape).Where(l => l is not null).Select(l => l!).ToList();
            if (lines.Count == 0)
            {
                continue;   // every line voided / filtered → not on the report (proc is line-driven)
            }

            var isTeamJob = first.JobTypeId == 2;
            var chargePerReg = isTeamJob ? first.PerTeamCharge : first.PerPlayerCharge;
            var newThisMonth = isTeamJob ? first.CountNewTeamsThisMonth : first.CountNewPlayersThisMonth;
            var activeToDate = isTeamJob ? first.CountActiveTeamsToDate : first.CountActivePlayersToDate;
            var billedLastMonth = isTeamJob ? first.CountActiveTeamsToDateLastMonth : first.CountActivePlayersToDateLastMonth;

            var ccReceived = lines.Where(l => l.Category == "Credit Card Payment").Sum(l => l.Amount);
            var ccRefunded = lines.Where(l => l.Category == "Credit Card Credit").Sum(l => l.Amount);
            var registrantCharges = (newThisMonth ?? 0) * (chargePerReg ?? 0m);
            var processingFee = lines.Sum(l => l.CcCharges);
            var totalCharges = registrantCharges + processingFee;

            var categories = lines
                .GroupBy(l => l.Category)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CategoryGroup
                {
                    Category = g.Key,
                    Lines = g.OrderBy(l => l.PaymentDate ?? DateTime.MinValue)
                              .ThenBy(l => l.LastName, StringComparer.OrdinalIgnoreCase)
                              .ThenBy(l => l.FirstName, StringComparer.OrdinalIgnoreCase)
                              .ToList(),
                    Total = g.Sum(l => l.Amount),
                    Count = g.Count(),
                })
                .ToList();

            venues.Add(new VenueData
            {
                Title = $"{first.CustomerName}:{first.CustomerName}:{first.JobName}",
                Categories = categories,
                IsTeamJob = isTeamJob,
                ActiveToDate = activeToDate,
                BilledLastMonth = billedLastMonth,
                NewThisMonth = newThisMonth,
                ChargePerReg = chargePerReg,
                RegistrantCharges = registrantCharges,
                CcReceived = ccReceived,
                CcRefunded = ccRefunded,
                NetCc = ccReceived + ccRefunded,
                ProcessingFee = processingFee,
                TotalCharges = totalCharges,
                BalanceDue = (ccReceived + ccRefunded) - totalCharges,
            });
        }

        return (year, month, venues.OrderBy(v => v.Title, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static InvoiceLine? Shape(InvoiceLineRawDto r)
    {
        if (string.Equals(r.PaymentMethodName, "Credit Card Void", StringComparison.OrdinalIgnoreCase))
        {
            return null;   // proc: WHERE Category != 'Credit Card Void'
        }

        var isCredited = string.Equals(r.TransactionStatus, "Credited", StringComparison.OrdinalIgnoreCase);
        decimal? settlement = ParseDecimal(r.SettlementAmountText);
        if (settlement.HasValue && isCredited)
        {
            settlement = -settlement.Value;   // view negates 'Credited' settlement amounts
        }

        var isCc = r.PaymentMethodName is "Credit Card Payment" or "Credit Card Credit";
        var payamt = r.Payamt ?? 0m;
        // Player: CC categories use the signed settlement, everything else uses payamt. Team: payamt.
        var amount = r.IsTeam ? payamt : (isCc ? (settlement ?? 0m) : payamt);
        var amountForFee = r.IsTeam ? payamt : (settlement ?? 0m);
        var pct = (r.ProcessingFeePercent ?? 3.5m) / 100m;
        var ccCharges = (isCc && amountForFee != 0m) ? pct * Math.Abs(amountForFee) : 0m;

        var settleDate = ParseAdnSettlement(r.SettlementDateTimeText);
        var paymentDate = r.IsTeam ? (settleDate ?? r.AcctCreatedate) : (settleDate ?? r.AcctModified);

        var inv = !string.IsNullOrEmpty(r.TxnInvoiceNumber) ? r.TxnInvoiceNumber : (r.CheckNo ?? string.Empty);
        // Both branches show the RegistrationAccounting.AId (autoincrement) under the "Id" column —
        // a stable per-transaction row id. (Player rows previously parsed a RegID out of the ADN
        // invoice number; the raw AId is more useful and consistent with the team rows.)
        var regId = r.AcctAId.ToString(CultureInfo.InvariantCulture);

        var onlineDate = r.IsTeam ? (r.AcctCreatedate ?? settleDate) : r.RegistrationTs;

        string memberName;
        string? memberId;
        if (r.IsTeam)
        {
            memberName = $"{r.ClubCustomerName ?? string.Empty}:{r.AgegroupName}:{r.TeamName}";
            memberId = null;
        }
        else
        {
            memberName = $"{r.UserFirstName} {r.UserLastName}".Trim();
            memberId = $"(ID:{r.UserName})";
        }

        return new InvoiceLine
        {
            Category = r.PaymentMethodName ?? string.Empty,
            PaymentDate = paymentDate,
            MemberName = memberName,
            MemberId = memberId,
            CcTxOrCheck = inv,
            Amount = amount,
            CcCharges = ccCharges,
            OnlineRegDate = onlineDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
            RegId = regId,
            LastName = r.UserLastName ?? string.Empty,
            FirstName = r.UserFirstName ?? string.Empty,
        };
    }

    private static decimal? ParseDecimal(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    // ADN stores the settlement date as raw text "DD-Mon-YYYY HH:MM:SS tt ZZZ" (e.g.
    // "12-Sep-2023 06:17:37 PM EDT"). We slice fixed positions instead of coercing to datetime
    // (no schema change); the trailing zone is ignored (wall-clock, as the legacy view does).
    private static DateTime? ParseAdnSettlement(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 11)
        {
            return null;
        }
        if (!int.TryParse(text.AsSpan(0, 2), out var day))
        {
            return null;
        }
        var monthNo = MonthAbbrToInt(text.Substring(3, 3));
        if (monthNo == 0)
        {
            return null;
        }
        if (!int.TryParse(text.AsSpan(7, 4), out var year))
        {
            return null;
        }

        int hour = 0, min = 0, sec = 0;
        if (text.Length > 12)
        {
            var segs = text[12..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length >= 1)
            {
                var hms = segs[0].Split(':');
                if (hms.Length >= 2)
                {
                    int.TryParse(hms[0], out hour);
                    int.TryParse(hms[1], out min);
                    if (hms.Length >= 3)
                    {
                        int.TryParse(hms[2], out sec);
                    }
                }
            }
            if (segs.Length >= 2)
            {
                if (segs[1].Equals("PM", StringComparison.OrdinalIgnoreCase) && hour < 12)
                {
                    hour += 12;
                }
                else if (segs[1].Equals("AM", StringComparison.OrdinalIgnoreCase) && hour == 12)
                {
                    hour = 0;
                }
            }
        }

        try
        {
            return new DateTime(year, monthNo, day, hour, min, sec);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int MonthAbbrToInt(string abbr) => abbr.ToLowerInvariant() switch
    {
        "jan" => 1, "feb" => 2, "mar" => 3, "apr" => 4, "may" => 5, "jun" => 6,
        "jul" => 7, "aug" => 8, "sep" => 9, "oct" => 10, "nov" => 11, "dec" => 12,
        _ => 0,
    };

    // ── Drawing ─────────────────────────────────────────────────────────

    private const float ColHeaderH = 14f;
    private const float SummaryBlockH = 230f;

    private static PdfDocument NewDocument()
    {
        var doc = new PdfDocument();
        doc.PageSettings.Size = PdfPageSize.Letter;
        doc.PageSettings.Orientation = PdfPageOrientation.Landscape;
        doc.PageSettings.Margins.Left = MarginX;
        doc.PageSettings.Margins.Right = MarginX;
        doc.PageSettings.Margins.Top = MarginTop;
        doc.PageSettings.Margins.Bottom = MarginBottom;
        AddFooterTemplate(doc);
        return doc;
    }

    private static float DrawVenueBand(PdfGraphics g, string title, string printStamp, float y, Fonts fonts)
    {
        g.DrawRectangle(new PdfSolidBrush(BandColor), new RectangleF(0, y, ContentW, 16f));
        g.DrawString(title, fonts.BandTitle, PdfBrushes.Black,
            new RectangleF(CellPadX, y, ContentW - 160f, 16f),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        g.DrawString(printStamp, fonts.Small, new PdfSolidBrush(new PdfColor(90, 90, 90)),
            new RectangleF(ContentW - 200f, y, 200f - CellPadX, 16f),
            new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle));
        return y + 16f + 6f;
    }

    private static float DrawSectionHeader(PdfGraphics g, string text, float y, Fonts fonts)
    {
        g.DrawString(text, fonts.Section, PdfBrushes.Black,
            new RectangleF(0, y, ContentW, 14f),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        return y + 16f;
    }

    private static float DrawColumnHeader(PdfGraphics g, float y, Fonts fonts, Pens pens)
    {
        var x = 0f;
        foreach (var (key, w, align) in Cols)
        {
            g.DrawString(key, fonts.ColHeader, PdfBrushes.Black,
                new RectangleF(x + CellPadX, y, w - (CellPadX * 2), ColHeaderH),
                new PdfStringFormat(align, PdfVerticalAlignment.Bottom));
            x += w;
        }
        g.DrawLine(pens.Header, new PointF(0, y + ColHeaderH), new PointF(ContentW, y + ColHeaderH));
        return y + ColHeaderH + 1f;
    }

    private static float MeasureMemberHeight(InvoiceLine line, Fonts fonts)
    {
        var memberW = Cols[1].W - (CellPadX * 2);
        var text = line.MemberId is null ? line.MemberName : $"{line.MemberName}\n{line.MemberId}";
        var sz = fonts.Cell.MeasureString(text, memberW, WrapFmt);
        return Math.Max(15f, sz.Height + 3f);
    }

    private static readonly PdfStringFormat WrapFmt =
        new(PdfTextAlignment.Left, PdfVerticalAlignment.Top) { WordWrap = PdfWordWrapType.Word, LineLimit = false };

    private static float DrawLineRow(PdfGraphics g, InvoiceLine line, float y, float rowH, Fonts fonts, Pens pens)
    {
        var member = line.MemberId is null ? line.MemberName : $"{line.MemberName}\n{line.MemberId}";
        var values = new[]
        {
            line.PaymentDate?.ToString("M/d/yyyy  h:mm", CultureInfo.InvariantCulture) ?? string.Empty,
            member,
            line.CcTxOrCheck,
            Money(line.Amount),
            string.Empty,                 // Count is shown only at the subtotal
            line.OnlineRegDate,
            line.RegId,
        };

        var x = 0f;
        for (var i = 0; i < Cols.Length; i++)
        {
            var (_, w, align) = Cols[i];
            if (values[i].Length > 0)
            {
                var fmt = i == 1
                    ? WrapFmt
                    : new PdfStringFormat(align, PdfVerticalAlignment.Top);
                g.DrawString(values[i], fonts.Cell, PdfBrushes.Black,
                    new RectangleF(x + CellPadX, y + 1f, w - (CellPadX * 2), rowH - 1f), fmt);
            }
            x += w;
        }
        g.DrawLine(pens.Divider, new PointF(0, y + rowH), new PointF(ContentW, y + rowH));
        return y + rowH;
    }

    private static float DrawSubtotal(PdfGraphics g, CategoryGroup cat, float y, Fonts fonts, Pens pens)
    {
        g.DrawLine(pens.Header, new PointF(0, y + 1f), new PointF(ContentW, y + 1f));
        var amountX = Cols[0].W + Cols[1].W + Cols[2].W;       // start of Amount column
        var countX = amountX + Cols[3].W;
        g.DrawString(Money(cat.Total), fonts.CellBold, PdfBrushes.Black,
            new RectangleF(amountX + CellPadX, y + 2f, Cols[3].W - (CellPadX * 2), 14f),
            new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle));
        g.DrawString(cat.Count.ToString(CultureInfo.InvariantCulture), fonts.CellBold, PdfBrushes.Black,
            new RectangleF(countX + CellPadX, y + 2f, Cols[4].W - (CellPadX * 2), 14f),
            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
        return y + 18f;
    }

    private static void DrawSummaryBlock(
        PdfGraphics g, VenueData v, float top, string period, Fonts fonts, Pens pens, bool drawTitle)
    {
        var center = new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle);
        var y = top;

        if (drawTitle)
        {
            g.DrawString(v.Title, fonts.TitleBlue, new PdfSolidBrush(TitleBlue),
                new RectangleF(0, y, ContentW, 18f), center);
            y += 20f;
        }

        var headerText = $"ACCOUNTING SUMMARY : MONTHLY: {period}";
        g.DrawString(headerText, fonts.SummaryHeader, PdfBrushes.Black,
            new RectangleF(0, y, ContentW, 14f), center);
        var hw = fonts.SummaryHeader.MeasureString(headerText).Width;
        g.DrawLine(pens.Divider, new PointF((ContentW - hw) / 2f, y + 14f), new PointF((ContentW + hw) / 2f, y + 14f));
        y += 26f;

        // Upper-left count rows. A team-billed job (JobTypeId 2) counts/charges TEAMS, not
        // registrants — the legacy report swaps the noun in exactly these four labels (the lower
        // "TSIC Registrant Charges This Month" line stays "Registrant" for both).
        var noun = v.IsTeamJob ? "Teams" : "Registrants";
        var unit = v.IsTeamJob ? "Team" : "Registrant";
        var countTop = y;
        DrawKv(g, $"# Total Active {noun} To Date:", Int(v.ActiveToDate), countTop, false, fonts, 0f, 250f);
        DrawKv(g, $"# Total {noun} Billed Through Last Month:", Int(v.BilledLastMonth), countTop + 14f, false, fonts, 0f, 250f);
        DrawKv(g, $"# New {noun} Billed This Month:", Int(v.NewThisMonth), countTop + 28f, false, fonts, 0f, 250f);
        DrawKv(g, $"TSIC Charge Per {unit}:", v.ChargePerReg.HasValue ? Money(v.ChargePerReg.Value) : string.Empty,
            countTop + 46f, true, fonts, 0f, 250f);

        // Lower two-column money block.
        var moneyTop = countTop + 90f;
        DrawKv(g, "TSIC Registrant Charges This Month:", Money(v.RegistrantCharges), moneyTop, true, fonts, 0f, 360f);
        DrawKv(g, "Credit Card Dollars Received This Month :", Money(v.CcReceived), moneyTop + 16f, false, fonts, 0f, 360f);
        DrawKv(g, "CC Processing Fee:", Money(v.ProcessingFee), moneyTop + 32f, true, fonts, 0f, 360f);
        DrawKv(g, "TOTAL TSIC CHARGES:", Money(v.TotalCharges), moneyTop + 48f, true, fonts, 0f, 360f);

        DrawKv(g, "Credit Card Dollars Received This Month:", Money(v.CcReceived), moneyTop, false, fonts, 400f, ContentW);
        DrawKv(g, "Credit Card Dollars Refunded This Month:", Money(v.CcRefunded), moneyTop + 16f, false, fonts, 400f, ContentW);
        DrawKv(g, "Net CC Dollars:", Money(v.NetCc), moneyTop + 32f, true, fonts, 400f, ContentW);
        DrawKv(g, "Total TSIC Charges:", Money(v.TotalCharges), moneyTop + 48f, false, fonts, 400f, ContentW);
        DrawKv(g, "BALANCE DUE CLIENT:", Money(v.BalanceDue), moneyTop + 64f, true, fonts, 400f, ContentW);

        g.DrawString("Notes:", fonts.LabelBold, PdfBrushes.Black,
            new RectangleF(0, moneyTop + 92f, 200f, 14f), new PdfStringFormat(PdfTextAlignment.Left));
    }

    private static void DrawKv(
        PdfGraphics g, string label, string value, float y, bool bold, Fonts fonts, float left, float right)
    {
        var font = bold ? fonts.LabelBold : fonts.Label;
        g.DrawString(label, font, PdfBrushes.Black,
            new RectangleF(left, y, (right - left) - 80f, 13f),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));
        g.DrawString(value, font, PdfBrushes.Black,
            new RectangleF(right - 90f, y, 90f, 13f),
            new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle));
    }

    private static string Int(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Money(decimal v) => v.ToString("$#,##0.00", CultureInfo.InvariantCulture);

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

    private static ReportExportResult Save(PdfDocument doc, string fileName)
    {
        using var ms = new MemoryStream();
        doc.Save(ms);
        doc.Close(true);
        return new ReportExportResult { FileBytes = ms.ToArray(), ContentType = "application/pdf", FileName = fileName };
    }

    // ── Render-time models ──────────────────────────────────────────────

    private sealed class VenueData
    {
        public required string Title { get; init; }
        public required List<CategoryGroup> Categories { get; init; }
        public bool IsTeamJob { get; init; }
        public int? ActiveToDate { get; init; }
        public int? BilledLastMonth { get; init; }
        public int? NewThisMonth { get; init; }
        public decimal? ChargePerReg { get; init; }
        public decimal RegistrantCharges { get; init; }
        public decimal CcReceived { get; init; }
        public decimal CcRefunded { get; init; }
        public decimal NetCc { get; init; }
        public decimal ProcessingFee { get; init; }
        public decimal TotalCharges { get; init; }
        public decimal BalanceDue { get; init; }
    }

    private sealed class CategoryGroup
    {
        public required string Category { get; init; }
        public required List<InvoiceLine> Lines { get; init; }
        public decimal Total { get; init; }
        public int Count { get; init; }
    }

    private sealed record InvoiceLine
    {
        public required string Category { get; init; }
        public DateTime? PaymentDate { get; init; }
        public required string MemberName { get; init; }
        public string? MemberId { get; init; }
        public required string CcTxOrCheck { get; init; }
        public decimal Amount { get; init; }
        public decimal CcCharges { get; init; }
        public required string OnlineRegDate { get; init; }
        public required string RegId { get; init; }
        public required string LastName { get; init; }
        public required string FirstName { get; init; }
    }

    private sealed class Fonts
    {
        public PdfStandardFont Title { get; } = new(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
        public PdfStandardFont TitleBlue { get; } = new(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
        public PdfStandardFont SummaryHeader { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
        public PdfStandardFont BandTitle { get; } = new(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
        public PdfStandardFont Section { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 7, PdfFontStyle.Bold);
        public PdfStandardFont Cell { get; } = new(PdfFontFamily.Helvetica, 7);
        public PdfStandardFont CellBold { get; } = new(PdfFontFamily.Helvetica, 7, PdfFontStyle.Bold);
        public PdfStandardFont Label { get; } = new(PdfFontFamily.Helvetica, 8);
        public PdfStandardFont LabelBold { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Small { get; } = new(PdfFontFamily.Helvetica, 7);
    }

    private sealed class Pens
    {
        public PdfPen Header { get; } = new(new PdfColor(0, 0, 0), 0.75f);
        public PdfPen Divider { get; } = new(new PdfColor(210, 210, 210), 0.5f);
    }
}
