using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Repositories;

public interface IEmailLogRepository
{
    Task<List<EmailLogSummaryDto>> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<EmailLogDetailDto?> GetDetailAsync(int emailId, Guid jobId, CancellationToken cancellationToken = default);
}
