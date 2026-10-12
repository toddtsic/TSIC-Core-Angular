namespace TSIC.Contracts.Dtos;

/// <summary>
/// One active accounting record of an active player for the "Player Transactions (Excel)" export —
/// the EF replacement for the <c>reporting_migrate.Get_Player_Transactions_ForExcelExport</c> proc.
/// Job-scoped; a player with several records appears once per record.
/// </summary>
public record PlayerTransactionExcelRowDto
{
    public required Guid RegistrationId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Gender { get; init; }
    public DateTime? Dob { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public string? PaymentMethod { get; init; }
    public required int AccountingId { get; init; }
    public decimal? DueAmount { get; init; }
    public decimal? PayAmount { get; init; }
    public string? AdnCc4 { get; init; }
    public string? AdnCcExpDate { get; init; }
    public string? AdnInvoiceNo { get; init; }
    public string? AdnTransactionId { get; init; }
    public string? CheckNo { get; init; }
    public string? Comment { get; init; }
    public DateTime? CreateDate { get; init; }
}
