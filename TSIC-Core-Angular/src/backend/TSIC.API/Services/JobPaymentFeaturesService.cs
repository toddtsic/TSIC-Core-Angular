using Microsoft.Extensions.Configuration;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services;

/// <summary>
/// Resolves per-job merchant payment capabilities from config + the job's customer id.
/// See <see cref="IJobPaymentFeaturesService"/> for the fail-closed contract.
/// </summary>
public sealed class JobPaymentFeaturesService : IJobPaymentFeaturesService
{
    private const string AmexClientIdsKey = "PaymentMethods_NonMCVisa_ClientIds:Amex";

    private readonly IConfiguration _config;
    private readonly IJobRepository _jobRepo;

    public JobPaymentFeaturesService(IConfiguration config, IJobRepository jobRepo)
    {
        _config = config;
        _jobRepo = jobRepo;
    }

    public async Task<bool> UsesAmexAsync(Guid? jobId, CancellationToken ct = default)
    {
        if (jobId is null) return false;

        var customerId = await _jobRepo.GetCustomerIdAsync(jobId.Value, ct);
        if (customerId is null) return false;

        var amexIds = _config.GetSection(AmexClientIdsKey).Get<string[]>() ?? Array.Empty<string>();
        var cust = customerId.Value.ToString();
        return Array.Exists(amexIds, id => string.Equals(id, cust, StringComparison.OrdinalIgnoreCase));
    }
}
