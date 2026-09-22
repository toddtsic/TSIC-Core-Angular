namespace TSIC.Domain.Time;

/// <summary>
/// Arizona is the zone every datetime in this database is anchored in, and the database stores
/// no offset with any of them. This attaches the one it has always had.
///
/// UTC-7 is HARDCODED, not derived from the machine. Arizona does not observe daylight saving,
/// so the offset holds all year -- and the label has to describe the zone THE DATA IS ANCHORED
/// IN, not whatever zone an app server happens to be configured for, which can silently
/// disagree with the database. A box misconfigured to Eastern would otherwise re-stamp every
/// timestamp it served.
/// </summary>
public static class Arizona
{
    /// <summary>UTC-7, year round. Arizona observes no daylight saving.</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-7);

    /// <summary>
    /// Stamps an Arizona-local <see cref="DateTime"/> with its real offset so a client can
    /// convert by arithmetic instead of hardcoding the zone itself. The instant does not move --
    /// only the offset it carries becomes explicit.
    /// </summary>
    public static DateTimeOffset At(DateTime arizonaLocal) =>
        new(DateTime.SpecifyKind(arizonaLocal, DateTimeKind.Unspecified), Offset);

    /// <summary>Nullable overload, so call sites do not each write the same conditional.</summary>
    public static DateTimeOffset? At(DateTime? arizonaLocal) =>
        arizonaLocal is { } value ? At(value) : null;

    /// <summary>
    /// Now, in Arizona, with the offset attached. <c>DateTime.Now</c> rather than a converted
    /// UTC because the app servers run in Arizona and the database's own defaults
    /// (<c>SYSDATETIME()</c>) do exactly this.
    /// </summary>
    public static DateTimeOffset Now => At(DateTime.Now);
}
