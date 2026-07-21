using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.EmailTroubleshooter;
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

    public async Task<List<PlayerSentEmailDto>> GetSentToAddressesAllJobsAsync(
        IReadOnlyList<string> addresses,
        CancellationToken cancellationToken = default)
    {
        // Delimiter-anchored, case-insensitive: wrap the stored ';'-joined recipient list in ';'
        // and match "%;addr;%" so an address can't match as a substring of a longer one.
        var lowered = (addresses ?? Array.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim().ToLower())
            .Distinct()
            .ToList();
        if (lowered.Count == 0)
        {
            return new List<PlayerSentEmailDto>();
        }

        // Cross-job: no JobId filter. Job name comes from the (nullable) Job navigation via a LEFT
        // JOIN, so orphaned/null-job rows still surface with a null name.
        var baseQuery = _context.EmailLogs
            .AsNoTracking()
            .Where(e => e.SendTo != null);

        // One LIKE per address, OR-combined via Union so the query translates for any address
        // count. Project to a small keyed shape BEFORE the Union so the nvarchar(max) msg/sendTo
        // columns are never pulled into the DISTINCT; EmailId keeps distinct batches separate.
        var union = lowered
            .Select(addr => baseQuery
                .Where(e => EF.Functions.Like(";" + e.SendTo!.ToLower() + ";", "%;" + addr + ";%"))
                .Select(e => new { e.EmailId, JobName = e.Job!.JobName, e.Subject, e.SendTs }))
            .Aggregate((a, b) => a.Union(b));

        return await union
            .OrderByDescending(x => x.SendTs)
            .Select(x => new PlayerSentEmailDto { JobName = x.JobName, Subject = x.Subject, SentAt = x.SendTs })
            .ToListAsync(cancellationToken);
    }

    public async Task LogAsync(
        EmailLogs entry,
        CancellationToken cancellationToken = default)
    {
        _context.EmailLogs.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateProgressAsync(
        int emailId,
        int count,
        string sendTo,
        CancellationToken cancellationToken = default)
    {
        var row = await _context.EmailLogs
            .FirstOrDefaultAsync(e => e.EmailId == emailId, cancellationToken);
        if (row == null) return;

        row.Count = count;
        row.SendTo = sendTo;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
