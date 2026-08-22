namespace TSIC.Domain.Constants;

/// <summary>
/// Region tokens for the public ASL roster board.
///
/// American Select Lacrosse names its Main Event teams "ASL:{Region} {qualifier} {gradYear}"
/// — region is encoded in the team name, not stored as a column. Legacy resolved it with a
/// hardcoded ternary chain duplicated across two controller actions; this is that same ordered
/// first-match list, declared once.
///
/// Matching is <c>Contains</c> against the team name, first match wins, so ordering is
/// load-bearing whenever one token is a substring of another. A team matching nothing falls
/// back to its own full team name, which is what legacy did.
/// </summary>
public static class AslRegionConstants
{
    /// <summary>Prefix every ASL team name carries.</summary>
    public const string TeamNamePrefix = "ASL:";

    /// <summary>
    /// Ordered region tokens. Add new regions here — this is the single place that decides
    /// both the region dropdown and each card's region label.
    /// </summary>
    public static readonly IReadOnlyList<string> Regions =
    [
        "ASL:California",
        "ASL:NorCal",
        "ASL:SoCal",
        "ASL:Canada",
        "ASL:Colorado",
        "ASL:Connecticut",
        "ASL:DC - Virginia",
        "ASL:Delaware",
        "ASL:Florida",
        "ASL:Georgia",
        "ASL:Ireland",
        "ASL:Long Island",
        "ASL:Maryland",
        "ASL:Massachusetts",
        "ASL:Midwest",
        "ASL:New Jersey",
        "ASL:New York Capital Region",
        "ASL:North Carolina",
        "ASL:North East",
        "ASL:NY Downstate",
        "ASL:NY Upstate",
        "ASL:Ohio",
        "ASL:Pacific Northwest",
        "ASL:Pennsylvania",
        "ASL:Six Nations",
        "ASL:Tennessee",
        "ASL:Texas",
        "ASL:Utah"
    ];

    /// <summary>
    /// Resolve a team name to its region token. Falls back to the trimmed team name when no
    /// token matches, mirroring legacy so an unrecognized team still appears rather than vanishing.
    /// </summary>
    public static string ResolveRegion(string? teamName)
    {
        var name = (teamName ?? string.Empty).Trim();
        foreach (var region in Regions)
        {
            if (name.Contains(region, StringComparison.OrdinalIgnoreCase))
                return region;
        }
        return name;
    }

    /// <summary>
    /// Trailing 4 characters of the team name — the graduation year. Team names carry stray
    /// trailing whitespace in live data, so trim before slicing or the year comes back as "028 ".
    /// </summary>
    public static string ResolveGradYear(string? teamName)
    {
        var name = (teamName ?? string.Empty).Trim();
        return name.Length >= 4 ? name[^4..] : name;
    }
}
