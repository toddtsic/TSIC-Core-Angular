using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class NuveiFundingRepository : INuveiFundingRepository
{
    private readonly SqlDbContext _context;

    public NuveiFundingRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<HashSet<string>> GetExistingFingerprintsAsync(
        IEnumerable<string> fingerprints,
        CancellationToken cancellationToken = default)
    {
        var inputs = fingerprints.ToList();
        if (inputs.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        // RefNumber narrows the candidate set cheaply; we then fingerprint client-side
        // using the same composite key used at insert time.
        var refNumbers = inputs
            .Select(f => f.Split('|', 2)[0])
            .Distinct()
            .ToList();

        var rows = await _context.NuveiFunding
            .AsNoTracking()
            .Where(n => refNumbers.Contains(n.RefNumber))
            .Select(n => new { n.FundingEvent, n.FundingType, n.RefNumber, n.FundingAmount, n.FundingDate })
            .ToListAsync(cancellationToken);

        var existing = rows
            .Select(n => string.Join('|',
                n.RefNumber,
                n.FundingEvent,
                n.FundingType ?? string.Empty,
                n.FundingAmount,
                n.FundingDate.ToString("o")));

        var hits = new HashSet<string>(existing, StringComparer.Ordinal);
        hits.IntersectWith(inputs);
        return hits;
    }

    public async Task AddRangeAsync(
        IEnumerable<NuveiFunding> records,
        CancellationToken cancellationToken = default)
    {
        await _context.NuveiFunding.AddRangeAsync(records, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
