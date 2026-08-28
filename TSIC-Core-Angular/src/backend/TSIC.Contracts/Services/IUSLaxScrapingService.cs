using TSIC.Contracts.Dtos.Rankings;

namespace TSIC.Contracts.Services;

/// <summary>
/// Scrapes club lacrosse rankings from usclublax.com
/// </summary>
public interface IUSLaxScrapingService
{
    /// <summary>
    /// Get the seasons usclublax.com publishes, newest first, with the one the
    /// site currently serves flagged IsCurrent.
    /// </summary>
    Task<List<RankingSeasonDto>> GetAvailableSeasonsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get available Girls age groups for a season. Pass null for the season the
    /// site currently serves. The set of groups differs season to season, so this
    /// must be re-fetched whenever the season changes.
    /// </summary>
    Task<List<AgeGroupOptionDto>> GetAvailableAgeGroupsAsync(string? yr = null, CancellationToken ct = default);

    /// <summary>
    /// Scrape rankings for a specific age group.
    /// v = version (20=Girls Overall, 21=Girls National), alpha = sort, yr = season
    /// </summary>
    Task<ScrapeResultDto> ScrapeRankingsAsync(string v, string alpha, string yr, CancellationToken ct = default);
}
