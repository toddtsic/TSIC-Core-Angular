using TSIC.Domain.Constants;

namespace TSIC.Domain.JobRules;

/// <summary>
/// Which mobile app a job's push notifications go to. Exactly one, never both — the two
/// apps are separate Firebase projects and a token minted by one is rejected by the other's
/// credential, so "send to everyone" is not a thing that can exist.
/// </summary>
public enum PushAudience
{
    /// <summary>This job feeds neither app. A send has no audience and must not go out.</summary>
    None = 0,

    /// <summary>TSIC-Events — the tournament/league app. Pool is Device_Jobs.</summary>
    Events = 1,

    /// <summary>TSIC-Teams — the player-site app. Pool is Device_Teams rows carrying a RegistrationId.</summary>
    Teams = 2
}

/// <summary>
/// The ONE rule that decides a job's push audience. Every send path and every readiness
/// readout resolves through here — a badge that disagrees with what actually shipped is
/// the failure this type exists to prevent.
/// </summary>
public static class PushAudienceResolver
{
    /// <summary>
    /// Job type picks the app; the TSIC-Teams flag is a second, independent gate on top of it.
    ///
    /// Scheduling jobs (tournament, league) are TSIC-Events and do not consult the flag —
    /// no scheduling job has ever had it set. Everything else is TSIC-Teams, but only when
    /// the director has actually turned the app on. Showcase is deliberately off both apps.
    /// </summary>
    public static PushAudience Resolve(int jobTypeId, bool teamsEnabled) => jobTypeId switch
    {
        JobConstants.JobTypeTournament => PushAudience.Events,
        JobConstants.JobTypeLeague => PushAudience.Events,

        // Showcase runs no mobile app at all — not Events, not Teams, flag or no flag.
        JobConstants.JobTypeShowcase => PushAudience.None,

        // Root is a system container, not a real job.
        JobConstants.JobTypeRoot => PushAudience.None,

        // Club, camp, sales — and any type added later. The flag is what makes it real.
        _ => teamsEnabled ? PushAudience.Teams : PushAudience.None
    };
}
