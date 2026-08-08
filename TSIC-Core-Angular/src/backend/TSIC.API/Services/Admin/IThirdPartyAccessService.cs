using TSIC.Contracts.Dtos.ThirdPartyAccess;

namespace TSIC.API.Services.Admin;

/// <summary>
/// "3rd Party Data Access" console: customer-scoped self-service management of
/// vendor export logins (ApiAuthorized role). SU + SuperDirector only.
/// </summary>
public interface IThirdPartyAccessService
{
    /// <summary>Customer overview (vendor history + open-window jobs), scoped from the caller's job.</summary>
    Task<ThirdPartyAccessOverviewDto> GetOverviewAsync(Guid callerJobId, CancellationToken ct = default);

    /// <summary>
    /// Grant (create-or-reactivate) the vendor login on a job. Reuse-only: the user must
    /// already appear in the customer's ApiAuthorized history. Also ensures the job's
    /// ApiAuthorized report entitlement row exists so the granted login can actually download.
    /// </summary>
    Task<ThirdPartyAccessOverviewDto> GrantAsync(
        Guid callerJobId, Guid targetJobId, string userId, string currentUserId, CancellationToken ct = default);

    /// <summary>Disable the job's vendor login(s) (bActive = 0 — history preserved, never deleted).</summary>
    Task<ThirdPartyAccessOverviewDto> DisableAsync(
        Guid callerJobId, Guid targetJobId, string currentUserId, CancellationToken ct = default);
}
