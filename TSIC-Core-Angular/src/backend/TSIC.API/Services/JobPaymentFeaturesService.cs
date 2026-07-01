using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services;

/// <summary>
/// Resolves per-job merchant payment capabilities from the job's customer (merchant account).
/// See <see cref="IJobPaymentFeaturesService"/> for the fail-closed contract.
/// </summary>
public sealed class JobPaymentFeaturesService : IJobPaymentFeaturesService
{
    private readonly IJobRepository _jobRepo;

    public JobPaymentFeaturesService(IJobRepository jobRepo)
    {
        _jobRepo = jobRepo;
    }

    public async Task<bool> UsesAmexAsync(Guid? jobId, CancellationToken ct = default)
    {
        if (jobId is null) return false;

        // AMEX acceptance is a property of the customer's ADN merchant account, stored on
        // Jobs.Customers.bAllowAmex (fail-closed default 0). Replaces the former appsettings
        // GUID allow-list.
        return await _jobRepo.GetCustomerUsesAmexAsync(jobId.Value, ct);
    }
}
