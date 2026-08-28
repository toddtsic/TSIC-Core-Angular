using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// The three store fulfilment PDFs, hand-drawn with Syncfusion.Pdf. See
/// <see cref="IStoreFulfillmentPdfService"/> for what replaces what and why.
/// </summary>
public sealed class StoreFulfillmentPdfService : IStoreFulfillmentPdfService
{
    private readonly IStoreRepository _storeRepository;
    private readonly IStoreAnalyticsRepository _analyticsRepository;

    public StoreFulfillmentPdfService(
        IStoreRepository storeRepository,
        IStoreAnalyticsRepository analyticsRepository)
    {
        _storeRepository = storeRepository;
        _analyticsRepository = analyticsRepository;
    }

    // ══════════════════════════════════════════════════════════
    //  Shared data shaping
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// One family+player bag: the label boundary. Family alone was rejected as the boundary —
    /// a family with three children buying four items each produces a label nothing fits on.
    /// </summary>
    private sealed record Bag
    {
        public required string FamilyUserId { get; init; }
        public required string PlayerKey { get; init; }
        public required StoreFulfillmentRowDto Head { get; init; }
        public required List<StoreFulfillmentRowDto> Lines { get; init; }

        /// <summary>1-based position of this player within the family, and the family's total.</summary>
        public int PlayerRank { get; set; }
        public int PlayersInFamily { get; set; }
    }

    /// <summary>
    /// Groups paid lines into bags, dropping anything fully returned. The player key falls back to
    /// the registration id and then to a literal so an unattached line still gets its own bag
    /// instead of being merged with every other unattached line in the family.
    /// </summary>
    private static List<Bag> ToBags(IEnumerable<StoreFulfillmentRowDto> rows)
    {
        var bags = rows
            .Where(r => r.NetQuantity > 0)
            .GroupBy(r => new
            {
                r.FamilyUserId,
                PlayerKey = r.PlayerUserId
                    ?? r.PlayerRegId?.ToString()
                    ?? $"cartsku:{r.CartSkuId}",
            })
            .Select(g => new Bag
            {
                FamilyUserId = g.Key.FamilyUserId,
                PlayerKey = g.Key.PlayerKey,
                Head = g.First(),
                Lines = g.OrderBy(r => r.ItemName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.SizeName ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.ColorName ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .ToList();

        foreach (var family in bags.GroupBy(b => b.FamilyUserId))
        {
            var ordered = family
                .OrderBy(b => PlayerSortName(b.Head), StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].PlayerRank = i + 1;
                ordered[i].PlayersInFamily = ordered.Count;
            }
        }

        return bags
            .OrderBy(b => b.Head.PlayerRegId == null ? 1 : 0)
            .ThenBy(b => b.Head.IsWalkUp ? 1 : 0)
            .ThenBy(b => b.Head.AgegroupName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Head.ClubName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Head.TeamName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => PlayerSortName(b.Head), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string PlayerSortName(StoreFulfillmentRowDto r)
        => $"{Trim(r.PlayerLastName)}, {Trim(r.PlayerFirstName)}".Trim(' ', ',');

    private static async Task<(int StoreId, List<StoreFulfillmentRowDto> Rows)> LoadAsync(
        IStoreRepository storeRepository,
        IStoreAnalyticsRepository analyticsRepository,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var store = await storeRepository.GetByJobIdAsync(jobId, cancellationToken);
        if (store == null)
        {
            return (0, new List<StoreFulfillmentRowDto>());
        }

        var rows = await analyticsRepository.GetFulfillmentRowsAsync(store.StoreId, cancellationToken);
        return (store.StoreId, rows);
    }

    // ══════════════════════════════════════════════════════════
    //  1. BAG LABELS — Avery 5163 (2" x 4", 10 per Letter sheet)
    // ══════════════════════════════════════════════════════════

    // Avery 5163 geometry in points (72pt = 1in). Side margins are symmetric at 0.15625in and the
    // rows butt against each other with no vertical gutter — that is the stock, not a choice.
    private const float PageW = 612f, PageH = 792f;
    private const float LabelW = 288f, LabelH = 144f;
    private const int LabelCols = 2, LabelRows = 5;
    private const float LabelOriginX = 11.25f, LabelOriginY = 36f;
    private const float LabelGutterX = 13.5f;
    private const float LabelPad = 7f;

    private const float LabelInnerW = LabelW - (LabelPad * 2);   // 274
    private const float LabelInnerH = LabelH - (LabelPad * 2);   // 130

    // Vertical rhythm inside a label.
    private const float NameH = 13f;
    private const float MetaH = 10f;
    private const float ItemLineH = 11f;
    private const float FootH = 9f;
    private const float ItemsTop = NameH + MetaH + MetaH + 5f;   // name, placement, contact, rule
    private static readonly float ItemsAvailH = LabelInnerH - ItemsTop - FootH - 2f;
    private static readonly int ItemsPerLabel = Math.Max(1, (int)(ItemsAvailH / ItemLineH));

    public async Task<ReportExportResult> GenerateBagLabelsAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var (_, rows) = await LoadAsync(_storeRepository, _analyticsRepository, jobId, cancellationToken);
        var bags = ToBags(rows);

        using var document = new PdfDocument();
        document.PageSettings.Size = new SizeF(PageW, PageH);
        document.PageSettings.Margins.All = 0;   // absolute positioning: the stock defines the grid

        // Third place the stock is named: the viewer's title bar / tab, which survives a rename.
        document.DocumentInformation.Title = "Store Bag Labels (Avery 5163)";

        var fonts = new Fonts();

        // Expand each bag into one or more labels — a player with more items than fit continues
        // onto a second label rather than losing the overflow.
        var labels = new List<(Bag Bag, List<StoreFulfillmentRowDto> Lines, int Part, int Parts)>();
        foreach (var bag in bags)
        {
            var chunks = Chunk(bag.Lines, ItemsPerLabel).ToList();
            for (var i = 0; i < chunks.Count; i++)
            {
                labels.Add((bag, chunks[i], i + 1, chunks.Count));
            }
        }

        if (labels.Count == 0)
        {
            var empty = document.Pages.Add().Graphics;
            empty.DrawString("No paid store orders to label.", fonts.LabelName, PdfBrushes.Gray,
                new RectangleF(0, PageH / 2f - 10f, PageW, 20f),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
            DrawStockCaption(empty, fonts);
            return Save(document, LabelFileName);
        }

        var perPage = LabelCols * LabelRows;
        PdfGraphics? g = null;

        for (var i = 0; i < labels.Count; i++)
        {
            var slot = i % perPage;
            if (slot == 0)
            {
                g = document.Pages.Add().Graphics;
                DrawStockCaption(g, fonts);
            }

            var col = slot % LabelCols;
            var row = slot / LabelCols;
            var x = LabelOriginX + (col * (LabelW + LabelGutterX));
            var y = LabelOriginY + (row * LabelH);

            DrawLabel(g!, labels[i].Bag, labels[i].Lines, labels[i].Part, labels[i].Parts, x, y, fonts);
        }

        return Save(document, LabelFileName);
    }

    /// <summary>
    /// Names the stock on every sheet, in the 0.5in strip below the last row of labels.
    ///
    /// <para>That strip is backing paper on a 5163 sheet — the label grid runs y=36 to y=756 on a
    /// 792pt page — so this prints on the carrier and never on a label a customer receives. It is
    /// the only free space on the page, and the sheet is otherwise anonymous once it leaves the
    /// printer: a director holding a stack of these has no way to tell which stock to reload.</para>
    ///
    /// <para>The scale warning earns its place. "Fit to page" silently shrinks the grid by ~4% and
    /// every label drifts progressively further off its die-cut down the sheet — the failure looks
    /// like a bad layout rather than a print setting, and it wastes a sheet of label stock each
    /// time. The filename carries the Avery number too, since that is what a director sees in the
    /// browser's download list before ever opening the file.</para>
    /// </summary>
    private static void DrawStockCaption(PdfGraphics g, Fonts fonts)
    {
        var captionY = LabelOriginY + (LabelRows * LabelH) + 8f;   // 764: inside the bottom strip
        g.DrawString(
            "Avery 5163  |  2in x 4in  |  10 per sheet  |  Print at 100% scale - do NOT use 'fit to page'",
            fonts.LabelFoot, GrayBrush,
            new RectangleF(0, captionY, PageW, 10f),
            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Top));
    }

    /// <summary>Stock number in the filename: it is what shows in the browser's download list.</summary>
    private const string LabelFileName = "Store-Bag-Labels-Avery-5163.pdf";

    private static void DrawLabel(
        PdfGraphics g,
        Bag bag,
        List<StoreFulfillmentRowDto> lines,
        int part,
        int parts,
        float ox,
        float oy,
        Fonts fonts)
    {
        var x = ox + LabelPad;
        var y = oy + LabelPad;
        var head = bag.Head;

        // ── Player name (the thing a packer reads from two feet away) ──
        var playerName = ComposeLastFirst(head.PlayerLastName, head.PlayerFirstName);
        if (playerName.Length == 0)
        {
            playerName = "(no player named)";
        }

        var rankW = 62f;
        DrawClip(g, playerName, fonts.LabelName, PdfBrushes.Black, x, y, LabelInnerW - rankW, NameH);

        if (bag.PlayersInFamily > 1)
        {
            g.DrawString($"Player {bag.PlayerRank} of {bag.PlayersInFamily}",
                fonts.LabelMeta, GrayBrush,
                new RectangleF(x + LabelInnerW - rankW, y + 3f, rankW, MetaH),
                new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Top));
        }
        y += NameH;

        // ── Placement ──
        DrawClip(g, ComposePlacement(head), fonts.LabelMeta, GrayBrush, x, y, LabelInnerW, MetaH);
        y += MetaH;

        // ── Family contact ──
        DrawClip(g, ComposeContact(head), fonts.LabelMeta, PdfBrushes.Black, x, y, LabelInnerW, MetaH);
        y += MetaH + 2f;

        g.DrawLine(HairlinePen, new PointF(x, y), new PointF(x + LabelInnerW, y));
        y += 3f;

        // ── Items ──
        foreach (var line in lines)
        {
            var qty = line.NetQuantity;
            g.DrawString(qty.ToString(CultureInfo.InvariantCulture) + " x",
                fonts.LabelItem, PdfBrushes.Black,
                new RectangleF(x, y, 20f, ItemLineH),
                new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Top));
            DrawClip(g, ComposeItem(line), fonts.LabelItem, PdfBrushes.Black,
                x + 24f, y, LabelInnerW - 24f, ItemLineH);
            y += ItemLineH;
        }

        // ── Footer: invoice + continuation marker ──
        var footY = oy + LabelH - LabelPad - FootH;
        var invoice = Trim(head.InvoiceNo);
        if (invoice.Length > 0)
        {
            DrawClip(g, "Inv " + invoice, fonts.LabelFoot, GrayBrush, x, footY, LabelInnerW - 60f, FootH);
        }
        if (parts > 1)
        {
            g.DrawString($"Label {part} of {parts}", fonts.LabelFoot, RedBrush,
                new RectangleF(x + LabelInnerW - 60f, footY, 60f, FootH),
                new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Top));
        }
    }

    // ══════════════════════════════════════════════════════════
    //  2. PICKUP SIGNOFF — one block per family, signature line each
    // ══════════════════════════════════════════════════════════

    private const float SheetMargin = 36f;
    private const float SheetW = PageW - (SheetMargin * 2);      // 540
    private const float SheetTop = 34f;                          // below the page title band
    private const float SheetMaxY = PageH - (SheetMargin * 2) - 18f;

    public async Task<ReportExportResult> GeneratePickupSignoffAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var (_, rows) = await LoadAsync(_storeRepository, _analyticsRepository, jobId, cancellationToken);
        var bags = ToBags(rows);

        using var document = new PdfDocument();
        document.PageSettings.Size = new SizeF(PageW, PageH);
        document.PageSettings.Margins.All = SheetMargin;
        AddFooter(document, SheetW, "Store pickup signoff");

        var fonts = new Fonts();

        var families = bags
            .GroupBy(b => b.FamilyUserId)
            .Select(g => g.OrderBy(b => b.PlayerRank).ToList())
            .OrderBy(g => ComposeFamilySortName(g[0].Head), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (families.Count == 0)
        {
            var empty = document.Pages.Add().Graphics;
            empty.DrawString("No paid store orders to hand over.", fonts.SectionTitle, PdfBrushes.Gray,
                new RectangleF(0, 40f, SheetW, 20f),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
            return Save(document, "Store-Pickup-Signoff.pdf");
        }

        PdfGraphics? g = null;
        var y = 0f;

        foreach (var family in families)
        {
            var blockH = MeasureFamilyBlock(family);

            // Never split a family across pages — a half-signed order is a dispute at the table.
            if (g == null || y + blockH > SheetMaxY)
            {
                g = NewSheetPage(document, fonts, "STORE PICKUP - SIGNOFF");
                y = SheetTop;
            }

            y = DrawFamilyBlock(g, family, y, fonts);
        }

        return Save(document, "Store-Pickup-Signoff.pdf");
    }

    private const float FamHeaderH = 15f;
    private const float FamContactH = 10f;
    private const float PlayerHeadH = 12f;
    private const float SignRowH = 26f;
    private const float BlockGap = 10f;

    private static float MeasureFamilyBlock(List<Bag> family)
    {
        var h = FamHeaderH + FamContactH + 4f;
        foreach (var bag in family)
        {
            h += PlayerHeadH + (bag.Lines.Count * ItemLineH) + 3f;
        }
        return h + SignRowH + BlockGap;
    }

    private static float DrawFamilyBlock(PdfGraphics g, List<Bag> family, float y, Fonts fonts)
    {
        var head = family[0].Head;
        var top = y;

        // ── Family header band ──
        g.DrawRectangle(BandBrush, new RectangleF(0, y, SheetW, FamHeaderH));
        DrawClip(g, ComposeFamilySortName(head), fonts.SectionTitle, PdfBrushes.Black,
            4f, y + 2.5f, SheetW - 150f, FamHeaderH);

        var invoice = Trim(head.InvoiceNo);
        if (invoice.Length > 0)
        {
            g.DrawString("Invoice " + invoice, fonts.Small, GrayBrush,
                new RectangleF(SheetW - 146f, y + 3.5f, 142f, FamHeaderH),
                new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Top));
        }
        y += FamHeaderH;

        // ── Contacts ──
        DrawClip(g, ComposeContact(head), fonts.Small, PdfBrushes.Black, 4f, y + 1f, SheetW - 8f, FamContactH);
        y += FamContactH + 4f;

        // ── One sub-block per player ──
        foreach (var bag in family)
        {
            var name = ComposeLastFirst(bag.Head.PlayerLastName, bag.Head.PlayerFirstName);
            if (name.Length == 0)
            {
                name = "(no player named)";
            }

            DrawClip(g, name, fonts.RowBold, PdfBrushes.Black, 10f, y, 200f, PlayerHeadH);
            DrawClip(g, ComposePlacement(bag.Head), fonts.Small, GrayBrush, 214f, y + 1f, SheetW - 224f, PlayerHeadH);
            y += PlayerHeadH;

            foreach (var line in bag.Lines)
            {
                g.DrawString(line.NetQuantity.ToString(CultureInfo.InvariantCulture) + " x",
                    fonts.Row, PdfBrushes.Black,
                    new RectangleF(20f, y, 24f, ItemLineH),
                    new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Top));
                DrawClip(g, ComposeItem(line), fonts.Row, PdfBrushes.Black, 50f, y, 330f, ItemLineH);

                // A returned unit is shown, not quietly netted away — the family asks about it.
                if (line.Restocked > 0)
                {
                    g.DrawString($"({line.Restocked} returned)", fonts.Small, RedBrush,
                        new RectangleF(384f, y + 0.5f, 152f, ItemLineH),
                        new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top));
                }
                y += ItemLineH;
            }
            y += 3f;
        }

        // ── Signature row (pre-filled when the batch is already signed for) ──
        y += 4f;
        var signedBy = Trim(head.SignedForBy);
        if (signedBy.Length > 0)
        {
            var when = head.SignedForDate.HasValue
                ? head.SignedForDate.Value.ToString("MM/dd/yyyy h:mm tt", CultureInfo.InvariantCulture)
                : "";
            g.DrawString($"Signed for by {signedBy}    {when}", fonts.RowBold, GreenBrush,
                new RectangleF(10f, y, SheetW - 20f, 12f),
                new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top));
        }
        else
        {
            g.DrawString("Received by", fonts.Small, GrayBrush,
                new RectangleF(10f, y + 5f, 60f, 12f),
                new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top));
            g.DrawLine(SignPen, new PointF(70f, y + 14f), new PointF(370f, y + 14f));
            g.DrawString("Date", fonts.Small, GrayBrush,
                new RectangleF(384f, y + 5f, 30f, 12f),
                new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top));
            g.DrawLine(SignPen, new PointF(412f, y + 14f), new PointF(SheetW, y + 14f));
        }

        y = top + MeasureFamilyBlock(family);
        g.DrawLine(HairlinePen, new PointF(0, y - 5f), new PointF(SheetW, y - 5f));
        return y;
    }

    // ══════════════════════════════════════════════════════════
    //  3. PER-FAMILY PIVOT — landscape, families down, SKUs across
    // ══════════════════════════════════════════════════════════

    private const float LandW = 792f, LandH = 612f;
    private const float PivMargin = 28.8f;
    private const float PivW = LandW - (PivMargin * 2);          // 734.4
    private const float PivTop = 30f;
    private const float PivMaxY = LandH - (PivMargin * 2) - 18f;
    private const float FamilyColW = 150f;
    private const float TotalColW = 34f;
    private const float PivHeaderH = 66f;
    private const float PivRowH = 13f;
    private const float MinSkuColW = 26f;

    public async Task<ReportExportResult> GenerateFamilyPivotAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var (_, rows) = await LoadAsync(_storeRepository, _analyticsRepository, jobId, cancellationToken);
        var live = rows.Where(r => r.NetQuantity > 0).ToList();

        using var document = new PdfDocument();
        document.PageSettings.Orientation = PdfPageOrientation.Landscape;
        document.PageSettings.Size = new SizeF(PageW, PageH);
        document.PageSettings.Margins.All = PivMargin;
        AddFooter(document, PivW, "Store per-family pivot");

        var fonts = new Fonts();

        if (live.Count == 0)
        {
            var empty = document.Pages.Add().Graphics;
            empty.DrawString("No paid store orders.", fonts.SectionTitle, PdfBrushes.Gray,
                new RectangleF(0, 40f, PivW, 20f),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
            return Save(document, "Store-Per-Family-Pivot.pdf");
        }

        // Columns = distinct SKUs actually purchased, in catalogue order.
        var skus = live
            .Select(ComposeItem)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Rows = families, with a per-SKU quantity map.
        var families = live
            .GroupBy(r => r.FamilyUserId)
            .Select(g => new
            {
                Name = ComposeFamilySortName(g.First()),
                Cells = g.GroupBy(ComposeItem, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Sum(r => r.NetQuantity), StringComparer.OrdinalIgnoreCase),
                Total = g.Sum(r => r.NetQuantity),
            })
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // How many SKU columns fit one landscape page? Beyond that the table is split into
        // panels, each repeating the family column — a wide store stays readable instead of
        // shrinking columns to illegibility.
        var availW = PivW - FamilyColW - TotalColW;
        var perPanel = Math.Max(1, (int)(availW / MinSkuColW));
        var panels = Chunk(skus, perPanel).ToList();

        foreach (var panel in panels)
        {
            var colW = Math.Min(60f, availW / panel.Count);
            PdfGraphics? g = null;
            var y = 0f;

            var panelIndex = panels.IndexOf(panel);
            var title = panels.Count > 1
                ? $"PER-FAMILY PIVOT  (columns {panelIndex + 1} of {panels.Count})"
                : "PER-FAMILY PIVOT";

            foreach (var family in families)
            {
                if (g == null || y + PivRowH > PivMaxY)
                {
                    g = NewSheetPage(document, fonts, title, PivW);
                    y = PivTop;
                    y = DrawPivotHeader(g, panel, colW, y, fonts);
                }

                DrawClip(g, family.Name, fonts.Row, PdfBrushes.Black, 2f, y + 1.5f, FamilyColW - 4f, PivRowH);

                var x = FamilyColW;
                var panelTotal = 0;
                foreach (var sku in panel)
                {
                    if (family.Cells.TryGetValue(sku, out var qty) && qty > 0)
                    {
                        panelTotal += qty;
                        g.DrawString(qty.ToString(CultureInfo.InvariantCulture), fonts.Row, PdfBrushes.Black,
                            new RectangleF(x, y + 1.5f, colW, PivRowH),
                            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Top));
                    }
                    x += colW;
                }

                g.DrawString(panelTotal.ToString(CultureInfo.InvariantCulture), fonts.RowBold, PdfBrushes.Black,
                    new RectangleF(FamilyColW + (panel.Count * colW), y + 1.5f, TotalColW, PivRowH),
                    new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Top));

                g.DrawLine(HairlinePen, new PointF(0, y + PivRowH), new PointF(FamilyColW + (panel.Count * colW) + TotalColW, y + PivRowH));
                y += PivRowH;
            }

            // Column totals close the panel.
            if (g != null)
            {
                if (y + PivRowH > PivMaxY)
                {
                    g = NewSheetPage(document, fonts, title, PivW);
                    y = PivTop;
                    y = DrawPivotHeader(g, panel, colW, y, fonts);
                }

                DrawClip(g, "TOTAL", fonts.RowBold, PdfBrushes.Black, 2f, y + 1.5f, FamilyColW - 4f, PivRowH);
                var x = FamilyColW;
                var grand = 0;
                foreach (var sku in panel)
                {
                    var colTotal = families.Sum(f => f.Cells.TryGetValue(sku, out var q) ? q : 0);
                    grand += colTotal;
                    g.DrawString(colTotal.ToString(CultureInfo.InvariantCulture), fonts.RowBold, PdfBrushes.Black,
                        new RectangleF(x, y + 1.5f, colW, PivRowH),
                        new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Top));
                    x += colW;
                }
                g.DrawString(grand.ToString(CultureInfo.InvariantCulture), fonts.RowBold, PdfBrushes.Black,
                    new RectangleF(FamilyColW + (panel.Count * colW), y + 1.5f, TotalColW, PivRowH),
                    new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Top));
            }
        }

        return Save(document, "Store-Per-Family-Pivot.pdf");
    }

    /// <summary>
    /// Column headers wrap within a tall band rather than rotating: rotated text is fragile across
    /// viewers and a wrapped 6pt label stays selectable and searchable.
    /// </summary>
    private static float DrawPivotHeader(
        PdfGraphics g, List<string> panel, float colW, float y, Fonts fonts)
    {
        g.DrawRectangle(BandBrush, new RectangleF(0, y, FamilyColW + (panel.Count * colW) + TotalColW, PivHeaderH));
        DrawClip(g, "Family", fonts.ColHeader, PdfBrushes.Black, 2f, y + PivHeaderH - 12f, FamilyColW - 4f, 11f);

        var x = FamilyColW;
        foreach (var sku in panel)
        {
            // Break on the SKU's own separator rather than letting word-wrap pick the point —
            // otherwise a column reads "Hoodie - Large -" over "Light Blue", with a dangling dash.
            g.DrawString(sku.Replace(" - ", "\n"), fonts.Tiny, PdfBrushes.Black,
                new RectangleF(x + 1f, y + 2f, colW - 2f, PivHeaderH - 4f),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Bottom)
                {
                    WordWrap = PdfWordWrapType.Word,
                    LineLimit = true,
                });
            g.DrawLine(HairlinePen, new PointF(x, y), new PointF(x, y + PivHeaderH));
            x += colW;
        }

        g.DrawString("Total", fonts.ColHeader, PdfBrushes.Black,
            new RectangleF(x, y + PivHeaderH - 12f, TotalColW, 11f),
            new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Top));

        g.DrawLine(RulePen, new PointF(0, y + PivHeaderH), new PointF(x + TotalColW, y + PivHeaderH));
        return y + PivHeaderH;
    }

    // ══════════════════════════════════════════════════════════
    //  Shared drawing helpers
    // ══════════════════════════════════════════════════════════

    private static PdfGraphics NewSheetPage(PdfDocument document, Fonts fonts, string title, float width = SheetW)
    {
        var g = document.Pages.Add().Graphics;
        g.DrawString(title, fonts.PageTitle, PdfBrushes.Black,
            new RectangleF(0, 0, width, 18f),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top));
        g.DrawLine(RulePen, new PointF(0, 20f), new PointF(width, 20f));
        return g;
    }

    private static void AddFooter(PdfDocument document, float width, string caption)
    {
        var footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
        var gray = new PdfSolidBrush(new PdfColor(102, 102, 102));
        var footer = new PdfPageTemplateElement(new RectangleF(0, 0, width, 16f));
        footer.Graphics.DrawString($"{caption} - TeamSportsInfo.com", footerFont, gray, new PointF(2, 4));

        var composite = new PdfCompositeField(
            footerFont, gray, "Page {0} of {1}",
            new PdfPageNumberField(footerFont, gray),
            new PdfPageCountField(footerFont, gray))
        {
            Bounds = new RectangleF(0, 4, width, 16f),
            StringFormat = new PdfStringFormat(PdfTextAlignment.Right),
        };
        composite.Draw(footer.Graphics, new PointF(0, 4));
        document.Template.Bottom = footer;
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

    private static void DrawClip(
        PdfGraphics g, string text, PdfFont font, PdfBrush brush, float x, float y, float w, float h)
    {
        if (text.Length == 0)
        {
            return;
        }
        // LineLimit stays FALSE deliberately. With it on, Syncfusion drops any line whose measured
        // height (font size plus leading) exceeds the rectangle — so an 11pt name in a 13pt box
        // rendered as nothing at all, silently, and the bag labels came out with no player on them.
        // WordWrap.None already guarantees a single line, and the bounds rectangle clips it.
        g.DrawString(text, font, brush, new RectangleF(x, y, w, h),
            new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Top)
            {
                WordWrap = PdfWordWrapType.None,
                LineLimit = false,
            });
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }

    // ── Shaping ──

    /// <summary>
    /// Placement line. Walk-ups and unattached lines are NAMED rather than left blank — a blank
    /// line reads as a rendering bug, and these are exactly the rows Crystal used to drop.
    /// </summary>
    private static string ComposePlacement(StoreFulfillmentRowDto r)
    {
        if (r.PlayerRegId == null)
        {
            return "UNASSIGNED - not linked to a registration";
        }
        if (r.IsWalkUp)
        {
            return "WALK-UP / counter sale";
        }

        var parts = new[] { Trim(r.AgegroupName), Trim(r.ClubName), Trim(r.TeamName) }
            .Where(s => s.Length > 0)
            .ToArray();
        return parts.Length > 0 ? string.Join(" : ", parts) : "No team assigned";
    }

    /// <summary>Mom first, Dad as fallback — matches the Mom-primary order both Crystal procs use.</summary>
    private static string ComposeContact(StoreFulfillmentRowDto r)
    {
        var mom = ComposeFirstLast(r.MomFirstName, r.MomLastName);
        var dad = ComposeFirstLast(r.DadFirstName, r.DadLastName);

        if (mom.Length > 0)
        {
            return Join(mom, FormatPhone(r.MomCellphone));
        }
        if (dad.Length > 0)
        {
            return Join(dad, FormatPhone(r.DadCellphone));
        }
        return Trim(r.FamilyUsername);
    }

    private static string ComposeFamilySortName(StoreFulfillmentRowDto r)
    {
        var mom = ComposeLastFirst(r.MomLastName, r.MomFirstName);
        if (mom.Length > 0)
        {
            return mom;
        }
        var dad = ComposeLastFirst(r.DadLastName, r.DadFirstName);
        return dad.Length > 0 ? dad : Trim(r.FamilyUsername);
    }

    /// <summary>
    /// Separators are ASCII on purpose. <see cref="PdfStandardFont"/> is a WinAnsi base-14 face and
    /// silently drops glyphs it cannot encode — an em dash and a bullet both rendered as nothing,
    /// leaving "Shirt  Adult Medium  Blue" looking like a spacing bug. Embedding a TrueType face
    /// would fix the encoding, at the cost of carrying a font file for two punctuation marks.
    /// </summary>
    private static string ComposeItem(StoreFulfillmentRowDto r)
    {
        var parts = new[] { Trim(r.ItemName), Trim(r.SizeName), Trim(r.ColorName) }
            .Where(s => s.Length > 0);
        return string.Join(" - ", parts);
    }

    private static string ComposeFirstLast(string? first, string? last)
        => $"{Trim(first)} {Trim(last)}".Trim();

    private static string ComposeLastFirst(string? last, string? first)
    {
        var l = Trim(last);
        var f = Trim(first);
        if (l.Length == 0 && f.Length == 0)
        {
            return "";
        }
        return l.Length == 0 ? f : (f.Length == 0 ? l : $"{l}, {f}");
    }

    private static string Join(string a, string b)
        => b.Length > 0 ? $"{a}  |  {b}" : a;

    private static string FormatPhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 10
            ? $"{digits[..3]}-{digits.Substring(3, 3)}-{digits[6..]}"
            : Trim(phone);
    }

    private static string Trim(string? s) => (s ?? "").Trim();

    // ── Render-time resources ──

    private static readonly PdfSolidBrush GrayBrush = new(new PdfColor(105, 105, 105));
    private static readonly PdfSolidBrush RedBrush = new(new PdfColor(190, 30, 30));
    private static readonly PdfSolidBrush GreenBrush = new(new PdfColor(20, 120, 60));
    private static readonly PdfSolidBrush BandBrush = new(new PdfColor(235, 238, 242));
    private static readonly PdfPen HairlinePen = new(new PdfColor(200, 200, 200), 0.4f);
    private static readonly PdfPen RulePen = new(new PdfColor(60, 60, 60), 0.8f);
    private static readonly PdfPen SignPen = new(new PdfColor(120, 120, 120), 0.6f);

    private sealed class Fonts
    {
        public PdfStandardFont LabelName { get; } = new(PdfFontFamily.Helvetica, 11, PdfFontStyle.Bold);
        public PdfStandardFont LabelMeta { get; } = new(PdfFontFamily.Helvetica, 7);
        public PdfStandardFont LabelItem { get; } = new(PdfFontFamily.Helvetica, 8.5f);
        public PdfStandardFont LabelFoot { get; } = new(PdfFontFamily.Helvetica, 6.5f);

        public PdfStandardFont PageTitle { get; } = new(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
        public PdfStandardFont SectionTitle { get; } = new(PdfFontFamily.Helvetica, 9.5f, PdfFontStyle.Bold);
        public PdfStandardFont ColHeader { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont RowBold { get; } = new(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
        public PdfStandardFont Row { get; } = new(PdfFontFamily.Helvetica, 8);
        public PdfStandardFont Small { get; } = new(PdfFontFamily.Helvetica, 7);
        public PdfStandardFont Tiny { get; } = new(PdfFontFamily.Helvetica, 6);
    }
}
