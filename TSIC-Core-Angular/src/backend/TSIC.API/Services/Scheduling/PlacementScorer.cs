using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Greedy slot finder for chip-stack placement.
///
/// The chip-stack model relies on correct stack ordering
/// (Day→Wave→Round→Agegroup→Division→Game) so that greedy
/// first-valid-slot placement produces a correct schedule.
///
/// Three hard filters:
///   1. Occupied slot (can't double-book)
///   2. Wave time floor (wave 2+ can't bleed into wave 1's window)
///   3. Team time overlap (a team can't play two games simultaneously)
///
/// Four-pass cascade (progressively relaxed):
///   Pass 1: at-or-after target time + skip Avoid fields (ideal)
///   Pass 2: at-or-after target time + any fields (relax field preference)
///   Pass 3: any time + skip Avoid fields (relax time floor)
///   Pass 4: any time + any fields (last resort)
/// </summary>
public static class PlacementScorer
{
    /// <summary>
    /// Find the first valid slot for a game using greedy cascade.
    /// Candidates should be pre-filtered to the selected date and
    /// ordered time-first (GDate asc, FieldName asc).
    /// </summary>
    public static PlacementResult? FindFirstValidSlot(
        List<CandidateSlot> candidates,
        GameContext game,
        DivisionSizeProfile profile,
        PlacementState state,
        Dictionary<DateTime, DateTime>? waveFloorByDay = null)
    {
        if (candidates.Count == 0)
            return null;

        var hasTimeFloor = game.TargetTime.HasValue;
        var hasAvoidFields = state.FieldPreferences.Count > 0;

        // Pass 1: target time floor + skip Avoid fields
        if (hasTimeFloor || hasAvoidFields)
        {
            var slot = TryScan(candidates, game, profile, state, waveFloorByDay,
                useTimeFloor: hasTimeFloor, skipAvoidFields: hasAvoidFields);
            if (slot != null)
                return new PlacementResult { Slot = slot };
        }

        // Pass 2: target time floor + any fields
        if (hasTimeFloor && hasAvoidFields)
        {
            var slot = TryScan(candidates, game, profile, state, waveFloorByDay,
                useTimeFloor: true, skipAvoidFields: false);
            if (slot != null)
                return new PlacementResult { Slot = slot, AvoidField = true };
        }

        // Pass 3: any time + skip Avoid fields
        if (hasAvoidFields)
        {
            var slot = TryScan(candidates, game, profile, state, waveFloorByDay,
                useTimeFloor: false, skipAvoidFields: true);
            if (slot != null)
                return new PlacementResult { Slot = slot, TimeFallback = hasTimeFloor };
        }

        // Pass 4: any time + any fields (no constraints beyond hard filters)
        {
            var slot = TryScan(candidates, game, profile, state, waveFloorByDay,
                useTimeFloor: false, skipAvoidFields: false);
            if (slot != null)
                return new PlacementResult
                {
                    Slot = slot,
                    TimeFallback = hasTimeFloor,
                    AvoidField = hasAvoidFields
                        && state.FieldPreferences.TryGetValue(slot.FieldId, out var p) && p == 2
                };
        }

        return null;
    }

    // ══════════════════════════════════════════════════════════
    // Single-pass greedy scan
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Iterate candidates in order. Return the first one that passes
    /// all hard filters plus the optional time floor and avoid-field skip.
    /// </summary>
    private static CandidateSlot? TryScan(
        List<CandidateSlot> candidates,
        GameContext game,
        DivisionSizeProfile profile,
        PlacementState state,
        Dictionary<DateTime, DateTime>? waveFloorByDay,
        bool useTimeFloor,
        bool skipAvoidFields)
    {
        foreach (var c in candidates)
        {
            // ═══ Hard filter: occupied slot ═══
            if (state.OccupiedSlots.Contains((c.FieldId, c.GDate)))
                continue;

            // ═══ Hard filter: wave time floor (per-day) ═══
            if (waveFloorByDay != null
                && waveFloorByDay.TryGetValue(c.GDate.Date, out var dayFloor)
                && c.GDate < dayFloor)
                continue;

            // ═══ Hard filter: team time overlap ═══
            if (profile.GsiMinutes > 0 && IsTimeOverlap(c, game, profile, state))
                continue;

            // ═══ Optional: target time floor ═══
            if (useTimeFloor && game.TargetTime.HasValue
                && c.GDate.TimeOfDay < game.TargetTime.Value)
                continue;

            // ═══ Optional: skip Avoid fields ═══
            if (skipAvoidFields
                && state.FieldPreferences.TryGetValue(c.FieldId, out var pref)
                && pref == 2)
                continue;

            return c;
        }

        return null;
    }

    // ══════════════════════════════════════════════════════════
    // Hard filter helpers
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Hard filter: would placing this game here cause either team to be in two
    /// games at the same time? Returns true if the gap to any existing game for
    /// either team is less than 1 GSI tick (i.e. games overlap in real time).
    /// </summary>
    private static bool IsTimeOverlap(
        CandidateSlot candidate, GameContext game, DivisionSizeProfile profile, PlacementState state)
    {
        var gameDay = candidate.GDate.Date;
        var gameTime = candidate.GDate.TimeOfDay;

        var gap1 = ComputeMinGapTicks(state, game.DivId, game.T1No, gameDay, gameTime, profile.GsiMinutes);
        var gap2 = ComputeMinGapTicks(state, game.DivId, game.T2No, gameDay, gameTime, profile.GsiMinutes);

        return gap1 < 1 || gap2 < 1;
    }

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
            return int.MaxValue;

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
}
