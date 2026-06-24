using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IEmailLogRepository
{
    Task<List<EmailLogSummaryDto>> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<EmailLogDetailDto?> GetDetailAsync(int emailId, Guid jobId, CancellationToken cancellationToken = default);
    Task LogAsync(EmailLogs entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementally update a batch's audit row (created by LogAsync) as the send progresses.
    /// Re-fetches by EmailId on the caller's scope so it is safe to call from a periodic flush.
    /// No-op if the row is gone.
    /// </summary>
    Task UpdateProgressAsync(int emailId, int count, string sendTo, CancellationToken cancellationToken = default);
}
