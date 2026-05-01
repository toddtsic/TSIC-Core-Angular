using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class VerticalInsurePayoutsRepository : IVerticalInsurePayoutsRepository
{
    private readonly SqlDbContext _context;

    public VerticalInsurePayoutsRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<HashSet<string>> GetExistingPolicyNumbersAsync(
        IEnumerable<string> policyNumbers,
        CancellationToken cancellationToken = default)
    {
        var input = policyNumbers.ToList();
        if (input.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hits = await _context.VerticalInsurePayouts
            .AsNoTracking()
            .Where(p => p.PolicyNumber != null && input.Contains(p.PolicyNumber))
            .Select(p => p.PolicyNumber!)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(hits, StringComparer.OrdinalIgnoreCase);
    }

    public async Task AddRangeAsync(
        IEnumerable<VerticalInsurePayouts> payouts,
        CancellationToken cancellationToken = default)
    {
        await _context.VerticalInsurePayouts.AddRangeAsync(payouts, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
