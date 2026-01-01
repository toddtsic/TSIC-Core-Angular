using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository interface for Customers entity.
/// </summary>
public interface ICustomerRepository
{
    /// <summary>
    /// Get Authorize.Net credentials for a customer.
    /// </summary>
    Task<AdnCredentialsViewModel?> GetAdnCredentialsAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Authorize.Net credentials for a job's associated customer.
    /// </summary>
    Task<AdnCredentialsViewModel?> GetAdnCredentialsByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);
}
