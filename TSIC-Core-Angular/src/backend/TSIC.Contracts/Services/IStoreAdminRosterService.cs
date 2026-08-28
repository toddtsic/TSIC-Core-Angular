using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Store;

namespace TSIC.Contracts.Services;

/// <summary>
/// The Store Administrators roster — legacy's <c>StoreAdminAddController</c>.
/// </summary>
/// <remarks>
/// A job's Store Admins are already visible and editable on the SuperUser Administrators
/// screen, which manages every admin role at once. This service exists for the half of legacy
/// that screen cannot reach: legacy let the event's own DIRECTOR staff their merch table
/// (policy "AdminOnly" on the write, "StoreAdmin" on the read), while the Administrators
/// screen is SuperUser-only and must stay that way — widening it would let a Director mint
/// Directors and SuperDirectors.
///
/// So the reach is legacy's and the surface is deliberately narrow: Store Admin registrations
/// only, on the caller's own job, with no role field to change and no delete (legacy's grid
/// passed <c>del: false</c> to navGrid — the Active toggle is how a store admin is retired).
/// The eligibility rules are NOT re-implemented here; adds delegate to
/// <see cref="IAdministratorService"/> so the AM-004 lane wall has exactly one home.
/// </remarks>
public interface IStoreAdminRosterService
{
    Task<List<StoreAdminRosterRowDto>> GetRosterAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grant Store Admin on this job to an existing account. Throws
    /// <see cref="ArgumentException"/> with a user-facing message when the account is
    /// ineligible (family credential, wrong lane, already registered here).
    /// </summary>
    Task<List<StoreAdminRosterRowDto>> AddAsync(
        Guid jobId,
        StoreAdminAddRequest request,
        string currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update one Store Admin's Active flag, email and cell phone. Throws
    /// <see cref="InvalidOperationException"/> if the registration is not a Store Admin on
    /// this job — the role field is not editable from this surface.
    /// </summary>
    Task<List<StoreAdminRosterRowDto>> UpdateAsync(
        Guid jobId,
        Guid registrationId,
        StoreAdminUpdateRequest request,
        string currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Typeahead for the add form, pinned to the Store Admin lane.
    /// </summary>
    Task<UserSearchResponseDto> SearchCandidatesAsync(
        string query,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
