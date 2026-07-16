using TSIC.Contracts.Dtos.AdminExpiry;

namespace TSIC.API.Services.Admin;

public interface IAdminExpiryService
{
    Task<List<AdminExpiryCustomerDto>> GetExpiredJobsAsync(CancellationToken ct = default);

    Task UpdateExpiryAsync(Guid jobId, UpdateAdminExpiryRequest request, CancellationToken ct = default);
}
