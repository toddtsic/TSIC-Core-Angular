namespace TSIC.Contracts.Dtos;

/// <summary>
/// The month-end ledger, rendered human-readable — the on-screen equivalent of the export workbook's
/// tabs. Sourced from the SAME sproc result sets that build the .xlsx / .iif, so what's reviewed on
/// screen and what lands in the file cannot diverge. IIF (double-entry) tabs are flattened: each
/// QuickBooks TRNS+SPL…ENDTRNS group collapses to one <see cref="LedgerEntry"/> (amount = the one
/// side, splits attached). QA tabs pass through as plain tables.
/// </summary>
public record MonthEndLedger
{
    public required int SettlementMonth { get; init; }
    public required int SettlementYear { get; init; }
    public required IReadOnlyList<LedgerTab> Tabs { get; init; }
}

public record LedgerTab
{
    public required string Name { get; init; }

    /// <summary>"Registration" or "Merch".</summary>
    public required string Stack { get; init; }

    /// <summary>"transactions" (flattened double-entry) or "table" (plain QA passthrough).</summary>
    public required string Kind { get; init; }

    /// <summary>Populated for "table" tabs (QA sheets); empty for "transactions" tabs.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>Populated for "table" tabs (QA sheets); empty for "transactions" tabs.</summary>
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }

    /// <summary>Populated for "transactions" tabs (IIF sheets); empty for "table" tabs.</summary>
    public required IReadOnlyList<LedgerEntry> Entries { get; init; }
}

/// <summary>
/// One flattened transaction — a collapsed TRNS+SPL…ENDTRNS group. <see cref="Party"/> is the client
/// (QuickBooks customer:job) we owe, <see cref="Amount"/> is the one-sided transaction amount (never
/// the double-entry sum, which is zero), and <see cref="Splits"/> is the allocation breakdown.
/// </summary>
public record LedgerEntry
{
    public required string Date { get; init; }
    public required string Type { get; init; }
    public required string Party { get; init; }
    public required string Account { get; init; }
    public required decimal Amount { get; init; }
    public required string DocNum { get; init; }
    public required string Memo { get; init; }
    public required IReadOnlyList<LedgerSplit> Splits { get; init; }
}

public record LedgerSplit
{
    public required string Account { get; init; }
    public required string Party { get; init; }
    public required decimal Amount { get; init; }
    public required string Memo { get; init; }
}
