using TSIC.Contracts.Dtos.MyRoster;

namespace TSIC.Contracts.Services;

public interface IMyRosterService
{
    /// <summary>
    /// Resolves the caller's team roster. When visibility is denied by role or job flag,
    /// returns an MyRosterResponseDto with Allowed=false (not an exception) so the
    /// frontend can render a friendly alert.
    /// </summary>
    Task<MyRosterResponseDto> GetMyRosterAsync(Guid callerRegistrationId, CancellationToken ct = default);

    /// <summary>
    /// Starts a background batch email to the caller's teammates on the shared email engine.
    /// If request.RegistrationIds is null/empty, targets the entire team roster. Otherwise validates
    /// each id is on the caller's team (throws UnauthorizedAccessException on any leak). Returns a
    /// handle to poll for progress; the engine owns opt-out suppression, the unsubscribe footer,
    /// retry, rate-limiting, and the audit row.
    /// </summary>
    Task<EmailBatchHandle> StartBatchEmailAsync(
        Guid callerRegistrationId,
        string callerUserId,
        MyRosterBatchEmailRequest request,
        CancellationToken ct = default);
}
