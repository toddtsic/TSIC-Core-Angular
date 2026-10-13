namespace TSIC.Contracts.Dtos;

/// <summary>
/// The active accounting records of a customer's jobs from January 1 two years back, for the
/// "accounting records" export — the EF replacement for the
/// <c>reporting.Customer_RegistrationAccounting_Records</c> proc. Customer-scoped, except for one
/// hard-wired job that reports a fixed group of customers (the proc's "ALL STEPS" branch).
/// </summary>
public record CustomerAccountingRecordsDto
{
    /// <summary>True for the hard-wired job: the fixed customer group, in its own column layout.</summary>
    public required bool AllStepsCustomers { get; init; }
    /// <summary>The calling job's customer name; null when the job or the name is missing.</summary>
    public string? CustomerName { get; init; }
    public required List<CustomerAccountingRecordExcelRowDto> Rows { get; init; }
}

/// <summary>One accounting record; team and agegroup are set only for a club rep's team payment.</summary>
public record CustomerAccountingRecordExcelRowDto
{
    public string? CustomerName { get; init; }
    public string? JobName { get; init; }
    public DateTime? CreateDate { get; init; }
    public string? PaymentMethod { get; init; }
    public string? AdnTransactionId { get; init; }
    public string? AdnCc4 { get; init; }
    public string? AdnCcExpDate { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Role { get; init; }
    public string? UserName { get; init; }
    public string? FamilyUserName { get; init; }
    public decimal? PayAmount { get; init; }
    public string? CardComment { get; init; }
    public string? Comment { get; init; }
    public string? ClubName { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
}
