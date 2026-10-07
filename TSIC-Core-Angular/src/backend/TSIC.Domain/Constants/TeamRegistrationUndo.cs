namespace TSIC.Domain.Constants;

/// <summary>
/// The club rep's mistake-undo on a just-registered team (Todd 2026-09-26): "allow them to remove a
/// team they recorded by mistake, but not a historic one they committed to and the event is
/// counting on". Applies REGARDLESS of the director's BClubRepAllowDelete toggle, when ALL hold:
///   - inside <see cref="Window"/> of the team's Createdate (server clock);
///   - nothing paid, no ledger rows, no ARB subscription (checked by the caller);
///   - still in holding: the agegroup's Unassigned division (no director placement yet), or waitlisted.
/// The rep's own team and an open event are the caller's gates, as for any unregister.
/// Removing the row frees the age-group slot: the count is live over active Teams rows.
/// </summary>
public static class TeamRegistrationUndo
{
    /// <summary>60 minutes — Todd's ruled value (2026-10-07, AR-146).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    /// <summary>Not yet placed by the director: no division, the Unassigned holding division, or a waitlist.</summary>
    public static bool IsInHolding(string? divisionName, string? agegroupName) =>
        string.IsNullOrEmpty(divisionName)
        || string.Equals(divisionName, DivisionConstants.Unassigned, StringComparison.OrdinalIgnoreCase)
        || AgegroupConstants.IsWaitlist(agegroupName);

    /// <summary>Whole seconds left in the window at <paramref name="now"/>; 0 once it has closed.</summary>
    public static int SecondsLeft(DateTime createdate, DateTime now)
    {
        var left = (createdate + Window - now).TotalSeconds;
        return left <= 0 ? 0 : (int)Math.Ceiling(left);
    }
}
