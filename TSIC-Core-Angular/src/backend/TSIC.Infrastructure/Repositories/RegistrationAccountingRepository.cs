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

    public async Task<Dictionary<Guid, PaymentSummary>> GetPaymentSummariesAsync(
        IReadOnlyCollection<Guid> registrationIds, CancellationToken cancellationToken = default)
    {
        if (registrationIds.Count == 0) return new();

        return await _context.RegistrationAccounting
            .AsNoTracking()
            .Where(ra => ra.RegistrationId.HasValue
                && registrationIds.Contains(ra.RegistrationId.Value)
                && ra.Active == true)
            .Join(_context.AccountingPaymentMethods,
                ra => ra.PaymentMethodId,
                apm => apm.PaymentMethodId,
                (ra, apm) => new { ra.RegistrationId, ra.Payamt, apm.PaymentMethod })
            .GroupBy(x => x.RegistrationId!.Value)
            .Select(g => new
            {
                RegistrationId = g.Key,
                TotalPayments = g.Sum(x => x.Payamt ?? 0),
                NonCcPayments = g.Where(x => x.PaymentMethod != "Credit Card Payment")
                                 .Sum(x => x.Payamt ?? 0)
            })
            .ToDictionaryAsync(
                x => x.RegistrationId,
                x => new PaymentSummary
                {
                    TotalPayments = x.TotalPayments,
                    NonCcPayments = x.NonCcPayments
                },
                cancellationToken);
    }
}
