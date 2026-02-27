# Auto-Schedule V2 — Ground-Up Redesign

**Status**: APPROVED — Ready to implement
**Date**: 2026-02-26
**Context**: Current auto-build produces schedules that an expert scheduler rejected on sight. The fundamental placement strategy is wrong (vertical fill instead of horizontal), and the algorithm doesn't extract or honor the real scheduling attributes that matter. This plan redesigns from first principles.

---

## The Sales Pitch (Our North Star)

> "We'll auto-build this year according to the patterns you used last year."

The scheduler's only job should be to review the extracted findings, nod (or tweak), and hit Build.

---

## What Went Wrong With V1

### 1. Vertical Fill
`TimeslotSlotFinder.FindNextAvailable()` walks **dates → fields → time slots** in a nested loop. This fills a field top-to-bottom before moving to the next field. The result: games for a single round are stacked vertically down one field column instead of spread horizontally across fields at the same time.

An expert scheduler saw this shape immediately and rejected the output.

### 2. Literal Replay Instead of Attribute Extraction
V1 tries to replay each game slot-for-slot from last year: same field name, same time of day, same day ordinal. When any single slot breaks (field renamed, slot occupied, BTB conflict), it drops into the vertical-fill fallback and loses the pattern entirely.

This is fragile. Real schedulers don't memorize individual slots — they work from attributes: "5-team divisions go horizontal on fields 1–4 on Saturday mornings."

### 3. No Understanding of Division Size as the Key Grouping
V1 maps agegroup-to-agegroup and division-to-division by name. But the scheduling pattern is driven by **team count (TCnt)**. Every division of the same size uses the same pairing table. All 5-team divisions get the same treatment. All 8-team divisions get the same treatment. Division names are irrelevant to layout.

### 4. No Prerequisites Enforced
V1 allows building without verifying that Pools, Pairings, and Timeslots are complete. This leads to partial or broken schedules.

### 5. No Priority Ranking System
V1 has no concept of constraint priority. When conflicts arise (and they always do), the algorithm needs to know which constraint to sacrifice first. Without a ranking, V1 makes arbitrary choices — which is how you end up with vertical fill when the scheduler would have sacrificed exact time match instead.

### 6. No Processing Order Control
V1 doesn't let the scheduler control which agegroups or divisions get placed first. The first divisions placed get the best slots; later ones get leftovers. Without explicit ordering, the algorithm makes this decision arbitrarily.

---

## The Scheduler's Mental Model

Everything a scheduler needs to reproduce a schedule can be captured by asking **ten questions per division size (TCnt)** about last year's schedule. These are not asked of the scheduler — they are **computed from the historical data**.

All attributes are scoped to **round-robin games only** (T1Type = 'T' AND T2Type = 'T').

### Q1: What days did they play?
For a given TCnt, which day-of-week values appear? (e.g., Saturday + Sunday)

*Extracted from*: `Schedule.GDate.DayOfWeek` grouped by TCnt.

### Q2: Where in the timeslot window did they play?

**Critical principle: Timeslots are the canvas, not the instruction.** A timeslot window of 8:00 AM – 2:00 PM doesn't mean "start at 8:00." It means "8:00 is available if you want it." Timeslot configurations are deliberately broader than what any single division needs. The meaningful signal is WHERE within that window a division size historically lived — not the absolute clock time.

For a given TCnt, capture BOTH the absolute time range AND the offset from the source job's timeslot window start:

- **Absolute**: First game at 9:15, last game at 11:00 on Saturday
- **Relative**: First game started 75 minutes into Saturday's timeslot window (window opened at 8:00)

The **relative offset** is more transferable across years. If this year's Saturday window opens at 8:30 instead of 8:00, the offset approach says "start at 9:45" (preserving the structural position — same 75-minute gap) rather than "start at 9:15" (which is now a different structural position, only 45 minutes into the window).

*Extracted from*: `Schedule.GDate.TimeOfDay` grouped by TCnt + DayOfWeek, cross-referenced against the **source job's timeslot configuration** (`FieldTimeslots.StartTime` for the corresponding agegroup). The extractor MUST read both Schedule data and Timeslot config for the source job — game times alone are insufficient.

**Stored as**:
- `StartOffsetFromWindow`: TimeSpan per day — how far into the available timeslot window this TCnt's first game began
- `TimeRangeAbsolute`: (start, end) per day — the raw clock times (for display and fallback)
- `WindowUtilization`: float per day — what fraction of the available window was actually used

### Q3: What fields did they use?
For a given TCnt, which fields were used? This defines the "field band" for that division size.

*Extracted from*: `Schedule.FieldId` / `Schedule.Field.FieldName` grouped by TCnt.

### Q4a: How many rounds?
Total round count for this TCnt.

*Known from current job's pairings* — the current `PairingsLeagueSeason` records for this TCnt define this explicitly. Cross-validated against prior year.

### Q4b: What is the game guarantee?
Minimum number of games any team plays. For even divisions = round count. For odd divisions = round count minus 1 (one bye per team).

*Derived from*: TCnt (even/odd) + round count. **Encoded in current pairings.**

### Q4c: Were rounds in order?
Did round numbers progress chronologically? (Expected: yes. Not surfaced to scheduler — round ordering is a natural byproduct of sequential placement. BTB tracker handles the actual concern of team rest.)

### Q5: Horizontal or vertical placement?
For each round: did all games share the same time slot (horizontal) or were they spread across time slots on fewer fields (vertical)?

*Extracted from*: For each round, count distinct `GDate.TimeOfDay` values.

**Metric**: `DISTINCT(TimeOfDay) / GameCount` per round. Ratio near 0 = horizontal. Ratio near 1 = vertical.

**Partial horizontal**: A round with 8 games but only 6 available fields produces 6 games at one time slot and 2 at the next. Captured as the actual games-per-timeslot distribution. The spill-over games are a **BTB risk zone**.

### Q6: Per-day on-site interval
From first game start to last game start for a TCnt on a given day. This is the "footprint" — how long teams of this division size are at the venue.

*Extracted from*: `MAX(GDate.TimeOfDay) - MIN(GDate.TimeOfDay)` per TCnt per day.

### Q7: Field desirability distribution
Which fields got lighter usage, and was exposure distributed across teams? A field that hosted fewer games than peers was likely in poor condition — the scheduler spread those games so no team played there twice.

*Extracted from*: Game count per field (compared to field average), and per-team repeat count on low-usage fields. **Not asked of the scheduler — the pattern is in the data.**

### Q8: Rounds per day (multi-day events)
How many rounds were played on each day?

*Extracted from*: `COUNT(DISTINCT Rnd)` per TCnt per DayOfWeek/DayOrdinal.

### Q9: Odd-division extra round placement
For odd TCnt where game guarantee is odd: which day received the extra round? (Odd divisions are "taller" — they need more rounds than the game guarantee because each round has a bye.)

*Extracted from*: Compare rounds-per-day distribution. The day with more rounds is where the extra landed.

### Q10: Inter-round interval (rhythm)
What was the time gap between consecutive round start times? This is the pace of the day — distinct from total footprint.

Example: 3 rounds with a 3-hour footprint could be 60-minute intervals (8:00/9:00/10:00) or 90-minute intervals (8:00/9:30/11:00). Same footprint, very different team experience.

*Extracted from*: For consecutive rounds on the same day, compute `RoundN+1.StartTime - RoundN.StartTime`. Report the median interval.

**Note**: Q10 is a placement parameter that guides inter-round timing. It is NOT on the constraint priority list — it's used directly by the placement engine when determining target times.

---

## Three Scheduler Inputs

The algorithm takes three explicit inputs from the scheduler that control HOW placement happens.

### Input 1: Processing Order & Include/Exclude

#### A. Agegroup Processing Order

First agegroup processed gets best slot availability. Alphabetical default, scheduler drag-and-drops to reorder.

Each agegroup row shows:
- Agegroup name
- Division count + total team count available for scheduling
- Include checkbox (pre-checked by default)

```
┌─────────────────────────────────────────────────────────────┐
│  Agegroup Processing Order                                  │
│  First agegroup gets best slot availability.                │
│  Drag to reorder. Uncheck to exclude.                       │
│                                                             │
│  ☰  1. ☑ 2030 Boys      (4 divs, 22 teams)                │
│  ☰  2. ☑ 2030 Girls     (3 divs, 17 teams)                │
│  ☰  3. ☑ 2029 Boys      (5 divs, 28 teams)                │
│  ☰  4. ☑ 2029 Girls     (3 divs, 15 teams)                │
│  ☰  5. ☐ 2028 Boys      (4 divs, 20 teams)                │
│                                                             │
│  [Sort A–Z]  [Sort by team count ↓]  [Check All]           │
└─────────────────────────────────────────────────────────────┘
```

#### B. Division Include/Exclude (within agegroup accordion)

Expanding an agegroup shows its divisions with individual include/exclude checkboxes:

```
▼ 2030 Boys (22 teams)                              ☑ Include
    ☑ Div A — 6 teams
    ☑ Div B — 6 teams
    ☑ Div C — 5 teams
    ☐ Div D — 5 teams (hand-scheduled, excluded)
▼ 2029 Girls (15 teams)                             ☑ Include
    ☑ Div A — 8 teams
    ☑ Div B — 7 teams
```

Unchecking an agegroup unchecks all its divisions. Unchecking individual divisions within a checked agegroup gives surgical control.

#### C. Division Processing Order (within each agegroup)

```
┌─────────────────────────────────────────────────────────┐
│  Division Processing Order                              │
│                                                         │
│  ○ Alphabetical (A, B, C, D)                           │
│  ○ Odd-sized first, then alphabetical                   │
│  ○ Custom per agegroup                                  │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

#### D. Pre-Build Confirmation Guard

Before build executes, the scheduler sees a summary of what's included and excluded. Only exceptions (excluded items) are listed — no need to enumerate 40 included divisions.

**When nothing excluded:**
```
┌────────────────────────────────────────────────────────┐
│  Ready to build schedule                               │
│                                                        │
│  Scheduling ALL 6 agegroups, 22 divisions, 326 games   │
│                                                        │
│  ⚠ Existing games in included divisions will be       │
│    deleted and rebuilt.                                 │
│                                                        │
│  Type CONFIRM to proceed: [________]                   │
└────────────────────────────────────────────────────────┘
```

**When items excluded:**
```
┌────────────────────────────────────────────────────────┐
│  Ready to build schedule                               │
│                                                        │
│  Scheduling 5 agegroups, 18 divisions, 246 games       │
│                                                        │
│  EXCLUDED:                                             │
│    ⬜ 2028 Boys — all 4 divisions                      │
│    ⬜ 2030 Girls / Div B — hand-scheduled              │
│                                                        │
│  ⚠ Existing games in included divisions will be       │
│    deleted and rebuilt.                                 │
│                                                        │
│  Type CONFIRM to proceed: [________]                   │
└────────────────────────────────────────────────────────┘
```

### Input 2: Constraint Priority Ranking

#### Priority List (5 items — all reorderable)

Correct pairing is always enforced (assumed, not scored). The scheduler controls the priority order of these 5 pattern-preservation constraints:

| Priority | Constraint | What It Preserves | Trade-off |
|----------|-----------|-------------------|-----------|
| 1 | **Correct Day** | Same games/team/day ratio as source (Q1, Q8) | Games/team/day ratio may differ from source |
| 2 | **Placement Shape** | Horizontal vs vertical round layout (Q5) | Round layout may differ from source |
| 3 | **On-Site Window** | First-to-last game time spread per day (Q2, Q6) | On-site window may differ from source |
| 4 | **Field Assignment** | Same set of fields for each pool size (Q3) | Games placed on different fields |
| 5 | **Field Distribution** | Per-team balance of different fields used (Q7) | Some teams may repeat fields |

**Removed constraints** (redundant — covered by the 5 above):
- ~~Rounds per day~~ — covered by Correct Day (games/team/day ratio)
- ~~Exact time match~~ — covered by timeslot configuration
- ~~Extra round day~~ — covered by Correct Day (games/team/day ratio)

#### Scheduler UI

All 5 priorities are freely reorderable via up/down arrows. Items at the top matter most; when slots are tight, the engine sacrifices from the bottom up.

**UI behavior:**
- Each row has up/down arrow buttons for reordering
- All 5 constraints are reorderable (no locked items)
- Each row shows a description of what the constraint preserves
- [Reset to Defaults] restores the original ordering
- Scheduler's custom ordering persists in localStorage

### Input 3: Source Event Selection

Dropdown of prior-year jobs. Or "Clean Sheet" for new events with no history.

Agegroup mapping (year-increment strategy from V1) is still needed to map source agegroups to current agegroups — this powers the processing order list and profile extraction context.

---

## Scoring Algorithm

### Weighted Scoring with Squared Priorities

Each constraint gets a weight derived from its priority position. Candidates are discrete (field, timeslot) slots — all valid positions defined by Manage Timeslots. No continuous distance calculations.

```
weight(priority) = (totalConstraints - priorityRank + 1)²
```

For 5 constraints:
| Priority | Weight |
|----------|--------|
| 1 | 25 |
| 2 | 16 |
| 3 | 9 |
| 4 | 4 |
| 5 | 1 |

Squaring makes high-priority constraints disproportionately important. Violating #1 once (lose 25 points) is worse than violating #3, #4, and #5 combined (lose 9+4+1=14 points).

### Placement Evaluation

```
ScoreCandidate(slot, game, profile, constraints_ranked):
  score = 0
  violations = []

  For each constraint in ranked order:
    if constraint.IsSatisfied(slot, game, profile):
      score += constraint.Weight
    else:
      violations.add(constraint)

  return (score, violations)

PlaceGame(game, profile, constraints_ranked, allCandidateSlots):
  best = null

  For each candidate in allCandidateSlots:
    (score, violations) = ScoreCandidate(candidate, game, profile, constraints_ranked)

    If score == maxPossibleScore:
      place here (perfect fit)
      return

    If best == null OR score > best.score:
      best = (candidate, score, violations)

  If best != null:
    place at best.candidate
    Log best.violations to sacrifice report
  Else:
    Add to unplaced games table
```

### When No Slot Exists

The algorithm does NOT silently drop games. Unplaced games are collected into a results table.

---

## Hard Prerequisites (Non-Negotiable)

The auto-build button MUST be disabled until all three are verified complete for the current job:

### 1. Assign to Pools ✓
- Every active team has a division assignment
- Every division has a known TCnt
- Validation: `COUNT(teams WHERE DivId IS NULL AND Active = true) = 0`

### 2. Manage Pairings ✓
- Every active division size (TCnt) has pairings in `PairingsLeagueSeason`
- Round-robin pairings exist (T1Type = 'T', T2Type = 'T')
- Validation: For each distinct TCnt in active divisions, `COUNT(pairings WHERE TCnt = X) > 0`

### 3. Manage Timeslots ✓
- Every agegroup (or division) has at least one timeslot date configured
- Fields are assigned to days
- Validation: For each agegroup with active divisions, `COUNT(timeslot dates) > 0`

**UI**: Checklist panel showing green/red per prerequisite. Build button enabled only when all green.

---

## The Current Job's Pairings Define the Structure

The current job's `PairingsLeagueSeason` and `Masterpairingtable` already encode:

- **How many rounds** per TCnt
- **Which teams play which** per round
- **Bye rotation** for odd divisions (T1 sits round 1, T2 sits round 2, etc.)
- **Game guarantee** (implicit from round count + TCnt parity)
- **The extra game** for odd divisions (which team plays one more)

We do NOT extract these from last year. We read them from the current job's pairings. Last year only tells us the **layout pattern** — how those rounds were arranged in time and space.

---

## Profile Extraction & Presentation

### Extraction (from Prior Year)

```
Input:  Prior-year JobId
Output: Dictionary<int, DivisionSizeProfile> keyed by TCnt

For each distinct TCnt in prior year's scheduled RR games:
  Extract Q1–Q10 attributes
  Store as DivisionSizeProfile
```

**Important**: The extractor reads TWO data sources from the prior year:
1. `Schedule` table — actual game placements (times, fields, rounds)
2. `FieldTimeslots` / `TimeslotDates` — the timeslot configuration that DEFINED the available window

Q2 offset computation requires both. The game time alone (9:15 AM) is meaningless without knowing the window opened at 8:00 AM. If the source job's timeslot config is unavailable (e.g., deleted or legacy data), fall back to absolute times from `TimeRangeAbsolute`.

**DivisionSizeProfile** (per TCnt):
```
TCnt: int
PlayDays: List<DayOfWeek>                                    // Q1
StartOffsetFromWindow: Dict<DayOfWeek, TimeSpan>             // Q2 (primary — relative to source timeslot window)
TimeRangeAbsolute: Dict<DayOfWeek, (TimeSpan start, end)>    // Q2 (fallback — raw clock times)
WindowUtilization: Dict<DayOfWeek, float>                    // Q2 (diagnostic — fraction of window used)
FieldBand: List<string>                                       // Q3 (field names, ordered)
RoundCount: int                                               // Q4a
GameGuarantee: int                                            // Q4b
PlacementShape: Dict<int, GamesPerTimeslotDistribution>       // Q5 (per round)
OnsiteIntervalPerDay: Dict<DayOfWeek, TimeSpan>               // Q6
FieldDesirability: Dict<string, FieldUsageProfile>             // Q7
RoundsPerDay: Dict<DayOfWeek, int>                            // Q8
ExtraRoundDay: DayOfWeek?                                     // Q9 (null if even)
InterRoundInterval: TimeSpan                                   // Q10 (median gap)
```

### Presentation (Read-Only Profile Cards)

Profiles are **informational only** — the scheduler does NOT edit them. If fields or times need to change, adjust via Manage Timeslots. The algorithm works within current reality, guided by last year's profile.

```
┌──────────────────────────────────────────────────────────┐
│ 5-Team Divisions (3 divisions this year)                 │
├──────────────────────────────────────────────────────────┤
│ Days:          Saturday + Sunday                         │
│ Times:         Sat 9:15–11:00, Sun 9:15–9:15            │
│ Window offset: Sat +75min, Sun +75min                    │
│ Fields:        LBTS-01 through LBTS-04                   │
│ Rounds:        4 (3-game guarantee + 1 bye/team)         │
│ Layout:        Horizontal (2 games/round × 1 timeslot)   │
│ Round rhythm:  75 min between rounds                     │
│ Footprint:     Sat 3h, Sun 1h15m                         │
│ Rounds/Day:    Sat 3, Sun 1                              │
│ Extra round:   Saturday                                  │
│ Field notes:   LBTS-03 had 30% fewer games               │
│                (distributed: no team played 2x)          │
└──────────────────────────────────────────────────────────┘
```

---

## Build Algorithm: Horizontal-First Placement

### Core Change

Instead of walking dates → fields → slots (vertical), we walk **rounds → time slots → fields** (horizontal).

Processing order follows the scheduler's Input 1 selections (agegroup order, division order, include/exclude).

```
For each agegroup (in scheduler-defined order, included only):
  For each division (in scheduler-defined order, included only):
    profile = DivisionSizeProfile for this TCnt
    pairings = current job's PairingsLeagueSeason for this TCnt

    Group pairings by round number

    For each round (in order):
      Determine target day from profile.RoundsPerDay

      // TARGET START TIME — offset-based, not absolute
      // First round of the day:
      //   targetTime = currentJob.TimeslotWindowStart(day) + profile.StartOffsetFromWindow(day)
      //   Snap to nearest available timeslot in current job's config
      // Subsequent rounds:
      //   targetTime = previousRoundTime + profile.InterRoundInterval
      Determine target time using offset from current job's timeslot window

      games_in_round = pairings for this round
      candidate_slots = all unoccupied (field, timeslot) combinations

      // HORIZONTAL PLACEMENT: all games in this round at SAME time
      For each game in round:
        Score ALL candidate slots against prioritized constraints
        Pick highest-scoring candidate

        If no candidate available:
          Add to unplaced games table

        Place game at best candidate
        Mark slot as occupied
        Update BTB tracker
```

---

## Post-Build Results

### Unplaced Games Table

```
┌──────────────────────────────────────────────────────────────┐
│  Unplaced Games (3)                                 🖨 Print │
├────────────┬──────────┬───────┬─────────┬────────────────────┤
│ Agegroup   │ Division │ Round │ Pairing │ Reason             │
├────────────┼──────────┼───────┼─────────┼────────────────────┤
│ 2030 Boys  │ Div C    │ 4     │ T3 v T5 │ No slots on Sun    │
│ 2029 Girls │ Div A    │ 3     │ T1 v T4 │ All fields occupied│
│ 2029 Girls │ Div A    │ 4     │ T2 v T5 │ All fields occupied│
└────────────┴──────────┴───────┴─────────┴────────────────────┘

  Go to Schedule Division ➜        Run QA ➜
```

Ideally this table is empty. Scheduler prints it, hand-fixes in schedule-division, then runs QA.

### QA Report

Existing checks plus new conformance checks:

- **Unplaced games**: Any pairing in `PairingsLeagueSeason` with no Schedule record (verify existing QA covers this; ensure Excel export includes)
- **Shape conformance**: Per round, % of games at same time slot (horizontal %)
- **Footprint conformance**: Per-day on-site interval vs profile target
- **Rhythm conformance**: Inter-round intervals vs profile target
- **Field distribution**: Low-desirability fields have even team exposure
- **Rounds-per-day**: Matches profile distribution
- **Constraint sacrifice log**: Which priorities were dropped, how often, which games
- **BTB report**: Back-to-back violations (existing)

### Iterate Loop

QA report includes **[Adjust & Rebuild]** button → returns to processing order / priority ranking steps.

**Strong CONFIRM guard on every rebuild**: "You are about to delete [N] scheduled games in [M] divisions and rebuild. Type CONFIRM to proceed." Nothing worse than accidentally nuking a good schedule.

---

## Clean Sheet Mode (No Prior Year)

Brand new events have no historical data. The algorithm still works:

1. Prerequisites are still mandatory (Pools + Pairings + Timeslots)
2. No profile extraction — skip source event selection
3. Scheduler sets processing order and constraint priorities (these matter even more without a profile)
4. Algorithm uses horizontal-first placement with defaults inferred from timeslot configuration
5. Default profile: available days from timeslots, all fields in field band, interval from field config

This is the "auto-schedule done right" fallback — horizontal-first with priority-based scoring instead of vertical fill.

**Litmus test**: If clean-sheet mode produces decent schedules, the engine is sound and prior-year extraction adds genuine value rather than papering over a bad algorithm.

---

## Frontend State Persistence

### localStorage (Strongly Typed)

The auto-build page reads `AutoBuildConfig` from localStorage on init, restoring the scheduler's previous session. All state survives page refresh.

```typescript
interface AutoBuildConfig {
  sourceJobId: string;
  agegroupOrder: string[];
  divisionOrderStrategy: 'alpha' | 'odd-first' | 'custom';
  excludedAgegroupIds: string[];
  excludedDivisionIds: string[];
  constraintPriorities: string[];
}
```

- **On page init**: Read from localStorage, populate UI with saved state
- **On any change**: Write updated config to localStorage
- **Reset to Defaults**: Clear localStorage entry, reload defaults
- Keyed per job to prevent cross-job contamination: `autobuild-config-{jobId}`

---

## What Changes in the Codebase

### Backend — New/Modified

| File | Change |
|------|--------|
| `AutoBuildRepository.cs` | Replace `ExtractPatternAsync()` with `ExtractAttributesAsync()` returning `DivisionSizeProfile` per TCnt |
| `AutoBuildScheduleService.cs` | Rewrite `BuildAsync()` to use scored horizontal placement with processing order and include/exclude |
| `TimeslotSlotFinder.cs` | Replace entirely with `PlacementScorer.cs` — candidate evaluation, not slot walking |
| `AutoBuildController.cs` | Add prerequisite check, profile review, processing order, priority ranking endpoints |
| New: `DivisionSizeProfile.cs` | DTO for the ten extracted attributes |
| New: `AttributeExtractor.cs` | Service to compute Q1–Q10 from prior year schedule data |
| New: `PlacementScorer.cs` | Weighted scoring engine — evaluates candidates against prioritized constraints |
| New: `ConstraintEvaluators/` | 5 evaluators: `CorrectDayEvaluator`, `PlacementShapeEvaluator`, `OnsiteWindowEvaluator`, `FieldAssignmentEvaluator`, `FieldDistributionEvaluator` |

### Backend — Unchanged
| File | Reason |
|------|--------|
| `PairingsService.cs` | Pairings management is correct and separate |
| `BtbTracker.cs` | Still needed — especially for partial-horizontal spill-over detection |
| `ScheduleQaService.cs` | Extend with new conformance checks, verify unplaced games coverage |

### Frontend — Modified
| File | Change |
|------|--------|
| `auto-build.component.ts` | Complete redesign: prerequisite checklist, profile cards, agegroup accordion with include/exclude, processing order lists, priority ranking, build progress, unplaced games table, QA report with sacrifice log, localStorage persistence |

---

## Open Questions

1. **Bracket games**: Deferred to V3. Get RR placement right first.

2. **Cross-division field sharing**: When multiple TCnt groups share the same field band, occupancy tracking prevents double-booking. Should the profile cards warn the scheduler when field bands overlap?

3. **Field band overflow**: If this year has fewer fields than last year's field band, how do we compress? Options: proportional mapping, scheduler adjusts via Manage Timeslots, or auto-shift to adjacent fields.

4. **Profile persistence in DB**: Currently profiles are extracted on-demand. Should they be saved to a table for year-over-year comparison? Or is on-demand extraction sufficient?

5. **Build iteration performance**: Rapid tweak-rebuild-QA cycles need fast builds. Should we support incremental rebuild (only re-process changed agegroups) or always full rebuild of included divisions?

---

## Three-Tier Scheduling Pyramid

### The Insight

The auto-build page (tournament-level auto-schedule) is not a separate feature — it's the top tier of a **three-tier pyramid** that shares a single engine. The scheduler picks their comfort level:

- **Cautious**: Division by division, inspect after each one
- **Moderate**: Agegroup at a time
- **Confident**: Whole tournament in one shot

All three tiers use the same scoring engine, constraint evaluators, and horizontal-first placement. The only difference is scope.

### Where It Lives

The division and agegroup tiers live on the **schedule-division page** (`scheduling/schedule-division`). The scheduler is already there looking at the grid, pairings, and teams — this is where granular control belongs.

The tournament tier lives on the **auto-build page** (`scheduling/auto-build`) where the full workflow (source selection, profile extraction, processing order, priorities) makes sense for bulk operations.

### Tier Details

| Tier | Scope | Auto-Schedule Button | Enabled When | Delete Button | Delete Guard |
|------|-------|---------------------|--------------|---------------|-------------|
| **Division** | One division | On schedule-division grid header | Division has zero scheduled games | "Delete Div Games" | Standard confirm modal |
| **Agegroup** | All divisions in one agegroup | On schedule-division agegroup header | No division in the agegroup has scheduled games | "Delete Agegroup Games" | Standard confirm modal |
| **Tournament** | All agegroups, all divisions | On auto-build page | No games scheduled anywhere in the job | "Delete All Games" | Type-CONFIRM pattern |

### Enable/Disable Logic

The pattern is consistent: **auto-schedule is disabled if any game exists within that scope**. The scheduler must explicitly delete before rebuilding. This prevents accidental partial overwrites and forces intentional decisions.

- **Division auto-schedule**: Disabled if that division has any scheduled games. The existing "Clear Div" button (already on the page) returns it to a clean slate.
- **Agegroup auto-schedule**: Disabled if ANY division in the agegroup has scheduled games. The scheduler must delete at the agegroup level first, or clear each division individually.
- **Tournament auto-schedule**: Disabled if ANY game exists in the job. The scheduler must delete all games first.

This also means the tiers are **mutually exclusive in practice**: if you auto-schedule division by division, the agegroup button stays disabled (divisions have games). If you auto-schedule at the agegroup level, the tournament button stays disabled. The scheduler commits to a granularity level for each agegroup.

### Delete Operations (Three Tiers)

Each tier has a corresponding delete operation with escalating confirmation guards:

**Division Delete** (existing — already implemented):
```
┌────────────────────────────────────────────────────┐
│  Delete all games in 2030 Boys Div A?              │
│  This will remove 12 round-robin games.            │
│                                                    │
│  [Cancel]  [Delete]                                │
└────────────────────────────────────────────────────┘
```

**Agegroup Delete** (new):
```
┌────────────────────────────────────────────────────┐
│  Delete all games in 2030 Boys?                    │
│  This will remove 48 games across 4 divisions:     │
│    Div A (12 games), Div B (12 games),             │
│    Div C (14 games), Div D (10 games)              │
│                                                    │
│  [Cancel]  [Delete]                                │
└────────────────────────────────────────────────────┘
```

**Tournament Delete** (new — nuclear option):
```
┌────────────────────────────────────────────────────┐
│  Delete ALL scheduled games for this tournament?   │
│                                                    │
│  This will remove 326 games across 22 divisions    │
│  in 6 agegroups.                                   │
│                                                    │
│  This cannot be undone.                            │
│                                                    │
│  Type CONFIRM to proceed: [________]               │
│                                                    │
│  [Cancel]                                          │
└────────────────────────────────────────────────────┘
```

### Division-Level Auto-Schedule Flow

When the scheduler clicks "Auto-Schedule" on a division:

1. Engine reads the division's TCnt
2. Looks up the `DivisionSizeProfile` for that TCnt (from source event extraction, or clean-sheet defaults)
3. Fetches the division's pairings from current job
4. Loads occupied slots on the grid (other divisions' games)
5. Runs scored horizontal-first placement for this division only
6. Grid refreshes in place — scheduler sees the result immediately
7. Unplaced games (if any) shown in a results toast/panel
8. Scheduler can hand-fix via the existing mouse/keyboard placement tools, or delete and retry

### Agegroup-Level Auto-Schedule Flow

When the scheduler clicks "Auto-Schedule" on an agegroup:

1. Processes each division within the agegroup sequentially (alphabetical default, or division-order strategy from config)
2. Same engine as division-level, but scope is wider
3. Each division's placement respects slots already occupied by earlier divisions in the same agegroup
4. Results panel shows per-division breakdown (scheduled/unplaced counts)
5. Grid refreshes to show the full agegroup result

### Backend API Shape

```
POST /api/schedule-division/auto-schedule-div/{divId}      ← existing (rewire to new engine)
POST /api/schedule-division/auto-schedule-agegroup/{agId}   ← new
POST /api/auto-build/build                                  ← existing (rewire to new engine)

DELETE /api/schedule-division/delete-div-games               ← existing
POST   /api/schedule-division/delete-agegroup-games          ← new
POST   /api/schedule-division/delete-tournament-games        ← new (or on auto-build controller)
```

### Why This Matters

The current auto-build is all-or-nothing. The scheduler hits build, waits, then stares at 326 games trying to assess quality. That's overwhelming.

With the pyramid, the scheduler can:
1. Auto-schedule 2030 Boys Div A → inspect → looks good
2. Auto-schedule 2030 Boys Div B → inspect → tweak one game by hand → good
3. Auto-schedule 2030 Boys Div C → inspect → bad, delete, retry with different priorities → good
4. Move to next agegroup

Each step is reviewable. The scheduler builds confidence in the engine incrementally rather than betting the whole tournament on one button press.

---

## Proposed Workflow (UX)

```
loading → summary → preparing → order → confirm → building → results

loading:   Fetch game summary
summary:   Show current schedule status (agegroup tree + game counts)
preparing: AUTOMATED — prerequisites check + source job discovery + profile extraction
           All communicated via conversational chat messages, no interactive cards
order:     INTERACTIVE — agegroup include/exclude + ordering + division strategy + 5 priorities
confirm:   Summary + type CONFIRM guard
building:  Dramatic spinner + scoring progress
results:   Unplaced games + sacrifice log + division breakdown + QA + undo
```

**Preparation step is fully automated** — the scheduler's first interactive moment is the order/priorities step. Prerequisites, source selection, and profile extraction happen in sequence with conversational status messages. If prerequisites fail, a "Back" button appears to return to summary.

---

## Key Insight Summary

| Principle | Old V1 | New V2 |
|-----------|--------|--------|
| Grouping key | Agegroup name + div name | **TCnt (division size) — sole key** |
| Pattern unit | Individual game slot | **Ten aggregate attributes per TCnt** |
| Placement direction | Vertical (field-first) | **Horizontal (round-first)** |
| Fallback strategy | Same vertical fill | **Scored placement with priority degradation** |
| Prior year usage | Literal slot replay | **Attribute extraction (read-only profiles)** |
| Current job role | Optional | **Defines structure (Pools + Pairings + Timeslots = mandatory)** |
| Conflict resolution | Arbitrary fallback | **Weighted scoring from scheduler-ranked priorities** |
| Processing order | Arbitrary | **Scheduler-controlled with include/exclude** |
| Slot exhaustion | Silent failure | **Unplaced games table (print + hand-fix + QA)** |
| Scheduler involvement | Select source, hit build | **Review profiles, set order, rank priorities, iterate** |
| New events | No support | **Clean sheet mode — same engine, no profile** |
| State persistence | None | **localStorage, strongly typed, per-job** |

---

**Next Step**: Implement. Start with attribute extraction and scoring engine, then build the UI workflow around it.
