using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IJobDiscountCodeRepository using Entity Framework Core.
/// Encapsulates all EF-specific query logic for JobDiscountCodes entity.
/// </summary>
public class JobDiscountCodeRepository : IJobDiscountCodeRepository
{
    private readonly SqlDbContext _context;

    public JobDiscountCodeRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<(int Ai, bool? BAsPercent, decimal? CodeAmount)?> GetActiveCodeAsync(
        Guid jobId,
        string codeNameLower,
        DateTime currentTime,
        CancellationToken cancellationToken = default)
    {
        var result = await _context.JobDiscountCodes
            .AsNoTracking()
            .Where(d => d.JobId == jobId
                        && d.Active
                        && d.CodeStartDate <= currentTime
                        && d.CodeEndDate >= currentTime
                        && d.CodeName.ToLower() == codeNameLower)
            .Select(d => new { d.Ai, d.BAsPercent, d.CodeAmount })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null ? (result.Ai, result.BAsPercent, result.CodeAmount) : null;
    }

    public async Task<(bool? BAsPercent, decimal? CodeAmount)?> GetByAiAsync(int discountCodeAi, CancellationToken cancellationToken = default)
    {
        var result = await _context.JobDiscountCodes
            .AsNoTracking()
            .Where(d => d.Ai == discountCodeAi)
            .Select(d => new { d.BAsPercent, d.CodeAmount })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null ? (result.BAsPercent, result.CodeAmount) : null;
    }

    public async Task<List<JobDiscountCodes>> GetActiveCodesForJobAsync(
        Guid jobId,
        DateTime currentTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobDiscountCodes
            .AsNoTracking()
            .Where(d => d.JobId == jobId
                && d.Active
                && d.CodeStartDate <= currentTime
                && d.CodeEndDate >= currentTime)
            .ToListAsync(cancellationToken);
    }

    // === ADMIN MANAGEMENT METHODS ===

    public async Task<List<(JobDiscountCodes Code, int UsageCount)>> GetAllByJobIdWithUsageAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var rows = await ProjectWithUsage(
                _context.JobDiscountCodes
                    .AsNoTracking()
                    .Where(d => d.JobId == jobId)
                    .OrderByDescending(d => d.Modified))
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Code, r.UsageCount)).ToList();
    }

    /// <summary>
    /// THE definition of "this code has been redeemed", in one place.
    ///
    /// A redemption is a foreign key pointed at the code — and four things point at one:
    /// player registrations, club-rep teams, the accounting ledger, and store carts. Counting
    /// only Registrations (as this repo once did) reports zero usage for a code redeemed solely
    /// by a team, which both unlocks its terms and lets the delete guard wave it through into an
    /// FK violation. Both the edit lock and the delete guard read this projection, so they can
    /// never disagree about what "used" means.
    ///
    /// Each Count() becomes a correlated subquery — one round trip for the whole list.
    /// </summary>
    private static IQueryable<UsageRow> ProjectWithUsage(IQueryable<JobDiscountCodes> source)
    {
        // S2971 (prefer the .Count property) does not apply inside an expression tree: this
        // projection is translated to SQL, where the Count() *method* is what becomes the
        // correlated subquery. Reading the .Count property would enumerate a navigation
        // collection that was never loaded.
#pragma warning disable S2971
        return source.Select(d => new UsageRow
        {
            Code = d,
            UsageCount = d.Registrations.Count()
                       + d.Teams.Count()
                       + d.RegistrationAccounting.Count()
                       + d.StoreCartBatchAccounting.Count()
        });
#pragma warning restore S2971
    }

    private sealed record UsageRow
    {
        public required JobDiscountCodes Code { get; init; }
        public required int UsageCount { get; init; }
    }

    public async Task<JobDiscountCodes?> GetByIdAsync(
        int ai,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobDiscountCodes
            .FirstOrDefaultAsync(d => d.Ai == ai, cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(
        Guid jobId,
        string codeName,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobDiscountCodes
            .AsNoTracking()
            .AnyAsync(d => d.JobId == jobId && d.CodeName.ToLower() == codeName.ToLower(), cancellationToken);
    }

    public async Task<int> GetUsageCountAsync(
        int ai,
        CancellationToken cancellationToken = default)
    {
        var row = await ProjectWithUsage(
                _context.JobDiscountCodes
                    .AsNoTracking()
                    .Where(d => d.Ai == ai))
            .FirstOrDefaultAsync(cancellationToken);

        return row?.UsageCount ?? 0;
    }

    public void Add(JobDiscountCodes code)
    {
        _context.JobDiscountCodes.Add(code);
    }

    public void Remove(JobDiscountCodes code)
    {
        _context.JobDiscountCodes.Remove(code);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
