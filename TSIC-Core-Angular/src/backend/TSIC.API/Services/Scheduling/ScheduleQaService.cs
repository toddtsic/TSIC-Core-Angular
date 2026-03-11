using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Centralized schedule QA validation.
/// Delegates to AutoBuildRepository for data queries.
/// Cross-event analysis fires automatically when the job belongs to a comparison group.
/// </summary>
public sealed class ScheduleQaService : IScheduleQaService
{
    private readonly IAutoBuildRepository _repo;

    public ScheduleQaService(IAutoBuildRepository repo)
    {
        _repo = repo;
    }

    // ══════════════════════════════════════════════════════════
    // Cross-Event Comparison Group Config
    // ══════════════════════════════════════════════════════════
    // Event NAMES are stable across years. When a job's name contains
    // any pattern in a group, all jobs matching that group's patterns
    // (that have scheduled games) are compared for overplay.
    //
    // Add new groups or patterns here — no schema changes needed.
    // ══════════════════════════════════════════════════════════

    private static readonly ComparisonGroup[] ComparisonGroups =
    [
        new("Girls Summer",
        [
            new("LIVE LOVE LAX", "LLL", 1),
            new("LAX FOR THE CURE", "LFTC", 2),
            new("G8", "G8", 3),
            new("MARYLAND CUP", "MDCup", 4),
            new("LAX BY THE SEA", "LBTS", 5),
        ]),
        // Future: new("Boys Summer", [ ... ]),
    ];

    private record ComparisonGroup(string Name, ComparisonEvent[] Events);
    private record ComparisonEvent(string NamePattern, string Abbreviation, int SortRank);

    public async Task<AutoBuildQaResult> RunValidationAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // Run standard single-job QA
        var result = await _repo.RunQaValidationAsync(jobId, ct);

        // Check if this job belongs to a cross-event comparison group
        var crossEvent = await TryBuildCrossEventAnalysisAsync(jobId, ct);

        if (crossEvent != null)
        {
            // Return a new result with the cross-event data attached
            result = result with { CrossEventAnalysis = crossEvent };
        }

        return result;
    }

    private async Task<CrossEventQaResult?> TryBuildCrossEventAnalysisAsync(
        Guid jobId, CancellationToken ct)
    {
        // Resolve this job's name and year
        var jobName = await _repo.GetJobNameAsync(jobId, ct);
        if (string.IsNullOrEmpty(jobName)) return null;

        var jobYear = await _repo.GetJobYearAsync(jobId, ct);
        if (string.IsNullOrEmpty(jobYear)) return null;

        // Find which comparison group this job belongs to
        var group = ComparisonGroups.FirstOrDefault(g =>
            g.Events.Any(e => jobName.Contains(e.NamePattern, StringComparison.OrdinalIgnoreCase)));
        if (group == null) return null;

        // Find all jobs matching any pattern in this group for the same year (that have games)
        var namePatterns = group.Events.Select(e => e.NamePattern).ToList();
        var matchedJobs = await _repo.FindJobsByNamePatternsAsync(namePatterns, jobYear, ct);

        if (matchedJobs.Count < 2) return null; // Need at least 2 events to compare

        // Build abbreviation lookup: for each matched job, find which pattern it matches
        var jobAbbreviations = new Dictionary<Guid, string>();
        foreach (var (mJobId, mJobName) in matchedJobs)
        {
            var evt = group.Events.FirstOrDefault(e =>
                mJobName.Contains(e.NamePattern, StringComparison.OrdinalIgnoreCase));
            jobAbbreviations[mJobId] = evt?.Abbreviation ?? "?";
        }

        // Get all cross-event matchup data
        var jobIds = matchedJobs.Select(j => j.JobId).ToList();
        var matchups = await _repo.GetCrossEventMatchupsAsync(jobIds, ct);

        // Build event info list
        var eventInfos = matchedJobs
            .Select(j => new CrossEventJobInfo
            {
                Abbreviation = jobAbbreviations.GetValueOrDefault(j.JobId, "?"),
                JobName = j.JobName,
                GameCount = matchups.Count(m => m.JobId == j.JobId) / 2 // /2 because each game = 2 records
            })
            .OrderBy(e =>
            {
                var evt = group.Events.FirstOrDefault(ev =>
                    e.JobName.Contains(ev.NamePattern, StringComparison.OrdinalIgnoreCase));
                return evt?.SortRank ?? 99;
            })
            .ToList();

        // ── Club Overplay: Club A vs Club B teams play each other >1x across events ──
        var clubOverplay = matchups
            .GroupBy(m => new { m.Agegroup, m.TeamClub, m.OpponentClub })
            .Where(g => g.Select(m => m.JobId).Distinct().Count() > 1
                        && string.Compare(g.Key.TeamClub, g.Key.OpponentClub, StringComparison.Ordinal) < 0)
            .Select(g => new CrossEventClubOverplay
            {
                Agegroup = g.Key.Agegroup,
                TeamClub = g.Key.TeamClub,
                OpponentClub = g.Key.OpponentClub,
                MatchCount = g.Count()
            })
            .OrderBy(c => c.Agegroup)
            .ThenByDescending(c => c.MatchCount)
            .ThenBy(c => c.TeamClub)
            .ToList();

        // ── Team Overplay: specific Team X vs Team Y play >1x across events ──
        var teamOverplay = matchups
            .Where(m => string.Compare(
                $"{m.TeamClub}:{m.TeamName}", $"{m.OpponentClub}:{m.OpponentName}",
                StringComparison.Ordinal) < 0) // normalize direction
            .GroupBy(m => new { m.Agegroup, m.TeamClub, m.TeamName, m.OpponentClub, m.OpponentName })
            .Where(g => g.Count() > 1)
            .Select(g => new CrossEventTeamOverplay
            {
                Agegroup = g.Key.Agegroup,
                TeamClub = g.Key.TeamClub,
                TeamName = g.Key.TeamName,
                OpponentClub = g.Key.OpponentClub,
                OpponentName = g.Key.OpponentName,
                MatchCount = g.Count(),
                Events = string.Join(", ", g
                    .Select(m => jobAbbreviations.GetValueOrDefault(m.JobId, "?"))
                    .OrderBy(e => e)
                    .Distinct())
            })
            .OrderBy(t => t.Agegroup)
            .ThenByDescending(t => t.MatchCount)
            .ThenBy(t => t.TeamClub)
            .ThenBy(t => t.TeamName)
            .ToList();

        return new CrossEventQaResult
        {
            GroupName = group.Name,
            ComparedEvents = eventInfos,
            ClubOverplay = clubOverplay,
            TeamOverplay = teamOverplay
        };
    }
}
