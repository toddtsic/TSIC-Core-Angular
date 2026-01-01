using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
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
}
