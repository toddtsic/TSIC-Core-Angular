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

        IF OBJECT_ID('tempdb..#reg') IS NOT NULL DROP TABLE #reg;
        IF OBJECT_ID('tempdb..#merch') IS NOT NULL DROP TABLE #merch;

        SELECT t.[Transaction ID] AS tid, t.[Invoice Number] AS inv, t.[Transaction Status] AS status,
               CONVERT(money, t.[Settlement Amount]) AS rawAmt,
               CASE WHEN t.[Transaction Status] = 'Credited'
                    THEN -CONVERT(money, t.[Settlement Amount])
                    ELSE CONVERT(money, t.[Settlement Amount]) END AS signedAmt,
               CASE WHEN ra.adnTransactionID IS NOT NULL THEN 1 ELSE 0 END AS matched
        INTO #reg
        FROM adn.Txs t
        LEFT JOIN (SELECT DISTINCT adnTransactionID FROM Jobs.Registration_Accounting) ra
               ON ra.adnTransactionID = t.[Transaction ID]
        WHERE t.[Settlement Date Time] LIKE @mkey
          AND t.[Transaction Status] IN ('Settled Successfully', 'Credited')
          AND CHARINDEX('_M', t.[Invoice Number]) = 0;

        SELECT t.[Transaction ID] AS tid, t.[Invoice Number] AS inv, t.[Transaction Status] AS status,
               CONVERT(money, t.[Settlement Amount]) AS rawAmt,
               CASE WHEN t.[Transaction Status] = 'Credited'
                    THEN -CONVERT(money, t.[Settlement Amount])
                    ELSE CONVERT(money, t.[Settlement Amount]) END AS signedAmt,
               CASE WHEN sba.adnTransactionID IS NOT NULL THEN 1 ELSE 0 END AS matched
        INTO #merch
        FROM adn.Txs t
        LEFT JOIN (SELECT DISTINCT adnTransactionID FROM stores.StoreCartBatchAccounting) sba
               ON sba.adnTransactionID = t.[Transaction ID]
        WHERE t.[Settlement Date Time] LIKE @mkey
          AND t.[Transaction Status] IN ('Settled Successfully', 'Credited')
          AND CHARINDEX('_M', t.[Invoice Number]) > 0;

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

        DROP TABLE #reg;
        DROP TABLE #merch;
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
        var connection = _context.Database.GetDbConnection();
        var cmd = connection.CreateCommand();
        cmd.CommandText = ReconciliationSql;
        cmd.CommandTimeout = 240;
        cmd.Parameters.Add(new SqlParameter("@settlementMonth", SqlDbType.Int) { Value = settlementMonth });
        cmd.Parameters.Add(new SqlParameter("@settlementYear", SqlDbType.Int) { Value = settlementYear });

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var summaries = new Dictionary<string, StackTotals>(StringComparer.Ordinal);
        var unmatched = new Dictionary<string, List<ReconciliationUnmatched>>(StringComparer.Ordinal)
        {
            ["Reg"] = new(),
            ["Merch"] = new(),
        };

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
                    InvoiceNumber = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Amount = reader.GetDecimal(3),
                    Status = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                });
            }
        }

        return new MonthEndReconciliationResult
        {
            SettlementMonth = settlementMonth,
            SettlementYear = settlementYear,
            Reg = BuildStack(summaries.GetValueOrDefault("Reg"), unmatched["Reg"]),
            Merch = BuildStack(summaries.GetValueOrDefault("Merch"), unmatched["Merch"]),
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
