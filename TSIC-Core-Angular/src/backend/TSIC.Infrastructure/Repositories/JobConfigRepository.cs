using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Job Configuration Editor data access.
/// </summary>
public class JobConfigRepository : IJobConfigRepository
{
    private readonly SqlDbContext _context;

    public JobConfigRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Read ─────────────────────────────────────────────

    public async Task<Jobs?> GetJobByIdAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.JobId == jobId, ct);
    }

    public async Task<GameClockParams?> GetGameClockParamsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.GameClockParams
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.JobId == jobId, ct);
    }

    public async Task<List<JobAdminCharges>> GetAdminChargesAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobAdminCharges
            .AsNoTracking()
            .Include(c => c.ChargeType)
            .Where(c => c.JobId == jobId)
            .OrderByDescending(c => c.Year).ThenByDescending(c => c.Month)
            .ToListAsync(ct);
    }

    // ── Reference data ───────────────────────────────────

    public async Task<List<JobTypeRefDto>> GetJobTypesAsync(CancellationToken ct = default)
    {
        return await _context.JobTypes
            .AsNoTracking()
            .OrderBy(jt => jt.JobTypeName)
            .Select(jt => new JobTypeRefDto
            {
                JobTypeId = jt.JobTypeId,
                Name = jt.JobTypeName
            })
            .ToListAsync(ct);
    }

    public async Task<List<SportRefDto>> GetSportsAsync(CancellationToken ct = default)
    {
        return await _context.Sports
            .AsNoTracking()
            .OrderBy(s => s.SportName)
            .Select(s => new SportRefDto
            {
                SportId = s.SportId,
                Name = s.SportName
            })
            .ToListAsync(ct);
    }

    public async Task<List<CustomerRefDto>> GetCustomersAsync(CancellationToken ct = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .OrderBy(c => c.CustomerName)
            .Select(c => new CustomerRefDto
            {
                CustomerId = c.CustomerId,
                Name = c.CustomerName
            })
            .ToListAsync(ct);
    }

    public async Task<List<BillingTypeRefDto>> GetBillingTypesAsync(CancellationToken ct = default)
    {
        return await _context.BillingTypes
            .AsNoTracking()
            .OrderBy(bt => bt.BillingTypeName)
            .Select(bt => new BillingTypeRefDto
            {
                BillingTypeId = bt.BillingTypeId,
                Name = bt.BillingTypeName
            })
            .ToListAsync(ct);
    }

    public async Task<List<ChargeTypeRefDto>> GetChargeTypesAsync(CancellationToken ct = default)
    {
        return await _context.JobAdminChargeTypes
            .AsNoTracking()
            .OrderBy(ct2 => ct2.Name)
            .Select(ct2 => new ChargeTypeRefDto
            {
                Id = ct2.Id,
                Name = ct2.Name
            })
            .ToListAsync(ct);
    }

    // ── Write ────────────────────────────────────────────

    public async Task<Jobs?> GetJobTrackedAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .FirstOrDefaultAsync(j => j.JobId == jobId, ct);
    }

    public async Task<GameClockParams?> GetGameClockParamsTrackedAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.GameClockParams
            .FirstOrDefaultAsync(g => g.JobId == jobId, ct);
    }

    public void AddGameClockParams(GameClockParams gcp)
    {
        _context.GameClockParams.Add(gcp);
    }

    public void AddAdminCharge(JobAdminCharges charge)
    {
        _context.JobAdminCharges.Add(charge);
    }

    public void RemoveAdminCharge(JobAdminCharges charge)
    {
        _context.JobAdminCharges.Remove(charge);
    }

    public async Task<JobAdminCharges?> GetAdminChargeByIdAsync(int chargeId, Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobAdminCharges
            .FirstOrDefaultAsync(c => c.Id == chargeId && c.JobId == jobId, ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
