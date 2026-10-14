namespace TSIC.Contracts.Dtos;

/// <summary>
/// Everything the registration month-end close reads for one settlement month, as plain sets:
/// the gateway's transactions, the accounting records on them, every job and the month's
/// admin charges. Names are ranked by the DATABASE, whose collation the close sorts under.
/// </summary>
public record MonthEndRegistrationDataDto
{
    public required IReadOnlyList<MonthEndTransactionDto> Transactions { get; init; }
    public required IReadOnlyList<MonthEndAccountingRecordDto> AccountingRecords { get; init; }
    public required IReadOnlyList<MonthEndJobDto> Jobs { get; init; }
    public required IReadOnlyList<MonthEndAdminChargeDto> AdminCharges { get; init; }
}

/// <summary>
/// Everything the merchandise month-end close reads for one settlement month: the gateway's
/// merchandise transactions, the store payments recorded against them, and every job.
/// </summary>
public record MonthEndMerchandiseDataDto
{
    public required IReadOnlyList<MonthEndTransactionDto> Transactions { get; init; }
    public required IReadOnlyList<MonthEndStorePaymentDto> Payments { get; init; }
    public required IReadOnlyList<MonthEndJobDto> Jobs { get; init; }
}

/// <summary>One store payment record carrying a gateway transaction id.</summary>
public record MonthEndStorePaymentDto
{
    public required string TransactionId { get; init; }
    public required int AccountingId { get; init; }

    /// <summary>
    /// False when the record's cart, store, payment method, job or customer cannot be found:
    /// such a record still counts toward how often its transaction repeats, and nothing else.
    /// </summary>
    public required bool Resolved { get; init; }
    public required string? PaymentMethod { get; init; }
    public required Guid? JobId { get; init; }

    /// <summary>The product fees of every line in the payment's cart batch; null when it has no lines.</summary>
    public required decimal? BatchProductFees { get; init; }
}

/// <summary>One gateway transaction as the gateway reports it: every value is its text.</summary>
public record MonthEndTransactionDto
{
    public required string TransactionId { get; init; }
    public required string? TransactionStatus { get; init; }
    public required string? SettlementDateTime { get; init; }
    public required string? SettlementAmount { get; init; }
    public required string? InvoiceNumber { get; init; }
    public required string? TransactionType { get; init; }
}

/// <summary>One accounting record carrying a gateway transaction id.</summary>
public record MonthEndAccountingRecordDto
{
    public required string TransactionId { get; init; }
    public required int AccountingId { get; init; }
    public required string? PaymentMethod { get; init; }

    /// <summary>The job of the record's registration; null when it has no registration.</summary>
    public required Guid? RegistrationJobId { get; init; }
}

public record MonthEndJobDto
{
    public required Guid JobId { get; init; }
    public required int JobAi { get; init; }
    public required Guid CustomerId { get; init; }
    public required string? CustomerName { get; init; }
    public required IReadOnlyList<string> CustomerGroupNames { get; init; }
    public required bool SettlesThroughTsic { get; init; }
    public required string? JobName { get; init; }
    public required string? JobNameQbp { get; init; }

    /// <summary>False when the job's type has no row in the job types table.</summary>
    public required bool HasJobType { get; init; }
    public required int JobTypeId { get; init; }
    public required string? JobTypeDesc { get; init; }
    public required decimal? PerPlayerCharge { get; init; }
    public required decimal? PerTeamCharge { get; init; }
    public required decimal? ProcessingFeePercent { get; init; }
    public required decimal? EcheckProcessingFeePercent { get; init; }

    /// <summary>TSIC's share of the job's store sales, as a fraction; null when the job has none of its own.</summary>
    public required decimal? StoreTsicRate { get; init; }

    /// <summary>Rank of the customer's name in the database's order; equal names share a rank.</summary>
    public required int CustomerNameOrder { get; init; }

    /// <summary>Rank of the job's own name.</summary>
    public required int JobNameOrder { get; init; }

    /// <summary>Rank of the name the job is reported under: its QuickBooks name, else its own.</summary>
    public required int ReportedNameOrder { get; init; }
}

public record MonthEndAdminChargeDto
{
    public required int Id { get; init; }
    public required Guid JobId { get; init; }
    public required int ChargeTypeId { get; init; }
    public required decimal ChargeAmount { get; init; }
    public required string? Comment { get; init; }
    public required DateTime CreateDate { get; init; }
}

/// <summary>
/// One job's billing counts for one month (a row of adn.Monthly_Job_Stats). <c>Aid</c> is null
/// on a row that has been worked out but not yet stored.
/// </summary>
public record MonthEndJobStatDto
{
    public required int? Aid { get; init; }
    public required Guid JobId { get; init; }
    public required int? ActivePlayersToDateLastMonth { get; init; }
    public required int? ActivePlayersToDate { get; init; }
    public required int? NewPlayersThisMonth { get; init; }
    public required int? ActiveTeamsToDateLastMonth { get; init; }
    public required int? ActiveTeamsToDate { get; init; }
    public required int? NewTeamsThisMonth { get; init; }
}
