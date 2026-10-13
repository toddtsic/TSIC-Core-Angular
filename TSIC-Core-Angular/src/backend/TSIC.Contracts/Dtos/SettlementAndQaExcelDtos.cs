namespace TSIC.Contracts.Dtos;

/// <summary>
/// One team of the camp previous-month breakout — the EF replacement for the
/// <c>reporting.CathyCampBreakoutPerJob_PreviousMonth</c> proc: the team's players registered in
/// the month, and its settled card payments in the month, one entry per payment method.
/// </summary>
public record CampBreakoutMonthTeamDto
{
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public Guid TeamId { get; init; }
    public int Registrants { get; init; }
    public required List<CampBreakoutMonthPaymentDto> Payments { get; init; }
}

/// <summary>A team's settled card payments in the month for one payment method.</summary>
public record CampBreakoutMonthPaymentDto
{
    public Guid TeamId { get; init; }
    public int Transactions { get; init; }

    /// <summary>Null when no payment in the group carries an amount.</summary>
    public decimal? Amount { get; init; }
}

/// <summary>
/// One registration-by-payment row of the job transaction rollup — the EF replacement for the
/// raw data behind the <c>reporting.GetJobTransactionRollup</c> proc. A registration with no
/// accounting record still returns one row, its payment fields null.
/// </summary>
public record JobTransactionRollupRowDto
{
    public Guid RegistrationId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Gender { get; init; }
    public DateTime? Dob { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public decimal FeeDiscount { get; init; }
    public decimal PaidTotal { get; init; }
    public decimal OwedTotal { get; init; }
    public string? PaymentMethod { get; init; }
    public int? AccountingId { get; init; }
    public decimal? DueAmount { get; init; }
    public decimal? PayAmount { get; init; }
    public string? AdnCc4 { get; init; }
    public string? AdnCcExpDate { get; init; }
    public string? AdnInvoiceNo { get; init; }
    public string? AdnTransactionId { get; init; }
    public string? CheckNo { get; init; }
    public string? Comment { get; init; }
    public DateTime? CreateDate { get; init; }
    public int? DiscountCodeId { get; init; }

    /// <summary>True when the agegroup name contains "WAITLIST" (any case).</summary>
    public bool Waitlist { get; init; }
}

/// <summary>
/// One referee assignment for the assignment QA export — the EF replacement for the
/// <c>reporting.RefAssignmentQA</c> proc — with the same referee's next game that day, if any.
/// </summary>
public record RefAssignmentQaRowDto
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public DateTime? GameDate { get; init; }
    public int Gid { get; init; }
    public string? FieldName { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public Guid RefRegistrationId { get; init; }
    public DateTime? NextGameSameDay { get; init; }
}
