using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class RegistrationAccountingRepository : IRegistrationAccountingRepository
{
    private readonly SqlDbContext _context;

    public RegistrationAccountingRepository(SqlDbContext context)
    {
        _context = context;
    }

    public void Add(RegistrationAccounting entry)
    {
        _context.RegistrationAccounting.Add(entry);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> AnyDuplicateAsync(Guid jobId, string familyUserId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await _context.RegistrationAccounting
            .Join(_context.Registrations, a => a.RegistrationId, r => r.RegistrationId, (a, r) => new { a, r })
            .AnyAsync(x => x.r.JobId == jobId && x.r.FamilyUserId == familyUserId && x.a.AdnInvoiceNo == idempotencyKey, cancellationToken);
    }

    public async Task<string?> GetLatestAdnTransactionIdAsync(IEnumerable<Guid> registrationIds, CancellationToken cancellationToken = default)
    {
        var regIdSet = registrationIds.ToHashSet();
        return await _context.RegistrationAccounting
            .AsNoTracking()
            .Where(a => a.RegistrationId != null && regIdSet.Contains(a.RegistrationId.Value) && !string.IsNullOrWhiteSpace(a.AdnTransactionId))
            .OrderByDescending(a => a.Createdate)
            .Select(a => a.AdnTransactionId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
