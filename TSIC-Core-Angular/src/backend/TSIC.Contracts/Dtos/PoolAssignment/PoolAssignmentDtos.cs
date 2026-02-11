namespace TSIC.Contracts.Dtos.PoolAssignment;

/// <summary>
/// Division option for the Pool Assignment dropdown, grouped by agegroup.
/// </summary>
public record PoolDivisionOptionDto
{
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public required int TeamCount { get; init; }
    public required int MaxTeams { get; init; }
    public required bool IsDroppedTeams { get; init; }
    public required decimal? TeamFee { get; init; }
    public required decimal? RosterFee { get; init; }
}

/// <summary>
/// Team row in a division's team list.
/// </summary>
public record PoolTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public string? ClubName { get; init; }
    public string? ClubRepName { get; init; }
    public string? LevelOfPlay { get; init; }
    public DateTime? RegistrationTs { get; init; }
    public string? TeamComments { get; init; }
    public required bool Active { get; init; }
    public required int DivRank { get; init; }
    public required int RosterCount { get; init; }
    public required int MaxCount { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required bool IsScheduled { get; init; }
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public Guid? DivId { get; init; }
    public string? DivName { get; init; }
}

/// <summary>
/// Transfer request. Supports one-directional moves and symmetrical swaps.
/// For symmetrical swap: SourceTeamIds go to TargetDivId and TargetTeamIds go to SourceDivId.
/// </summary>
public record PoolTransferRequest
{
    public required List<Guid> SourceTeamIds { get; init; }
    public required List<Guid> TargetTeamIds { get; init; }
    public required Guid SourceDivId { get; init; }
    public required Guid TargetDivId { get; init; }
    public required bool IsSymmetricalSwap { get; init; }
}

/// <summary>
/// Fee impact preview per team.
/// </summary>
public record PoolTransferPreviewDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string Direction { get; init; }
    public required bool AgegroupChanges { get; init; }
    public required decimal CurrentFeeBase { get; init; }
    public required decimal CurrentFeeTotal { get; init; }
    public required decimal NewFeeBase { get; init; }
    public required decimal NewFeeTotal { get; init; }
    public required decimal FeeDelta { get; init; }
    public required bool IsScheduled { get; init; }
    public required bool RequiresSymmetricalSwap { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Club rep financial impact summary (shown before transfer).
/// </summary>
public record PoolClubRepImpactDto
{
    public required string ClubName { get; init; }
    public required Guid ClubRepRegistrationId { get; init; }
    public required decimal CurrentTotal { get; init; }
    public required decimal NewTotal { get; init; }
    public required decimal Delta { get; init; }
}

/// <summary>
/// Preview request.
/// </summary>
public record PoolTransferPreviewRequest
{
    public required List<Guid> SourceTeamIds { get; init; }
    public required List<Guid> TargetTeamIds { get; init; }
    public required Guid SourceDivId { get; init; }
    public required Guid TargetDivId { get; init; }
    public required bool IsSymmetricalSwap { get; init; }
}

/// <summary>
/// Combined preview response.
/// </summary>
public record PoolTransferPreviewResponse
{
    public required List<PoolTransferPreviewDto> Teams { get; init; }
    public required List<PoolClubRepImpactDto> ClubRepImpacts { get; init; }
    public required bool HasScheduledTeams { get; init; }
    public required bool RequiresSymmetricalSwap { get; init; }
}

/// <summary>
/// Transfer result.
/// </summary>
public record PoolTransferResultDto
{
    public required int TeamsMoved { get; init; }
    public required int FeesRecalculated { get; init; }
    public required int TeamsDeactivated { get; init; }
    public required int ScheduleRecordsUpdated { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Active status toggle for a team.
/// </summary>
public record UpdateTeamActiveRequest
{
    public required bool Active { get; init; }
}

/// <summary>
/// DivRank inline update for a team.
/// </summary>
public record UpdateTeamDivRankRequest
{
    public required int DivRank { get; init; }
}
