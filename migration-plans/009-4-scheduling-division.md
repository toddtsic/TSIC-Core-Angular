# Migration Plan 009-4: ScheduleDivision/Index → Schedule by Division

## Context

The Schedule Division page is **step 4** and the **heart** of the scheduling pipeline. This is where the auto-scheduling algorithm lives — it takes the pairings (009-2) and timeslots (009-3) and produces the actual game schedule. It is by far the most complex controller in the legacy system (1,433 lines) and contains the core business logic that customers consider "the best in the business."

The page has a three-column layout:
1. **Division Navigator** — select which agegroup/division to schedule
2. **Pairings & Teams** — view pairing templates and division teams (with inline editing)
3. **Schedule Grid** — dynamic date×field grid showing scheduled games and open slots

Admins can auto-schedule an entire division with one click, or manually place individual games by clicking a pairing then clicking an open field slot. Games can be moved, swapped, or deleted directly from the grid.

**Legacy URL:** `/ScheduleDivision/Index` (Controller=ScheduleDivision, Action=Index)

**Legacy Controller:** `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Scheduling/ScheduleDivisionController.cs` (1,433 lines)
**Legacy View:** `reference/TSIC-Unify-2024/TSIC-Unify/Views/ScheduleDivision/Index.cshtml` (658 lines)

---

## 1. Legacy Strengths (Preserve These!)

- **Auto-schedule algorithm** — iterates pairings by round/game, finds next available timeslot, places game; respects field capacity (MaxGamesPerField) and game intervals
- **Manual placement workflow** — click pairing → click open slot → game created; intuitive point-and-click
- **Game move/swap** — click scheduled game → click destination (empty = move, occupied = swap); automatic email notifications to coaches
- **Dynamic schedule grid** — columns = fields, rows = date/times; cells show game details or "OPEN FIELD SLOT"
- **Who Plays Who matrix** — N×N grid showing how many times each team pair meets; highlights pairs that never play (0 games)
- **Division team ranking** — editable team ranks with automatic schedule recalculation when ranks change
- **Bracket advancement** — `ScheduleRecord_RecalcValues()` auto-advances winners through bracket games
- **Custom game insertion** — add games outside the pairing system for exhibition or consolation matches
- **Cascading delete** — `DeleteDivGames()` cleans up DeviceGids, BracketSeeds, and Schedule records
- **Age group color coding** — games in the grid show their agegroup color for quick identification in multi-division views

## 2. Legacy Pain Points (Fix These!)

- **jqGrid with dynamic columns** — fragile column generation, poor responsiveness, no sticky headers
- **Three-column layout breaks on smaller screens** — pairings and schedule fight for space
- **No progress indicator** — auto-schedule runs synchronously; 100+ games take several seconds with no feedback
- **No undo for auto-schedule** — once scheduled, must manually delete all games
- **No conflict detection** — doesn't warn if same team is double-booked on same date
- **Move game email is fire-and-forget** — no delivery confirmation, error handling is minimal
- **Direct SqlDbContext** — 1,433-line controller with inline database queries

## 3. Modern Vision

**Recommended UI: Responsive Two-Panel Layout with Dynamic Schedule Grid**

Split into two panels: left panel for division context (navigator + pairings + teams), right panel for the schedule grid. The schedule grid uses Syncfusion Grid with frozen date column and dynamically generated field columns.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Schedule by Division                                                        │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│ ┌─ Division Context ────────────────┐ ┌─ Schedule Grid ─────────────────────┐│
│ │                                    │ │                                     ││
│ │ ▸ U8                               │ │ U10 Gold (8 teams)                  ││
│ │ ▾ U10                              │ │                                     ││
│ │   ● Gold (8)   ◄── selected       │ │ [Auto-Schedule] [Delete All Games]  ││
│ │   ○ Silver (8)                     │ │                                     ││
│ │   ○ Bronze (8)                     │ │ ┌──────────────────────────────┐    ││
│ │ ▸ U12                              │ │ │ Date/Time    │Cedar Pk│Lake │    ││
│ │                                    │ │ ├──────────────────────────────┤    ││
│ │ ── Pairings ──                     │ │ │Sat 3/1 8:00  │ R1 G1  │ R1 G2│   ││
│ │ ┌────────────────────────┐         │ │ │              │ 1v2    │ 3v4  │   ││
│ │ │ G# │ Rnd │ T1│T2│ Avl │         │ │ │Sat 3/1 9:00  │ R1 G3  │ R1 G4│   ││
│ │ ├────────────────────────┤         │ │ │              │ 5v6    │ 7v8  │   ││
│ │ │  1 │  1  │ 1 │ 2│ ● ◄─ click   │ │ │Sat 3/1 10:00 │ OPEN ◄─│ OPEN │   ││
│ │ │  2 │  1  │ 3 │ 4│ ○   │         │ │ │              │ click  │      │   ││
│ │ │  3 │  1  │ 5 │ 6│ ○   │         │ │ │Sun 3/2 9:00  │ OPEN   │ OPEN │   ││
│ │ │  4 │  1  │ 7 │ 8│ ○   │         │ │ │...                              │ ││
│ │ │...                     │         │ │ └──────────────────────────────┘    ││
│ │ └────────────────────────┘         │ │                                     ││
│ │                                    │ │ [+ Custom Game]                     ││
│ │ ── Teams ──                        │ │                                     ││
│ │ ┌─────────────────────────┐        │ │ ── Move Mode ──                     ││
│ │ │ # │ Club       │ Team   │        │ │ Selected: Game #1 (1v2, R1)         ││
│ │ ├─────────────────────────┤        │ │ Click a destination slot...         ││
│ │ │ 1 │ Storm SC   │ Blue   │        │ │                                     ││
│ │ │ 2 │ Lonestar   │ Red    │        │ │                                     ││
│ │ │ 3 │ Texans FC  │ Gold   │        │ │                                     ││
│ │ │...                      │        │ │                                     ││
│ │ └─────────────────────────┘        │ │                                     ││
│ │                                    │ │                                     ││
│ └────────────────────────────────────┘ └─────────────────────────────────────┘│
│                                                                              │
│ ── Who Plays Who (collapsible) ──                                           │
│ ┌──────────────────────────┐                                                │
│ │     │ T1│ T2│ T3│ T4│...│  Yellow cells = 0 games (potential concern)     │
│ │ T1  │ — │ 3 │ 2 │ 3 │   │                                                │
│ │ ...                      │                                                │
│ └──────────────────────────┘                                                │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Key improvements:**
- ✅ **Progress indicator** — auto-schedule shows progress bar ("Scheduling game 15 of 48...")
- ✅ **Conflict detection** — highlight when same team is scheduled twice on same date
- ✅ **Undo auto-schedule** — "Delete All Games" with typed confirmation (more deliberate than legacy JS confirm)
- ✅ **Responsive grid** — horizontal scroll with frozen date column for narrow viewports
- ✅ **Sticky headers** — field column names stay visible while scrolling vertically
- ✅ **Game cell interactivity** — click to place, click to select for move, click to delete — all with visual state indicators

---

## 4. Security

- **Authorization:** `[Authorize(Policy = "AdminOnly")]`
- **Exception:** `FieldDirectionsData` is `[AllowAnonymous]` (public map directions)
- **Scoping:** JWT `regId` → `jobId` → `leagueId` + `season` + `year` (via `ResolveLeagueSeasonAsync` pattern)
- **Email notifications:** Move/swap game sends to coach emails associated with affected teams

---

## 5. Core Algorithms

### Auto-Schedule Algorithm (`AutoScheduleDivByAgfields`)

This is the primary scheduling engine. It must be ported faithfully.

```
Input:
  - DivId (division to schedule)
  - From JWT: JobId, LeagueId, Season, Year

Algorithm:
  1. DELETE all existing games for this division
  2. Get team count for division
  3. QUERY TimeslotsLeagueSeasonDates for agegroup (or division if div-specific dates exist)
     - If none found: try agegroup-level dates
  4. QUERY TimeslotsLeagueSeasonFields for agegroup (or division if div-specific fields exist)
     - If none found: try agegroup-level fields
  5. QUERY PairingsLeagueSeason WHERE TCnt = teamCount, T1Type = "T", T2Type = "T"
     - ORDER BY Rnd, GameNumber

  6. FOR EACH pairing:
     a. Skip if already scheduled (shouldn't happen after step 1, but safety check)
     b. FindNextAvailableTimeslot():
        FOR EACH date in dates:
          FOR EACH field WHERE dow matches date's day-of-week:
            FOR game_index = 0 to MaxGamesPerField - 1:
              gameTime = date + StartTime + (game_index × GamestartInterval minutes)
              IF no Schedule record exists at this time/field:
                RETURN {FieldId, gameTime}
        RETURN null (no available slot)
     c. IF slot found:
        - Create Schedule record:
          { GDate, FieldId, DivId, AgegroupId, LeagueId, Season, Year, JobId,
            T1No = pairing.T1, T2No = pairing.T2,
            T1Type = pairing.T1Type, T2Type = pairing.T2Type,
            Rnd = pairing.Rnd, GStatusCode = 1 (Scheduled),
            Annotations, CalcTypes from pairing }
        - Call ScheduleRecord_RecalcValues(gid):
          1. UpdateGameIds() — map T1No/T2No to real TeamIds via DivRank
          2. AutoadvanceSingleEliminationBracketGameWinner() — bracket logic
          3. PopulateBracketSeeds() — update BracketSeeds
```

### Game Move/Swap Algorithm (`MoveGame`)

```
Input: sourceGid, targetGDate, targetFieldId

1. GET record A = Schedule WHERE Gid = sourceGid
2. GET record B = Schedule WHERE GDate = targetGDate AND FieldId = targetFieldId

3a. IF B is null (empty slot):
    - A.GDate = targetGDate
    - A.FieldId = targetFieldId
    - A.FName = field lookup
    - SaveChanges

3b. IF B exists (occupied slot — SWAP):
    - temp = {A.GDate, A.FieldId, A.FName}
    - A.GDate = B.GDate; A.FieldId = B.FieldId; A.FName = B.FName
    - B.GDate = temp.GDate; B.FieldId = temp.FieldId; B.FName = temp.FName
    - SaveChanges

4. Send email notifications to affected team coaches (if configured)
5. Increment RescheduleCount
```

### Schedule Recalculation (`ScheduleRecord_RecalcValues`)

Called after every game creation/modification:
1. **UpdateGameIds** — For each game in division, maps `T1No`/`T2No` (rank position) to actual `TeamId`/`TeamName` from Teams table using `DivRank`
2. **AutoadvanceSingleEliminationBracketGameWinner** — When both teams have scores and game is bracket type, populates the next bracket game's team slot with the winner
3. **PopulateBracketSeeds** — Creates/updates `BracketSeeds` records linking games to seeded divisions and ranks

---

## 6. Database Entities

### Schedule (primary — extends existing entity)
| Column | Type | Notes |
|--------|------|-------|
| `Gid` | int (PK) | Auto-increment game ID |
| `JobId` | Guid (FK) | |
| `LeagueId` | Guid (FK) | |
| `AgegroupId` | Guid (FK) | |
| `AgegroupName` | string | Denormalized |
| `DivId` | Guid (FK) | Primary division |
| `DivName` | string | Denormalized |
| `Div2Id` | Guid? (FK) | Cross-division games |
| `Div2Name` | string? | Denormalized |
| `Season`, `Year` | string | |
| `GNo` | int? | Game number |
| `GDate` | DateTime | Game date/time |
| `FieldId` | Guid (FK) | |
| `GStatusCode` | int | 1=Scheduled, 2=Completed, etc. |
| `Rnd` | byte | Round number |
| `T1No`, `T2No` | int/byte | Team rank positions |
| `T1Type`, `T2Type` | string | T/Q/S/F/X/Z/Y/C/RRD |
| `T1Id`, `T2Id` | Guid? (FK) | Resolved team IDs |
| `T1Name`, `T2Name` | string? | Resolved team names |
| `T1Score`, `T2Score` | int? | Game scores |
| `T1Ann`, `T2Ann` | string? | Annotations |
| `T1GnoRef`, `T2GnoRef` | int? | Bracket game references |
| `T1CalcType`, `T2CalcType` | string? | Winner/Loser/Placement |
| `RefCount` | decimal? | Referee count |
| `RescheduleCount` | int? | Times rescheduled |
| `LebUserId` | string | Audit |
| `Modified` | DateTime | Audit |

### BracketSeeds
| Column | Type | Notes |
|--------|------|-------|
| `AId` | int (PK) | |
| `Gid` | int (FK) | → Schedule |
| `WhichSide` | int? | Bracket position |
| `T1SeedDivId` | Guid? (FK) | Division T1 qualified from |
| `T1SeedRank` | int? | T1's rank in that division |
| `T2SeedDivId` | Guid? (FK) | Division T2 qualified from |
| `T2SeedRank` | int? | T2's rank in that division |

---

## 7. Implementation Steps

### Phase 1: Backend — DTOs

**File:** `TSIC.Contracts/Dtos/Scheduling/ScheduleDivisionDtos.cs`

```csharp
// ── Response DTOs ──

public record ScheduleGameDto
{
    public required int Gid { get; init; }
    public required DateTime GDate { get; init; }
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public required int Rnd { get; init; }
    public required string AgDivLabel { get; init; }   // "U10:Gold"
    public required string T1Label { get; init; }       // "(P1) Storm Blue"
    public required string T2Label { get; init; }       // "(T2) Lonestar Red"
    public string? Color { get; init; }                  // Agegroup color
    public required string T1Type { get; init; }
    public required string T2Type { get; init; }
}

public record ScheduleGridResponse
{
    /// Column headers: Date/Time + field names
    public required List<string> ColNames { get; init; }
    /// Column field IDs for placement (index 0 = date, index 1+ = fields)
    public required List<Guid?> ColFieldIds { get; init; }
    /// Grid rows: each row = one timeslot, cells = game details or null (open)
    public required List<ScheduleGridRow> Rows { get; init; }
}

public record ScheduleGridRow
{
    public required DateTime GDate { get; init; }
    /// One cell per field column — null means open slot
    public required List<ScheduleGameDto?> Cells { get; init; }
}

public record DivisionTeamDto
{
    public required Guid TeamId { get; init; }
    public required int DivRank { get; init; }
    public required string ClubName { get; init; }
    public required string TeamName { get; init; }
}

public record AutoScheduleProgressDto
{
    public required int TotalPairings { get; init; }
    public required int ScheduledCount { get; init; }
    public required int FailedCount { get; init; }  // no available timeslot
}

// ── Request DTOs ──

public record PlaceGameRequest
{
    public required int PairingAi { get; init; }     // PairingsLeagueSeason.Ai
    public required DateTime GDate { get; init; }
    public required Guid FieldId { get; init; }
    public required Guid AgegroupId { get; init; }
    public required Guid DivId { get; init; }
}

public record MoveGameRequest
{
    public required int Gid { get; init; }
    public required DateTime TargetGDate { get; init; }
    public required Guid TargetFieldId { get; init; }
}

public record CustomGameRequest
{
    public required DateTime GDate { get; init; }
    public required Guid FieldId { get; init; }
    public required Guid T1TeamId { get; init; }
    public required Guid T2TeamId { get; init; }
    public required Guid DivId { get; init; }
}

public record EditTeamRankRequest
{
    public required Guid TeamId { get; init; }
    public required int NewDivRank { get; init; }
    public string? NewTeamName { get; init; }
}

public record DeleteDivGamesRequest
{
    public required Guid DivId { get; init; }
}
```

### Phase 2: Backend — Extend IScheduleRepository

Add to the existing `IScheduleRepository`:

```
New Methods:
- GetDivisionScheduleAsync(Guid divId, Guid leagueId, string season, string year) → List<Schedule>
- GetScheduleGridAsync(Guid agegroupId, Guid divId, int teamCount, ...) → ScheduleGridResponse
- CreateGameAsync(Schedule game) → Schedule
- DeleteGameAsync(int gid) → void (with cascade: DeviceGids, BracketSeeds)
- DeleteDivGamesAsync(Guid divId, Guid leagueId, string season, string year) → void
- MoveGameAsync(int gid, DateTime targetGDate, Guid targetFieldId) → void
- SwapGamesAsync(int gidA, int gidB) → void
- GetDivisionTeamsAsync(Guid divId) → List<Teams> (ordered by DivRank)
- UpdateTeamRankAsync(Guid teamId, int newRank) → void
- GetNextAvailableTimeslotAsync(...) → (Guid fieldId, DateTime gDate)?
- RecalcScheduleValuesAsync(int gid) → void
- UpdateGameIdsAsync(int gid) → void
- AutoadvanceBracketWinnerAsync(int gid) → void
```

### Phase 3: Backend — Service

**Interface:** `TSIC.Contracts/Services/IScheduleDivisionService.cs`
**Implementation:** `TSIC.API/Services/Scheduling/ScheduleDivisionService.cs`

The service contains the auto-schedule orchestration and the `ScheduleRecord_RecalcValues` pipeline. The three sub-operations (UpdateGameIds, AutoadvanceBracketWinner, PopulateBracketSeeds) should be methods on the service, calling into the repository for data access.

### Phase 4: Backend — Controller

**File:** `TSIC.API/Controllers/ScheduleDivisionController.cs`

```
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/[controller]")]

GET    /api/schedule-division/agegroups             → GetAgegroupsWithDivisionsAsync()
GET    /api/schedule-division/{divId}/pairings       → GetDivisionPairingsAsync(divId)
GET    /api/schedule-division/{divId}/teams           → GetDivisionTeamsAsync(divId)
GET    /api/schedule-division/{divId}/grid            → GetScheduleGridAsync(divId)
GET    /api/schedule-division/who-plays-who?tCount=N → GetWhoPlaysWhoAsync(tCount)

POST   /api/schedule-division/place-game             → PlaceGameAsync(request)
POST   /api/schedule-division/move-game              → MoveGameAsync(request)
POST   /api/schedule-division/custom-game            → AddCustomGameAsync(request)
POST   /api/schedule-division/auto-schedule/{divId}  → AutoScheduleDivAsync(divId)
PUT    /api/schedule-division/team-rank              → EditTeamRankAsync(request)

DELETE /api/schedule-division/game/{gid}             → DeleteGameAsync(gid)
POST   /api/schedule-division/delete-div-games       → DeleteDivGamesAsync(request)

[AllowAnonymous]
GET    /api/schedule-division/field-directions/{fieldId} → GetFieldDirectionsAsync(fieldId)
```

### Phase 5: Frontend — Generate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

### Phase 6: Frontend — Components

**Main:** `src/app/views/admin/scheduling/schedule-division/schedule-division.component.ts`

Key signals:
- `selectedDivision` — signal<DivisionSummaryDto | null>
- `pairings` — signal<PairingDto[]>
- `divisionTeams` — signal<DivisionTeamDto[]>
- `scheduleGrid` — signal<ScheduleGridResponse | null>
- `selectedPairing` — signal<PairingDto | null> (for manual placement mode)
- `selectedGame` — signal<ScheduleGameDto | null> (for move mode)
- `isAutoScheduling` — signal<boolean>
- `autoScheduleProgress` — signal<AutoScheduleProgressDto | null>

Child components:
- `division-navigator.component.ts` — shared with 009-2, 009-3; filters out "Dropped Teams" (exact, case-insensitive) and agegroups starting with "WAITLIST"; sorted alphabetically
- `schedule-grid.component.ts` — dynamic Syncfusion Grid with clickable cells
- `who-plays-who-matrix.component.ts` — shared with 009-2

**Placement workflow (signals-driven):**
1. User clicks pairing row → `selectedPairing.set(pairing)`
2. Grid cells become clickable (highlight open slots)
3. User clicks open cell → call `PlaceGameAsync` → refresh grid
4. `selectedPairing.set(null)` to exit placement mode

### Phase 7: Frontend — Route

```typescript
{
  path: 'admin/scheduling/schedule-division',
  loadComponent: () => import('./views/admin/scheduling/schedule-division/schedule-division.component')
    .then(m => m.ScheduleDivisionComponent),
  canActivate: [authGuard],
  data: { roles: ['SuperUser', 'Director', 'SuperDirector'] }
}
```

### Phase 8: Testing

**Critical — these tests validate the core scheduling engine:**

- **Auto-schedule correctness:** Generate schedule for 8-team division with 7 rounds → verify all 28 games placed, no conflicts
- **Timeslot capacity:** 4 fields × 6 MaxGames = 24 slots; schedule 24 games → verify all placed; add 25th → verify "no available slot" handling
- **Bracket advancement:** Score a semifinal game → verify winner auto-populates in finals
- **Game move:** Move game from Field A to Field B → verify dates/fields updated, old slot is empty
- **Game swap:** Move game to occupied slot → verify both games swap positions
- **Team rank edit:** Swap rank 1 and rank 3 → verify all scheduled games update T1Name/T2Name
- **Division delete cascade:** Delete all games → verify BracketSeeds and DeviceGids also cleaned up
- **Who Plays Who matrix:** After scheduling, matrix should be symmetric and match actual game count
- **Email notification:** Move game → verify email sent to affected team contacts (when configured)
