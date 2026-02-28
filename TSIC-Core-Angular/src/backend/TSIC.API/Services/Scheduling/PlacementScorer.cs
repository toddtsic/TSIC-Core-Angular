using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// V2.1 placement engine: distance-from-source scoring.
///
/// Two hard filters:
///   1. Occupied slot (can't double-book)
///   2. Team span (prefer candidates within source median span; fall back only when
///      no span-safe slot exists — this guarantees tight clustering by construction)
///
/// Soft penalties (tiebreakers among span-safe candidates, in priority order):
///   1. Wrong day — not a source play day, or not the round's target day
///   2. Min team gap violation — creating a gap smaller than source observed
///   3. Target time distance — ticks away from expected time for this game
///   4. Round layout mismatch — horizontal round but different time from round target
///   5. Field distribution imbalance — team overusing a field vs their least-used
/// </summary>
public static class PlacementScorer
{
    // ── Penalty weights (used as tiebreakers within span tier) ──
    // Span is enforced as a hard preference (tier system), not via weights.
    // These weights rank the remaining soft penalties.
    private const int PenaltyWrongDay = 10_000;
    private const int PenaltyWrongTargetDay = 500;
    private const int PenaltyTeamSpanPerTick = 2_000;
    private const int PenaltyTeamGapPerTick = 1_000;
    private const int PenaltyTargetTimePerTick = 100;
    private const int PenaltyLayoutMismatch = 200;
    private const int PenaltyFieldImbalance = 50;
    private const int PenaltyAvoidField = 300;

    /// <summary>
    /// Find the best slot for a game. Two-tier selection:
    ///   Tier 1 — span-safe candidates (within source MedianTeamSpan). Always preferred.
    ///   Tier 2 — span-violating candidates. Used only when no span-safe slot exists.
    /// Within each tier, lowest total penalty wins.
    /// </summary>
    public static ScoredCandidate? FindBestSlot(
        List<CandidateSlot> candidates,
        GameContext game,
        DivisionSizeProfile profile,
        PlacementState state)
    {
        if (candidates.Count == 0)
            return null;

        ScoredCandidate? best = null;
        var bestPenalty = int.MaxValue;
        var bestIsSpanSafe = false;

        foreach (var candidate in candidates)
        {
            // ═══ Hard filter: occupied slot ═══
            if (state.OccupiedSlots.Contains((candidate.FieldId, candidate.GDate)))
                continue;

            // ═══ Hard filter: BTB / min team gap ═══
            // Round-by-round placement always has room further down the canvas,
            // so there's no reason to accept a slot that creates a BTB.
            if (profile.MinTeamGapTicks > 0 && profile.GsiMinutes > 0
                && IsGapViolation(candidate, game, profile, state))
                continue;

            var penalty = 0;
            var breakdown = new Dictionary<string, int>();

            // ── P1: Play Day ──
            var dayPenalty = ScorePlayDay(candidate, game, profile);
            if (dayPenalty > 0)
            {
                penalty += dayPenalty;
                breakdown["wrong-day"] = dayPenalty;
            }

            // ── Span check (hard preference, not a weighted penalty) ──
            var spanPenalty = ScoreTeamSpan(candidate, game, profile, state);
            var isSpanSafe = spanPenalty == 0;
            if (spanPenalty > 0)
            {
                penalty += spanPenalty;
                breakdown["team-span"] = spanPenalty;
            }

            // ── P3: Target Time Distance ──
            var timePenalty = ScoreTargetTime(candidate, game, profile, state);
            if (timePenalty > 0)
            {
                penalty += timePenalty;
                breakdown["target-time"] = timePenalty;
            }

            // ── P4: Round Layout Match ──
            var layoutPenalty = ScoreRoundLayout(candidate, game, profile, state);
            if (layoutPenalty > 0)
            {
                penalty += layoutPenalty;
                breakdown["round-layout"] = layoutPenalty;
            }

            // ── P5: Field Distribution Fairness ──
            var fieldPenalty = ScoreFieldDistribution(candidate, game, state);
            if (fieldPenalty > 0)
            {
                penalty += fieldPenalty;
                breakdown["field-balance"] = fieldPenalty;
            }

            // ── P6: Field Preference (Avoid flag) ──
            var prefPenalty = ScoreFieldPreference(candidate, state);
            if (prefPenalty > 0)
            {
                penalty += prefPenalty;
                breakdown["field-avoid"] = prefPenalty;
            }

            // ── Candidate ranking ──
            // Span-safe ALWAYS beats span-violating, regardless of other penalties.
            // Within same span tier, lowest total penalty wins.
            var isBetter = best == null
                || (isSpanSafe && !bestIsSpanSafe)
                || (isSpanSafe == bestIsSpanSafe && penalty < bestPenalty);

            if (isBetter)
            {
                bestPenalty = penalty;
                bestIsSpanSafe = isSpanSafe;
                best = new ScoredCandidate
                {
                    Slot = candidate,
                    TotalPenalty = penalty,
                    PenaltyBreakdown = breakdown
                };

                // Short-circuit: span-safe with zero penalty = perfect source match
                if (isSpanSafe && penalty == 0)
                    return best;
            }
        }

        return best;
    }

    // ══════════════════════════════════════════════════════════
    // Per-property penalty computations
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// P1: Play day match.
    /// 0 if candidate is on the round's target day.
    /// Small penalty if on a valid play day but not the target.
    /// Large penalty if not a source play day at all.
    /// </summary>
    private static int ScorePlayDay(CandidateSlot candidate, GameContext game, DivisionSizeProfile profile)
    {
        var dow = candidate.GDate.DayOfWeek;

        if (game.TargetDay.HasValue)
        {
            if (dow == game.TargetDay.Value)
                return 0;
            // On a play day but not the target day for this round
            if (profile.PlayDays.Contains(dow))
                return PenaltyWrongTargetDay;
            // Not a play day at all
            return PenaltyWrongDay;
        }

        // No target day set — just check play days
        if (profile.PlayDays.Count > 0 && !profile.PlayDays.Contains(dow))
            return PenaltyWrongDay;

        return 0;
    }

    /// <summary>
    /// P2: Team span — penalize if placing here stretches a team's first-to-last game span
    /// beyond the source's median team span. The engine extracted MedianTeamSpan (Q11) from
    /// the source but previously never used it. This is the most impactful penalty for
    /// tournament quality — parents care most about total wait time at the field.
    /// Checks both teams; takes the worst (largest excess).
    /// </summary>
    private static int ScoreTeamSpan(
        CandidateSlot candidate, GameContext game, DivisionSizeProfile profile, PlacementState state)
    {
        if (!profile.MedianTeamSpan.HasValue || profile.GsiMinutes <= 0)
            return 0;

        var gameDay = candidate.GDate.Date;
        var gameTime = candidate.GDate.TimeOfDay;
        var maxSpanMinutes = profile.MedianTeamSpan.Value.TotalMinutes;

        var span1 = ComputeSpanExcessTicks(state, game.DivId, game.T1No, gameDay, gameTime, maxSpanMinutes, profile.GsiMinutes);
        var span2 = ComputeSpanExcessTicks(state, game.DivId, game.T2No, gameDay, gameTime, maxSpanMinutes, profile.GsiMinutes);

        return Math.Max(span1, span2) * PenaltyTeamSpanPerTick;
    }

    /// <summary>
    /// Compute how many GSI ticks the team's span would EXCEED the source median
    /// if this game were placed at the given time. Returns 0 if within acceptable span.
    /// </summary>
    private static int ComputeSpanExcessTicks(
        PlacementState state, Guid divId, int teamNo, DateTime gameDay, TimeSpan gameTime,
        double maxSpanMinutes, int gsiMinutes)
    {
        if (teamNo <= 0) return 0;

        var key = (divId, teamNo, gameDay);
        if (!state.TeamDayGameTimes.TryGetValue(key, out var times) || times.Count == 0)
            return 0; // First game on this day — no span concern

        // What would the span be if we add this game?
        var earliest = times[0];
        var latest = times[0];
        for (var i = 1; i < times.Count; i++)
        {
            if (times[i] < earliest) earliest = times[i];
            if (times[i] > latest) latest = times[i];
        }

        var newEarliest = gameTime < earliest ? gameTime : earliest;
        var newLatest = gameTime > latest ? gameTime : latest;
        var newSpanMinutes = (newLatest - newEarliest).TotalMinutes;

        if (newSpanMinutes <= maxSpanMinutes)
            return 0;

        return (int)Math.Ceiling((newSpanMinutes - maxSpanMinutes) / gsiMinutes);
    }

    /// <summary>
    /// P3: Min team gap — penalize if placing here creates a gap smaller than source observed.
    /// Checks both teams; takes the worst (smallest) gap.
    /// </summary>
    private static int ScoreMinTeamGap(
        CandidateSlot candidate, GameContext game, DivisionSizeProfile profile, PlacementState state)
    {
        if (profile.MinTeamGapTicks <= 0 || profile.GsiMinutes <= 0)
            return 0;

        var gameDay = candidate.GDate.Date;
        var gameTime = candidate.GDate.TimeOfDay;

        var gap1 = ComputeMinGapTicks(state, game.DivId, game.T1No, gameDay, gameTime, profile.GsiMinutes);
        var gap2 = ComputeMinGapTicks(state, game.DivId, game.T2No, gameDay, gameTime, profile.GsiMinutes);
        var worstGap = Math.Min(gap1, gap2);

        if (worstGap < profile.MinTeamGapTicks)
        {
            var deficit = profile.MinTeamGapTicks - worstGap;
            return deficit * PenaltyTeamGapPerTick;
        }

        return 0;
    }

    /// <summary>
    /// P4: Target time distance — ticks away from the expected time for this game.
    /// Uses game.TargetTime (computed by the placement loop from offset + inter-round gap).
    /// </summary>
    private static int ScoreTargetTime(
        CandidateSlot candidate, GameContext game, DivisionSizeProfile profile, PlacementState state)
    {
        if (!game.TargetTime.HasValue || profile.GsiMinutes <= 0)
            return 0;

        var actualMinutes = candidate.GDate.TimeOfDay.TotalMinutes;
        var targetMinutes = game.TargetTime.Value.TotalMinutes;
        var tickDistance = (int)Math.Round(Math.Abs(actualMinutes - targetMinutes) / profile.GsiMinutes);

        return tickDistance * PenaltyTargetTimePerTick;
    }

    /// <summary>
    /// P5: Round layout — for horizontal rounds, all games must match the round's target time.
    /// For sequential rounds, no additional penalty (games are expected at different times).
    /// </summary>
    private static int ScoreRoundLayout(
        CandidateSlot candidate, GameContext game, DivisionSizeProfile profile, PlacementState state)
    {
        if (profile.RoundLayout != RoundLayout.Horizontal)
            return 0; // Sequential — no layout penalty

        // For horizontal rounds, check if this candidate matches the round's established time
        var roundKey = (game.DivId, game.Round);
        if (!state.RoundTargetTimes.TryGetValue(roundKey, out var roundTime))
            return 0; // First game in round — no reference yet

        // Horizontal: all games in round should be at the same time
        if (candidate.GDate.TimeOfDay != roundTime)
            return PenaltyLayoutMismatch;

        return 0;
    }

    /// <summary>
    /// P6: Field preference — penalize "Avoid" fields to push games toward Normal/Preferred fields.
    /// Preferred fields get no bonus — they're simply "not penalized" while Avoid fields are
    /// heavily penalized, naturally pushing teams toward Preferred/Normal fields.
    /// Applied per team (both teams get the penalty).
    /// </summary>
    private static int ScoreFieldPreference(CandidateSlot candidate, PlacementState state)
    {
        if (state.FieldPreferences.Count == 0)
            return 0;

        if (state.FieldPreferences.TryGetValue(candidate.FieldId, out var pref) && pref == 2)
            return PenaltyAvoidField;

        return 0;
    }

    /// <summary>
    /// P6: Field distribution — penalize if placing here means either team has
    /// played on this field more than their least-used field. Promotes even rotation.
    /// Only applies when source used democratic distribution.
    /// </summary>
    private static int ScoreFieldDistribution(
        CandidateSlot candidate, GameContext game, PlacementState state)
    {
        var penalty = 0;

        if (IsTeamFieldImbalanced(state, game.DivId, game.T1No, candidate.FieldName))
            penalty += PenaltyFieldImbalance;

        if (IsTeamFieldImbalanced(state, game.DivId, game.T2No, candidate.FieldName))
            penalty += PenaltyFieldImbalance;

        return penalty;
    }

    // ══════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Compute the minimum gap in GSI ticks between the candidate time and the team's
    /// nearest existing game on the same day. Returns int.MaxValue if no existing games.
    /// </summary>
    private static int ComputeMinGapTicks(
        PlacementState state, Guid divId, int teamNo, DateTime gameDay, TimeSpan gameTime, int gsiMinutes)
    {
        if (teamNo <= 0) return int.MaxValue;

        var key = (divId, teamNo, gameDay);
        if (!state.TeamDayGameTimes.TryGetValue(key, out var times) || times.Count == 0)
            return int.MaxValue; // No existing games — infinite gap

        var minGapMinutes = double.MaxValue;
        foreach (var t in times)
        {
            var gap = Math.Abs((gameTime - t).TotalMinutes);
            if (gap < minGapMinutes)
                minGapMinutes = gap;
        }

        return gsiMinutes > 0
            ? (int)Math.Round(minGapMinutes / gsiMinutes)
            : (int)minGapMinutes;
    }

    /// <summary>
    /// Check if a team has used this field more times than their least-used field.
    /// </summary>
    private static bool IsTeamFieldImbalanced(PlacementState state, Guid divId, int teamNo, string fieldName)
    {
        if (teamNo <= 0) return false;

        var fieldCount = state.TeamFieldCounts.GetValueOrDefault((divId, teamNo, fieldName));

        // Find team's least-used field count
        var minCount = int.MaxValue;
        var found = false;
        foreach (var (key, count) in state.TeamFieldCounts)
        {
            if (key.DivId == divId && key.TeamNo == teamNo)
            {
                found = true;
                if (count < minCount) minCount = count;
            }
        }

        var minFieldCount = found ? minCount : 0;
        return fieldCount > minFieldCount;
    }

    /// <summary>
    /// Hard filter: would placing this game here create a gap smaller than
    /// MinTeamGapTicks for either team? If so, skip this slot — there's
    /// always room further down the canvas with round-by-round placement.
    /// </summary>
    private static bool IsGapViolation(
        CandidateSlot candidate, GameContext game, DivisionSizeProfile profile, PlacementState state)
    {
        var gameDay = candidate.GDate.Date;
        var gameTime = candidate.GDate.TimeOfDay;

        var gap1 = ComputeMinGapTicks(state, game.DivId, game.T1No, gameDay, gameTime, profile.GsiMinutes);
        var gap2 = ComputeMinGapTicks(state, game.DivId, game.T2No, gameDay, gameTime, profile.GsiMinutes);

        return gap1 < profile.MinTeamGapTicks || gap2 < profile.MinTeamGapTicks;
    }
}
