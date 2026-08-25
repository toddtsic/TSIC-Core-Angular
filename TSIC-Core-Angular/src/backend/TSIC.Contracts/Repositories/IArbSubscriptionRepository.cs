using TSIC.Contracts.Dtos.Arb;

namespace TSIC.Contracts.Repositories;

public interface IArbSubscriptionRepository
{
    Task<List<ArbRegistrationProjection>> GetActiveSubscriptionsForJobAsync(
        Guid jobId, CancellationToken ct = default);

    Task<List<ArbRegistrationProjection>> GetRegistrationsByInvoiceNumbersAsync(
        List<string> invoiceNumbers, Guid? jobIdFilter,
        CancellationToken ct = default);

    Task<ArbRegistrationDetail?> GetRegistrationArbDetailAsync(
        Guid registrationId, CancellationToken ct = default);

    Task<decimal> GetArbPaymentsTotalAsync(
        Guid registrationId, CancellationToken ct = default);

    Task<List<ArbDirectorProjection>> GetDirectorsForJobsAsync(
        List<Guid> jobIds, CancellationToken ct = default);

    /// <summary>
    /// ONE director per job - the job's default sender. Jobs.PrimaryContactRegistrationId wins when
    /// that registration is an active Director with a usable email; otherwise the earliest-registered
    /// active Director (Registrations.RegistrationAi - the only reliable "joined first" ordering key,
    /// as Jobs.Registrations carries no create date). Directors with no email are dropped BEFORE the
    /// pick, so a starred primary contact missing an address falls through instead of yielding none.
    /// Jobs with no usable director are simply absent from the result.
    /// </summary>
    Task<List<ArbDirectorProjection>> GetDefaultDirectorsForJobsAsync(
        List<Guid> jobIds, CancellationToken ct = default);

    /// <summary>
    /// Distinct jobs holding at least one subscription that can still draft (status active or
    /// suspended). Drives the unattended expiring-card pass, which needs per-job ADN credentials and
    /// so cannot be done in one estate-wide call. Dead plans are excluded: an expiring card on a
    /// terminated subscription has nothing left to fail.
    /// </summary>
    Task<List<Guid>> GetJobIdsWithLiveSubscriptionsAsync(CancellationToken ct = default);

    Task<(string Email, string DisplayName)?> GetSenderInfoAsync(
        string userId, CancellationToken ct = default);

    Task UpdateSubscriptionStatusAsync(
        Guid registrationId, string newStatus, CancellationToken ct = default);

    /// <summary>
    /// All registrations in the job with a subscription ID — NO bActive filter; an
    /// inactive registration can still carry a live, billing subscription.
    /// </summary>
    Task<List<ArbStatusRefreshTarget>> GetStatusRefreshTargetsForJobAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>Batch write of refreshed statuses — one SaveChanges for the whole set.</summary>
    Task UpdateSubscriptionStatusesAsync(
        IReadOnlyDictionary<Guid, string> statusByRegistrationId, CancellationToken ct = default);
}
