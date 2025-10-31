using Microsoft.EntityFrameworkCore;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public interface IJobLookupService
{
    Task<Guid?> GetJobIdByPathAsync(string jobPath);
    Task<bool> IsPlayerRegistrationActiveAsync(Guid jobId);
}

public class JobLookupService : IJobLookupService
{
    private readonly SqlDbContext _context;
    private readonly ILogger<JobLookupService> _logger;

    public JobLookupService(SqlDbContext context, ILogger<JobLookupService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Guid?> GetJobIdByPathAsync(string jobPath)
    {
        var job = await _context.Jobs
            .Where(j => j.JobPath == jobPath)
            .Select(j => new { j.JobId })
            .SingleOrDefaultAsync();

        return job?.JobId;
    }

    public async Task<bool> IsPlayerRegistrationActiveAsync(Guid jobId)
    {
        var job = await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.BRegistrationAllowPlayer })
            .SingleOrDefaultAsync();

        return job?.BRegistrationAllowPlayer ?? false;
    }
}
