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
