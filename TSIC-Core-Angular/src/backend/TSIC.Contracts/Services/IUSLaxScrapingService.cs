using TSIC.Contracts.Dtos.Rankings;

namespace TSIC.Contracts.Services;

/// <summary>
/// Scrapes Girls National rankings from usclublax.com
/// </summary>
public interface IUSLaxScrapingService
{
    /// <summary>
    /// Get available age groups for Girls National rankings from usclublax.com
    /// </summary>
    Task<List<AgeGroupOptionDto>> GetAvailableAgeGroupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Scrape rankings for a specific age group.
    /// v = version (20=Girls Overall, 21=Girls National), alpha = sort, yr = graduation year
    /// </summary>
    Task<ScrapeResultDto> ScrapeRankingsAsync(string v, string alpha, string yr, CancellationToken ct = default);
}
