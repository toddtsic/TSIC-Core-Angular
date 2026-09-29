namespace TSIC.API.Services.Teams;

/// <summary>
/// Club job (customer name + team gender) -> the club name that club registers under at
/// tournaments. The ONE derivation behind the TSIC-Teams Schedules tab; every endpoint of it
/// resolves the name here, server side, and never takes it from the client.
///
/// Hardcoded by ruling: a new client is a code change and a deploy, not a table. Validate every
/// entry by ROSTER OVERLAP before adding it -- the club-job customer name and the in-event club
/// name routinely differ ("STEPS Lacrosse" girls play as "STEPS Elite NJ"), and a guessed entry
/// silently shows the wrong club's schedules.
///
/// An unlisted customer/gender resolves to null, which the tab shows as no tournaments.
/// </summary>
public static class TournamentClubNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // STEPS -- validated by roster overlap 2026-09-29. "All American Aim" is AIM's name from
        // before STEPS bought it; it has no upcoming games and is deliberately absent.
        [Key("STEPS Lacrosse", "F")] = "STEPS Elite NJ",
        [Key("STEPS Lacrosse", "M")] = "STEPS",
        [Key("STEPS Elite AIM", "F")] = "STEPS Elite AIM!",
        [Key("STEPS Lacrosse California", "F")] = "STEPS California",
    };

    public static string? Resolve(string? customerName, string? gender)
    {
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(gender)) return null;
        return Map.GetValueOrDefault(Key(customerName, gender));
    }

    private static string Key(string customerName, string gender) =>
        $"{customerName.Trim()}|{gender.Trim()}";
}
