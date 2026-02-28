using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for per-division-name scheduling strategy profiles.
/// </summary>
public class DivisionProfileRepository : IDivisionProfileRepository
{
    private readonly SqlDbContext _context;

    public DivisionProfileRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<DivisionScheduleProfile>> GetByJobIdAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.DivisionScheduleProfile
            .AsNoTracking()
            .Where(p => p.JobId == jobId)
            .OrderBy(p => p.DivisionName)
            .ToListAsync(ct);
    }

    public async Task UpsertBatchAsync(
        Guid jobId,
        List<DivisionScheduleProfile> profiles,
        CancellationToken ct = default)
    {
        // Load existing profiles for this job (tracked)
        var existing = await _context.DivisionScheduleProfile
            .Where(p => p.JobId == jobId)
            .ToListAsync(ct);

        var existingByName = existing
            .ToDictionary(p => p.DivisionName, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;

        foreach (var incoming in profiles)
        {
            if (existingByName.TryGetValue(incoming.DivisionName, out var existing1))
            {
                // Update existing row
                existing1.Placement = incoming.Placement;
                existing1.GapPattern = incoming.GapPattern;
                existing1.InferredFromJob = incoming.InferredFromJob;
                existing1.ModifiedUtc = now;
            }
            else
            {
                // Insert new row
                incoming.JobId = jobId;
                incoming.CreatedUtc = now;
                incoming.ModifiedUtc = now;
                _context.DivisionScheduleProfile.Add(incoming);
            }
        }
    }

    public async Task DeleteByJobIdAsync(Guid jobId, CancellationToken ct = default)
    {
        var existing = await _context.DivisionScheduleProfile
            .Where(p => p.JobId == jobId)
            .ToListAsync(ct);

        _context.DivisionScheduleProfile.RemoveRange(existing);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
