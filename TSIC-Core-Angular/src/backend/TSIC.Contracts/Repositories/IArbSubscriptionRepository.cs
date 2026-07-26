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
