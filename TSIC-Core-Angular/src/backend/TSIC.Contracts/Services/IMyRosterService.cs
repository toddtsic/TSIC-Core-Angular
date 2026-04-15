using TSIC.Contracts.Dtos.MyRoster;
using TSIC.Contracts.Dtos.RegistrationSearch;

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
    /// Sends a batch email to the caller's teammates. If request.RegistrationIds is
    /// null/empty, targets the entire team roster. Otherwise validates each id is on
    /// the caller's team (throws UnauthorizedAccessException on any leak).
    /// </summary>
    Task<BatchEmailResponse> SendBatchEmailAsync(
        Guid callerRegistrationId,
        string callerUserId,
        MyRosterBatchEmailRequest request,
        CancellationToken ct = default);
}
