namespace TSIC.Contracts.Dtos;

/// <summary>
/// Lightweight projection of a team for player self-rostering display.
/// Fields intentionally limited; expand as wizard requires (e.g., waitlist substitution, fees breakdown).
/// </summary>
public record AvailableTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required Guid AgegroupId { get; init; }
    public string? AgegroupName { get; init; }
    public Guid? DivisionId { get; init; }
    public string? DivisionName { get; init; }
    public required int MaxRosterSize { get; init; }
    public required int CurrentRosterSize { get; init; }
    public required bool RosterIsFull { get; init; }
    public bool? TeamAllowsSelfRostering { get; init; }
    public bool? AgegroupAllowsSelfRostering { get; init; }
    public decimal? Fee { get; init; }
    public decimal? Deposit { get; init; }
    /// <summary>Fee + active late fees − active discounts (early-bird, discount), evaluated at list time. Excludes codes / insurance / processing — those are opt-ins applied on the Payment step.</summary>
    public decimal? EffectiveFee { get; init; }
    /// <summary>
    /// True when a JobFees row resolves at some cascade level (team/agegroup/job).
    /// False = no fee configured anywhere — the team is NOT registerable; the wizard
    /// shows "Fee not set" and blocks selection instead of fabricating/charging $0.
    /// (A legitimately-free event is FeeConfigured=true with Fee 0.)
    /// </summary>
    public required bool FeeConfigured { get; init; }
    /// <summary>
    /// Per-scope payment phase (canonical <see cref="TSIC.Contracts.Repositories.ResolvedFee.ResolveFullPaymentPhase"/>):
    /// a JobFees.BFullPaymentRequired override (team → agegroup → league) wins, else the
    /// job-level baseline (Jobs.BPlayersFullPaymentRequired). True → this team must be paid in
    /// full (no deposit slice offered); false → a deposit may be taken. Lets the payment screen
    /// pick the right phase PER NEW SELECTION (one that has no stamped registration yet) so a
    /// family cart spanning deposit and full-payment scopes bills each line by its own phase.
    /// </summary>
    public required bool FullPaymentRequired { get; init; }
    public required bool JobUsesWaitlists { get; init; }
    public Guid? WaitlistTeamId { get; set; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public decimal? PerRegistrantFee { get; init; }
    public string? ClubName { get; init; }
}
