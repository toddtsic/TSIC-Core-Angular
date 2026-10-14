namespace TSIC.Contracts.Dtos;

/// <summary>
/// Active players on one team name of one regional job, in total and as of the same day of the
/// job's own year — the EF replacement for the data behind the
/// <c>utility.AmericanSelect_CountsAtYear_ByTeam</c> proc.
/// </summary>
public record RegionalTeamPlayerCountDto
{
    public Guid JobId { get; init; }
    public string? JobName { get; init; }
    public string? Year { get; init; }
    public string? TeamName { get; init; }
    public int Players { get; init; }
    public int PlayersToDate { get; init; }
}

/// <summary>
/// The two result sets of the RegSaver payouts export — the EF replacement for the
/// <c>reporting.RegsaverRegistrants_Charlie</c> proc.
/// </summary>
public record RegsaverPayoutsDto
{
    public required List<RegsaverPayoutExcelRowDto> Payouts { get; init; }
    public required List<RegsaverPayoutPeriodDto> PeriodPayouts { get; init; }
}

/// <summary>One insurance payout matched to the registration that bought the policy.</summary>
public record RegsaverPayoutExcelRowDto
{
    public double? Year { get; init; }
    public double? Month { get; init; }
    public string? JobName { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? PolicyId { get; init; }
    public DateTime? PurchaseDate { get; init; }
    public decimal FeeTotal { get; init; }
    public decimal? Payout { get; init; }
}

/// <summary>One matched payout with the year and month it was paid in.</summary>
public record RegsaverPayoutPeriodDto
{
    public double? Year { get; init; }
    public double? Month { get; init; }
    public decimal? Payout { get; init; }
}

/// <summary>
/// Club teams entered in one tournament in one year — the EF replacement for the grouped data
/// behind the <c>reporting.TournyYearOverYearClubTeams</c> proc.
/// </summary>
public record ClubTeamYearCountDto
{
    public string? JobNameRoot { get; init; }
    public int Year { get; init; }
    public string? Club { get; init; }
    public int Teams { get; init; }
}

/// <summary>
/// The job settings listed by the clone QA export — the EF replacement for the
/// <c>utility.JobCloneQA</c> proc.
/// </summary>
public record JobCloneQaDto
{
    public string? CustomerName { get; init; }
    public string? AdnLoginId { get; init; }
    public string? JobName { get; init; }
    public string? JobNameQbp { get; init; }
    public string? JobDescription { get; init; }
    public string? JobPath { get; init; }
    public required List<string?> ParallaxSlide1Text1 { get; init; }
    public required List<string?> ParallaxSlide1Text2 { get; init; }
    public bool? RegistrationAllowPlayer { get; init; }
    public bool? RegistrationAllowTeam { get; init; }
    public DateTime ExpiryAdmin { get; init; }
    public DateTime ExpiryUsers { get; init; }
    public string? Season { get; init; }
    public string? Year { get; init; }
    public string? JobTypeName { get; init; }
    public string? SportName { get; init; }
    public bool? AdnArb { get; init; }
    public int? AdnArbBillingOccurences { get; init; }
    public int? AdnArbIntervalLength { get; init; }
    public DateTime? AdnArbStartDate { get; init; }
    public bool AddProcessingFees { get; init; }
    public bool? ApplyProcessingFeesToTeamDeposit { get; init; }
    public bool? TeamsFullPaymentRequired { get; init; }
    public int PaymentMethodsAllowedCode { get; init; }
    public string? PlayerRegRefundPolicy { get; init; }
    public string? BillingTypeName { get; init; }
    public decimal? PerPlayerCharge { get; init; }
    public decimal? PerTeamCharge { get; init; }
    public string? CoreRegformPlayer { get; init; }
    public bool? OfferPlayerRegsaverInsurance { get; init; }
    public bool UseWaitlists { get; init; }
    public bool AllowRosterViewPlayer { get; init; }
    public bool? RestrictPlayerTeamsToAgerange { get; init; }
    public int? PlayerRegMultiPlayerDiscountPercent { get; init; }
    public string? RegformNameClubRep { get; init; }
    public string? RegformNameCoach { get; init; }
    public string? RegformNameTeam { get; init; }
    public bool AllowRosterViewAdult { get; init; }
    public bool? ClubRepAllowEdit { get; init; }
    public bool? ClubRepAllowDelete { get; init; }
    public bool? ClubRepAllowAdd { get; init; }
    public bool? ScheduleAllowPublicAccess { get; init; }
    public bool AllowMobileLogin { get; init; }
    public bool SuspendPublic { get; init; }
    public bool? AllowMobileRegn { get; init; }
    public bool? EnableTsicTeams { get; init; }
}

/// <summary>
/// Player counts across the regional jobs that share the invoking job's name prefix — the EF
/// replacement for the data behind the <c>utility.AmericanSelect_CountsAtYear_ByTeamII</c> proc.
/// </summary>
public record RegionalPlayerCountsDto
{
    /// <summary>The job name up to and including its first colon; null when the job is unknown.</summary>
    public string? NamePrefix { get; init; }

    /// <summary>The distinct job years, in order: one pair of report columns each.</summary>
    public required List<string> Years { get; init; }

    public required List<RegionalTeamPlayerCountDto> Counts { get; init; }
}
