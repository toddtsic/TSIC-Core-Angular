using System.Globalization;
using System.Text;

namespace TSIC.Infrastructure.Repositories.Shared;

/// <summary>
/// Reading a SEASON out of a job's stored fields: the year it belongs to, and the lineage key
/// that collapses every season of one event onto a single name.
/// </summary>
/// <remarks>
/// Lifted out of <c>CustomerJobRevenueRepository</c> when the feeder-pace widget needed the same
/// two rules. Kept as ONE definition on purpose: the boundary conditions below were each found
/// on real customer data, and a second copy would drift away from them silently. Both reports
/// must place a job in the same season and under the same lineage or they will disagree on
/// screen about the same event.
/// </remarks>
internal static class JobSeasonNaming
{
    /// <summary>
    /// <c>Jobs.year</c> is varchar and not validated on write. Anything that is not a plausible
    /// 4-digit season is refused rather than coerced — a job that cannot be placed in a column
    /// is reported to the reader, not guessed at.
    /// </summary>
    public static int? ParseYear(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var s = raw.Trim();
        if (s.Length != 4 || !int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }
        return year is >= 1990 and <= 2100 ? year : null;
    }

    /// <summary>
    /// The lineage key: the job name with its SEASON DESIGNATOR removed, so every year of one
    /// event collapses to a single group.
    /// </summary>
    /// <remarks>
    /// This used to strip only the job's OWN <c>Jobs.year</c> where it stood alone, and that was
    /// wrong twice over on real data (found on STEPS Lacrosse California, 2026-09-02):
    ///
    ///   JobName                          Jobs.year
    ///   Girls Elite Players 2020-2021    2021
    ///   Girls Elite Players 2026-2027    2027
    ///
    /// The name carries a SPAN, and Jobs.year is the SECOND year of it. "2021" inside "2020-2021"
    /// is not standalone — a digit precedes it — so nothing was stripped and every single season
    /// became its own lineage. Twenty-two "events" for what are really a handful, no history under
    /// any of them, and worse: a lineage whose only member had expired was no longer live at the
    /// range start, so it was dropped from the report altogether. That is why seasons through
    /// 2024 were missing rather than merely uncompared.
    ///
    /// So the rule is about the SHAPE of a season designator, not about one job's stored year:
    /// a standalone four-digit year, or a span of two joined by a dash or slash, with the second
    /// half written either way ("2024-2025", "2024-25"). Jobs.year is still what places a job in
    /// its column — it is only no longer trusted to describe the name.
    ///
    /// Deliberately NOT a regex: this runs over every job in a customer group on every request,
    /// and the boundary rules (no digit or letter either side) are the whole correctness argument
    /// — worth reading as code rather than hiding in a pattern.
    /// </remarks>
    public static string StripSeasonToken(string jobName)
    {
        var stripped = new StringBuilder(jobName.Length);

        var i = 0;
        while (i < jobName.Length)
        {
            var len = SeasonTokenLength(jobName, i);
            if (len > 0)
            {
                i += len;
                continue;
            }
            stripped.Append(jobName[i]);
            i++;
        }

        // Collapse the whitespace the removal left behind, so "Fall  Draw" cannot fork a group.
        var collapsed = new StringBuilder(stripped.Length);
        var lastWasSpace = false;
        foreach (var ch in stripped.ToString())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    collapsed.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }
            collapsed.Append(ch);
            lastWasSpace = false;
        }

        var key = collapsed.ToString().Trim(' ', '-', '–', '—', ',', ':', '/');
        // A name that was NOTHING but its season leaves nothing to group on — keep the original
        // rather than emit an empty key that would swallow every other such job.
        return key.Length == 0 ? jobName.Trim() : key;
    }

    /// <summary>
    /// Length of the season designator starting at <paramref name="start"/>, or 0 if there is
    /// none. Matches "2026" and "2026-2027" / "2026-27" / "2026/27" and the dash variants.
    /// </summary>
    private static int SeasonTokenLength(string s, int start)
    {
        // A designator never begins mid-token: a preceding digit means we are inside a longer
        // number, a preceding letter means it is part of a word ("U2026" is not a season).
        if (start > 0 && (char.IsDigit(s[start - 1]) || char.IsLetter(s[start - 1])))
        {
            return 0;
        }

        if (!IsYearAt(s, start))
        {
            return 0;
        }

        var end = start + 4;

        // Optional second half: a separator, then two OR four digits. Spaces are allowed around
        // the separator because "2026 - 2027" is written that way often enough to matter.
        var probe = end;
        while (probe < s.Length && s[probe] == ' ')
        {
            probe++;
        }
        if (probe < s.Length && (s[probe] == '-' || s[probe] == '/' || s[probe] == '–' || s[probe] == '—'))
        {
            probe++;
            while (probe < s.Length && s[probe] == ' ')
            {
                probe++;
            }
            if (IsYearAt(s, probe))
            {
                end = probe + 4;
            }
            else if (probe + 2 <= s.Length && char.IsDigit(s[probe]) && char.IsDigit(s[probe + 1])
                && (probe + 2 == s.Length || !char.IsDigit(s[probe + 2])))
            {
                end = probe + 2;
            }
        }

        // And it never ends mid-token either.
        if (end < s.Length && (char.IsDigit(s[end]) || char.IsLetter(s[end])))
        {
            return 0;
        }

        return end - start;
    }

    /// <summary>
    /// Four digits at <paramref name="at"/> forming a plausible season year. Bounded so a street
    /// number or a jersey number cannot be mistaken for one.
    /// </summary>
    private static bool IsYearAt(string s, int at)
    {
        if (at + 4 > s.Length)
        {
            return false;
        }
        for (var k = at; k < at + 4; k++)
        {
            if (!char.IsDigit(s[k]))
            {
                return false;
            }
        }
        if (at + 4 < s.Length && char.IsDigit(s[at + 4]))
        {
            return false;
        }
        var value = ((s[at] - '0') * 1000) + ((s[at + 1] - '0') * 100) + ((s[at + 2] - '0') * 10) + (s[at + 3] - '0');
        return value is >= 1990 and <= 2100;
    }
}
