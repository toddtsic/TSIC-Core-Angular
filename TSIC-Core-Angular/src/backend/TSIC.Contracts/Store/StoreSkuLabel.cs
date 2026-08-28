namespace TSIC.Contracts.Store;

/// <summary>
/// The one way a SKU is named for a human: <c>Item:Size:Color</c>.
/// </summary>
/// <remarks>
/// Legacy interpolated the three parts with a bare <c>':'</c>. In SQL that makes the WHOLE label
/// NULL when any part is null, so a SKU missing a colour rendered as nothing at all. This joins
/// the non-blank parts instead — identical for every SKU that carries all three (which is all of
/// them today) and degrading to "Item:Large" rather than blank when one is missing.
///
/// Callers must build this on the CLIENT side, after materialising the query. EF cannot
/// translate it, and it does not need to: every caller already projects to an anonymous row
/// first.
/// </remarks>
public static class StoreSkuLabel
{
    public static string Build(string? itemName, string? sizeName, string? colorName) =>
        string.Join(":", new[] { itemName, sizeName, colorName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
}
