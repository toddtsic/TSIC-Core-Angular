using TSIC.Contracts.Dtos.UsLax;

namespace TSIC.Contracts.Services;

public interface IUsLaxMembershipService
{
    /// <summary>List the USLax reconciliation candidate set for a job (pre-ping), scoped by role.</summary>
    Task<IReadOnlyList<UsLaxReconciliationCandidateDto>> GetCandidatesAsync(Guid jobId, UsLaxMembershipRole role, CancellationToken ct = default);

    /// <summary>
    /// Run reconciliation against USA Lacrosse for the given candidates. When the caller
    /// passes null/empty RegistrationIds, every eligible candidate for the job (in the
    /// request's Role scope) is pinged. Writes SportAssnIdexpDate on rows where the USALax
    /// response includes a new exp_date and involvement contains "Player".
    /// </summary>
    Task<UsLaxReconciliationResponse> ReconcileAsync(Guid jobId, UsLaxReconciliationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Send the legacy-style USA Lacrosse "action required" email to the supplied recipients.
    /// Tokens (<c>!PLAYER</c>, <c>!PLAYERDOB</c>, <c>!USLAXMEMBERID</c>, <c>!USLAXMEMBERSTATUSSTATUS</c>,
    /// <c>!USLAXAGEVERIFIED</c>, <c>!USLAXEXPIRY</c>, <c>!JOBNAME</c>, <c>!JOBLINK</c>) are substituted
    /// per-recipient from the payload snapshots. Body is sent as HTML.
    /// </summary>
    Task<UsLaxEmailResponse> SendEmailAsync(Guid jobId, string? senderUserId, UsLaxEmailRequest request, CancellationToken ct = default);
}
