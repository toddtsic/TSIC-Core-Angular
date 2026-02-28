using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Dtos.CustomerJobRevenue;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Admin;

public class CustomerJobRevenueService : ICustomerJobRevenueService
{
    private readonly ICustomerJobRevenueRepository _repo;
    private readonly IAdnApiService _adnApiService;

    // Legacy TSIC-owned ADN login IDs — used to pick the correct sproc variant
    private static readonly HashSet<string> TsicAdnLoginIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "4dE5m4WR9ey",
        "teamspt52"
    };

    public CustomerJobRevenueService(
        ICustomerJobRevenueRepository repo,
        IAdnApiService adnApiService)
    {
        _repo = repo;
        _adnApiService = adnApiService;
    }

    public async Task<JobRevenueDataDto> GetRevenueDataAsync(
        Guid jobId, DateTime startDate, DateTime endDate,
        List<string> jobNames, CancellationToken ct = default)
    {
        // Determine which sproc variant to use based on ADN credentials
        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobId, bProdOnly: true);
        var isTsicAdn = credentials?.AdnLoginId != null
                        && TsicAdnLoginIds.Contains(credentials.AdnLoginId);

        var listJobsString = jobNames.Count > 0 ? string.Join(",", jobNames) : string.Empty;

        return await _repo.GetRevenueDataAsync(jobId, startDate, endDate, listJobsString, isTsicAdn, ct);
    }

    public async Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default)
    {
        await _repo.UpdateMonthlyCountAsync(aid, request, userId, ct);
    }
}
