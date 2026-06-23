namespace TSIC.Contracts.Dtos;

/// <summary>
/// Real-time availability snapshot for a job's public page.
/// Drives the "Job Pulse" widget and the user-dropdown task list.
/// Job-level flags are always populated. My* fields are populated only when
/// the request is authenticated AND the JWT jobPath matches the URL jobPath.
/// </summary>
public record JobPulseDto
{
    public required bool PlayerRegistrationOpen { get; init; }

    /// <summary>
    /// Computed: true when the job has at least one team currently within its
    /// registration-availability window (Effectiveasofdate/Expireondate) AND
    /// allowing self-rostering (team- or agegroup-level). Independent of the
    /// admin toggle PlayerRegistrationOpen — both must be true for a family
    /// to actually register a player.
    /// </summary>
    public required bool PlayerTeamsAvailableForRegistration { get; init; }

    public required bool PlayerRegRequiresToken { get; init; }
    public required bool TeamRegistrationOpen { get; init; }
    public required bool TeamRegRequiresToken { get; init; }
    public required bool ClubRepAllowAdd { get; init; }
    public required bool ClubRepAllowEdit { get; init; }
    public required bool ClubRepAllowDelete { get; init; }
    public required bool AllowRosterViewPlayer { get; init; }
    public required bool AllowRosterViewAdult { get; init; }
    /// <summary>Inverse of bRestrictPublicRosters — drives the public "Rosters" card.
    /// Distinct from AllowRosterView* (those gate a logged-in user's OWN roster).</summary>
    public required bool PublicRostersAvailable { get; init; }
    public required bool OfferPlayerRegsaverInsurance { get; init; }
    public required bool OfferTeamRegsaverInsurance { get; init; }
    public required bool StoreEnabled { get; init; }
    public required bool StoreHasActiveItems { get; init; }
    public required bool AllowStoreWalkup { get; init; }
    public required bool EnableStayToPlay { get; init; }
    public required bool SchedulePublished { get; init; }
    public required bool PlayerRegistrationPlanned { get; init; }
    public required bool AdultRegistrationPlanned { get; init; }
    public required bool PublicSuspended { get; init; }
    public DateTime? RegistrationExpiry { get; init; }

    /// <summary>
    /// Soonest close date (Teams.Expireondate) among player self-rosterable teams
    /// that are currently within their registration window. Drives the anonymous
    /// landing countdown ("Registration closes in X"). Null when no such team is
    /// open or none has a close date (open-ended). Mirrors the
    /// PlayerTeamsAvailableForRegistration team filter.
    /// </summary>
    public DateTime? PlayerRegClosesSoonest { get; init; }

    /// <summary>
    /// Soonest open date (Teams.Effectiveasofdate) among player self-rosterable
    /// teams whose window has not started yet. Drives "Registration opens in X"
    /// when nothing is open. Null when no upcoming team exists.
    /// </summary>
    public DateTime? PlayerRegOpensSoonest { get; init; }

    /// <summary>
    /// Earliest scheduled game date (Schedule.GDate) for this job, or null when no
    /// games are scheduled. Day-granular. Combined with SchedulePublished, lets the
    /// landing hero tell "schedule published but season not started yet" (preEvent,
    /// FirstGameDate in the future) from "in season" (FirstGameDate today or past).
    /// </summary>
    public DateTime? FirstGameDate { get; init; }

    /// <summary>
    /// Latest scheduled game date (Schedule.GDate) for this job, or null when no
    /// games are scheduled. Day-granular. Once SchedulePublished and this has fully
    /// passed, the event is concluded — the landing hero suppresses registration
    /// CTAs and the countdown regardless of any director toggle left on. This is the
    /// factual "event is over" signal, independent of the registration flags.
    /// </summary>
    public DateTime? LastGameDate { get; init; }

    /// <summary>
    /// When set, a later-year sibling event (same customer, same name prefix
    /// with the year stripped) is currently accepting registration. Treat the
    /// current job as superseded — hide registration CTAs and surface a
    /// callout pointing to this newer event.
    /// Heuristic: regex extracts the first 4-digit year from JobName; the
    /// remainder (whitespace-collapsed, case-insensitive) is the series key.
    /// </summary>
    public SupersedingEventInfoDto? SupersededByLaterEvent { get; init; }

    // --- Authenticated user context (null when anonymous or JWT job mismatch) ---

    // Registrant context (Player / Family / Staff). Null when user is not a registrant in this job.
    public Guid? MyAssignedTeamId { get; init; }
    public decimal? MyRegistrationOwedTotal { get; init; }
    public bool? MyHasPurchasedPlayerRegsaver { get; init; }
    /// <summary>
    /// Authorize.net ARB subscription id for this registration, if one exists.
    /// Gates the Family/Player "Update CC Info" dropdown item — non-null = ARB user
    /// whose stored card can fail and needs self-service update.
    /// </summary>
    public string? MyAdnSubscriptionId { get; init; }

    // ClubRep context. Null when user is not a ClubRep in this job.
    public int? MyClubRepTeamCount { get; init; }
    public decimal? MyClubRepTotalOwed { get; init; }
    public bool? MyClubRepHasTeamWithoutRegsaver { get; init; }

    // Display name of the regId owner (Player / ClubRep / Staff / etc). Used for header initials.
    public string? MyFirstName { get; init; }
    public string? MyLastName { get; init; }
}

/// <summary>
/// Identifies a later-year sibling event currently accepting registration.
/// Surfaced on JobPulseDto.SupersededByLaterEvent so the public landing page
/// can redirect intent from a stale event to the live one.
/// </summary>
public record SupersedingEventInfoDto
{
    public required string JobPath { get; init; }
    public required string JobName { get; init; }
}

/// <summary>
/// Per-user, per-job context that overlays onto JobPulseDto when the request
/// is authenticated. Repo-internal shape; the controller maps these onto the
/// JobPulseDto.My* fields.
/// </summary>
public record JobPulseUserContext
{
    // Registrant (Player / Family-acting-as-player / Staff)
    public Guid? AssignedTeamId { get; init; }
    public decimal? RegistrationOwedTotal { get; init; }
    public bool? HasPurchasedPlayerRegsaver { get; init; }
    public string? AdnSubscriptionId { get; init; }

    // ClubRep
    public int? ClubRepTeamCount { get; init; }
    public decimal? ClubRepTotalOwed { get; init; }
    public bool? ClubRepHasTeamWithoutRegsaver { get; init; }

    // Display name of the regId owner
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
