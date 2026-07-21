namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// Canonical list of legacy ASP.NET-MVC URL fragments that identify a bulletin whose
/// hyperlinks were authored for the old app.
///
/// This is the DETECTION half of legacy-link handling. The REWRITE half — mapping each
/// fragment to a new Angular route — lives in the frontend TranslateLegacyUrlsPipe
/// (infrastructure/pipes/translate-legacy-urls.pipe.ts). The two are intentionally
/// separate: detection is a flat substring test and belongs where the consequential action
/// (Active = 0) happens; rewriting is Angular route construction and must stay client-side.
/// If a legacy pattern is ever added to the pipe, add it here too — keep the lists in sync.
///
/// Used by <see cref="BulletinService"/> to auto-retire legacy-link bulletins in go-live
/// environments (bGoLive = true), where smart bulletins have superseded them.
/// </summary>
public static class LegacyBulletinPatterns
{
    // Lowercase fragments, matched case-insensitively against the bulletin body.
    // Mirrors the substrings TranslateLegacyUrlsPipe keys on.
    private static readonly string[] Fragments =
    {
        "startaregistration",
        "jobadministrator/admin",
        "rosters/rosterspubliclookuptourny",
        "rosters/rosterpubliclookup",
        "schedules/index",
        "playerwaiverupdate",
    };

    /// <summary>
    /// True if the bulletin body contains any known legacy URL fragment (case-insensitive).
    /// </summary>
    public static bool HasLegacyLink(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var fragment in Fragments)
        {
            if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
