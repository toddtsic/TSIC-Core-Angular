namespace TSIC.Contracts.Dtos;

/// <summary>
/// Custodial reconciliation for a settlement month. We hold client money in our pooled merchant
/// account and owe every dollar back to a client (less our fee); ADN is the source of truth for what
/// actually settled. This answers: does every transaction ADN gave us have a matching row in our
/// accounting system? Non-merch must match <c>Jobs.Registration_Accounting</c>; merch (<c>_M</c>)
/// must match <c>stores.StoreCartBatchAccounting</c> — both keyed on <c>adnTransactionID</c>. An
/// unmatched transaction is client money we can't attribute to anyone. The paid/credit totals are the
/// reference figures staff compare against QuickBooks after importing the IIF — they must match.
/// </summary>
public record MonthEndReconciliationResult
{
    public required int SettlementMonth { get; init; }
    public required int SettlementYear { get; init; }
    public required ReconciliationStackSummary Reg { get; init; }
    public required ReconciliationStackSummary Merch { get; init; }

    /// <summary>
    /// Greatest settlement date/time present in the loaded data for the month — a proxy for data
    /// currency ("we have settlements through here"), NOT when the pull ran (Txs records no import
    /// timestamp). Null when the month holds no rows.
    /// </summary>
    public DateTime? LatestSettlementAt { get; init; }
}

public record ReconciliationStackSummary
{
    /// <summary>All ADN transactions for the month in this stack.</summary>
    public required int TransactionCount { get; init; }

    /// <summary>Transactions with a matching accounting row.</summary>
    public required int MatchedCount { get; init; }

    /// <summary>Transactions with no matching accounting row — client money we can't attribute.</summary>
    public required int UnmatchedCount { get; init; }

    /// <summary>Signed dollar total of the unmatched transactions.</summary>
    public required decimal UnmatchedTotal { get; init; }

    /// <summary>Count of charges (Settled Successfully).</summary>
    public required int PaidCount { get; init; }

    /// <summary>CC dollars paid — sum of Settled Successfully. Compared against QuickBooks post-import.</summary>
    public required decimal PaidTotal { get; init; }

    /// <summary>Count of refunds (Credited).</summary>
    public required int CreditCount { get; init; }

    /// <summary>CC dollars credited — sum of Credited (positive magnitude). Compared against QuickBooks post-import.</summary>
    public required decimal CreditTotal { get; init; }

    public required IReadOnlyList<ReconciliationUnmatched> Unmatched { get; init; }
}

public record ReconciliationUnmatched
{
    public required string TransactionId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required decimal Amount { get; init; }
    public required string Status { get; init; }
}

/// <summary>
/// Metadata for a month's persisted close artifacts (bundle.zip + ledger.json on disk). Returned by the
/// eager "prepare" build and stored as meta.json alongside the artifacts; its presence marks the month
/// as built. The sprocs run once to produce these — every later Step-2/Step-3 read is served from disk.
/// </summary>
public record MonthEndArtifactsInfo
{
    public required int SettlementMonth { get; init; }
    public required int SettlementYear { get; init; }
    public required DateTime BuiltAt { get; init; }
    public required int LedgerTabCount { get; init; }
    public required int RegSourceTrnsCount { get; init; }
    public required int RegConsolidatedTrnsCount { get; init; }
    public required int MerchSourceTrnsCount { get; init; }
    public required int MerchConsolidatedTrnsCount { get; init; }
}
