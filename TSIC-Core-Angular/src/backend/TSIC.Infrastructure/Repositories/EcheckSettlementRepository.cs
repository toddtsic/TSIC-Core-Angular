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

    public async Task<List<Settlement>> GetPendingDueAsync(DateTime now, CancellationToken ct = default)
    {
        return await _context.Settlement
            .Include(s => s.RegistrationAccounting)
                .ThenInclude(ra => ra.Registration)
            .Where(s => s.Status == "Pending" && s.NextCheckAt <= now)
            .ToListAsync(ct);
    }

    public async Task<SweepLog> StartSweepLogAsync(string triggeredBy, CancellationToken ct = default)
    {
        var log = new SweepLog
        {
            StartedAt = DateTime.UtcNow,
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
        log.CompletedAt = DateTime.UtcNow;
        log.RecordsChecked = recordsChecked;
        log.RecordsSettled = recordsSettled;
        log.RecordsReturned = recordsReturned;
        log.RecordsErrored = recordsErrored;
        log.ErrorMessage = errorMessage;
        await _context.SaveChangesAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
