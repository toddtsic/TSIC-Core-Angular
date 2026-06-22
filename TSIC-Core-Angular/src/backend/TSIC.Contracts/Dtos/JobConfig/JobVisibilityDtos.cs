namespace TSIC.Contracts.Dtos.JobConfig;

/// <summary>
/// The public landing-hero CTA visibility flags for a job — the SuperUser
/// "Quick Links" editor's read shape. Each property maps to the exact Jobs.Jobs
/// column the landing hero grounds the corresponding CTA card on (see
/// JobRepository pulse projection). This is a focused view over flags that also
/// live in their logical Configure Job tabs; it stores no separate state.
/// </summary>
public record JobVisibilityDto
{
    /// <summary>BRegistrationAllowPlayer — "Register Player" card.</summary>
    public required bool AllowPlayerRegistration { get; init; }

    /// <summary>BRegistrationAllowTeam — "Register Team" card.</summary>
    public required bool AllowTeamRegistration { get; init; }

    /// <summary>BScheduleAllowPublicAccess — "View Schedule" card.</summary>
    public required bool PublishSchedule { get; init; }

    /// <summary>BAllowRosterViewPlayer — "Rosters" card.</summary>
    public required bool ShowPublicRosters { get; init; }

    /// <summary>BEnableStore — "Store" card (the hero additionally requires the store to have active items).</summary>
    public required bool EnableStore { get; init; }

    /// <summary>BOfferPlayerRegsaverInsurance — "Insurance Update" card.</summary>
    public required bool OfferPlayerInsurance { get; init; }
}

/// <summary>
/// Partial update for the Quick Links editor. Only non-null flags are applied, so
/// the UI can save a single toggle at a time (save-on-change) without clobbering
/// the others.
/// </summary>
public record UpdateJobVisibilityRequest
{
    public bool? AllowPlayerRegistration { get; init; }
    public bool? AllowTeamRegistration { get; init; }
    public bool? PublishSchedule { get; init; }
    public bool? ShowPublicRosters { get; init; }
    public bool? EnableStore { get; init; }
    public bool? OfferPlayerInsurance { get; init; }
}
