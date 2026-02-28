using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Extracts aggregate scheduling attributes (Q1–Q12) from a prior year's schedule,
/// grouped by team count (TCnt). Pure computation — no DI, no DB access.
/// </summary>
public static class AttributeExtractor
{
    /// <summary>
    /// Extract a DivisionSizeProfile for each distinct TCnt found in the source patterns.
    /// </summary>
    /// <param name="patterns">Game placement patterns from prior year (must include RR games only: T1Type=T, T2Type=T).</param>
    /// <param name="sourceDivisions">Division summaries from the prior year (provides TCnt per agegroup+division).</param>
    /// <param name="currentDivisionCountByTCnt">How many current-year divisions have each TCnt (for DivisionCount field).</param>
    /// <param name="sourceTimeslotWindow">Earliest configured field start time per DOW from the source job's timeslot config.
    /// When provided, Q2 computes offset-from-window instead of absolute times only.</param>
    public static Dictionary<int, DivisionSizeProfile> ExtractProfiles(
        List<GamePlacementPattern> patterns,
        List<SourceDivisionSummary> sourceDivisions,
        Dictionary<int, int>? currentDivisionCountByTCnt = null,
        Dictionary<DayOfWeek, TimeSpan>? sourceTimeslotWindow = null)
    {
        // Filter to round-robin games only
        var rrPatterns = patterns
            .Where(p => p.T1Type == "T" && p.T2Type == "T")
            .ToList();

        if (rrPatterns.Count == 0)
            return new Dictionary<int, DivisionSizeProfile>();

        // Build lookup: (AgegroupName, DivName) → TCnt
        var tcntLookup = sourceDivisions
            .ToDictionary(
                d => (d.AgegroupName, d.DivName),
                d => d.TeamCount);

        // Attach TCnt to each pattern
        var patternsWithTCnt = rrPatterns
            .Where(p => tcntLookup.ContainsKey((p.AgegroupName, p.DivName)))
            .Select(p => (Pattern: p, TCnt: tcntLookup[(p.AgegroupName, p.DivName)]))
            .ToList();

        // Group by TCnt and extract profiles
        var result = new Dictionary<int, DivisionSizeProfile>();

        foreach (var group in patternsWithTCnt.GroupBy(x => x.TCnt))
        {
            var tcnt = group.Key;
            var games = group.Select(x => x.Pattern).ToList();
            var divCount = currentDivisionCountByTCnt?.GetValueOrDefault(tcnt, 0) ?? 0;

            result[tcnt] = ExtractSingleProfile(tcnt, divCount, games, sourceTimeslotWindow);
        }

        return result;
    }

    private static DivisionSizeProfile ExtractSingleProfile(
        int tcnt, int divisionCount, List<GamePlacementPattern> games,
        Dictionary<DayOfWeek, TimeSpan>? sourceTimeslotWindow)
    {
        // Q1: Play days
        var playDays = games
            .Select(g => g.DayOfWeek)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // Q2: Time range per day (absolute)
        var timeRangeAbsolute = games
            .GroupBy(g => g.DayOfWeek)
            .ToDictionary(
                g => g.Key,
                g => new TimeRangeDto
                {
                    Start = g.Min(x => x.TimeOfDay),
                    End = g.Max(x => x.TimeOfDay)
                });

        // Q2: Offset from source timeslot window start
        // Captures WHERE within the window this TCnt historically started
        Dictionary<DayOfWeek, TimeSpan>? startOffsetFromWindow = null;
        Dictionary<DayOfWeek, double>? windowUtilization = null;

        if (sourceTimeslotWindow is { Count: > 0 })
        {
            startOffsetFromWindow = new Dictionary<DayOfWeek, TimeSpan>();
            windowUtilization = new Dictionary<DayOfWeek, double>();

            foreach (var (dow, range) in timeRangeAbsolute)
            {
                if (sourceTimeslotWindow.TryGetValue(dow, out var windowStart))
                {
                    // Offset = first game time - window start
                    var offset = range.Start - windowStart;
                    if (offset < TimeSpan.Zero) offset = TimeSpan.Zero; // safety
                    startOffsetFromWindow[dow] = offset;

                    // Utilization = time range used / total window available
                    // Window end is harder to define, so use (last game - window start) / total window span
                    // For now, capture as ratio of game spread to window start offset
                    var gameSpread = range.End - range.Start;
                    var totalFromWindowStart = range.End - windowStart;
                    windowUtilization[dow] = totalFromWindowStart > TimeSpan.Zero
                        ? Math.Round(gameSpread / totalFromWindowStart, 2)
                        : 1.0;
                }
            }
        }

        // Q3: Field band (ordered by game count descending for desirability signal)
        var fieldBand = games
            .GroupBy(g => g.FieldName)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        // Q4a: Round count
        var roundCount = games.Max(g => g.Rnd);

        // Q4b: Game guarantee
        var gameGuarantee = tcnt % 2 == 0 ? roundCount : roundCount - 1;

        // Q5: Placement shape per round (horizontal vs vertical)
        var placementShapePerRound = games
            .GroupBy(g => g.Rnd)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var gameCount = g.Count();
                    var distinctTimeSlots = g.Select(x => x.TimeOfDay).Distinct().Count();
                    // Ratio: 0 = horizontal (1 timeslot for all games), 1 = vertical (each game at different time)
                    var verticalityRatio = gameCount <= 1
                        ? 0.0
                        : (distinctTimeSlots - 1.0) / (gameCount - 1.0);
                    return new RoundShapeDto
                    {
                        GameCount = gameCount,
                        DistinctTimeSlots = distinctTimeSlots,
                        VerticalityRatio = Math.Clamp(verticalityRatio, 0.0, 1.0)
                    };
                });

        // Q6: On-site interval per day (first-to-last game time)
        var onsiteIntervalPerDay = games
            .GroupBy(g => g.DayOfWeek)
            .ToDictionary(
                g => g.Key,
                g => g.Max(x => x.TimeOfDay) - g.Min(x => x.TimeOfDay));

        // Q7: Field desirability
        var fieldDesirability = ExtractFieldDesirability(games, fieldBand);

        // Q8: Rounds per day
        var roundsPerDay = games
            .GroupBy(g => g.DayOfWeek)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Rnd).Distinct().Count());

        // Q9: Extra round day (for odd TCnt)
        DayOfWeek? extraRoundDay = null;
        if (tcnt % 2 != 0 && roundsPerDay.Count > 1)
        {
            // The day with the most rounds is where the extra round lands
            var maxRounds = roundsPerDay.Values.Max();
            var daysWithMax = roundsPerDay
                .Where(kv => kv.Value == maxRounds)
                .Select(kv => kv.Key)
                .ToList();

            // If one day clearly has more rounds, that's the extra round day
            if (daysWithMax.Count == 1)
                extraRoundDay = daysWithMax[0];
        }

        // Q10: Inter-round interval (median gap between consecutive round start times on same day)
        var interRoundInterval = ExtractInterRoundInterval(games);

        // Q11: Median team span per day (first-to-last game per team per day)
        var medianTeamSpan = ExtractMedianTeamSpan(games);

        // ── Tick-based properties (V2.1) ──

        // GSI: infer from most common interval between consecutive games on same field+day
        var gsiMinutes = InferGsiMinutes(games);

        // Round layout: derive from Q5 — median VerticalityRatio across rounds
        var roundLayout = DeriveRoundLayout(placementShapePerRound);

        // Start tick offset: convert Q2 TimeSpan offsets to GSI ticks
        Dictionary<DayOfWeek, int>? startTickOffset = null;
        if (startOffsetFromWindow is { Count: > 0 } && gsiMinutes > 0)
        {
            startTickOffset = startOffsetFromWindow.ToDictionary(
                kv => kv.Key,
                kv => (int)Math.Round(kv.Value.TotalMinutes / gsiMinutes));
        }

        // Inter-round gap: convert Q10 to ticks
        var interRoundGapTicks = gsiMinutes > 0
            ? (int)Math.Round(interRoundInterval.TotalMinutes / gsiMinutes)
            : 1;

        // Q12: Minimum team gap in GSI ticks
        var minTeamGapTicks = ExtractMinTeamGapTicks(games, gsiMinutes);

        // Field fairness: derive from Q7 — check if any field has usage ratio > 1.5x
        var fieldFairness = DeriveFieldFairness(fieldDesirability);

        return new DivisionSizeProfile
        {
            TCnt = tcnt,
            DivisionCount = divisionCount,
            PlayDays = playDays,
            StartOffsetFromWindow = startOffsetFromWindow,
            TimeRangeAbsolute = timeRangeAbsolute,
            WindowUtilization = windowUtilization,
            FieldBand = fieldBand,
            RoundCount = roundCount,
            GameGuarantee = gameGuarantee,
            PlacementShapePerRound = placementShapePerRound,
            OnsiteIntervalPerDay = onsiteIntervalPerDay,
            FieldDesirability = fieldDesirability,
            RoundsPerDay = roundsPerDay,
            ExtraRoundDay = extraRoundDay,
            InterRoundInterval = interRoundInterval,
            MedianTeamSpan = medianTeamSpan,
            // Tick-based properties (V2.1)
            GsiMinutes = gsiMinutes,
            RoundLayout = roundLayout,
            StartTickOffset = startTickOffset,
            InterRoundGapTicks = interRoundGapTicks,
            MinTeamGapTicks = minTeamGapTicks,
            FieldFairness = fieldFairness
        };
    }

    /// <summary>
    /// Q7: Per-field usage profile — game count, usage ratio, and max team repeat count.
    /// </summary>
    private static Dictionary<string, FieldUsageDto> ExtractFieldDesirability(
        List<GamePlacementPattern> games, List<string> fieldBand)
    {
        var fieldGameCounts = games
            .GroupBy(g => g.FieldName)
            .ToDictionary(g => g.Key, g => g.Count());

        var avgGamesPerField = fieldBand.Count > 0
            ? (double)games.Count / fieldBand.Count
            : 0.0;

        // Track per-team per-field game counts for max repeat
        // Team numbers are scoped per division, so key by (AgegroupName, DivName, TeamNo, FieldName)
        var teamFieldCounts = new Dictionary<(string ag, string div, int teamNo, string field), int>();

        foreach (var game in games)
        {
            if (game.T1No.HasValue)
            {
                var key1 = (game.AgegroupName, game.DivName, game.T1No.Value, game.FieldName);
                teamFieldCounts[key1] = teamFieldCounts.GetValueOrDefault(key1) + 1;
            }
            if (game.T2No.HasValue)
            {
                var key2 = (game.AgegroupName, game.DivName, game.T2No.Value, game.FieldName);
                teamFieldCounts[key2] = teamFieldCounts.GetValueOrDefault(key2) + 1;
            }
        }

        var result = new Dictionary<string, FieldUsageDto>();

        foreach (var field in fieldBand)
        {
            var gameCount = fieldGameCounts.GetValueOrDefault(field, 0);
            var usageRatio = avgGamesPerField > 0 ? gameCount / avgGamesPerField : 1.0;

            // Max times any team played on this field
            var maxRepeat = teamFieldCounts
                .Where(kv => kv.Key.field == field)
                .Select(kv => kv.Value)
                .DefaultIfEmpty(0)
                .Max();

            result[field] = new FieldUsageDto
            {
                FieldName = field,
                GameCount = gameCount,
                UsageRatio = Math.Round(usageRatio, 2),
                MaxTeamRepeatCount = maxRepeat
            };
        }

        return result;
    }

    /// <summary>
    /// Q11: Median team span — minutes from a team's first to last game on a given day.
    /// Unpivots both T1 and T2, groups by (division, team, day), computes per-team-day span,
    /// then returns the median across all team-days with 2+ games.
    /// </summary>
    private static TimeSpan? ExtractMedianTeamSpan(List<GamePlacementPattern> games)
    {
        // Unpivot: each game produces entries for both teams
        var teamDayGames = new Dictionary<(string ag, string div, int teamNo, DayOfWeek day), List<TimeSpan>>();

        foreach (var g in games)
        {
            if (g.T1No.HasValue)
            {
                var key = (g.AgegroupName, g.DivName, g.T1No.Value, g.DayOfWeek);
                if (!teamDayGames.TryGetValue(key, out var list1))
                    teamDayGames[key] = list1 = [];
                list1.Add(g.TimeOfDay);
            }
            if (g.T2No.HasValue)
            {
                var key = (g.AgegroupName, g.DivName, g.T2No.Value, g.DayOfWeek);
                if (!teamDayGames.TryGetValue(key, out var list2))
                    teamDayGames[key] = list2 = [];
                list2.Add(g.TimeOfDay);
            }
        }

        // Compute span for each team-day with 2+ games
        var spans = teamDayGames.Values
            .Where(times => times.Count >= 2)
            .Select(times => times.Max() - times.Min())
            .OrderBy(s => s)
            .ToList();

        if (spans.Count == 0)
            return null;

        // Median
        var mid = spans.Count / 2;
        return spans.Count % 2 == 0
            ? TimeSpan.FromTicks((spans[mid - 1].Ticks + spans[mid].Ticks) / 2)
            : spans[mid];
    }

    /// <summary>
    /// Q10: Median time gap between consecutive round start times on the same day.
    /// </summary>
    private static TimeSpan ExtractInterRoundInterval(List<GamePlacementPattern> games)
    {
        // For each day, find the earliest game time per round, then compute gaps between consecutive rounds
        var roundStartsByDay = games
            .GroupBy(g => g.DayOfWeek)
            .SelectMany(dayGroup =>
            {
                var roundStarts = dayGroup
                    .GroupBy(g => g.Rnd)
                    .Select(rg => (Round: rg.Key, StartTime: rg.Min(x => x.TimeOfDay)))
                    .OrderBy(x => x.StartTime)
                    .ToList();

                var gaps = new List<TimeSpan>();
                for (var i = 1; i < roundStarts.Count; i++)
                {
                    var gap = roundStarts[i].StartTime - roundStarts[i - 1].StartTime;
                    if (gap > TimeSpan.Zero)
                        gaps.Add(gap);
                }

                return gaps;
            })
            .OrderBy(g => g)
            .ToList();

        if (roundStartsByDay.Count == 0)
            return TimeSpan.FromMinutes(75); // Sensible default

        // Median
        var midIndex = roundStartsByDay.Count / 2;
        return roundStartsByDay.Count % 2 == 0
            ? TimeSpan.FromTicks((roundStartsByDay[midIndex - 1].Ticks + roundStartsByDay[midIndex].Ticks) / 2)
            : roundStartsByDay[midIndex];
    }

    // ══════════════════════════════════════════════════════════
    // V2.1 — Tick-based property derivations
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Infer GSI (Game Start Interval) in minutes from source game patterns.
    /// Groups games by (field, day), sorts by time, computes the mode of consecutive intervals.
    /// </summary>
    private static int InferGsiMinutes(List<GamePlacementPattern> games)
    {
        var intervals = games
            .GroupBy(g => (g.FieldName, g.DayOfWeek))
            .SelectMany(fieldDay =>
            {
                var sorted = fieldDay.OrderBy(g => g.TimeOfDay).ToList();
                var gaps = new List<int>();
                for (var i = 1; i < sorted.Count; i++)
                {
                    var gapMinutes = (int)Math.Round((sorted[i].TimeOfDay - sorted[i - 1].TimeOfDay).TotalMinutes);
                    if (gapMinutes > 0)
                        gaps.Add(gapMinutes);
                }
                return gaps;
            })
            .ToList();

        if (intervals.Count == 0)
            return 60; // Sensible default

        // Mode (most common interval) = the GSI
        return intervals
            .GroupBy(i => i)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;
    }

    /// <summary>
    /// Derive binary round layout from Q5 PlacementShapePerRound.
    /// If the median VerticalityRatio across rounds is below 0.3, the source used horizontal layout.
    /// </summary>
    private static RoundLayout DeriveRoundLayout(Dictionary<int, RoundShapeDto> shapePerRound)
    {
        if (shapePerRound.Count == 0)
            return RoundLayout.Sequential;

        var ratios = shapePerRound.Values
            .Select(s => s.VerticalityRatio)
            .OrderBy(r => r)
            .ToList();

        var median = ratios[ratios.Count / 2];
        return median < 0.3 ? RoundLayout.Horizontal : RoundLayout.Sequential;
    }

    /// <summary>
    /// Q12: Minimum team gap in GSI ticks — smallest gap between any team's consecutive games on a day.
    /// Subsumes BTB detection: 1 tick = BTBs existed, 2+ = no BTBs.
    /// </summary>
    private static int ExtractMinTeamGapTicks(List<GamePlacementPattern> games, int gsiMinutes)
    {
        if (gsiMinutes <= 0)
            return 2; // Safe default: no BTBs

        // Unpivot: each game produces entries for both teams
        var teamDayGames = new Dictionary<(string ag, string div, int teamNo, DayOfWeek day), List<TimeSpan>>();

        foreach (var g in games)
        {
            if (g.T1No.HasValue)
            {
                var key = (g.AgegroupName, g.DivName, g.T1No.Value, g.DayOfWeek);
                if (!teamDayGames.TryGetValue(key, out var list1))
                    teamDayGames[key] = list1 = [];
                list1.Add(g.TimeOfDay);
            }
            if (g.T2No.HasValue)
            {
                var key = (g.AgegroupName, g.DivName, g.T2No.Value, g.DayOfWeek);
                if (!teamDayGames.TryGetValue(key, out var list2))
                    teamDayGames[key] = list2 = [];
                list2.Add(g.TimeOfDay);
            }
        }

        // For each team-day with 2+ games, compute min consecutive gap in ticks
        var minGapTicks = int.MaxValue;

        foreach (var times in teamDayGames.Values)
        {
            if (times.Count < 2) continue;

            var sorted = times.OrderBy(t => t).ToList();
            for (var i = 1; i < sorted.Count; i++)
            {
                var gapMinutes = (sorted[i] - sorted[i - 1]).TotalMinutes;
                var gapTicks = (int)Math.Round(gapMinutes / gsiMinutes);
                if (gapTicks > 0 && gapTicks < minGapTicks)
                    minGapTicks = gapTicks;
            }
        }

        return minGapTicks == int.MaxValue ? 2 : minGapTicks;
    }

    /// <summary>
    /// Derive field fairness from Q7 FieldDesirability.
    /// Democratic if all fields have usage ratio between 0.5 and 1.5; Biased otherwise.
    /// </summary>
    private static FieldFairness DeriveFieldFairness(Dictionary<string, FieldUsageDto> fieldDesirability)
    {
        if (fieldDesirability.Count <= 1)
            return FieldFairness.Democratic;

        var maxRatio = fieldDesirability.Values.Max(f => f.UsageRatio);
        var minRatio = fieldDesirability.Values.Min(f => f.UsageRatio);

        // If the spread between most-used and least-used field is > 2x, it's biased
        return maxRatio > 1.5 || minRatio < 0.5
            ? FieldFairness.Biased
            : FieldFairness.Democratic;
    }
}
