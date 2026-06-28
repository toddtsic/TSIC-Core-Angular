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

    /// <summary>Coach/staff self-registration is released AND teams exist to request.
    /// Grounds the public "Register Coach/Staff" hero card. BRegistrationAllowStaff is
    /// the director's release gate (opened after teams are in); teams-exist is the
    /// precondition so a coach has a real team to request.</summary>
    public required bool StaffRegistrationOpen { get; init; }

    /// <summary>Referee self-registration is released (BRegistrationAllowReferee).
    /// Grounds the public "Register Referee" hero card.</summary>
    public required bool RefereeRegistrationOpen { get; init; }

    /// <summary>College-recruiter self-registration is released
    /// (BRegistrationAllowRecruiter). Grounds the public "Register College Recruiter" card.</summary>
    public required bool RecruiterRegistrationOpen { get; init; }
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
    /// Director-stated event start (Jobs.EventStartDate), or null. NOT an EventConcluded input
    /// (start-passed ≠ over). Used as a VETO in derivePhase's residual tail: a start date in the
    /// future blocks the participation signal from mislabeling a still-upcoming event 'concluded'
    /// (the "took early signups then paused, no end date" case). Render-only otherwise.
    /// </summary>
    public DateTime? EventStartDate { get; init; }

    /// <summary>
    /// Director-stated event end (Jobs.EventEndDate), or null. An input to EventConcluded:
    /// when no published schedule exists, this is the authoritative "over" signal that the
    /// generous ExpiryUsers window (set ~a year out on purpose) misses. Render-only on the FE
    /// (countdowns) — never compared to now for a gating decision outside EventConcluded.
    /// </summary>
    public DateTime? EventEndDate { get; init; }

    /// <summary>
    /// Server-authoritative "the event is over" bit — the SINGLE source the FE consumes for
    /// the lifecycle gate (no client-clock recompute → no timezone/day-boundary drift). Computed
    /// by TSIC.Domain.JobRules.JobLifecycle.EventConcluded over the fact hierarchy
    /// (published lastGameDate → EventEndDate → ExpiryUsers fallback). When true (or superseded),
    /// the create fields below are already folded to false — the same door the write authority
    /// enforces, so a disabled button and a refused write can never disagree.
    /// </summary>
    public required bool EventConcluded { get; init; }

    /// <summary>
    /// True when the job has at least one ACTIVE non-admin registration (Player / ClubRep /
    /// Staff / Referee / Recruiter / Family / UnassignedAdult — excluding admins and store-
    /// purchase shells). Resolves the ONE residual new-vs-concluded ambiguity: a job with no
    /// date signal at all (no EventEndDate, no published schedule, future ExpiryUsers) is
    /// fact-identical to a brand-new not-yet-open job EXCEPT that a finished event accumulated
    /// real participants and a new one has none. DISPLAY-ONLY — consumed by derivePhase's
    /// quiet tail (registration not open) to label such a job 'concluded' vs 'coming soon';
    /// NEVER a write gate (the authority stays permissive in the residual).
    /// </summary>
    public required bool HasNonAdminActivity { get; init; }

    /// <summary>
    /// The event's calendar year (Jobs.Year), parsed to an int — null when Jobs.Year isn't a
    /// clean number ("isNumeric" guard). The most decisive residual signal in derivePhase: a
    /// prior year (&lt; now.year) means the event is over (overrides even a stale registration
    /// toggle — "last year's job"); a future year vetoes the participation signal. Year
    /// granularity ⇒ DISPLAY-ONLY; writes stay on real dates (EventConcluded), never the year.
    /// </summary>
    public int? EventYear { get; init; }

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
