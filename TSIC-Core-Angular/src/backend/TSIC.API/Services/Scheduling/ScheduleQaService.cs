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
    private readonly IBracketRepository _brackets;

    public ScheduleQaService(IAutoBuildRepository repo, IBracketRepository brackets)
    {
        _repo = repo;
        _brackets = brackets;
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

    private sealed record ComparisonGroup(string Name, ComparisonEvent[] Events);
    private sealed record ComparisonEvent(string NamePattern, string Abbreviation, int SortRank);

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

        // Structural bracket QA (null when the job has no materialized brackets)
        var bracketQa = await BuildBracketQaAsync(jobId, ct);
        if (bracketQa != null)
        {
            result = result with { BracketQa = bracketQa };
        }

        return result;
    }

    /// <summary>
    /// Validates each materialized bracket instance against its template: placed
    /// games map to template slots, all non-optional slots are placed, seeds cover
    /// every leaf, feeds are intact and time-ordered, and seed ranks are in range.
    /// Strategy-agnostic — iterates the instance's Template, not a fixed SE ladder.
    /// </summary>
    private async Task<BracketQaResult?> BuildBracketQaAsync(Guid jobId, CancellationToken ct)
    {
        var instances = await _brackets.GetInstanceInfosForJobAsync(jobId, ct);
        if (instances.Count == 0) return null;

        var findings = new List<BracketQaFinding>();
        var teamCountByDiv = new Dictionary<Guid, int>();

        foreach (var inst in instances)
        {
            var games = await _brackets.GetTemplateGamesAsync(inst.TemplateId, ct);
            var routes = await _brackets.GetTemplateRoutesAsync(inst.TemplateId, ct);
            var placed = await _brackets.GetPlacedBracketGamesAsync(
                jobId, inst.AgegroupId, inst.DivId, ct);

            var minLabels = BracketTemplateTopology.ComputeMinLabels(games, routes);

            // Template game keyed by (RoundType, min-label) — the placed-row match key.
            var templateByKey = games.ToDictionary(
                g => (g.RoundType, minLabels[g.TemplateGameId]), g => g);
            var placedByKey = placed
                .GroupBy(p => (p.RoundType, p.MinLabel))
                .ToDictionary(g => g.Key, g => g.ToList());

            BracketQaFinding Finding(string sev, string cat, int? gid, string detail) => new()
            {
                Severity = sev,
                Category = cat,
                AgegroupName = inst.AgegroupName,
                DivName = inst.DivName,
                Gid = gid,
                Detail = detail
            };

            // (1) Orphan placed games — no template slot / duplicate placements.
            foreach (var p in placed)
            {
                if (!templateByKey.ContainsKey((p.RoundType, p.MinLabel)))
                {
                    findings.Add(Finding("error", "OrphanGame", p.Gid,
                        $"Placed {p.RoundType} game (slot label {p.MinLabel}) matches no {inst.StrategyCode} template slot."));
                }
            }
            foreach (var dup in placedByKey.Where(kv => kv.Value.Count > 1))
            {
                findings.Add(Finding("error", "OrphanGame", dup.Value[0].Gid,
                    $"{dup.Value.Count} placed {dup.Key.RoundType} games share slot label {dup.Key.MinLabel} (should be one)."));
            }

            // (2) Completeness — every non-optional template game must be placed.
            foreach (var g in games)
            {
                if (g.IsOptional) continue; // bronze may legitimately be absent
                if (!placedByKey.ContainsKey((g.RoundType, minLabels[g.TemplateGameId])))
                {
                    findings.Add(Finding("warning", "Incomplete", null,
                        $"Template {g.RoundType} game (slot label {minLabels[g.TemplateGameId]}) is not placed on the schedule."));
                }
            }

            // (3) Seed coverage — every leaf slot must carry director seed intent. Read from
            //     Leagues.BracketSeeds (the seed source of truth), same projection the resolver
            //     uses, so QA and resolution cannot disagree on what a seeded slot is.
            var seedSlots = await _brackets.GetSeedSlotsByGidsAsync(
                placed.Select(p => p.Gid).ToList(), ct);
            var seededSlots = seedSlots.Select(s => (s.Gid, (int)s.TargetSlot)).ToHashSet();
            foreach (var p in placed)
            {
                if (!templateByKey.TryGetValue((p.RoundType, p.MinLabel), out var tg)) continue;
                for (var slot = 1; slot <= 2; slot++)
                {
                    var isLeaf = (slot == 1 ? tg.Slot1Seed : tg.Slot2Seed).HasValue;
                    if (isLeaf && !seededSlots.Contains((p.Gid, slot)))
                    {
                        findings.Add(Finding("warning", "SeedCoverage", p.Gid,
                            $"Leaf slot {slot} of {p.RoundType} game {p.Gid} has no seed source."));
                    }
                }
            }

            // (4) Seed-rank validity — rank within the source pool's active team count.
            foreach (var s in seedSlots)
            {
                if (!teamCountByDiv.TryGetValue(s.SeedDivId, out var teamCount))
                {
                    teamCount = await _brackets.GetActiveTeamCountByDivAsync(s.SeedDivId, ct);
                    teamCountByDiv[s.SeedDivId] = teamCount;
                }
                if (s.SeedRank < 1 || s.SeedRank > teamCount)
                {
                    findings.Add(Finding("warning", "SeedRank", s.Gid,
                        $"Seed rank {s.SeedRank} is outside the source pool's 1..{teamCount} team range."));
                }
            }

            // (5) Feeds — no dangling endpoints + target scheduled after its source.
            var feeds = await _brackets.GetFeedsByInstanceAsync(inst.BracketInstanceId, ct);
            var placedGids = placed.Select(p => p.Gid).ToHashSet();
            var gdates = await _brackets.GetGDatesByGidsAsync(placedGids, ct);
            foreach (var f in feeds)
            {
                if (!placedGids.Contains(f.SourceGid) || !placedGids.Contains(f.TargetGid))
                {
                    findings.Add(Finding("error", "FeedIntegrity", f.TargetGid,
                        $"Advancement feed references a game not on the schedule (source {f.SourceGid} → target {f.TargetGid})."));
                    continue;
                }
                if (gdates.TryGetValue(f.SourceGid, out var src) && src.HasValue &&
                    gdates.TryGetValue(f.TargetGid, out var tgt) && tgt.HasValue &&
                    tgt.Value < src.Value)
                {
                    findings.Add(Finding("warning", "TimeOrder", f.TargetGid,
                        $"Playoff game {f.TargetGid} is scheduled before its feeder game {f.SourceGid}."));
                }
            }
        }

        return new BracketQaResult { InstanceCount = instances.Count, Findings = findings };
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
