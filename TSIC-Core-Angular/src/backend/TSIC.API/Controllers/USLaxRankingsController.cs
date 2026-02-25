using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Rankings;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// US Lacrosse Girls National rankings — scrape usclublax.com, align with registered teams,
/// and import ranking data into TeamComments for pool seeding.
/// </summary>
[ApiController]
[Route("api/uslax-rankings")]
[Authorize]
public class USLaxRankingsController : ControllerBase
{
    private readonly IUSLaxScrapingService _scrapingService;
    private readonly IUSLaxMatchingService _matchingService;
    private readonly ITeamRepository _teamRepo;
    private readonly IAgeGroupRepository _ageGroupRepo;
    private readonly IJobLookupService _jobLookupService;

    public USLaxRankingsController(
        IUSLaxScrapingService scrapingService,
        IUSLaxMatchingService matchingService,
        ITeamRepository teamRepo,
        IAgeGroupRepository ageGroupRepo,
        IJobLookupService jobLookupService)
    {
        _scrapingService = scrapingService;
        _matchingService = matchingService;
        _teamRepo = teamRepo;
        _ageGroupRepo = ageGroupRepo;
        _jobLookupService = jobLookupService;
    }

    // ── Scrape endpoints ──

    /// <summary>
    /// Get available age groups from usclublax.com (Girls National page options).
    /// </summary>
    [HttpGet("age-groups")]
    public async Task<ActionResult<List<AgeGroupOptionDto>>> GetScrapedAgeGroups(
        CancellationToken ct)
    {
        var result = await _scrapingService.GetAvailableAgeGroupsAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Get active age groups from the current job's registered teams.
    /// Excludes DROPPED and WAITLIST groups.
    /// </summary>
    [HttpGet("registered-age-groups")]
    public async Task<ActionResult<List<AgeGroupOptionDto>>> GetRegisteredAgeGroups(
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var result = await _ageGroupRepo.GetActiveAgeGroupsForJobAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// Scrape rankings from usclublax.com for a specific age group.
    /// v = ranking version (20=Overall, 21=National), alpha = sort, yr = grad year
    /// </summary>
    [HttpGet("scrape")]
    public async Task<ActionResult<ScrapeResultDto>> ScrapeRankings(
        [FromQuery] string v,
        [FromQuery] string alpha,
        [FromQuery] string yr,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(yr))
            return BadRequest(new { message = "Parameters v and yr are required." });

        var result = await _scrapingService.ScrapeRankingsAsync(v, alpha ?? "", yr, ct);
        return Ok(result);
    }

    // ── Align endpoint ──

    /// <summary>
    /// Scrape rankings and align them with registered teams in the given age group
    /// using fuzzy matching. Returns matched pairs, unmatched rankings, and unmatched teams.
    /// </summary>
    [HttpGet("align")]
    public async Task<ActionResult<AlignmentResultDto>> AlignRankings(
        [FromQuery] string v,
        [FromQuery] string alpha,
        [FromQuery] string yr,
        [FromQuery] Guid agegroupId,
        [FromQuery] int clubWeight = 75,
        [FromQuery] int teamWeight = 25,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(yr))
            return BadRequest(new { message = "Parameters v and yr are required." });

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        // Scrape rankings from usclublax.com
        var scrapeResult = await _scrapingService.ScrapeRankingsAsync(v, alpha ?? "", yr, ct);
        if (!scrapeResult.Success)
            return Ok(new AlignmentResultDto
            {
                Success = false,
                ErrorMessage = scrapeResult.ErrorMessage ?? "Scrape failed.",
                AgeGroup = scrapeResult.AgeGroup,
                LastUpdated = scrapeResult.LastUpdated,
                AlignedTeams = [],
                UnmatchedRankings = [],
                UnmatchedTeams = [],
                TotalMatches = 0,
                TotalTeamsInAgeGroup = 0,
                MatchPercentage = 0
            });

        // Get registered teams for this job + agegroup
        var registeredTeams = await _teamRepo.GetTeamsForRankingsAsync(jobId.Value, agegroupId, ct);

        // Run matching algorithm
        var alignment = _matchingService.AlignRankingsWithTeams(
            scrapeResult.Rankings, registeredTeams, clubWeight, teamWeight);

        return Ok(alignment);
    }

    // ── Import / update endpoints ──

    /// <summary>
    /// Bulk-import aligned ranking data into TeamComments for all matches above
    /// the specified confidence category threshold.
    /// Format written: "{rank:D3}:{teamName}" (e.g., "012:Maryland United")
    /// </summary>
    [HttpPost("import-comments")]
    public async Task<ActionResult<ImportCommentsResultDto>> ImportComments(
        [FromBody] ImportCommentsRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        // Scrape + align
        var scrapeResult = await _scrapingService.ScrapeRankingsAsync(
            request.V, request.Alpha, request.Yr, ct);

        if (!scrapeResult.Success)
            return Ok(new ImportCommentsResultDto
            {
                Success = false,
                Message = scrapeResult.ErrorMessage ?? "Scrape failed.",
                UpdatedCount = 0,
                TotalMatches = 0,
                ConfidenceCategory = request.ConfidenceCategory
            });

        var registeredTeams = await _teamRepo.GetTeamsForRankingsAsync(
            jobId.Value, request.RegisteredTeamAgeGroupId, ct);

        var alignment = _matchingService.AlignRankingsWithTeams(
            scrapeResult.Rankings, registeredTeams, request.ClubWeight, request.TeamWeight);

        // Filter by confidence category
        double minScore = request.ConfidenceCategory switch
        {
            "high" => 0.75,
            "medium" => 0.50,
            _ => 0.50
        };

        var teamsToUpdate = alignment.AlignedTeams
            .Where(a => a.MatchScore >= minScore)
            .ToDictionary(
                a => a.RegisteredTeam.TeamId,
                a => (string?)$"{a.Ranking.Rank:D3}:{a.Ranking.Team}");

        if (teamsToUpdate.Count == 0)
            return Ok(new ImportCommentsResultDto
            {
                Success = true,
                Message = "No matches met the confidence threshold.",
                UpdatedCount = 0,
                TotalMatches = alignment.TotalMatches,
                ConfidenceCategory = request.ConfidenceCategory
            });

        var updatedCount = await _teamRepo.BulkUpdateTeamCommentsAsync(teamsToUpdate, ct);

        return Ok(new ImportCommentsResultDto
        {
            Success = true,
            Message = $"Updated {updatedCount} team comments.",
            UpdatedCount = updatedCount,
            TotalMatches = alignment.TotalMatches,
            ConfidenceCategory = request.ConfidenceCategory
        });
    }

    /// <summary>
    /// Update a single team's comment (manual override).
    /// </summary>
    [HttpPut("team-comment/{teamId:guid}")]
    public async Task<IActionResult> UpdateTeamComment(
        Guid teamId,
        [FromBody] UpdateTeamCommentRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var team = await _teamRepo.GetTeamFromTeamId(teamId, ct);
        if (team == null) return NotFound(new { message = "Team not found." });

        // Verify team belongs to this job
        if (team.JobId != jobId.Value)
            return BadRequest(new { message = "Team does not belong to the current job." });

        var update = new Dictionary<Guid, string?> { [teamId] = request.Comment };
        await _teamRepo.BulkUpdateTeamCommentsAsync(update, ct);

        return Ok(new { message = "Team comment updated." });
    }

    /// <summary>
    /// Clear all TeamComments for teams in the specified age group.
    /// </summary>
    [HttpDelete("team-comments/{agegroupId:guid}")]
    public async Task<IActionResult> ClearTeamComments(
        Guid agegroupId,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var cleared = await _teamRepo.ClearTeamCommentsForAgegroupAsync(jobId.Value, agegroupId, ct);

        return Ok(new { message = $"Cleared comments for {cleared} teams.", count = cleared });
    }

    // ── Export endpoint ──

    /// <summary>
    /// Export aligned rankings as a CSV download.
    /// </summary>
    [HttpGet("export-csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] string v,
        [FromQuery] string alpha,
        [FromQuery] string yr,
        [FromQuery] Guid agegroupId,
        [FromQuery] int clubWeight = 75,
        [FromQuery] int teamWeight = 25,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(yr))
            return BadRequest(new { message = "Parameters v and yr are required." });

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var scrapeResult = await _scrapingService.ScrapeRankingsAsync(v, alpha ?? "", yr, ct);
        if (!scrapeResult.Success)
            return BadRequest(new { message = scrapeResult.ErrorMessage ?? "Scrape failed." });

        var registeredTeams = await _teamRepo.GetTeamsForRankingsAsync(jobId.Value, agegroupId, ct);
        var alignment = _matchingService.AlignRankingsWithTeams(
            scrapeResult.Rankings, registeredTeams, clubWeight, teamWeight);

        var csv = BuildCsv(alignment);
        var bytes = Encoding.UTF8.GetBytes(csv);

        return File(bytes, "text/csv", $"uslax-rankings-{yr}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ── Private helpers ──

    private static string BuildCsv(AlignmentResultDto alignment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("National Rank,Ranked Team,State,Rating,Registered Team,Club,Match Score");

        foreach (var match in alignment.AlignedTeams.OrderBy(a => a.Ranking.Rank))
        {
            sb.AppendLine(string.Join(",",
                Escape(match.Ranking.Rank.ToString()),
                Escape(match.Ranking.Team),
                Escape(match.Ranking.State),
                Escape(match.Ranking.Rating.ToString("F2")),
                Escape(match.RegisteredTeam.TeamName),
                Escape(match.RegisteredTeam.ClubName ?? ""),
                Escape(match.MatchScore.ToString("P0"))));
        }

        if (alignment.UnmatchedRankings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Unmatched Rankings");
            sb.AppendLine("Rank,Team,State,Rating");
            foreach (var r in alignment.UnmatchedRankings.OrderBy(r => r.Rank))
            {
                sb.AppendLine(string.Join(",",
                    Escape(r.Rank.ToString()),
                    Escape(r.Team),
                    Escape(r.State),
                    Escape(r.Rating.ToString("F2"))));
            }
        }

        if (alignment.UnmatchedTeams.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Unmatched Registered Teams");
            sb.AppendLine("Team,Club,Age Group");
            foreach (var t in alignment.UnmatchedTeams.OrderBy(t => t.TeamName))
            {
                sb.AppendLine(string.Join(",",
                    Escape(t.TeamName),
                    Escape(t.ClubName ?? ""),
                    Escape(t.AgegroupName ?? "")));
            }
        }

        return sb.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
