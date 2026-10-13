namespace TSIC.Contracts.Dtos;

/// <summary>
/// One job admin charge, any job — the EF replacement for the data behind the
/// <c>reporting.JobAdminFeesAll</c> proc.
/// </summary>
public record JobAdminFeeAllRowDto
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required DateTime CreateDate { get; init; }
    public string? JobName { get; init; }
    public string? ChargeTypeName { get; init; }
    public required decimal ChargeAmount { get; init; }
    public string? Comment { get; init; }
}

/// <summary>
/// One bulletin with its job's name — the EF replacement for the data behind the
/// <c>utility.ExpiringBulletins</c> proc.
/// </summary>
public record BulletinExpiryRowDto
{
    public string? JobName { get; init; }
    public string? Title { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public required bool Active { get; init; }
}

/// <summary>
/// An active registration bulletin that has ended on a job still open for that registration —
/// the EF replacement for the data behind the <c>utility.PlayerRegistrationBulletinsQA</c> and
/// <c>utility.TeamRegistrationBulletinsQA</c> procs.
/// </summary>
public record RegistrationBulletinQaRowDto
{
    public string? JobName { get; init; }
    public bool? RegistrationAllowed { get; init; }
    public required DateTime ExpiryUsers { get; init; }
    public DateTime? UslaxNumberValidThroughDate { get; init; }
    public required bool Active { get; init; }
    public string? Title { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}

/// <summary>
/// A job and the date of its first active accounting record — the EF replacement for the data
/// behind the <c>reporting.NewTsicJobsWithTxs</c> proc.
/// </summary>
public record JobFirstTransactionDto
{
    public required Guid JobId { get; init; }
    public string? JobName { get; init; }
    public DateTime? FirstTransactionDate { get; init; }
}

/// <summary>
/// One club rep's contact details for one club and sport, with the number of active club rep
/// registrations behind it — the EF replacement for the data behind the
/// <c>reporting.ClubRepContacts-All</c> proc.
/// </summary>
public record ClubRepContactAllRowDto
{
    public string? ClubName { get; init; }
    public string? SportName { get; init; }
    public string? UserName { get; init; }
    public required int JobCount { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}

/// <summary>
/// One player registration carrying a RegSaver policy — the EF replacement for the "Player Data"
/// result of the <c>reporting.RegsaverPurchases_ALL_Rawdata</c> proc.
/// </summary>
public record RegsaverPlayerPolicyRowDto
{
    public required DateTime RegistrationTs { get; init; }
    public string? RegsaverPolicyId { get; init; }
    public DateTime? PolicyCreateDate { get; init; }
    public string? JobName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Assignment { get; init; }
    public string? FamilyEmail { get; init; }
    public string? FamilyUserName { get; init; }
    public required decimal FeeTotal { get; init; }
}

/// <summary>
/// One team carrying a RegSaver policy, with the club rep who bought it and the club rep who
/// holds the team now — the EF replacement for the "Team Data" result of the
/// <c>reporting.RegsaverPurchases_ALL_Rawdata</c> proc.
/// </summary>
public record RegsaverTeamPolicyRowDto
{
    public string? JobName { get; init; }
    public string? PurchaserClubName { get; init; }
    public string? PurchaserFirstName { get; init; }
    public string? PurchaserLastName { get; init; }
    public string? PurchaserEmail { get; init; }
    public string? CurrentClubName { get; init; }
    public string? CurrentFirstName { get; init; }
    public string? CurrentLastName { get; init; }
    public string? CurrentEmail { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? TeamName { get; init; }
    public bool? Active { get; init; }
    public string? ViPolicyId { get; init; }
    public required DateTime TeamCreateDate { get; init; }
    public DateTime? PolicyCreateDate { get; init; }
    public decimal? FeeTotal { get; init; }
    public decimal? OwedTotal { get; init; }
    public decimal? PaidTotal { get; init; }
}

/// <summary>
/// One month of processor funding, processor batches and gateway settlements — the EF
/// replacement for the data loads of the <c>adn.ReconcileNuvei</c> proc.
/// </summary>
public record NuveiReconcileDto
{
    public required List<NuveiFundingRowDto> Funding { get; init; }
    public required List<NuveiBatchRowDto> Batches { get; init; }
    public required List<GatewaySettlementRowDto> Settlements { get; init; }
}

/// <summary>A processor funding event (deposit, chargeback…) other than the monthly fee.</summary>
public record NuveiFundingRowDto
{
    public required string FundingEvent { get; init; }
    public required DateTime FundingDate { get; init; }
    public required decimal FundingAmount { get; init; }
}

/// <summary>A processor batch: the day it closed and its net amount.</summary>
public record NuveiBatchRowDto
{
    public required DateTime BatchCloseDate { get; init; }
    public required decimal BatchNet { get; init; }
}

/// <summary>
/// A settled or credited gateway transaction whose invoice number names an existing job, as the
/// gateway reports it: the settlement date-time and amount are the gateway's text.
/// </summary>
public record GatewaySettlementRowDto
{
    public string? TransactionStatus { get; init; }
    public string? SettlementDateTime { get; init; }
    public string? SettlementAmount { get; init; }
}

/// <summary>
/// One job's configuration and headcounts — the EF replacement for the data behind the
/// <c>reporting.JobKeyAttributes-ALL</c> and <c>reporting.TournamentKeyAttributes-ALL</c> procs.
/// </summary>
public record JobKeyAttributesRowDto
{
    public required Guid JobId { get; init; }
    public string? CustomerName { get; init; }
    public string? AdnLoginId { get; init; }
    public string? Year { get; init; }
    public string? Season { get; init; }
    public string? JobName { get; init; }
    public string? JobNameQbp { get; init; }
    public string? JobTypeName { get; init; }
    public string? BillingTypeName { get; init; }
    public required DateTime ExpiryAdmin { get; init; }
    public required DateTime ExpiryUsers { get; init; }
    public DateTime? EventStartDate { get; init; }
    public DateTime? EventEndDate { get; init; }
    public DateTime? UslaxNumberValidThroughDate { get; init; }
    public bool? RegistrationAllowPlayer { get; init; }
    public bool? RegistrationAllowTeam { get; init; }
    public bool? OfferPlayerRegsaverInsurance { get; init; }
    public bool? OfferTeamRegsaverInsurance { get; init; }
    public bool? AdnArb { get; init; }
    public int? AdnArbBillingOccurences { get; init; }
    public int? AdnArbIntervalLength { get; init; }
    public decimal? AdnArbMinimumTotalCharge { get; init; }
    public string? CoreRegformPlayer { get; init; }
    public bool? EnableTsicTeams { get; init; }
    public required bool AllowMobileLogin { get; init; }
    public required bool SuspendPublic { get; init; }
    public decimal? PerPlayerCharge { get; init; }
    public decimal? PerTeamCharge { get; init; }
    public required int PaymentMethodsAllowedCode { get; init; }
    public required bool AddProcessingFees { get; init; }
    public bool? ApplyProcessingFeesToTeamDeposit { get; init; }
    public bool? TeamsFullPaymentRequired { get; init; }
    public bool? ClubRepAllowAdd { get; init; }
    public bool? ClubRepAllowEdit { get; init; }
    public bool? ClubRepAllowDelete { get; init; }

    /// <summary>The job's league's hide-contacts setting; null when the job has no league.</summary>
    public bool? HideContacts { get; init; }

    public required int ClubRepCount { get; init; }
    public required int OverpaidClubRepCount { get; init; }
    public required int UnderpaidClubRepCount { get; init; }
    public required int PlayerCount { get; init; }
    public required int OverpaidPlayerCount { get; init; }
    public required int UnderpaidPlayerCount { get; init; }

    /// <summary>Active teams in age groups that are neither waitlist nor dropped.</summary>
    public required int TeamCount { get; init; }

    /// <summary>Active teams in age groups whose name starts with WAITLIST.</summary>
    public required int WaitlistTeamCount { get; init; }
}

/// <summary>
/// One month's settled card totals, admin charges and new-registration counts by job — the EF
/// replacement for the data loads of the <c>adn.GetLastMonthsGrandTotals</c> proc.
/// </summary>
public record MonthlyGrandTotalsDto
{
    /// <summary>
    /// Every job with activity in the month, once per customer group its customer belongs to
    /// (once when it belongs to none), ordered by reported customer name then job name.
    /// </summary>
    public required List<GrandTotalsJobDto> Jobs { get; init; }

    /// <summary>The month's statistics rows.</summary>
    public required List<GrandTotalsMonthStatDto> MonthStats { get; init; }
}

/// <summary>A job under the name its customer is reported by: the customer's group, else the customer.</summary>
public record GrandTotalsJobDto
{
    public required Guid JobId { get; init; }
    public string? ReportedCustomerName { get; init; }
    public string? JobName { get; init; }

    /// <summary>True when the job's customer settles through TSIC's own gateway account.</summary>
    public required bool SettlesThroughTsic { get; init; }

    public decimal? ProcessingFeePercent { get; init; }
    public decimal? PerPlayerCharge { get; init; }
    public decimal? PerTeamCharge { get; init; }

    /// <summary>Active card payments on transactions settled in the month; null when there are none.</summary>
    public decimal? CardPayments { get; init; }

    /// <summary>Active card credits on transactions settled in the month; null when there are none.</summary>
    public decimal? CardCredits { get; init; }

    /// <summary>The job's admin charges for the month; null when there are none.</summary>
    public decimal? AdminCharges { get; init; }
}

/// <summary>A job's new players and teams for the month.</summary>
public record GrandTotalsMonthStatDto
{
    public required Guid JobId { get; init; }
    public int? NewPlayers { get; init; }
    public int? NewTeams { get; init; }
}

/// <summary>
/// Every insurance payout and every policy held by a registration or a team — the EF
/// replacement for the data loads of the <c>reporting.RegsaverRegistrants_ALL</c> proc, which
/// matches the two by policy number.
/// </summary>
public record RegsaverPurchasesDto
{
    /// <summary>Every payout, ordered by year, month and purchase date.</summary>
    public required List<InsurancePayoutRowDto> Payouts { get; init; }

    /// <summary>Registrations holding a policy.</summary>
    public required List<RegsaverPolicyHolderDto> PlayerPolicies { get; init; }

    /// <summary>Teams holding a policy, with their club rep's registration when there is one.</summary>
    public required List<RegsaverPolicyHolderDto> TeamPolicies { get; init; }
}

/// <summary>One row of the insurer's payout file, as loaded.</summary>
public record InsurancePayoutRowDto
{
    public double? Year { get; init; }
    public double? Month { get; init; }
    public string? PolicyNumber { get; init; }
    public string? PurchaseDateString { get; init; }
    public DateTime? PolicyEffectiveDate { get; init; }
    public decimal? NetWrittenPremium { get; init; }
    public decimal? Payout { get; init; }
    public DateTime? PurchaseDate { get; init; }
    public required Guid Id { get; init; }
}

/// <summary>
/// A policy and who holds it. For a player policy the registration is the holder's own; for a
/// team policy it is the team's club rep's, and may be missing.
/// </summary>
public record RegsaverPolicyHolderDto
{
    public required string PolicyId { get; init; }

    /// <summary>The job the registration (player policy) or the team (team policy) belongs to.</summary>
    public string? HolderJobName { get; init; }

    /// <summary>When the registration was made (player policy) or the team created (team policy).</summary>
    public required DateTime HolderCreated { get; init; }

    public required bool HasRegistration { get; init; }
    public decimal? FeeTotal { get; init; }
    public DateTime? RegistrationTs { get; init; }
    public string? RegistrationJobName { get; init; }
    public string? ParentFirstName { get; init; }
    public string? ParentLastName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    /// <summary>True when the registration's customer is one of those the payout share is reported for.</summary>
    public required bool InPayoutCustomerGroup { get; init; }
}
