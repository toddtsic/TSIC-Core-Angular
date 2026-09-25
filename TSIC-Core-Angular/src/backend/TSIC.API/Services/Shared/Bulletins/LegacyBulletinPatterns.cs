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
    /// <summary>
    /// The legacy SCHEDULE link fragment, on its own because one caller needs to identify
    /// schedule bulletins specifically rather than legacy bulletins generally — see
    /// <see cref="HasLegacyScheduleLink"/>.
    /// </summary>
    public const string ScheduleFragment = "schedules/index";

    // Lowercase fragments, matched case-insensitively against the bulletin body.
    // Mirrors the substrings TranslateLegacyUrlsPipe keys on.
    private static readonly string[] Fragments =
    {
        "startaregistration",
        "jobadministrator/admin",
        "rosters/rosterspubliclookuptourny",
        "rosters/rosterpubliclookup",
        ScheduleFragment,
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

    /// <summary>
    /// True if the bulletin body links at the legacy SCHEDULE route (case-insensitive).
    ///
    /// Narrower than <see cref="HasLegacyLink"/> on purpose. The schedule-release path
    /// removes the job's superseded schedule bulletin; matching the full fragment list
    /// there would also delete the job's legacy registration and roster bulletins, which
    /// have nothing to do with releasing a schedule.
    ///
    /// Matched on the BODY, never the title: the same bulletin ships under at least
    /// "Schedules Now Available!", "Schedules Are Posted!", "SCHEDULES ARE LIVE" and
    /// "Schedule is UP!" — a title match finds barely half of them.
    /// </summary>
    public static bool HasLegacyScheduleLink(string? text) =>
        !string.IsNullOrEmpty(text)
        && text.Contains(ScheduleFragment, StringComparison.OrdinalIgnoreCase);
}
