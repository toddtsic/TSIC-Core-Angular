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
    public decimal? Deposit { get; init; }
    public decimal? BalanceDue { get; init; }
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
    public decimal? Deposit { get; init; }
    public decimal? BalanceDue { get; init; }
    public List<FeeModifierDto>? Modifiers { get; init; }
}
