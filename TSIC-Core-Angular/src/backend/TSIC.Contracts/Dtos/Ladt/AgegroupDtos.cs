namespace TSIC.Contracts.Dtos.Ladt;

public record AgegroupDetailDto
{
    public required Guid AgegroupId { get; init; }
    public required Guid LeagueId { get; init; }
    public string? AgegroupName { get; init; }
    public string? Season { get; init; }
    public string? Color { get; init; }
    public string? Gender { get; init; }
    public DateOnly? DobMin { get; init; }
    public DateOnly? DobMax { get; init; }
    public int? GradYearMin { get; init; }
    public int? GradYearMax { get; init; }
    public short? SchoolGradeMin { get; init; }
    public short? SchoolGradeMax { get; init; }
    public decimal? TeamFee { get; init; }
    public string? TeamFeeLabel { get; init; }
    public decimal? RosterFee { get; init; }
    public string? RosterFeeLabel { get; init; }
    public decimal? DiscountFee { get; init; }
    public DateTime? DiscountFeeStart { get; init; }
    public DateTime? DiscountFeeEnd { get; init; }
    public decimal? LateFee { get; init; }
    public DateTime? LateFeeStart { get; init; }
    public DateTime? LateFeeEnd { get; init; }
    public required int MaxTeams { get; init; }
    public required int MaxTeamsPerClub { get; init; }
    public bool? BAllowSelfRostering { get; init; }
    public bool? BChampionsByDivision { get; init; }
    public bool? BAllowApiRosterAccess { get; init; }
    public bool? BHideStandings { get; init; }
    public decimal? PlayerFeeOverride { get; init; }
    public required byte SortAge { get; init; }
}

public record CreateAgegroupRequest
{
    public required Guid LeagueId { get; init; }
    public required string AgegroupName { get; init; }
    public string? Color { get; init; }
    public string? Gender { get; init; }
    public DateOnly? DobMin { get; init; }
    public DateOnly? DobMax { get; init; }
    public int? GradYearMin { get; init; }
    public int? GradYearMax { get; init; }
    public short? SchoolGradeMin { get; init; }
    public short? SchoolGradeMax { get; init; }
    public decimal? TeamFee { get; init; }
    public string? TeamFeeLabel { get; init; }
    public decimal? RosterFee { get; init; }
    public string? RosterFeeLabel { get; init; }
    public decimal? DiscountFee { get; init; }
    public DateTime? DiscountFeeStart { get; init; }
    public DateTime? DiscountFeeEnd { get; init; }
    public decimal? LateFee { get; init; }
    public DateTime? LateFeeStart { get; init; }
    public DateTime? LateFeeEnd { get; init; }
    public int MaxTeams { get; init; }
    public int MaxTeamsPerClub { get; init; }
    public bool? BAllowSelfRostering { get; init; }
    public bool? BChampionsByDivision { get; init; }
    public bool? BAllowApiRosterAccess { get; init; }
    public bool? BHideStandings { get; init; }
    public decimal? PlayerFeeOverride { get; init; }
    public byte SortAge { get; init; }
}

public record UpdateAgegroupRequest
{
    public string? AgegroupName { get; init; }
    public string? Color { get; init; }
    public string? Gender { get; init; }
    public DateOnly? DobMin { get; init; }
    public DateOnly? DobMax { get; init; }
    public int? GradYearMin { get; init; }
    public int? GradYearMax { get; init; }
    public short? SchoolGradeMin { get; init; }
    public short? SchoolGradeMax { get; init; }
    public decimal? TeamFee { get; init; }
    public string? TeamFeeLabel { get; init; }
    public decimal? RosterFee { get; init; }
    public string? RosterFeeLabel { get; init; }
    public decimal? DiscountFee { get; init; }
    public DateTime? DiscountFeeStart { get; init; }
    public DateTime? DiscountFeeEnd { get; init; }
    public decimal? LateFee { get; init; }
    public DateTime? LateFeeStart { get; init; }
    public DateTime? LateFeeEnd { get; init; }
    public int MaxTeams { get; init; }
    public int MaxTeamsPerClub { get; init; }
    public bool? BAllowSelfRostering { get; init; }
    public bool? BChampionsByDivision { get; init; }
    public bool? BAllowApiRosterAccess { get; init; }
    public bool? BHideStandings { get; init; }
    public decimal? PlayerFeeOverride { get; init; }
    public byte SortAge { get; init; }
}
