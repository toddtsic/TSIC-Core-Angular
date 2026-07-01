using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Customer;
using TSIC.Domain.Entities;

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

    // ── Customer Configure CRUD ──────────────────────────

    /// <summary>
    /// List all customers with AMEX flag, job count, and last registration activity
    /// (read-only). The frontend segments by job count; no-job customers have null
    /// last-activity fields.
    /// </summary>
    Task<List<CustomerListDto>> GetAllCustomersAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a single customer's editable details (read-only).
    /// </summary>
    Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Count how many jobs belong to a customer (for delete safety check).
    /// </summary>
    Task<int> GetCustomerJobCountAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Resolve a valid, non-null TzId for a newly created customer. Timezone is no longer
    /// user-editable, but the column is NOT NULL — carry the platform default customer's
    /// timezone, falling back to any existing timezone row.
    /// </summary>
    Task<int> ResolveDefaultTzIdAsync(Guid defaultCustomerId, CancellationToken ct = default);

    // ── Write (tracked) ──────────────────────────────────

    /// <summary>
    /// Load a customer with change tracking for update/delete.
    /// </summary>
    Task<Customers?> GetCustomerTrackedAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Stage a new customer for insertion.
    /// </summary>
    void AddCustomer(Customers customer);

    /// <summary>
    /// Stage a customer for deletion.
    /// </summary>
    void RemoveCustomer(Customers customer);

    /// <summary>
    /// Persist all tracked changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
