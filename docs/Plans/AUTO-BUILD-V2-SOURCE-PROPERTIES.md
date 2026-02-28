# Auto-Build V2: Source Schedule Properties Per Division

> **Status**: Approved
> **Date**: 2026-02-27
> **Context**: These 9 properties are extracted from last year's schedule per division and used to score candidate slots. The engine reproduces the source pattern — it does NOT enforce hard rules or minimize arbitrary metrics.

## Paradigm

The engine's job: "You did it right last year, we'll reproduce it this year."

Every scheduling property is an **observation** from the source schedule, not an imperative. The only true hard filter is **occupied** (can't double-book a field+time). Everything else is scored by distance from source attributes.

Each division's pattern is **self-contained** — one division's attributes have nothing to do with another's.

## The 9 Properties

All time-based properties are expressed in **GSI ticks** (not minutes) unless noted. This makes them portable across different GSI values.

### 1. Play Days (`DayOfWeek[]`)

Which days of the week this division played. Actual `DayOfWeek` values — no ordinal remapping. If Saturday doesn't work this year, surface as a disconnect.

### 2. Game Guarantee (`int`)

Minimum number of games per team. Drives the round count. Must be known before Rounds Per Day because odd-sized divisions may need extra rounds (bye-compensation) to meet the guarantee.

### 3. GSI — Game Start Interval (`int`, minutes)

The clock tick. The primitive unit that all other time properties are relative to. Extracted from the most common interval between consecutive game start times on the same field.

### 4. Rounds Per Day (`int`, per DOW)

How many rounds were played per day. For odd-sized divisions with bye-compensation extra rounds, a round may split across days — capture this per day-of-week. Depends on Game Guarantee to understand why the round count is what it is.

### 5. Start Tick Offset (`int`, GSI ticks)

How many GSI ticks from the field window start (earliest configured timeslot) the division's first game began. Expressed as tick count, not absolute time — portable across different window starts.

### 6. Round Layout (`enum`: Horizontal | Sequential)

Binary classification of how games within a round were arranged:

- **Horizontal**: All games in the round at the same tick on different fields. Requires as many fields as games in the round.
- **Sequential**: Games stacked on the GSI grid (tick 0, tick 1, tick 2...). Game order within a round is flexible — matchups are fixed but sequence can be swapped to dodge BTBs at round boundaries.

### 7. Inter-Round Gap (`int`, GSI ticks)

The number of GSI ticks between the last game of one round and the first game of the next round on the same day. Typically 0 (back-to-back rounds) or 1+ (rest period between rounds).

### 8. Field Distribution Fairness (`enum`: Democratic | Biased)

Whether teams were distributed evenly across available fields or concentrated on specific fields:

- **Democratic**: All teams played roughly equal games on each field (even rotation).
- **Biased**: Some teams were assigned to specific fields disproportionately.

The engine reproduces whatever the source did — if biased, stay biased. If democratic, distribute evenly.

### 9. Minimum Team Gap (`int`, GSI ticks)

The smallest observed gap between any team's consecutive games within a day. Subsumes BTB detection:

- **1 tick** = BTBs existed in the source (teams played consecutive ticks)
- **2 ticks** = no BTBs, one rest tick between games (most common)
- **3+ ticks** = intentional wider spacing

Replaces the old BTB hard filter. If the source had BTBs, the engine shouldn't reject them. If it didn't, penalize them — as a scored distance, not a wall.

## Scoring Model

For each candidate slot, compute the **distance** from the source attributes. Lower distance = better match. The scorer does NOT use hard filters (except occupied) or priority-based relaxation.

Each property contributes a distance component. The total distance determines slot ranking. When distance is 0 (perfect source match), take the slot immediately.

## Property Summary Table

| # | Property | Type | Unit | Extracted From |
|---|----------|------|------|---------------|
| 1 | Play Days | DayOfWeek[] | — | Game dates grouped by DOW |
| 2 | Game Guarantee | int | games | Min games per team across all teams |
| 3 | GSI | int | minutes | Most common field-consecutive interval |
| 4 | Rounds Per Day | Dict<DOW, int> | rounds | Round numbers grouped by game date DOW |
| 5 | Start Tick Offset | int | GSI ticks | (First game time - window start) / GSI |
| 6 | Round Layout | enum | — | Whether round games share a tick or stack |
| 7 | Inter-Round Gap | int | GSI ticks | Gap between consecutive rounds / GSI |
| 8 | Field Distribution | enum | — | Variance of team-field counts |
| 9 | Min Team Gap | int | GSI ticks | Smallest consecutive-game gap per team-day / GSI |
