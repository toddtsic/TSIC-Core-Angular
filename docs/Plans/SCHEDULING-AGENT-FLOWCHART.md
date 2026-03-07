# Scheduling Agent — Conversational Flowchart

## Context

The current bulk-date-assign modal requires O(agegroups × dates) manual decisions. Replace it with a **conversational scheduling agent** that asks strategic questions, reads existing configuration where available, and proposes a complete schedule configuration. The agent reduces director decisions from ~24 tactical choices to ~5 strategic ones.

---

## What the Agent Already Knows (reads from existing data)

| Data | Source |
|------|--------|
| Event type | `jobTypeName` from `JobService.currentJob()` |
| Agegroups | names, team counts, division counts from `AgegroupWithDivisionsDto[]` |
| Rounds needed per AG | `maxPairingRound` / prior year / RR formula (n-1 even, n odd) |
| Assigned fields | `FieldsLeagueSeason` → `Fields.FName` (event-level field inventory) |
| Field-to-AG/div mappings | `TimeslotsLeagueSeasonFields` (if already configured) |
| Existing dates | `TimeslotsLeagueSeasonDates` (if already configured) |
| Field config (GSI, start time, max/field) | `TimeslotsLeagueSeasonFields` per row |

The agent **reads before asking**. If data exists, it confirms. If not, it collects.

---

## localStorage Schema (Scenario B)

Typesafe interface for persisting agent answers per job:

```typescript
interface ScheduleAgentConfig {
  jobId: string;
  savedAt: string;                      // ISO timestamp
  eventType: 'league' | 'tournament';
  // Calendar
  dates: string[];                      // ISO dates
  dow?: string;                         // league DOW
  skipWeeks?: string[];                 // league skip dates
  // Structure
  agDaySpan: 'single' | 'multi' | 'mix';
  divDaySpan?: 'same' | 'different';
  agToDayMap?: Record<string, string>;  // agId → ISO date (single-day-per-AG)
  roundDistribution?: Record<string, Record<string, number>>; // agId → date → rounds
  // Fields
  fieldIds: string[];
  fieldMappingScope: 'shared' | 'per-ag' | 'per-div';
  fieldMapping?: Record<string, string[]>; // agId or divId → fieldId[]
  // Time config
  gsiScope: 'same' | 'per-ag';
  gsi: number | Record<string, number>;   // single value or agId → value
  startTimeScope: 'same' | 'per-ag';
  startTime: string | Record<string, string>;
  maxGamesPerField: number;
  // League-specific
  oddDivByeHandling?: boolean;           // bPlayOddDivisionByeTeam
}
```

---

## Flowchart

```
START
  │
  ▼
╔═══════════════════════════════════════════════════════════════╗
║  0. SCENARIO DETECTION + DATA LOAD                            ║
║                                                               ║
║  [READ EXISTING DATA]                                         ║
║  Load: jobTypeName, agegroups, team counts, divisions,        ║
║  assigned fields, existing timeslot config, existing dates    ║
║                                                               ║
║  Then determine input scenario (check in order):              ║
║                                                               ║
║  ① localStorage has saved agent config for this jobId?        ║
║     → SCENARIO B: RE-BUILD                                    ║
║     Load all previous answers from ScheduleAgentConfig.       ║
║     Every subsequent node starts in CONFIRM mode.             ║
║     Fastest path — potentially 1 exchange if nothing changed. ║
║                                                               ║
║  ② Prior year job exists? (GetPriorYearJobAsync)              ║
║     → SCENARIO A: PRIOR YEAR                                  ║
║     Pre-load from prior year:                                 ║
║       AVAILABLE: GSI, start time, max/field, rounds per AG    ║
║       GAPS: field mappings, specific dates, DOW pattern       ║
║     Nodes with prior-year data → CONFIRM mode.                ║
║     Nodes without → COLLECT mode.                             ║
║                                                               ║
║  ③ Neither                                                    ║
║     → SCENARIO C: NEW EVENT                                   ║
║     Global defaults: GSI=60, start=8:00AM, max/field=8,       ║
║     rounds from RR formula (n-1 even, n odd).                 ║
║     All subsequent nodes in COLLECT mode.                     ║
║                                                               ║
║  Output: scenario flag + pre-loaded defaults map              ║
║  (each node checks: do I have a pre-loaded value? → confirm   ║
║   or collect accordingly)                                     ║
╚══════════════════════╤════════════════════════════════════════╝
                       │
                       ▼
╔═══════════════════════════════════════╗
║  1. CONFIRM EVENT TYPE                ║
║  Detected: league or tournament       ║
║  Director confirms or corrects        ║
╚═══════════════════════════════════════╝
  │
  ├═══ LEAGUE TRAIL ════════════════════════════════════════════╗
  │                                                             ║
  │  ┌─────────────────────────────────┐                        ║
  │  │ 2L. GAME DAY DOW               │                        ║
  │  │ What day of week do you play?   │                        ║
  │  │ (e.g., Saturday)               │                        ║
  │  └────────────┬────────────────────┘                        ║
  │               │                                             ║
  │  ┌─────────────────────────────────┐                        ║
  │  │ 3L. SEASON START DATE           │                        ║
  │  │ When does the season start?     │                        ║
  │  │ (Agent calculates end date from │                        ║
  │  │  max rounds needed + DOW)       │                        ║
  │  └────────────┬────────────────────┘                        ║
  │               │                                             ║
  │  ┌─────────────────────────────────┐                        ║
  │  │ 4L. SKIP WEEKS                  │                        ║
  │  │ Any holidays or breaks?         │                        ║
  │  │ (Agent adjusts end date)        │                        ║
  │  └────────────┬────────────────────┘                        ║
  │               │                                             ║
  │  ┌─────────────────────────────────┐                        ║
  │  │ 5L. ODD DIVISION BYE HANDLING   │                        ║
  │  │ (only if any div has odd count) │                        ║
  │  │ Bye team sits out (default)     │                        ║
  │  │   OR                            │                        ║
  │  │ Bye team plays, another team    │                        ║
  │  │ gets double-header              │                        ║
  │  └────────────┬────────────────────┘                        ║
  │               │                                             ║
  │               ╚═══════════════════ MERGE TO COMMON ═════════╝
  │
  ├═══ TOURNAMENT TRAIL ════════════════════════════════════════╗
  │                                                             ║
  │  ┌─────────────────────────────────┐                        ║
  │  │ 2T. MULTI-DAY?                  │                        ║
  │  │ Single day or multiple days?    │                        ║
  │  └────────────┬────────────────────┘                        ║
  │               │                                             ║
  │        ┌──────┴──────┐                                      ║
  │        │             │                                      ║
  │     SINGLE        MULTI-DAY                                 ║
  │        │             │                                      ║
  │        │    ┌────────────────────────────┐                  ║
  │        │    │ 3T. TOURNAMENT DATES       │                  ║
  │        │    │ What are the dates?        │                  ║
  │        │    └───────────┬────────────────┘                  ║
  │        │                │                                   ║
  │        │    ┌────────────────────────────┐                  ║
  │        │    │ 4T. AG DAY SPAN            │                  ║
  │        │    │ Does each AG play in a     │                  ║
  │        │    │ single day or span days?   │                  ║
  │        │    └───────────┬────────────────┘                  ║
  │        │                │                                   ║
  │        │     ┌──────────┼──────────┐                        ║
  │        │     │          │          │                        ║
  │        │   SINGLE    MULTI-DAY    MIX                       ║
  │        │   PER AG    PER AG    (both)                       ║
  │        │     │          │          │                        ║
  │        │     │          │          │                        ║
  │        │     │    ┌─────────────────────────┐               ║
  │        │     │    │ 5T. DIV DAY SPAN        │               ║
  │        │     │    │ (only if AG spans days)  │               ║
  │        │     │    │ Do divisions within an   │               ║
  │        │     │    │ AG have different game    │               ║
  │        │     │    │ day lists?               │               ║
  │        │     │    └──────────┬──────────────┘               ║
  │        │     │               │                              ║
  │        │     │        ┌──────┴──────┐                       ║
  │        │     │      SAME          DIFFERENT                 ║
  │        │     │     (inherit AG)  (per-div dates)            ║
  │        │     │        │              │                      ║
  │        │     └────────┴──────────────┘                      ║
  │        │              │                                     ║
  │   ┌────┴──────────────┘                                     ║
  │   │                                                         ║
  │   │  ┌──────────────────────────────┐                       ║
  │   │  │ 6T. TOURNAMENT DATE(S)       │                       ║
  │   │  │ Single-day: what date?       │                       ║
  │   │  │ Multi-day: already collected │                       ║
  │   │  └───────────┬─────────────────┘                        ║
  │   │              │                                          ║
  │   │  ┌──────────────────────────────┐                       ║
  │   │  │ 7T. AG-TO-DAY ASSIGNMENT     │                       ║
  │   │  │ (if single-day-per-AG)       │                       ║
  │   │  │ Which AGs play which day?    │                       ║
  │   │  │ Agent proposes split based   │                       ║
  │   │  │ on field capacity + rounds   │                       ║
  │   │  └───────────┬─────────────────┘                        ║
  │   │              │                                          ║
  │   │  ┌──────────────────────────────┐                       ║
  │   │  │ 8T. ROUND DISTRIBUTION       │                       ║
  │   │  │ (if multi-day-per-AG)        │                       ║
  │   │  │ How many rounds per day?     │                       ║
  │   │  │ Agent proposes: even split,  │                       ║
  │   │  │ front-load, or back-load     │                       ║
  │   │  └───────────┬─────────────────┘                        ║
  │   │              │                                          ║
  │   │              ╚══════════════════ MERGE TO COMMON ═══════╝
  │   │
  ▼   ▼
╔═══════════════════════════════════════════════════════════════╗
║                    COMMON TAIL                                ║
║  (both league and tournament trails converge here)            ║
╚═══════════════════════════════════════════════════════════════╝
  │
  ▼
┌───────────────────────────────────────┐
│ C1. FIELD INVENTORY                   │
│ What fields are available?            │
│                                       │
│ If FieldsLeagueSeason has data:       │
│   → Confirm: "I see Fields 1-4"      │
│ If empty:                             │
│   → Collect: "What fields do you     │
│     have available?"                  │
└────────────────┬──────────────────────┘
                 │
┌───────────────────────────────────────┐
│ C2. FIELD MAPPING SCOPE               │
│ How are fields assigned?              │
│                                       │
│ If TimeslotsLeagueSeasonFields exist: │
│   → Confirm existing mapping          │
│ If not:                               │
│   → Collect scope:                    │
│     • All AGs share all fields        │
│     • Per agegroup                    │
│     • Per division                    │
│                                       │
│ If per-AG or per-div:                 │
│   → Collect specifics (which fields   │
│     for which AGs/divs)              │
└────────────────┬──────────────────────┘
                 │
┌───────────────────────────────────────┐
│ C3. GSI SCOPE + VALUE                 │
│ Game start interval                   │
│                                       │
│ • Same across all AGs → collect once  │
│ • Different per AG → collect per AG   │
│                                       │
│ If existing config has values:        │
│   → Confirm                           │
└────────────────┬──────────────────────┘
                 │
┌───────────────────────────────────────┐
│ C4. START TIME                        │
│ When do games begin?                  │
│                                       │
│ • Same for all AGs → collect once     │
│ • Different per AG → collect per AG   │
│                                       │
│ If existing config has values:        │
│   → Confirm                           │
└────────────────┬──────────────────────┘
                 │
┌───────────────────────────────────────┐
│ C5. MAX GAMES PER FIELD               │
│ Field capacity per day                │
│                                       │
│ Agent can calculate from GSI +        │
│ available hours, or director states   │
│ a hard cap                            │
└────────────────┬──────────────────────┘
                 │
                 ▼
╔═══════════════════════════════════════╗
║  PROPOSE CONFIGURATION                ║
║                                       ║
║  Agent summarizes everything:         ║
║  - Calendar (dates + rounds/day)      ║
║  - Field assignments                  ║
║  - Time config (GSI, start, capacity) ║
║  - Special rules (bye handling, etc.) ║
║                                       ║
║  Shows as confirmation matrix:        ║
║  AG × Date with rounds per cell       ║
║                                       ║
║  Director: confirms, adjusts, or      ║
║  restarts specific section            ║
╚════════════════╤══════════════════════╝
                 │
                 ▼
╔═══════════════════════════════════════╗
║  APPLY                                ║
║                                       ║
║  Execute via bulkAssignDate API       ║
║  (one call per date, sequential)      ║
║  Show progress + results              ║
╚═══════════════════════════════════════╝
```

---

## Key Design Principles

1. **Read before ask** — If data exists in the system, confirm it. Don't ask for what you already know.
2. **Shape before specifics** — Early nodes determine scope (same vs different, shared vs mapped). Later nodes collect values only where the director indicated variation.
3. **Funnel narrows** — Each branch eliminates questions. A single-day tournament with shared fields and uniform GSI might only need 5 exchanges total.
4. **Propose, don't just collect** — The agent should suggest (e.g., AG-to-day split, round distribution) based on what it knows, not just ask open-ended questions.
5. **Concepts are independent** — Field mapping ≠ waves. GSI ≠ start time. Day span ≠ field assignment. Never conflate.
6. **Even field distribution** — Engine placement rule (not a flowchart question). The scheduler should balance field assignments across teams so no team is punished/rewarded by field quality.

---

## Data Model Support (verified)

| Feature | DB Column | Nullable | Engine Support |
|---------|-----------|----------|----------------|
| Per-division dates | `TimeslotsLeagueSeasonDates.DivId` | Yes (nullable FK) | Fallback: div-specific → AG-level |
| Per-division fields | `TimeslotsLeagueSeasonFields.DivId` | Yes (nullable FK) | Same fallback pattern |
| Per-AG GSI | `TimeslotsLeagueSeasonFields.GamestartInterval` | Per row | Already per-AG/div/field |
| Per-AG start time | `TimeslotsLeagueSeasonFields.StartTime` | Per row | Already per-AG/div/field |
| Per-AG max games | `TimeslotsLeagueSeasonFields.MaxGamesPerField` | Per row | Already per-AG/div/field |
| Odd-div bye handling | **NEW** `bPlayOddDivisionByeTeam` | N/A | **Not yet implemented** — needs new column or config |
| Even field distribution | N/A (engine rule) | N/A | **Not yet implemented** — placement constraint |

---

## Open Design Questions

1. **Where does the agent live?** — Modal? Side panel? Full page? Inline chat in schedule-division?
2. **Waves** — Not yet placed in the flowchart. Where do they fit? Are they a common-tail node or event-type-specific?
3. **Strategy profiles** — The existing strategy grid (placement + gap pattern) per division — does the agent collect this, or is it a separate concern handled after the agent finishes?
4. **Restart granularity** — If the director wants to change one answer mid-flow, does the agent rewind to that node, or restart from scratch?
5. **Odd-div bye handling storage** — Where does `bPlayOddDivisionByeTeam` live? Job-level flag? Per-agegroup? Per-division?
