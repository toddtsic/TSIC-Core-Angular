using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class AdnReconciliationRepository : IAdnReconciliationRepository
{
    private const string TsicMasterCustomerName = "TeamSportsInfo.com";

    /// <summary>
    /// Custodial reconciliation: for the month, split ADN transactions into reg (non-<c>_M</c>) and
    /// merch (<c>_M</c>) stacks and flag each as matched when a row exists on its accounting table's
    /// <c>adnTransactionID</c> — <c>Jobs.Registration_Accounting</c> for reg, <c>stores.StoreCartBatchAccounting</c>
    /// for merch. Result set 1 = per-stack summary; result set 2 = the unmatched transactions.
    /// Month key mirrors the legacy <c>SettlementDateTime</c> substring format used by the import.
    /// </summary>
    private const string ReconciliationSql = """
        SET NOCOUNT ON;
        DECLARE @firstDay date = DATEFROMPARTS(@settlementYear, @settlementMonth, 1);
        DECLARE @mkey varchar(20) = '%' + LEFT(DATENAME(month, @firstDay), 3) + '-' + CONVERT(varchar, @settlementYear) + '%';

        IF OBJECT_ID('tempdb..#month') IS NOT NULL DROP TABLE #month;
        IF OBJECT_ID('tempdb..#ra') IS NOT NULL DROP TABLE #ra;
        IF OBJECT_ID('tempdb..#sba') IS NOT NULL DROP TABLE #sba;
        IF OBJECT_ID('tempdb..#reg') IS NOT NULL DROP TABLE #reg;
        IF OBJECT_ID('tempdb..#merch') IS NOT NULL DROP TABLE #merch;

        -- One scan of adn.Txs for the month; the join key is bounded to varchar(50) up front.
        SELECT CONVERT(varchar(50), t.[Transaction ID]) AS tid, t.[Invoice Number] AS inv, t.[Transaction Status] AS status,
               CONVERT(money, t.[Settlement Amount]) AS rawAmt,
               CASE WHEN t.[Transaction Status] = 'Credited'
                    THEN -CONVERT(money, t.[Settlement Amount])
                    ELSE CONVERT(money, t.[Settlement Amount]) END AS signedAmt,
               CASE WHEN CHARINDEX('_M', t.[Invoice Number]) > 0 THEN 1 ELSE 0 END AS isMerch,
               -- Settlement Date Time is nvarchar ('30-Jun-2026 06:13:59 PM EDT'); parse so MAX is
               -- chronological (12-hour AM/PM breaks a lexical MAX at the noon boundary).
               TRY_CONVERT(datetime, REPLACE(t.[Settlement Date Time], ' EDT', '')) AS settledAt
        INTO #month
        FROM adn.Txs t
        WHERE t.[Settlement Date Time] LIKE @mkey
          AND t.[Transaction Status] IN ('Settled Successfully', 'Credited');

        -- Accounting keys are nvarchar(MAX); DISTINCT/join on the LOB column directly is pathologically slow
        -- (100s+), so convert to a bounded type first, then the hash join is trivial.
        SELECT DISTINCT CONVERT(varchar(50), adnTransactionID) AS tid
        INTO #ra FROM Jobs.Registration_Accounting WHERE adnTransactionID IS NOT NULL;

        SELECT DISTINCT CONVERT(varchar(50), adnTransactionID) AS tid
        INTO #sba FROM stores.StoreCartBatchAccounting WHERE adnTransactionID IS NOT NULL;

        SELECT m.tid, m.inv, m.status, m.rawAmt, m.signedAmt,
               CASE WHEN EXISTS (SELECT 1 FROM #ra r WHERE r.tid = m.tid) THEN 1 ELSE 0 END AS matched
        INTO #reg
        FROM #month m WHERE m.isMerch = 0;

        SELECT m.tid, m.inv, m.status, m.rawAmt, m.signedAmt,
               CASE WHEN EXISTS (SELECT 1 FROM #sba s WHERE s.tid = m.tid) THEN 1 ELSE 0 END AS matched
        INTO #merch
        FROM #month m WHERE m.isMerch = 1;

        SELECT stack, transactionCount, matchedCount, unmatchedCount, unmatchedTotal,
               paidCount, paidTotal, creditCount, creditTotal
        FROM (
            SELECT 'Reg' AS stack, 1 AS ord,
                COUNT(*) AS transactionCount,
                ISNULL(SUM(matched), 0) AS matchedCount,
                ISNULL(SUM(CASE WHEN matched = 0 THEN 1 ELSE 0 END), 0) AS unmatchedCount,
                ISNULL(SUM(CASE WHEN matched = 0 THEN signedAmt ELSE 0 END), 0) AS unmatchedTotal,
                ISNULL(SUM(CASE WHEN status = 'Settled Successfully' THEN 1 ELSE 0 END), 0) AS paidCount,
                ISNULL(SUM(CASE WHEN status = 'Settled Successfully' THEN rawAmt ELSE 0 END), 0) AS paidTotal,
                ISNULL(SUM(CASE WHEN status = 'Credited' THEN 1 ELSE 0 END), 0) AS creditCount,
                ISNULL(SUM(CASE WHEN status = 'Credited' THEN rawAmt ELSE 0 END), 0) AS creditTotal
            FROM #reg
            UNION ALL
            SELECT 'Merch', 2,
                COUNT(*),
                ISNULL(SUM(matched), 0),
                ISNULL(SUM(CASE WHEN matched = 0 THEN 1 ELSE 0 END), 0),
                ISNULL(SUM(CASE WHEN matched = 0 THEN signedAmt ELSE 0 END), 0),
                ISNULL(SUM(CASE WHEN status = 'Settled Successfully' THEN 1 ELSE 0 END), 0),
                ISNULL(SUM(CASE WHEN status = 'Settled Successfully' THEN rawAmt ELSE 0 END), 0),
                ISNULL(SUM(CASE WHEN status = 'Credited' THEN 1 ELSE 0 END), 0),
                ISNULL(SUM(CASE WHEN status = 'Credited' THEN rawAmt ELSE 0 END), 0)
            FROM #merch
        ) s
        ORDER BY ord;

        SELECT stack, tid, inv, amt, status
        FROM (
            SELECT 'Reg' AS stack, 1 AS ord, tid, inv, signedAmt AS amt, status FROM #reg WHERE matched = 0
            UNION ALL
            SELECT 'Merch', 2, tid, inv, signedAmt, status FROM #merch WHERE matched = 0
        ) u
        ORDER BY ord, amt;

        SELECT MAX(settledAt) AS latestSettlement FROM #month;

        DROP TABLE #reg;
        DROP TABLE #merch;
        DROP TABLE #month;
        DROP TABLE #ra;
        DROP TABLE #sba;
        """;

    /// <summary>
    /// Postgres equivalent of <see cref="ReconciliationSql"/>: same three result sets, same
    /// columns and types. The month key arrives as @mkey (built in C#, as DATENAME(month) does).
    /// Mirrors the T-SQL exactly, including its quirks: only ' EDT' is stripped before parsing, so
    /// an EST settlement parses to NULL just as TRY_CONVERT leaves it; CHARINDEX on the CI
    /// collation is matched case-insensitively. Regexes run under "C" — the case-insensitive
    /// column collation does not support them.
    /// </summary>
    private const string ReconciliationPgSql = """
        DROP TABLE IF EXISTS pg_temp.rc_month, pg_temp.rc_ra, pg_temp.rc_sba, pg_temp.rc_reg, pg_temp.rc_merch;

        CREATE TEMP TABLE rc_month AS
        SELECT t."Transaction ID"::varchar(50) AS tid, t."Invoice Number" AS inv, t."Transaction Status" AS status,
               t."Settlement Amount"::numeric(19,4) AS rawamt,
               CASE WHEN t."Transaction Status" = 'Credited'
                    THEN -(t."Settlement Amount"::numeric(19,4))
                    ELSE t."Settlement Amount"::numeric(19,4) END AS signedamt,
               CASE WHEN strpos(lower(t."Invoice Number"), '_m') > 0 THEN 1 ELSE 0 END AS ismerch,
               CASE WHEN replace(t."Settlement Date Time", ' EDT', '') COLLATE "C"
                         ~ '^\d{1,2}-[A-Za-z]{3}-\d{4} \d{1,2}:\d{2}:\d{2} [AP]M$'
                    THEN to_timestamp(replace(t."Settlement Date Time", ' EDT', ''), 'DD-Mon-YYYY HH12:MI:SS AM')::timestamp
                    END AS settledat
        FROM adn."Txs" t
        WHERE t."Settlement Date Time" LIKE @mkey
          AND t."Transaction Status" IN ('Settled Successfully', 'Credited');

        CREATE TEMP TABLE rc_ra AS
        SELECT DISTINCT "adnTransactionID"::varchar(50) AS tid FROM "Jobs"."Registration_Accounting" WHERE "adnTransactionID" IS NOT NULL;

        CREATE TEMP TABLE rc_sba AS
        SELECT DISTINCT "AdnTransactionId"::varchar(50) AS tid FROM stores."StoreCartBatchAccounting" WHERE "AdnTransactionId" IS NOT NULL;

        CREATE TEMP TABLE rc_reg AS
        SELECT m.tid, m.inv, m.status, m.rawamt, m.signedamt,
               CASE WHEN EXISTS (SELECT 1 FROM rc_ra r WHERE r.tid = m.tid) THEN 1 ELSE 0 END AS matched
        FROM rc_month m WHERE m.ismerch = 0;

        CREATE TEMP TABLE rc_merch AS
        SELECT m.tid, m.inv, m.status, m.rawamt, m.signedamt,
               CASE WHEN EXISTS (SELECT 1 FROM rc_sba s WHERE s.tid = m.tid) THEN 1 ELSE 0 END AS matched
        FROM rc_month m WHERE m.ismerch = 1;

        SELECT stack, transactioncount, matchedcount, unmatchedcount, unmatchedtotal,
               paidcount, paidtotal, creditcount, credittotal
        FROM (
            SELECT 'Reg' AS stack, 1 AS ord,
                COUNT(*)::int AS transactioncount,
                COALESCE(SUM(matched), 0)::int AS matchedcount,
                COALESCE(SUM(CASE WHEN matched = 0 THEN 1 ELSE 0 END), 0)::int AS unmatchedcount,
                COALESCE(SUM(CASE WHEN matched = 0 THEN signedamt ELSE 0::numeric(19,4) END), 0::numeric(19,4)) AS unmatchedtotal,
                COALESCE(SUM(CASE WHEN status = 'Settled Successfully' THEN 1 ELSE 0 END), 0)::int AS paidcount,
                COALESCE(SUM(CASE WHEN status = 'Settled Successfully' THEN rawamt ELSE 0::numeric(19,4) END), 0::numeric(19,4)) AS paidtotal,
                COALESCE(SUM(CASE WHEN status = 'Credited' THEN 1 ELSE 0 END), 0)::int AS creditcount,
                COALESCE(SUM(CASE WHEN status = 'Credited' THEN rawamt ELSE 0::numeric(19,4) END), 0::numeric(19,4)) AS credittotal
            FROM rc_reg
            UNION ALL
            SELECT 'Merch', 2,
                COUNT(*)::int,
                COALESCE(SUM(matched), 0)::int,
                COALESCE(SUM(CASE WHEN matched = 0 THEN 1 ELSE 0 END), 0)::int,
                COALESCE(SUM(CASE WHEN matched = 0 THEN signedamt ELSE 0::numeric(19,4) END), 0::numeric(19,4)),
                COALESCE(SUM(CASE WHEN status = 'Settled Successfully' THEN 1 ELSE 0 END), 0)::int,
                COALESCE(SUM(CASE WHEN status = 'Settled Successfully' THEN rawamt ELSE 0::numeric(19,4) END), 0::numeric(19,4)),
                COALESCE(SUM(CASE WHEN status = 'Credited' THEN 1 ELSE 0 END), 0)::int,
                COALESCE(SUM(CASE WHEN status = 'Credited' THEN rawamt ELSE 0::numeric(19,4) END), 0::numeric(19,4))
            FROM rc_merch
        ) s
        ORDER BY ord;

        SELECT stack, tid, inv, amt, status
        FROM (
            SELECT 'Reg' AS stack, 1 AS ord, tid, inv, signedamt AS amt, status FROM rc_reg WHERE matched = 0
            UNION ALL
            SELECT 'Merch', 2, tid, inv, signedamt, status FROM rc_merch WHERE matched = 0
        ) u
        ORDER BY ord, amt;

        SELECT MAX(settledat) AS latestsettlement FROM rc_month;

        DROP TABLE rc_reg, rc_merch, rc_month, rc_ra, rc_sba;
        """;

    private readonly SqlDbContext _context;

    public AdnReconciliationRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<MonthEndReconciliationResult> GetMonthEndReconciliationAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
    {
        // Dedicated connection — NOT the EF scoped connection. Cancelling a raw command mid-flight
        // can poison the underlying connection; isolating it here keeps that fallout off EF's pooled
        // connection (which would otherwise surface as "A severe error occurred" on later requests).
        var connectionString = _context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("No connection string configured for the reconciliation query.");

        System.Data.Common.DbConnection connection;
        System.Data.Common.DbCommand cmd;
        if (_context.Database.IsNpgsql())
        {
            connection = new Npgsql.NpgsqlConnection(connectionString);
            cmd = connection.CreateCommand();
            cmd.CommandText = ReconciliationPgSql;
            var monthAbbrev = new DateTime(settlementYear, settlementMonth, 1)
                .ToString("MMM", System.Globalization.CultureInfo.InvariantCulture);
            cmd.Parameters.Add(new Npgsql.NpgsqlParameter("mkey", $"%{monthAbbrev}-{settlementYear}%"));
        }
        else
        {
            // PG-AB: SQL Server arm — delete at cutover.
            connection = new SqlConnection(connectionString);
            cmd = connection.CreateCommand();
            cmd.CommandText = ReconciliationSql;
            cmd.Parameters.Add(new SqlParameter("@settlementMonth", SqlDbType.Int) { Value = settlementMonth });
            cmd.Parameters.Add(new SqlParameter("@settlementYear", SqlDbType.Int) { Value = settlementYear });
        }
        await using var ownedConnection = connection;
        await using var ownedCommand = cmd;
        await connection.OpenAsync(cancellationToken);
        cmd.CommandTimeout = 240;

        var summaries = new Dictionary<string, StackTotals>(StringComparer.Ordinal);
        var unmatched = new Dictionary<string, List<ReconciliationUnmatched>>(StringComparer.Ordinal)
        {
            ["Reg"] = new(),
            ["Merch"] = new(),
        };
        DateTime? latestSettlementAt = null;

        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var stack = reader.GetString(0);
                summaries[stack] = new StackTotals(
                    TransactionCount: reader.GetInt32(1),
                    MatchedCount: reader.GetInt32(2),
                    UnmatchedCount: reader.GetInt32(3),
                    UnmatchedTotal: reader.GetDecimal(4),
                    PaidCount: reader.GetInt32(5),
                    PaidTotal: reader.GetDecimal(6),
                    CreditCount: reader.GetInt32(7),
                    CreditTotal: reader.GetDecimal(8));
            }

            await reader.NextResultAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var stack = reader.GetString(0);
                if (!unmatched.TryGetValue(stack, out var list))
                {
                    continue;
                }

                list.Add(new ReconciliationUnmatched
                {
                    TransactionId = reader.GetString(1),
                    InvoiceNumber = await reader.IsDBNullAsync(2, cancellationToken) ? string.Empty : reader.GetString(2),
                    Amount = reader.GetDecimal(3),
                    Status = await reader.IsDBNullAsync(4, cancellationToken) ? string.Empty : reader.GetString(4),
                });
            }

            await reader.NextResultAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken) && !await reader.IsDBNullAsync(0, cancellationToken))
            {
                latestSettlementAt = reader.GetDateTime(0);
            }
        }

        return new MonthEndReconciliationResult
        {
            SettlementMonth = settlementMonth,
            SettlementYear = settlementYear,
            Reg = BuildStack(summaries.GetValueOrDefault("Reg"), unmatched["Reg"]),
            Merch = BuildStack(summaries.GetValueOrDefault("Merch"), unmatched["Merch"]),
            LatestSettlementAt = latestSettlementAt,
        };
    }

    private static ReconciliationStackSummary BuildStack(
        StackTotals? totals,
        IReadOnlyList<ReconciliationUnmatched> unmatched)
    {
        var t = totals ?? default;
        return new ReconciliationStackSummary
        {
            TransactionCount = t.TransactionCount,
            MatchedCount = t.MatchedCount,
            UnmatchedCount = t.UnmatchedCount,
            UnmatchedTotal = t.UnmatchedTotal,
            PaidCount = t.PaidCount,
            PaidTotal = t.PaidTotal,
            CreditCount = t.CreditCount,
            CreditTotal = t.CreditTotal,
            Unmatched = unmatched,
        };
    }

    private readonly record struct StackTotals(
        int TransactionCount,
        int MatchedCount,
        int UnmatchedCount,
        decimal UnmatchedTotal,
        int PaidCount,
        decimal PaidTotal,
        int CreditCount,
        decimal CreditTotal);

    public async Task<AdnCredentialsViewModel?> GetTsicMasterAdnCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        var creds = await _context.Customers
            .AsNoTracking()
            .Where(c => c.CustomerName == TsicMasterCustomerName)
            .Select(c => new AdnCredentialsViewModel
            {
                AdnLoginId = c.AdnLoginId ?? string.Empty,
                AdnTransactionKey = c.AdnTransactionKey ?? string.Empty,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (creds == null
            || string.IsNullOrWhiteSpace(creds.AdnLoginId)
            || string.IsNullOrWhiteSpace(creds.AdnTransactionKey))
        {
            return null;
        }

        return creds;
    }

    public async Task<int> DeleteTxsForMonthKeyAsync(
        string monthKey,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.Txs
            .Where(t => t.SettlementDateTime != null && t.SettlementDateTime.Contains(monthKey))
            .ToListAsync(cancellationToken);

        if (existing.Count == 0) return 0;

        _context.Txs.RemoveRange(existing);
        await _context.SaveChangesAsync(cancellationToken);
        return existing.Count;
    }

    public async Task<HashSet<string>> GetExistingTransactionIdsAsync(
        IEnumerable<string> transactionIds,
        CancellationToken cancellationToken = default)
    {
        var input = transactionIds.ToList();
        if (input.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        var hits = await _context.Txs
            .AsNoTracking()
            .Where(t => input.Contains(t.TransactionId))
            .Select(t => t.TransactionId)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(hits, StringComparer.Ordinal);
    }

    public async Task AddRangeAsync(
        IEnumerable<Txs> txs,
        CancellationToken cancellationToken = default)
    {
        await _context.Txs.AddRangeAsync(txs, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
