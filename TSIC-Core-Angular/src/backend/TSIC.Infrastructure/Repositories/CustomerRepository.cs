using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Customer;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Customers entity using Entity Framework Core.
/// </summary>
public class CustomerRepository : ICustomerRepository
{
    private readonly SqlDbContext _context;

    public CustomerRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Existing ADN credential methods ──────────────────

    public async Task<AdnCredentialsViewModel?> GetAdnCredentialsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Select(c => new AdnCredentialsViewModel
            {
                AdnLoginId = c.AdnLoginId,
                AdnTransactionKey = c.AdnTransactionKey
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AdnCredentialsViewModel?> GetAdnCredentialsByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from j in _context.Jobs
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            where j.JobId == jobId
            select new AdnCredentialsViewModel
            {
                AdnLoginId = c.AdnLoginId,
                AdnTransactionKey = c.AdnTransactionKey
            }
        ).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    // ── Customer Configure CRUD ──────────────────────────

    public async Task<List<CustomerListDto>> GetAllCustomersAsync(CancellationToken ct = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .OrderBy(c => c.CustomerName)
            .Select(c => new CustomerListDto
            {
                CustomerId = c.CustomerId,
                CustomerAi = c.CustomerAi,
                CustomerName = c.CustomerName,
                TzId = c.TzId,
                TimezoneName = c.Tz.TzName,
                JobCount = c.Jobs.Count
            })
            .ToListAsync(ct);
    }

    public async Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid customerId, CancellationToken ct = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Select(c => new CustomerDetailDto
            {
                CustomerId = c.CustomerId,
                CustomerAi = c.CustomerAi,
                CustomerName = c.CustomerName,
                TzId = c.TzId,
                AdnLoginId = c.AdnLoginId,
                AdnTransactionKey = c.AdnTransactionKey
            })
            .SingleOrDefaultAsync(ct);
    }

    public async Task<List<TimezoneDto>> GetTimezonesAsync(CancellationToken ct = default)
    {
        return await _context.Timezones
            .AsNoTracking()
            .OrderBy(tz => tz.TzName)
            .Select(tz => new TimezoneDto
            {
                TzId = tz.TzId,
                TzName = tz.TzName
            })
            .ToListAsync(ct);
    }

    public async Task<int> GetCustomerJobCountAsync(Guid customerId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .CountAsync(j => j.CustomerId == customerId, ct);
    }

    public async Task<bool> TimezoneExistsAsync(int tzId, CancellationToken ct = default)
    {
        return await _context.Timezones
            .AsNoTracking()
            .AnyAsync(tz => tz.TzId == tzId, ct);
    }

    // ── Write (tracked) ──────────────────────────────────

    public async Task<Customers?> GetCustomerTrackedAsync(Guid customerId, CancellationToken ct = default)
    {
        return await _context.Customers
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);
    }

    public void AddCustomer(Customers customer)
    {
        _context.Customers.Add(customer);
    }

    public void RemoveCustomer(Customers customer)
    {
        _context.Customers.Remove(customer);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
