namespace TSIC.Contracts.Dtos.Ladt;

public record TeamDetailDto
{
    // Identity
    public required Guid TeamId { get; init; }
    public Guid? DivId { get; init; }
    public required Guid AgegroupId { get; init; }
    public required Guid LeagueId { get; init; }
    public required Guid JobId { get; init; }

    // Basic Info
    public string? TeamName { get; init; }
    public bool? Active { get; init; }
    public required int DivRank { get; init; }
    public string? DivisionRequested { get; init; }
    public string? LastLeagueRecord { get; init; }
    public string? Color { get; init; }

    // Roster Limits
    public required int MaxCount { get; init; }
    public bool? BAllowSelfRostering { get; init; }
    public required bool BHideRoster { get; init; }

    // Fees
    public decimal? FeeBase { get; init; }
    public decimal? PerRegistrantFee { get; init; }
    public decimal? PerRegistrantDeposit { get; init; }
    public decimal? DiscountFee { get; init; }
    public DateTime? DiscountFeeStart { get; init; }
    public DateTime? DiscountFeeEnd { get; init; }
    public decimal? LateFee { get; init; }
    public DateTime? LateFeeStart { get; init; }
    public DateTime? LateFeeEnd { get; init; }

    // Dates
    public DateTime? Startdate { get; init; }
    public DateTime? Enddate { get; init; }
    public DateTime? Effectiveasofdate { get; init; }
    public DateTime? Expireondate { get; init; }

    // Eligibility
    public DateOnly? DobMin { get; init; }
    public DateOnly? DobMax { get; init; }
    public int? GradYearMin { get; init; }
    public int? GradYearMax { get; init; }
    public short? SchoolGradeMin { get; init; }
    public short? SchoolGradeMax { get; init; }
    public string? Gender { get; init; }
    public string? Season { get; init; }
    public string? Year { get; init; }

    // Schedule Preferences
    public string? Dow { get; init; }
    public string? Dow2 { get; init; }
    public Guid? FieldId1 { get; init; }
    public Guid? FieldId2 { get; init; }
    public Guid? FieldId3 { get; init; }

    // Advanced
    public string? LevelOfPlay { get; init; }
    public string? Requests { get; init; }
    public string? KeywordPairs { get; init; }
    public string? TeamComments { get; init; }

    // Player count (from Registrations join)
    public int PlayerCount { get; init; }
}

public record CreateTeamRequest
{
    public required Guid DivId { get; init; }
    public required string TeamName { get; init; }
    public bool? Active { get; init; }
    public string? DivisionRequested { get; init; }
    public string? Color { get; init; }
    public int MaxCount { get; init; }
    public bool? BAllowSelfRostering { get; init; }
    public bool BHideRoster { get; init; }
    public decimal? FeeBase { get; init; }
    public decimal? PerRegistrantFee { get; init; }
    public decimal? PerRegistrantDeposit { get; init; }
    public decimal? DiscountFee { get; init; }
    public DateTime? DiscountFeeStart { get; init; }
    public DateTime? DiscountFeeEnd { get; init; }
    public decimal? LateFee { get; init; }
    public DateTime? LateFeeStart { get; init; }
    public DateTime? LateFeeEnd { get; init; }
    public DateTime? Startdate { get; init; }
    public DateTime? Enddate { get; init; }
    public DateTime? Effectiveasofdate { get; init; }
    public DateTime? Expireondate { get; init; }
    public DateOnly? DobMin { get; init; }
    public DateOnly? DobMax { get; init; }
    public int? GradYearMin { get; init; }
    public int? GradYearMax { get; init; }
    public short? SchoolGradeMin { get; init; }
    public short? SchoolGradeMax { get; init; }
    public string? Gender { get; init; }
    public string? Season { get; init; }
    public string? Year { get; init; }
    public string? Dow { get; init; }
    public string? Dow2 { get; init; }
    public Guid? FieldId1 { get; init; }
    public Guid? FieldId2 { get; init; }
    public Guid? FieldId3 { get; init; }
    public string? LevelOfPlay { get; init; }
    public string? Requests { get; init; }
    public string? KeywordPairs { get; init; }
    public string? TeamComments { get; init; }
}

public record DeleteTeamResultDto
{
    public required bool WasDeactivated { get; init; }
    public required string Message { get; init; }
}

public record UpdateTeamRequest
{
    public string? TeamName { get; init; }
    public bool? Active { get; init; }
    public string? DivisionRequested { get; init; }
    public string? LastLeagueRecord { get; init; }
    public string? Color { get; init; }
    public int? MaxCount { get; init; }
    public bool? BAllowSelfRostering { get; init; }
    public bool? BHideRoster { get; init; }
    public decimal? FeeBase { get; init; }
    public decimal? PerRegistrantFee { get; init; }
    public decimal? PerRegistrantDeposit { get; init; }
    public decimal? DiscountFee { get; init; }
    public DateTime? DiscountFeeStart { get; init; }
    public DateTime? DiscountFeeEnd { get; init; }
    public decimal? LateFee { get; init; }
    public DateTime? LateFeeStart { get; init; }
    public DateTime? LateFeeEnd { get; init; }
    public DateTime? Startdate { get; init; }
    public DateTime? Enddate { get; init; }
    public DateTime? Effectiveasofdate { get; init; }
    public DateTime? Expireondate { get; init; }
    public DateOnly? DobMin { get; init; }
    public DateOnly? DobMax { get; init; }
    public int? GradYearMin { get; init; }
    public int? GradYearMax { get; init; }
    public short? SchoolGradeMin { get; init; }
    public short? SchoolGradeMax { get; init; }
    public string? Gender { get; init; }
    public string? Season { get; init; }
    public string? Year { get; init; }
    public string? Dow { get; init; }
    public string? Dow2 { get; init; }
    public Guid? FieldId1 { get; init; }
    public Guid? FieldId2 { get; init; }
    public Guid? FieldId3 { get; init; }
    public string? LevelOfPlay { get; init; }
    public string? Requests { get; init; }
    public string? KeywordPairs { get; init; }
    public string? TeamComments { get; init; }
}
