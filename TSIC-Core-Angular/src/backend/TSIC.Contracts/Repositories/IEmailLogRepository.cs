using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.EmailTroubleshooter;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IEmailLogRepository
{
    Task<List<EmailLogSummaryDto>> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<EmailLogDetailDto?> GetDetailAsync(int emailId, Guid jobId, CancellationToken cancellationToken = default);
    Task LogAsync(EmailLogs entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batches (newest first) IN ONE JOB whose recipient list (emailLogs.sendTo, a ';'-joined
    /// address string) contains ANY of the given addresses. The JobId filter is an index seek, so
    /// the non-sargable sendTo LIKE only runs over that job's rows. Matching is delimiter-anchored
    /// and case-insensitive so "bob@x.com" cannot match inside "bob@x.comcast.net". Batch-level:
    /// a hit means the address was in that send, not a per-recipient delivery time/status.
    /// </summary>
    Task<List<PlayerSentEmailDto>> GetSentToAddressesAsync(
        Guid jobId, IReadOnlyList<string> addresses, CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementally update a batch's audit row (created by LogAsync) as the send progresses.
    /// Re-fetches by EmailId on the caller's scope so it is safe to call from a periodic flush.
    /// No-op if the row is gone.
    /// </summary>
    Task UpdateProgressAsync(int emailId, int count, string sendTo, CancellationToken cancellationToken = default);
}
