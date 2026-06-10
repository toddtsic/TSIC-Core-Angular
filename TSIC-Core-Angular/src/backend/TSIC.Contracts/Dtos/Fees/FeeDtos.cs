namespace TSIC.Contracts.Dtos.Fees;

/// <summary>
/// Fee configuration for a single role at a specific scope (job/agegroup/team).
/// </summary>
public record JobFeeDto
{
    public required Guid JobFeeId { get; init; }
    public required Guid JobId { get; init; }
    public required string RoleId { get; init; }
    public string? RoleName { get; init; }
    public Guid? AgegroupId { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? LeagueId { get; init; }
    public decimal? Deposit { get; init; }
    public decimal? BalanceDue { get; init; }
    /// <summary>Per-scope full-payment phase override: true = on, null = inherit from a
    /// less-specific scope / the job baseline. Drives deposit vs. balance-due phase.</summary>
    public bool? BFullPaymentRequired { get; init; }
    public List<FeeModifierDto>? Modifiers { get; init; }
}

/// <summary>
/// Time-windowed fee modifier (discount, late fee, etc.).
/// </summary>
public record FeeModifierDto
{
    public Guid? FeeModifierId { get; init; }
    public required string ModifierType { get; init; }
    public required decimal Amount { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}

/// <summary>
/// All fees for an agegroup: agegroup-level defaults + team-level overrides per role.
/// </summary>
public record AgegroupFeeSummaryDto
{
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public required List<JobFeeDto> AgegroupFees { get; init; }
    public required List<JobFeeDto> TeamOverrides { get; init; }
}

/// <summary>
/// Create or update a fee row for a role at a scope.
/// </summary>
public record SaveJobFeeRequest
{
    public required string RoleId { get; init; }
    public Guid? AgegroupId { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? LeagueId { get; init; }
    public decimal? Deposit { get; init; }
    public decimal? BalanceDue { get; init; }

    /// <summary>
    /// Per-scope full-payment phase: true = on, null = inherit/off. A CHANGE to this value
    /// is always retroactive — the save forces an existing-registration reprice regardless
    /// of <see cref="RepriceExisting"/> (phase flips are never future-only).
    /// </summary>
    public bool? BFullPaymentRequired { get; init; }

    /// <summary>
    /// "Update all prior" — when true, existing registrations in this scope are repriced to
    /// the new amounts. Default false = future-only (config saved; stamped fees untouched).
    /// Implied true whenever the phase flag changes.
    /// </summary>
    public bool RepriceExisting { get; init; }

    public List<FeeModifierDto>? Modifiers { get; init; }
}

/// <summary>
/// Result of a fee save: the saved row plus how many existing registrations were repriced
/// (0 when the change was future-only, or when nothing needed updating).
/// </summary>
public record SaveJobFeeResponse
{
    public required JobFeeDto Fee { get; init; }
    public required int RegistrationsRepriced { get; init; }
}

/// <summary>
/// The "blast area" for a pending fee/phase change: how many existing registrations a save
/// at this scope would reprice, shown to the admin BEFORE they confirm. Scope- and
/// role-accurate — Player rows count active player registrations in scope; ClubRep rows count
/// eligible (non-WAITLIST/DROPPED) teams in scope. A count of 0 means no prompt is needed
/// (nothing to reprice; the save is future-only by definition).
/// </summary>
public record AffectedRegistrationCountDto
{
    public required int Count { get; init; }
}

/// <summary>
/// Apply a payment-phase change to EVERY age group in a league at once (the LADT
/// "apply to all age groups" action). Only the phase is propagated — deposits, balances,
/// and modifiers on each age group are left as-is. A change is always retroactive.
/// </summary>
public record ApplyLeaguePhaseRequest
{
    public required string RoleId { get; init; }

    /// <summary>Phase to stamp on every in-scope age group: true = full payment now, null = inherit/off.</summary>
    public bool? BFullPaymentRequired { get; init; }
}

/// <summary>
/// Result of an "apply to all age groups" phase change: how many age groups were stamped
/// (those with an effective deposit, excluding WAITLIST/DROPPED buckets) and how many existing
/// registrations were converted by the canonical reprice.
/// </summary>
public record ApplyLeaguePhaseResponse
{
    public required int AgegroupsApplied { get; init; }
    public required int RegistrationsRepriced { get; init; }
}
