using Microsoft.Extensions.Options;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Dtos.Customer;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for Customer Configure CRUD operations (SuperUser-only).
/// </summary>
public class CustomerConfigureService : ICustomerConfigureService
{
    private readonly ICustomerRepository _repo;
    private readonly TsicSettings _tsicSettings;

    public CustomerConfigureService(
        ICustomerRepository repo,
        IOptions<TsicSettings> tsicSettings)
    {
        _repo = repo;
        _tsicSettings = tsicSettings.Value;
    }

    public async Task<List<CustomerListDto>> GetAllCustomersAsync(CancellationToken ct = default)
    {
        return await _repo.GetAllCustomersAsync(ct);
    }

    public async Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid customerId, CancellationToken ct = default)
    {
        return await _repo.GetCustomerByIdAsync(customerId, ct);
    }

    public async Task<List<TimezoneDto>> GetTimezonesAsync(CancellationToken ct = default)
    {
        return await _repo.GetTimezonesAsync(ct);
    }

    public async Task<CustomerDetailDto> CreateCustomerAsync(
        CreateCustomerRequest request, string userId, CancellationToken ct = default)
    {
        var trimmed = request.CustomerName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Customer name is required.");

        if (!await _repo.TimezoneExistsAsync(request.TzId, ct))
            throw new ArgumentException($"Timezone ID {request.TzId} is not valid.");

        // Default ADN credentials from TSIC default customer if not supplied
        var adnLogin = request.AdnLoginId;
        var adnKey = request.AdnTransactionKey;

        if (string.IsNullOrWhiteSpace(adnLogin) && _tsicSettings.DefaultCustomerId != Guid.Empty)
        {
            var defaults = await _repo.GetCustomerByIdAsync(_tsicSettings.DefaultCustomerId, ct);
            if (defaults is not null)
            {
                adnLogin = defaults.AdnLoginId;
                adnKey = defaults.AdnTransactionKey;
            }
        }

        var entity = new Customers
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = trimmed,
            TzId = request.TzId,
            AdnLoginId = adnLogin,
            AdnTransactionKey = adnKey,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _repo.AddCustomer(entity);
        await _repo.SaveChangesAsync(ct);

        return new CustomerDetailDto
        {
            CustomerId = entity.CustomerId,
            CustomerAi = entity.CustomerAi,
            CustomerName = entity.CustomerName,
            TzId = entity.TzId,
            AdnLoginId = entity.AdnLoginId,
            AdnTransactionKey = entity.AdnTransactionKey
        };
    }

    public async Task<CustomerDetailDto> UpdateCustomerAsync(
        Guid customerId, UpdateCustomerRequest request, string userId, CancellationToken ct = default)
    {
        var trimmed = request.CustomerName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Customer name is required.");

        if (!await _repo.TimezoneExistsAsync(request.TzId, ct))
            throw new ArgumentException($"Timezone ID {request.TzId} is not valid.");

        var entity = await _repo.GetCustomerTrackedAsync(customerId, ct)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found.");

        entity.CustomerName = trimmed;
        entity.TzId = request.TzId;
        entity.AdnLoginId = request.AdnLoginId;
        entity.AdnTransactionKey = request.AdnTransactionKey;
        entity.LebUserId = userId;
        entity.Modified = DateTime.UtcNow;

        await _repo.SaveChangesAsync(ct);

        return new CustomerDetailDto
        {
            CustomerId = entity.CustomerId,
            CustomerAi = entity.CustomerAi,
            CustomerName = entity.CustomerName,
            TzId = entity.TzId,
            AdnLoginId = entity.AdnLoginId,
            AdnTransactionKey = entity.AdnTransactionKey
        };
    }

    public async Task DeleteCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        var entity = await _repo.GetCustomerTrackedAsync(customerId, ct)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found.");

        var jobCount = await _repo.GetCustomerJobCountAsync(customerId, ct);
        if (jobCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete customer '{entity.CustomerName}' — it has {jobCount} associated job(s). Remove all jobs first.");

        _repo.RemoveCustomer(entity);
        await _repo.SaveChangesAsync(ct);
    }
}
