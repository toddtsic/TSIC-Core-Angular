namespace TSIC.Contracts.Dtos.RosterSwapper;

/// <summary>
/// Pool dropdown option (teams grouped by agegroup/division + special Unassigned Adults pool).
/// </summary>
public record SwapperPoolOptionDto
{
    public required Guid PoolId { get; init; }
    public required string PoolName { get; init; }
    public required bool IsUnassignedAdultsPool { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public Guid? AgegroupId { get; init; }
    public Guid? DivId { get; init; }
    public required int RosterCount { get; init; }
    public required int MaxCount { get; init; }
    public required bool Active { get; init; }
}

/// <summary>
/// Player row in roster table.
/// </summary>
public record SwapperPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string RoleName { get; init; }
    public required bool BActive { get; init; }
    public string? School { get; init; }
    public short? Grade { get; init; }
    public int? GradYear { get; init; }
    public string? Position { get; init; }
    public DateTime? Dob { get; init; }
    public string? Gender { get; init; }
    public string? SkillLevel { get; init; }
    public int? YrsExp { get; init; }
    public string? Requests { get; init; }
    public string? PrevCoach { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public DateTime? RegistrationTs { get; init; }
}

/// <summary>
/// Transfer request (supports multi-select).
/// Backend detects transfer type from source/target pool IDs and registration roles.
/// </summary>
public record RosterTransferRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required Guid SourcePoolId { get; init; }
    public required Guid TargetPoolId { get; init; }
}

/// <summary>
/// Fee impact preview per player.
/// </summary>
public record RosterTransferFeePreviewDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string TransferType { get; init; }
    public required decimal CurrentFeeBase { get; init; }
    public required decimal CurrentFeeTotal { get; init; }
    public required decimal NewFeeBase { get; init; }
    public required decimal NewFeeTotal { get; init; }
    public required decimal FeeDelta { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Fee preview request.
/// </summary>
public record RosterTransferPreviewRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required Guid SourcePoolId { get; init; }
    public required Guid TargetPoolId { get; init; }
}

/// <summary>
/// Transfer result — describes what happened (role-dependent).
/// </summary>
public record RosterTransferResultDto
{
    public required int PlayersTransferred { get; init; }
    public required int StaffCreated { get; init; }
    public required int StaffDeleted { get; init; }
    public required int FeesRecalculated { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Active status toggle.
/// </summary>
public record UpdatePlayerActiveRequest
{
    public required bool BActive { get; init; }
}
