using TSIC.Contracts.Dtos.Rankings;

namespace TSIC.Contracts.Services;

/// <summary>
/// Fuzzy matching algorithm that aligns scraped usclublax.com rankings
/// with registered TSIC teams. Pure business logic — no DB access.
/// </summary>
public interface IUSLaxMatchingService
{
    /// <summary>
    /// Align scraped rankings with registered teams using word-by-word matching
    /// with strict binary color/year gates.
    /// </summary>
    AlignmentResultDto AlignRankingsWithTeams(
        List<RankingEntryDto> rankings,
        List<RankingsTeamDto> registeredTeams,
        int clubWeight = 75,
        int teamWeight = 25);
}
