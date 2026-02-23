using TSIC.Contracts.Dtos.Arb;
using TSIC.Domain.Entities;

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

    Task RecordPaymentAsync(
        RegistrationAccounting entry, decimal amount, string userId,
        CancellationToken ct = default);
}
