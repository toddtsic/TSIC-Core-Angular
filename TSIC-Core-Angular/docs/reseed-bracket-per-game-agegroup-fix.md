# Reseed-tournament bracket seeding: replace the job-wide flag with a per-game age-group comparison

> Portable, self-contained writeup — concept, failure, fix. No file/line references so it stands
> on its own for a future session. Legacy names in parentheses only where they help mapping.

## Context — what a "reseed" tournament is

Some tournaments run round-robin (RR) pool play in one age group, then seed the resulting
standings into separate championship/medal age groups (e.g. Platinum/Gold/Silver/Bronze), where
each medal flight is a 16-team single-elimination bracket. So a bracket slot is defined as "the
Nth-place finisher of RR Pool X," and Pool X lives in a **different** age group than the bracket.

To keep each flight self-contained (not sharing a team row across two age groups, which
cross-pollutes single-counter team stats like games/wins/goals), each flight is pre-populated with
throwaway placeholder teams ("twins"). When a pool finishes, the seeding routine finds the
placeholder in the flight whose slot number matches the bracket slot and copies the qualifying
team's identity onto it (team name + club FK), while the placeholder keeps its own team id. The
bracket then advances those placeholder ids through the rounds.

## The bug

The system has a job-level flag (legacy: `BReseedTournament`) that switches this "copy identity onto
the twin" behavior on for the **entire tournament**. When it's on, every bracket-seed game in the
job is forced down the twin-copy path — including bracket/consolation games whose seeds actually
come from the **same** age group they live in.

The twin lookup is "find the team in this bracket game's own division whose rank/slot number matches
the seed slot." In a medal flight that team is a disposable placeholder, so overwriting it is
harmless. But if a bracket game is placed inside a real RR pool division (as happened with a set of
consolation games added to a pool), the "twins" it finds are the **live competitors** in that pool.
The routine then overwrites those real teams' names and club references with the identities of other
teams — corrupting the standings, renaming teams on web and mobile, and pointing the games at live
team ids.

**Observed symptom:** after the pool's games were scored, several real teams in that pool showed up
under other teams' names (including names bleeding in from a different pool), because the seed-copy
always writes into the bracket game's own division regardless of which pool the seed came from.

## Root cause

The decision "should this seed use the twin-copy behavior?" is being made at the wrong granularity.
It's a job-wide flag, but the real question is **per game**: is this bracket slot being fed from a
different age group than the game itself?

- Different age group → yes, twin-copy (that's the whole point of reseeding — avoid sharing a team
  row across age groups).
- Same age group → no, seed normally (ordinary bracket seeding: point the game at the real team by
  its real id, exactly like every single-age-group tournament; there's no cross-age-group sharing to
  avoid, so there's nothing to protect against).

The flag is a coarse approximation of that per-game question, and it's wrong precisely for
same-age-group games in a flagged job.

## The fix

Replace the job-level switch in the runtime seeding routine with a per-game age-group comparison:
compare the age group of the pool that just finished (the seed source) against the age group of the
bracket game being filled. Use the twin-copy path only when they **differ**; otherwise take the
normal seeding path.

This is strictly more correct than the flag (it can only ever agree with a correct flag or fix a
wrong one), it needs no configuration, and it makes the corruption impossible — a bracket game
sitting in a real pool's age group will always seed normally and never overwrite a live team.

## What to preserve

Keep the flag (or its nextgen equivalent) only as an **editor/UI gate** — it decides whether the
seeding editor lets an operator pick a seed source from a different age group in the first place. It
should no longer influence runtime seeding behavior. (Long-term the flag could be retired entirely by
always offering the cross-age-group picker defaulted to the game's own age group, but that's a
separate cleanup, not required for the fix.)

## Reproduce / verify

1. In a flagged reseed tournament, add a bracket/consolation game inside a real RR pool division,
   seeded from that pool's (same-age-group) standings.
2. Score the pool. **Before the fix:** live teams in that pool get renamed. **After the fix:** the
   pool's teams are untouched and the game points at the real qualifying teams by their own ids.
3. Confirm a genuine cross-age-group medal flight still seeds correctly (placeholders still adopt the
   qualifiers' identities).
