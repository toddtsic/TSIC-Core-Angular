namespace TSIC.Contracts.Dtos.CustomerJobRevenue;

public record JobRevenueDataDto
{
    public required List<JobRevenueRecordDto> RevenueRecords { get; init; }
    public required List<JobMonthlyCountDto> MonthlyCounts { get; init; }
    public required List<JobAdminFeeDto> AdminFees { get; init; }
    public required List<JobPaymentRecordDto> CreditCardRecords { get; init; }
    public required List<JobPaymentRecordDto> CheckRecords { get; init; }
    public required List<JobPaymentRecordDto> EcheckRecords { get; init; }
    public required List<string> AvailableJobs { get; init; }
}

public record JobRevenueRecordDto
{
    public required string JobName { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required string PayMethod { get; init; }
    public required decimal PayAmount { get; init; }
}

public record JobMonthlyCountDto
{
    public required int Aid { get; init; }
    public required string JobName { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required int CountActivePlayersToDate { get; init; }
    public required int CountActivePlayersToDateLastMonth { get; init; }
    public required int CountNewPlayersThisMonth { get; init; }
    public required int CountActiveTeamsToDate { get; init; }
    public required int CountActiveTeamsToDateLastMonth { get; init; }
    public required int CountNewTeamsThisMonth { get; init; }
}

public record JobAdminFeeDto
{
    public required string JobName { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required string ChargeType { get; init; }
    public required decimal ChargeAmount { get; init; }
    public required string Comment { get; init; }
}

public record JobPaymentRecordDto
{
    public required string JobName { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required string Registrant { get; init; }
    public required string PaymentMethod { get; init; }
    public required DateTime PaymentDate { get; init; }
    public required decimal PaymentAmount { get; init; }
}

public record UpdateMonthlyCountRequest
{
    public required int Aid { get; init; }
    public required int CountActivePlayersToDate { get; init; }
    public required int CountActivePlayersToDateLastMonth { get; init; }
    public required int CountNewPlayersThisMonth { get; init; }
    public required int CountActiveTeamsToDate { get; init; }
    public required int CountActiveTeamsToDateLastMonth { get; init; }
    public required int CountNewTeamsThisMonth { get; init; }
}
