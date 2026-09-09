using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Rankings;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// US Lacrosse Girls National rankings — scrape usclublax.com, align with registered teams,
/// and import ranking data into NationalRankingData (JSON) for pool seeding.
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
    private readonly IJobRepository _jobRepo;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public USLaxRankingsController(
        IUSLaxScrapingService scrapingService,
        IUSLaxMatchingService matchingService,
        ITeamRepository teamRepo,
        IAgeGroupRepository ageGroupRepo,
        IJobLookupService jobLookupService,
        IJobRepository jobRepo)
    {
        _scrapingService = scrapingService;
        _matchingService = matchingService;
        _teamRepo = teamRepo;
        _ageGroupRepo = ageGroupRepo;
        _jobLookupService = jobLookupService;
        _jobRepo = jobRepo;
    }

    /// <summary>
    /// National-ranking matching exists to seed tournament pools, so every endpoint that touches
    /// job data is tournament-only. The usclublax.com lookups above stay open — they read nothing
    /// of ours.
    /// </summary>
    private async Task<ActionResult?> RejectIfNotTournamentAsync(Guid jobId, CancellationToken ct)
    {
        var jobTypeId = await _jobRepo.GetJobTypeIdAsync(jobId, ct);
        if (jobTypeId == JobConstants.JobTypeTournament)
            return null;

        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "National ranking matching is available for tournament events only."
        });
    }

    // ── usclublax.com lookups ──
    //
    // These read usclublax.com only — no job data, no writes — so they carry no tournament gate;
    // there is nothing of ours to leak and nothing to protect. They feed the two national
    // dropdowns, which the screen only enables once an age group of your own is chosen.

    /// <summary>
    /// Get the seasons usclublax.com publishes, newest first, with the season the site
    /// currently serves flagged IsCurrent.
    /// </summary>
    [HttpGet("seasons")]
    public async Task<ActionResult<List<RankingSeasonDto>>> GetSeasons(CancellationToken ct)
    {
        var result = await _scrapingService.GetAvailableSeasonsAsync(ct);
        if (result is null)
            return Unreachable();

        return Ok(result);
    }

    /// <summary>
    /// Get available Girls age groups from usclublax.com. Pass yr to read an archived
    /// season; omit it for the season the site currently serves. The set of groups
    /// differs season to season, so re-fetch whenever the season changes.
    /// </summary>
    [HttpGet("age-groups")]
    public async Task<ActionResult<List<AgeGroupOptionDto>>> GetScrapedAgeGroups(
        [FromQuery] string? yr,
        CancellationToken ct)
    {
        var result = await _scrapingService.GetAvailableAgeGroupsAsync(yr, ct);
        if (result is null)
            return Unreachable();

        return Ok(result);
    }

    /// <summary>
    /// We could not reach usclublax.com at all. Distinct from an empty 200, which means
    /// we did reach it and it published nothing — the caller shows a different message
    /// for each, because one is worth retrying and the other is not.
    /// </summary>
    private ObjectResult Unreachable() =>
        StatusCode(StatusCodes.Status502BadGateway, new
        {
            message = "Couldn't reach usclublax.com. It may be temporarily unavailable — try again shortly."
        });

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

        var gate = await RejectIfNotTournamentAsync(jobId.Value, ct);
        if (gate is not null) return gate;

        var result = await _ageGroupRepo.GetActiveAgeGroupsForJobAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// Every team in an age group, each carrying whatever ranking stamp it already holds.
    ///
    /// This is what the screen loads the moment an age group is picked, before anything is
    /// scraped: the director sees their own teams first, can correct a name, and can tell at a
    /// glance which teams are already ranked. It deliberately returns ALL teams -- an earlier
    /// version filtered to those with NationalRankingData, which made it impossible to show the
    /// roster before a match had been run.
    /// </summary>
    [HttpGet("age-group-teams/{agegroupId:guid}")]
    public async Task<ActionResult<List<RankingsTeamDto>>> GetAgeGroupTeams(
        Guid agegroupId,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var gate = await RejectIfNotTournamentAsync(jobId.Value, ct);
        if (gate is not null) return gate;

        var teams = await _teamRepo.GetTeamsForRankingsAsync(jobId.Value, agegroupId, ct);
        return Ok(teams);
    }

    // The former GET scrape endpoint lived here. It backed a browse mode that showed the
    // published rankings on their own, for events that cannot match against them. That mode is
    // gone -- this tool exists to seed tournament pools, and anyone who just wants to read the
    // rankings can read them at usclublax.com. Align does its own scraping server-side.

    // ── Align endpoint ──

    /// <summary>
    /// Scrape rankings and align them with registered teams in the given age group
    /// using fuzzy matching. Returns matched pairs, unmatched rankings, and unmatched teams.
    /// </summary>
    [HttpGet("align")]
    public async Task<ActionResult<AlignmentResultDto>> AlignRankings(
        [FromQuery] string v,
        [FromQuery] string yr,
        [FromQuery] Guid agegroupId,
        [FromQuery] string alpha = "",
        [FromQuery] int clubWeight = 75,
        [FromQuery] int teamWeight = 25,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(yr))
            return BadRequest(new { message = "Parameters v and yr are required." });

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var gate = await RejectIfNotTournamentAsync(jobId.Value, ct);
        if (gate is not null) return gate;

        // Scrape rankings from usclublax.com
        var scrapeResult = await _scrapingService.ScrapeRankingsAsync(v, alpha, yr, ct);
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

    /// <summary>
    /// Re-run the matcher for one team against the rankings the director still has unpaired.
    ///
    /// Exists because renaming a team in this event changes the matcher's main input, and the
    /// reason to rename here is usually that the old name was hiding a pairing. A full re-align
    /// would find it too, but would also discard every hand correction already made — so this
    /// re-checks exactly the one team that changed and leaves the rest of the screen alone.
    ///
    /// Reuses the same scoring path as align, so a match found here is scored identically to one
    /// found there. No writes: the caller saves it with everything else.
    /// </summary>
    [HttpPost("reassess-team")]
    public async Task<ActionResult<ReassessTeamResultDto>> ReassessTeam(
        [FromBody] ReassessTeamRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var gate = await RejectIfNotTournamentAsync(jobId.Value, ct);
        if (gate is not null) return gate;

        // Re-read the team from the database rather than trusting a name in the payload: this runs
        // right after a rename, and the whole point is to match against the name that was actually
        // committed. It also scopes the team to this job and age group.
        var teams = await _teamRepo.GetTeamsForRankingsAsync(
            jobId.Value, request.RegisteredTeamAgeGroupId, ct);

        var team = teams.FirstOrDefault(t => t.TeamId == request.TeamId);
        if (team is null)
            return BadRequest(new { message = "That team is not in this event's selected age group." });

        if (request.CandidateRankings.Count == 0)
            return Ok(new ReassessTeamResultDto
            {
                Success = true,
                Message = "No unmatched rankings left to check against.",
                Match = null
            });

        var alignment = _matchingService.AlignRankingsWithTeams(
            request.CandidateRankings, [team], request.ClubWeight, request.TeamWeight);

        var match = alignment.AlignedTeams.FirstOrDefault();

        return Ok(new ReassessTeamResultDto
        {
            Success = true,
            Message = match is null
                ? $"No ranking matched {team.TeamName} above the confidence floor."
                : $"{team.TeamName} now matches #{match.Ranking.Rank} {match.Ranking.Team}.",
            Match = match
        });
    }

    // ── Save / update endpoints ──

    /// <summary>
    /// Persist the rankings the director reviewed on screen.
    ///
    /// This writes the decisions it is handed. It does NOT re-scrape usclublax.com and re-run the
    /// match: that is what the previous version did, and it meant the stored result was the
    /// server's fresh guess rather than the reviewed one -- an un-match never stuck, a hand
    /// correction raced the server, and a save could fail because a third-party site happened to
    /// be down at that moment. Matching is a read; saving is a write; they no longer share a call.
    ///
    /// Omitted teams are left alone and null-Ranking teams are cleared -- see
    /// <see cref="SaveRankingEntry"/> for why absence must not mean "clear".
    /// </summary>
    [HttpPost("save-rankings")]
    public async Task<ActionResult<SaveRankingsResultDto>> SaveRankings(
        [FromBody] SaveRankingsRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var gate = await RejectIfNotTournamentAsync(jobId.Value, ct);
        if (gate is not null) return gate;

        if (request.Teams.Count == 0)
            return Ok(new SaveRankingsResultDto
            {
                Success = true,
                Message = "Nothing to save.",
                UpdatedCount = 0,
                ClearedCount = 0
            });

        // Authoritative team set for this job + age group. Every posted TeamId is checked against
        // it, so a client cannot stamp a team in another job, or in another age group of this one,
        // by editing the payload. Reject the whole batch rather than silently writing the subset
        // that passes -- a partial write here is indistinguishable from a successful one.
        var allowedTeamIds = (await _teamRepo.GetTeamsForRankingsAsync(
                jobId.Value, request.RegisteredTeamAgeGroupId, ct))
            .Select(t => t.TeamId)
            .ToHashSet();

        var unknown = request.Teams.Select(t => t.TeamId).Where(id => !allowedTeamIds.Contains(id)).ToList();
        if (unknown.Count > 0)
            return BadRequest(new
            {
                message = $"{unknown.Count} team(s) do not belong to this event's selected age group."
            });

        var updates = new Dictionary<Guid, string?>();
        foreach (var entry in request.Teams)
        {
            // Last write wins on a duplicated TeamId -- an indexer assignment, not ToDictionary,
            // which would throw on a payload the client can trivially produce.
            updates[entry.TeamId] = entry.Ranking is null
                ? null
                : JsonSerializer.Serialize(entry.Ranking, JsonOpts);
        }

        await _teamRepo.BulkUpdateNationalRankingDataAsync(updates, ct);

        // Counted from the request, not from SaveChangesAsync's row count: re-saving an unchanged
        // stamp is a no-op to EF but is not a failure, and reporting 0 there reads as one.
        var cleared = updates.Values.Count(v => v is null);
        var updated = updates.Count - cleared;

        return Ok(new SaveRankingsResultDto
        {
            Success = true,
            Message = cleared > 0
                ? $"Saved {updated} team ranking(s), cleared {cleared}."
                : $"Saved {updated} team ranking(s).",
            UpdatedCount = updated,
            ClearedCount = cleared
        });
    }

    // The former PUT team-ranking/{teamId} lived here. It existed so a hand match could write
    // itself the instant it was clicked — which meant half a reviewed table was already committed
    // and half was not, and un-matching a row had no way to take that write back. SaveRankings is
    // now the single commit point for every row, hand-matched ones included.

    /// <summary>
    /// Clear all NationalRankingData for teams in the specified age group.
    /// </summary>
    [HttpDelete("team-rankings/{agegroupId:guid}")]
    public async Task<IActionResult> ClearTeamRankings(
        Guid agegroupId,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var gate = await RejectIfNotTournamentAsync(jobId.Value, ct);
        if (gate is not null) return gate;

        var cleared = await _teamRepo.ClearNationalRankingDataForAgegroupAsync(jobId.Value, agegroupId, ct);

        return Ok(new { message = $"Cleared rankings for {cleared} teams.", count = cleared });
    }

    // ── Export endpoint ──

    /// <summary>
    /// Export aligned rankings as a CSV download.
    /// </summary>
    [HttpGet("export-csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] string v,
        [FromQuery] string yr,
        [FromQuery] Guid agegroupId,
        [FromQuery] string alpha = "",
        [FromQuery] int clubWeight = 75,
        [FromQuery] int teamWeight = 25,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(yr))
            return BadRequest(new { message = "Parameters v and yr are required." });

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var gate = await RejectIfNotTournamentAsync(jobId.Value, ct);
        if (gate is not null) return gate;

        var scrapeResult = await _scrapingService.ScrapeRankingsAsync(v, alpha, yr, ct);
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
