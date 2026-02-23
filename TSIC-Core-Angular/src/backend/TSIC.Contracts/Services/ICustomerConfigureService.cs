using TSIC.Contracts.Dtos.Customer;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for Customer Configure CRUD operations (SuperUser-only).
/// </summary>
public interface ICustomerConfigureService
{
    Task<List<CustomerListDto>> GetAllCustomersAsync(CancellationToken ct = default);
    Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid customerId, CancellationToken ct = default);
    Task<List<TimezoneDto>> GetTimezonesAsync(CancellationToken ct = default);
    Task<CustomerDetailDto> CreateCustomerAsync(CreateCustomerRequest request, string userId, CancellationToken ct = default);
    Task<CustomerDetailDto> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequest request, string userId, CancellationToken ct = default);
    Task DeleteCustomerAsync(Guid customerId, CancellationToken ct = default);
}
