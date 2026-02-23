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
    /// List all customers with timezone name and job count (read-only).
    /// </summary>
    Task<List<CustomerListDto>> GetAllCustomersAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a single customer's editable details (read-only).
    /// </summary>
    Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// List all timezones for dropdown (read-only).
    /// </summary>
    Task<List<TimezoneDto>> GetTimezonesAsync(CancellationToken ct = default);

    /// <summary>
    /// Count how many jobs belong to a customer (for delete safety check).
    /// </summary>
    Task<int> GetCustomerJobCountAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Check whether a timezone ID is valid.
    /// </summary>
    Task<bool> TimezoneExistsAsync(int tzId, CancellationToken ct = default);

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
