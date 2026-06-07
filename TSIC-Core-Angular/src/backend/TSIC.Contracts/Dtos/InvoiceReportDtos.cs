namespace TSIC.Contracts.Dtos;

/// <summary>
/// One flat, raw payment line for the monthly client-invoice report — the EF replacement for the
/// <c>adn.rpt_invoice</c> stored proc (legacy Crystal "invoices2015" / "invoices2015SummariesOnly").
///
/// This is the denormalized superset the proc's two <c>UNION ALL</c> branches (player + team)
/// produce: every line carries its job-level fee rates AND that job's monthly-stats counts, so the
/// whole report is one flat query — the Crystal master-detail/subreport structure collapses into
/// "group these lines by venue in C#, draw the itemized table + the accounting summary." Money is
/// kept RAW here (settlement amount/date as text, exactly as the ADN import stores it); the service
/// layer parses text→decimal, negates credits, derives year/month from the text date, computes the
/// CC processing fee, and aggregates the per-venue summary. Nothing here coerces the ADN text date
/// to a datetime (no schema change), and nothing reads the <c>adn.vTxs</c> view — the base
/// <c>adn.Txs</c> table is queried directly.
/// </summary>
public record InvoiceLineRawDto
{
    // ── Venue / grouping ────────────────────────────────────────────────
    public required Guid JobId { get; init; }
    public string? CustomerName { get; init; }
    public string? JobName { get; init; }
    public int JobTypeId { get; init; }

    // ── Job-level fee rates + monthly stats (denormalized onto every line) ──
    public decimal? PerPlayerCharge { get; init; }
    public decimal? PerTeamCharge { get; init; }
    public decimal? ProcessingFeePercent { get; init; }
    public int? CountActivePlayersToDate { get; init; }
    public int? CountActivePlayersToDateLastMonth { get; init; }
    public int? CountNewPlayersThisMonth { get; init; }
    public int? CountActiveTeamsToDate { get; init; }
    public int? CountActiveTeamsToDateLastMonth { get; init; }
    public int? CountNewTeamsThisMonth { get; init; }

    // ── Line identity ───────────────────────────────────────────────────
    /// <summary>True for the team-registration branch, false for the player branch.</summary>
    public bool IsTeam { get; init; }
    public string? PaymentMethodName { get; init; }
    public Guid PaymentMethodId { get; init; }

    // ── Raw ADN settlement (text — parsed/negated in the service) ───────
    /// <summary>Raw <c>adn.Txs.[Settlement Date Time]</c>, e.g. "12-Sep-2023 06:17:37 PM EDT". Null when unsettled.</summary>
    public string? SettlementDateTimeText { get; init; }
    /// <summary>Raw <c>adn.Txs.[Settlement Amount]</c> as text (always positive); the service negates when status = "Credited".</summary>
    public string? SettlementAmountText { get; init; }
    public string? TransactionStatus { get; init; }
    public string? TxnInvoiceNumber { get; init; }

    // ── Accounting (Registration_Accounting) fallback ───────────────────
    public decimal? Payamt { get; init; }
    public string? CheckNo { get; init; }
    /// <summary>Player branch's unsettled-date fallback (proc: coalesce(settlement, ata.modified)).</summary>
    public DateTime AcctModified { get; init; }
    /// <summary>Team branch's unsettled-date fallback (proc: coalesce(settlement, ja.createdate)).</summary>
    public DateTime? AcctCreatedate { get; init; }
    /// <summary>Registration_Accounting.AId — the team branch's RegID.</summary>
    public int AcctAId { get; init; }

    // ── Player identity ─────────────────────────────────────────────────
    public string? UserFirstName { get; init; }
    public string? UserLastName { get; init; }
    public string? UserName { get; init; }
    public DateTime? RegistrationTs { get; init; }

    // ── Team identity ───────────────────────────────────────────────────
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    /// <summary>The team's club (Teams.Customer) name — first segment of the team "member" label.</summary>
    public string? ClubCustomerName { get; init; }
}
