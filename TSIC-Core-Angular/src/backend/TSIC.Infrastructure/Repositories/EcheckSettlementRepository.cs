using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class EcheckSettlementRepository : IEcheckSettlementRepository
{
    private readonly SqlDbContext _context;

    public EcheckSettlementRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<Settlement>> GetByAdnTransactionIdsAsync(
        IEnumerable<string> adnTransactionIds, CancellationToken ct = default)
    {
        var ids = adnTransactionIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await _context.Settlement
            .Include(s => s.RegistrationAccounting)
                .ThenInclude(ra => ra.Registration)
            .Where(s => ids.Contains(s.AdnTransactionId))
            .ToListAsync(ct);
    }

    public async Task<SweepLog> StartSweepLogAsync(string triggeredBy, CancellationToken ct = default)
    {
        var log = new SweepLog
        {
            StartedAt = DateTime.Now,
            TriggeredBy = triggeredBy
        };
        _context.SweepLog.Add(log);
        await _context.SaveChangesAsync(ct);
        return log;
    }

    public async Task CompleteSweepLogAsync(
        SweepLog log,
        int recordsChecked,
        int recordsSettled,
        int recordsReturned,
        int recordsErrored,
        string? errorMessage,
        CancellationToken ct = default)
    {
        log.CompletedAt = DateTime.Now;
        log.RecordsChecked = recordsChecked;
        log.RecordsSettled = recordsSettled;
        log.RecordsReturned = recordsReturned;
        log.RecordsErrored = recordsErrored;
        log.ErrorMessage = errorMessage;
        await _context.SaveChangesAsync(ct);
    }

    public void Add(Settlement settlement)
    {
        _context.Settlement.Add(settlement);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<Settlement>> GetStalePendingAsync(DateTime olderThan, CancellationToken ct = default)
    {
        // Tracked (the watchdog mutates Status/LastCheckedAt); Job loaded for digest reporting.
        return await _context.Settlement
            .Include(s => s.RegistrationAccounting)
                .ThenInclude(ra => ra.Registration)
                    .ThenInclude(r => r!.Job)
            .Where(s => s.Status == "Pending" && s.SubmittedAt < olderThan)
            .OrderBy(s => s.SubmittedAt)
            .ToListAsync(ct);
    }

    public async Task<List<UntrackedEcheckRaDto>> GetUntrackedEcheckAccountingAsync(
        Guid echeckPaymentMethodId, CancellationToken ct = default)
    {
        // Positive eCheck payments with a gateway tx id but no Settlement row — booked money the
        // sweep cannot watch. The atomic mint makes this unreachable going forward; this query is
        // the standing alarm for anything that slips through regardless.
        return await _context.RegistrationAccounting
            .AsNoTracking()
            .Where(ra => ra.PaymentMethodId == echeckPaymentMethodId
                && ra.Payamt > 0
                && ra.AdnTransactionId != null && ra.AdnTransactionId != ""
                && !_context.Settlement.Any(s => s.RegistrationAccountingId == ra.AId))
            .Select(ra => new UntrackedEcheckRaDto
            {
                AId = ra.AId,
                AdnTransactionId = ra.AdnTransactionId,
                Payamt = ra.Payamt ?? 0m,
                Createdate = ra.Createdate
            })
            .ToListAsync(ct);
    }
}
