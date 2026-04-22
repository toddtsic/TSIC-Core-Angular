using TSIC.Contracts.Dtos.UsLax;

namespace TSIC.Contracts.Services;

public interface IUsLaxMembershipService
{
    /// <summary>List the USLax reconciliation candidate set for a job (pre-ping).</summary>
    Task<IReadOnlyList<UsLaxReconciliationCandidateDto>> GetCandidatesAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Run reconciliation against USA Lacrosse for the given candidates. When the caller
    /// passes null/empty RegistrationIds, every eligible candidate for the job is pinged.
    /// Writes SportAssnIdexpDate on rows where the USALax response includes a new exp_date
    /// and involvement contains "Player".
    /// </summary>
    Task<UsLaxReconciliationResponse> ReconcileAsync(Guid jobId, UsLaxReconciliationRequest request, CancellationToken ct = default);
}
