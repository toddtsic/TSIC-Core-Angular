using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class AdnReconciliationRepository : IAdnReconciliationRepository
{
    private const string TsicMasterCustomerName = "TeamSportsInfo.com";

    private readonly SqlDbContext _context;

    public AdnReconciliationRepository(SqlDbContext context)
    {
        _context = context;
    }

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
