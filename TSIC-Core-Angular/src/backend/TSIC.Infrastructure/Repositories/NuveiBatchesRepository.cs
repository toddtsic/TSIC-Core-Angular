using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class NuveiBatchesRepository : INuveiBatchesRepository
{
    private readonly SqlDbContext _context;

    public NuveiBatchesRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<HashSet<string>> GetExistingFingerprintsAsync(
        IEnumerable<string> fingerprints,
        CancellationToken cancellationToken = default)
    {
        var inputs = fingerprints.ToList();
        if (inputs.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        // BatchId narrows the set cheaply; we then materialize and fingerprint client-side
        // to match the same composite key used at insert time.
        var batchIds = inputs
            .Select(f => f.Split('|', 2)[0])
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .Distinct()
            .ToList();

        var rows = await _context.NuveiBatches
            .AsNoTracking()
            .Where(b => batchIds.Contains(b.BatchId))
            .Select(b => new { b.BatchCloseDate, b.BatchId, b.BatchNet, b.SaleAmt, b.ReturnAmt })
            .ToListAsync(cancellationToken);

        var existing = rows
            .Select(b => string.Join('|',
                b.BatchId,
                b.BatchCloseDate.ToString("o"),
                b.BatchNet,
                b.SaleAmt,
                b.ReturnAmt?.ToString() ?? string.Empty));

        var hits = new HashSet<string>(existing, StringComparer.Ordinal);
        hits.IntersectWith(inputs);
        return hits;
    }

    public async Task AddRangeAsync(
        IEnumerable<NuveiBatches> batches,
        CancellationToken cancellationToken = default)
    {
        await _context.NuveiBatches.AddRangeAsync(batches, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
