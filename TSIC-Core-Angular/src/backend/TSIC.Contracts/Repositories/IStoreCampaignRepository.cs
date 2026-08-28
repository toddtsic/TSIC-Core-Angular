using TSIC.Contracts.Dtos.Store;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Audience queries for the three store email campaigns. Each returns WHO to mail; the composing,
/// rendering and sending is the batch engine's job, driven by <c>IStoreCampaignService</c>.
/// </summary>
public interface IStoreCampaignRepository
{
    /// <summary>
    /// Carts belonging to <paramref name="storeId"/> that were last touched between
    /// <paramref name="minAgeHours"/> and <paramref name="maxAgeHours"/> ago and have NO accounting
    /// row (i.e. were never paid for). Lines come back unfiltered — the caller drops sold-out ones.
    ///
    /// One query, grouped in memory. Legacy issued one availability round-trip per line on top of
    /// this, which is why the screen crawled on a busy store.
    /// </summary>
    Task<List<StoreAbandonedCartRowDto>> GetAbandonedCartsAsync(
        int storeId, int minAgeHours, int maxAgeHours, CancellationToken cancellationToken = default);

    /// <summary>
    /// Families with an active registration in the job that have never opened a cart in this store.
    /// </summary>
    Task<List<string>> GetFamilyUserIdsNeverOrderedAsync(
        Guid jobId, int storeId, CancellationToken cancellationToken = default);

    /// <summary>Families with at least one paid batch in this store.</summary>
    Task<List<string>> GetFamilyUserIdsThatOrderedAsync(
        int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Contact + consent details for the given families, scoped to <paramref name="jobId"/> for the
    /// representative registration and the opt-out roll-up. Families with no row are omitted.
    /// </summary>
    Task<List<StoreCampaignFamilyDto>> GetFamilyContactsAsync(
        Guid jobId, IReadOnlyCollection<string> familyUserIds, CancellationToken cancellationToken = default);
}
