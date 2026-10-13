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
