using TSIC.Contracts.Dtos.CustomerJobRevenue;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Admin;

public class CustomerJobRevenueService : ICustomerJobRevenueService
{
    private readonly ICustomerJobRevenueRepository _repo;
    private readonly ICustomerRepository _customerRepo;

    // Legacy TSIC-owned ADN login IDs — used to pick the correct sproc variant
    private static readonly HashSet<string> TsicAdnLoginIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "4dE5m4WR9ey",
        "teamspt52"
    };

    public CustomerJobRevenueService(
        ICustomerJobRevenueRepository repo,
        ICustomerRepository customerRepo)
    {
        _repo = repo;
        _customerRepo = customerRepo;
    }

    public async Task<List<string>> GetAvailableJobNamesAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _repo.GetAvailableJobNamesAsync(jobId, ct);
    }

    public async Task<RevenueRollupResponseDto> GetRollupAsync(
        Guid jobId, DateTime? startDate, DateTime? endDate,
        List<string> jobNames, CancellationToken ct = default)
    {
        var isTsicAdn = await IsTsicAdnAsync(jobId, ct);
        return await _repo.GetRollupAsync(jobId, isTsicAdn, startDate, endDate, jobNames, ct);
    }

    public async Task<List<JobPaymentRecordDto>> GetPaymentDetailsAsync(
        Guid jobId, string method, DateTime? startDate, DateTime? endDate,
        List<string> jobNames, CancellationToken ct = default)
    {
        var isTsicAdn = await IsTsicAdnAsync(jobId, ct);
        return await _repo.GetPaymentDetailsAsync(jobId, isTsicAdn, method, startDate, endDate, jobNames, ct);
    }

    // Which sproc-era variant the queries emulate is a per-customer DATA classification (is
    // this a legacy TSIC-owned ADN merchant?), not an environment concern. Read the customer's
    // stored ADN login id straight from the DB so the classification is identical in every
    // environment — never route this through the env-gated ADN credential resolver (which
    // would return the shared sandbox login off-Production and misclassify).
    private async Task<bool> IsTsicAdnAsync(Guid jobId, CancellationToken ct)
    {
        var credentials = await _customerRepo.GetAdnCredentialsByJobIdAsync(jobId, ct);
        return credentials?.AdnLoginId != null && TsicAdnLoginIds.Contains(credentials.AdnLoginId);
    }

    public async Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default)
    {
        await _repo.UpdateMonthlyCountAsync(aid, request, userId, ct);
    }
}
