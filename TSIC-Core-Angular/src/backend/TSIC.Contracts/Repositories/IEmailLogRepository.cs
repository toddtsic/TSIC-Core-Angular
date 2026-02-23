using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IEmailLogRepository
{
    Task<List<EmailLogSummaryDto>> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<EmailLogDetailDto?> GetDetailAsync(int emailId, Guid jobId, CancellationToken cancellationToken = default);
    Task LogAsync(EmailLogs entry, CancellationToken cancellationToken = default);
}
