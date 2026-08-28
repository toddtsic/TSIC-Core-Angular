namespace TSIC.Contracts.Store;

/// <summary>
/// The one way size names are put in SIZE order — XS before Small before XL — rather than
/// alphabetical order.
/// </summary>
/// <remarks>
/// <c>stores.StoreSizes</c> has no sort column and is one global list shared by every store on
/// the platform, so the order has to come out of the name. Legacy ordered by
/// <c>StoreSizeName</c> and lived with the result: an Adult S/M/L/XL shirt listed on the
/// storefront as "Adult Large, Adult Medium, Adult Small, Adult XL".
///
/// The key is (group, scale, name). Youth sorts before unprefixed before Adult; within a group
/// the scale token decides; and the name is the final tiebreak so anything unrecognised lands in
/// a stable alphabetical block at the END of its own group instead of jumping to the front.
///
/// A prefix only counts when a separator follows it, so "XS" is not read as an "X" size, and an
/// unrecognised prefix ("Mens Large") is left whole and falls to the unknown block rather than
/// being silently filed under a group it was never given.
///
/// Callers must apply this AFTER materialising the query — EF cannot translate it, exactly as
/// with <see cref="StoreSkuLabel"/>.
/// </remarks>
public static class StoreSizeOrder
{
    private const int UnknownScale = int.MaxValue;

    private const int YouthGroup = 0;
    private const int PlainGroup = 1;
    private const int AdultGroup = 2;

    private static readonly Dictionary<string, int> ScaleRanks = new(StringComparer.OrdinalIgnoreCase)
    {
        // One-size-fits-all leads: it is not a point on the scale, and a store that uses it
        // generally uses nothing else.
        ["one size"] = 0, ["onesize"] = 0, ["os"] = 0, ["standard"] = 0, ["std"] = 0,

        ["3xs"] = 10, ["xxxs"] = 10,
        ["2xs"] = 11, ["xxs"] = 11,
        ["xs"] = 12, ["x-small"] = 12, ["extra small"] = 12,
        ["petite"] = 13,
        ["s"] = 20, ["sm"] = 20, ["small"] = 20,
        ["m"] = 30, ["md"] = 30, ["med"] = 30, ["medium"] = 30,
        ["l"] = 40, ["lg"] = 40, ["large"] = 40,
        ["xl"] = 50, ["x-large"] = 50, ["extra large"] = 50,
        ["2xl"] = 51, ["xxl"] = 51,
        ["3xl"] = 52, ["xxxl"] = 52,
        ["4xl"] = 53, ["xxxxl"] = 53,
        ["5xl"] = 54,
    };

    private static readonly string[] YouthPrefixes = ["youth", "yth", "junior", "jr", "kids", "child"];
    private static readonly string[] AdultPrefixes = ["adult"];

    /// <summary>Sort key for a size name. Order by this, then by colour, as before.</summary>
    public static (int Group, int Scale, string Name) Key(string? sizeName)
    {
        var name = (sizeName ?? string.Empty).Trim();
        if (name.Length == 0) return (PlainGroup, UnknownScale, string.Empty);

        var (group, token) = SplitPrefix(name);
        return (group, Scale(token), name);
    }

    private static (int Group, string Token) SplitPrefix(string name)
    {
        foreach (var prefix in YouthPrefixes)
        {
            if (TryStrip(name, prefix, out var rest)) return (YouthGroup, rest);
        }

        foreach (var prefix in AdultPrefixes)
        {
            if (TryStrip(name, prefix, out var rest)) return (AdultGroup, rest);
        }

        return (PlainGroup, name);
    }

    private static bool TryStrip(string name, string prefix, out string remainder)
    {
        remainder = name;

        if (name.Length <= prefix.Length) return false;
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var separator = name[prefix.Length];
        if (separator != ' ' && separator != '-' && separator != '_') return false;

        remainder = name[(prefix.Length + 1)..].Trim();
        return true;
    }

    private static int Scale(string token)
    {
        if (ScaleRanks.TryGetValue(token, out var rank)) return rank;

        // Numeric sizes (youth 6/8/10, a waist measurement) sort numerically, after the lettered
        // scale — a store that mixes the two is naming two different things.
        if (int.TryParse(token, out var number) && number >= 0) return 100 + number;

        return UnknownScale;
    }
}
