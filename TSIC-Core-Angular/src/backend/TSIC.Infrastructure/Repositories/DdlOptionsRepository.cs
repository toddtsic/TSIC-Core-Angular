using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for reading/writing the Jobs.JsonOptions column.
/// </summary>
public class DdlOptionsRepository : IDdlOptionsRepository
{
    private readonly SqlDbContext _context;

    public DdlOptionsRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<string?> GetJsonOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.JsonOptions)
            .SingleOrDefaultAsync(ct);
    }

    public async Task UpdateJsonOptionsAsync(Guid jobId, string jsonOptions, CancellationToken ct = default)
    {
        await _context.Jobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.JsonOptions, jsonOptions), ct);
    }
}
