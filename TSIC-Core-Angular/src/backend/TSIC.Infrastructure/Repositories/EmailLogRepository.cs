using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class EmailLogRepository : IEmailLogRepository
{
    private readonly SqlDbContext _context;

    public EmailLogRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<EmailLogSummaryDto>> GetByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.EmailLogs
            .AsNoTracking()
            .Where(e => e.JobId == jobId)
            .OrderByDescending(e => e.SendTs)
            .Select(e => new EmailLogSummaryDto
            {
                EmailId = e.EmailId,
                SendTs = e.SendTs,
                SendFrom = e.SendFrom,
                Count = e.Count,
                Subject = e.Subject
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<EmailLogDetailDto?> GetDetailAsync(
        int emailId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.EmailLogs
            .AsNoTracking()
            .Where(e => e.EmailId == emailId && e.JobId == jobId)
            .Select(e => new EmailLogDetailDto
            {
                EmailId = e.EmailId,
                SendTo = e.SendTo,
                Msg = e.Msg
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task LogAsync(
        EmailLogs entry,
        CancellationToken cancellationToken = default)
    {
        _context.EmailLogs.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
