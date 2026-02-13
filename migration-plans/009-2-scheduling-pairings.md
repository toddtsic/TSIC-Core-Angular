# Migration Plan 009-2: Pairings/Index → Manage Pairings

## Context

The Pairings page is **step 2** of the scheduling pipeline. After fields are defined (009-1), administrators generate the matchup templates that define "who plays whom" for each division. The system supports two pairing modes:

1. **Round-Robin** — bulk-generated from `Masterpairingtable` (pre-computed matchup templates for any team count). Admin selects number of rounds, system inserts all pairings at once.
2. **Single-Elimination Brackets** — generated from `BracketDataSingleElimination` seed data. Admin clicks a bracket level (Quarterfinals, Semifinals, Finals, etc.) and the system auto-cascades through all subsequent rounds.

Pairings are **abstract templates** — they reference team *positions* (1, 2, 3...) not actual teams. The mapping from position to real team happens later when games are scheduled (009-4).

This is algorithmically the most interesting module in the scheduling suite. The pairing engine, bracket cascade, and game-reference system are finely tuned and must be preserved exactly.

**Legacy URL:** `/Pairings/Index` (Controller=Pairings, Action=Index)
**Menu DB entry:** Controller=Scheduling, Action=ManageLeagueSeasonPairings (translated via `legacyRouteMap` in client-menu component)

**Legacy Controller:** `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Scheduling/PairingsController.cs`
**Legacy View:** `reference/TSIC-Unify-2024/TSIC-Unify/Views/Pairings/Index.cshtml`

---

## 1. Legacy Strengths (Preserve These!)

- **Masterpairingtable lookup** — pre-computed round-robin pairings for all team counts (4–60+), battle-tested for balance and fairness
- **Single-elimination cascade** — one click generates an entire bracket tournament (Z→Y→X→Q→S→F) with automatic game references
- **Game reference system** — bracket games reference prior game numbers (`T1GnoRef`, `T2GnoRef`) with calculation types (`Winner`, `Loser`, `1st Place`–`8th Place`)
- **Flexible team types** — T1Type/T2Type encode whether a pairing slot is a direct team (`T`), bracket qualifier (`Q`, `S`, `F`, `X`, `Z`, `Y`), consolation (`C`), or round-robin division crossover (`RRD1`–`RRD8`)
- **Incremental building** — add blocks, add singles, add bracket rounds independently; game/round numbers auto-offset to prevent collisions
- **Full manual override** — every pairing cell is editable inline for custom tournament formats
- **Division navigator** — hierarchical agegroup → division list with team counts, color-coded
- **Annotations** — free-text per team slot (e.g., "Winner of Pool A", "2nd Place Group 3")

## 2. Legacy Pain Points (Fix These!)

- **jqGrid with 12+ columns** — cramped, hard to scan; many columns (annotations, calc types) are rarely used but always visible
- **No visual bracket preview** — pairings are shown as flat rows; admins can't visualize the bracket tree until they go to View Schedule (009-5)
- **No undo** — "Remove ALL" deletes instantly with only a JS confirm; no soft-delete or revision history
- **Confusing type codes** — "T", "Q", "S", "F", "X", "Z", "Y", "C", "RRD1"–"RRD8" are cryptic; no legend or tooltips
- **No validation on bracket integrity** — admin can manually create broken brackets (game referencing non-existent game)
- **Direct SqlDbContext** — controller accesses database directly
- **No "who plays who" preview** — legacy has this on the Schedule Division page, but it should be visible here too since pairings define it

## 3. Modern Vision

**Recommended UI: Division Navigator + Pairing Grid + Bracket Preview**

A three-section layout: division navigator on the left, editable pairing grid in the center, and a collapsible bracket visualization on the right (for divisions with bracket pairings).

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Manage Pairings                                                             │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│ ┌─ Agegroups ──────┐  ┌─ Division Pairings ─────────────────────────────────┐│
│ │                   │  │  U10 Gold (8 teams)                                ││
│ │  ▸ U8  (12 teams)│  │                                                     ││
│ │  ▾ U10 (24 teams)│  │  [Add Block ▼]  [Add Single]  [Championship ▼]     ││
│ │    Gold  (8)      │  │  [Remove ALL]                                       ││
│ │    Silver (8) ◄── │  │                                                     ││
│ │    Bronze (8)     │  │  ── Round-Robin Pairings ──────────────────────────  ││
│ │  ▸ U12 (16 teams)│  │  ┌──────────────────────────────────────────────┐   ││
│ │  ▸ U14 (8 teams) │  │  │ G# │ Rnd │ T1  │ vs │ T2  │ Type │ Status  │   ││
│ │                   │  │  ├──────────────────────────────────────────────┤   ││
│ │                   │  │  │  1 │  1  │  1  │ vs │  2  │ T▸T  │ ○ Open  │   ││
│ │                   │  │  │  2 │  1  │  3  │ vs │  4  │ T▸T  │ ○ Open  │   ││
│ │                   │  │  │  3 │  1  │  5  │ vs │  6  │ T▸T  │ ● Sched │   ││
│ │                   │  │  │  4 │  1  │  7  │ vs │  8  │ T▸T  │ ● Sched │   ││
│ │                   │  │  │  5 │  2  │  1  │ vs │  3  │ T▸T  │ ○ Open  │   ││
│ │                   │  │  │ ...                                          │   ││
│ │                   │  │  └──────────────────────────────────────────────┘   ││
│ │                   │  │                                                     ││
│ │                   │  │  ── Championship Pairings ─────────────────────────  ││
│ │                   │  │  ┌──────────────────────────────────────────────┐   ││
│ │                   │  │  │ G# │ Rnd │ T1       │ vs │ T2       │ Type  │   ││
│ │                   │  │  ├──────────────────────────────────────────────┤   ││
│ │                   │  │  │ 29 │  8  │ W of G25 │ vs │ W of G26 │ S▸S  │   ││
│ │                   │  │  │ 30 │  8  │ W of G27 │ vs │ W of G28 │ S▸S  │   ││
│ │                   │  │  │ 31 │  9  │ W of G29 │ vs │ W of G30 │ F▸F  │   ││
│ │                   │  │  └──────────────────────────────────────────────┘   ││
│ │                   │  │                                                     ││
│ └───────────────────┘  └────────────────────────────────────────────────────┘│
│                                                                              │
│  ── Who Plays Who Matrix (collapsible) ──────────────────────────────────── │
│  ┌─────────────────────────────────────────┐                                │
│  │     │ T1 │ T2 │ T3 │ T4 │ T5 │ T6 │ T7 │ T8 │                          │
│  │ T1  │  — │  3 │  2 │  3 │  2 │  3 │  2 │  3 │                          │
│  │ T2  │  3 │  — │  3 │  2 │  3 │  2 │  3 │  2 │                          │
│  │ T3  │  2 │  3 │  — │  3 │  2 │  3 │  2 │  3 │                          │
│  │ ...                                                                      │
│  │  Yellow = 0 games (teams never play each other)                          │
│  └─────────────────────────────────────────┘                                │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Key improvements:**
- ✅ **Type legend** — tooltip/badge showing what T/Q/S/F/X/C/RRD mean
- ✅ **Round-robin vs. championship separation** — visual grouping instead of flat list
- ✅ **Availability indicator** — shows which pairings are already scheduled (● vs ○)
- ✅ **Who Plays Who matrix** — brought here from ScheduleDivision where it logically belongs
- ✅ **Add Block dropdown** — select 1–14 rounds, then confirm
- ✅ **Championship dropdown** — select bracket size (Q→F, S→F, F only, etc.)
- ✅ **Inline editing** — double-click cells for manual adjustments (advanced users)
- ✅ **Confirmation modal** — "Remove ALL" requires typed confirmation, not just OK/Cancel
- ✅ **Color-coded rounds** — alternating row backgrounds per round for scanability

**Design alignment:** Glassmorphic cards, CSS variable colors, 8px grid spacing. Division navigator follows the same agegroup-accordion pattern as Pool Assignment (006).

---

## 4. Security

- **Authorization:** `[Authorize(Policy = "AdminOnly")]`
- **Scoping:** JWT `regId` → `jobId` → `leagueId` + `season` (via `ResolveLeagueSeasonAsync`) — pairings are per-league-season, not per-job
- **Team count isolation:** Pairings are keyed by `TCnt` (team count) within a league-season; divisions with the same team count share pairing templates

---

## 5. Database Entities

### Masterpairingtable (read-only seed data)
Pre-computed round-robin templates. **Never modified by the application.**

| Column | Type | Notes |
|--------|------|-------|
| `Id` | int (PK) | |
| `GNo` | int | Game number within block |
| `GCnt` | int | Game count/block counter |
| `TCnt` | int | Team count (4, 6, 8, ..., 60+) |
| `Rnd` | int | Round number |
| `T1` | int | Team 1 position |
| `T2` | int | Team 2 position |

### PairingsLeagueSeason (mutable)
Actual pairings for a specific league-season.

| Column | Type | Notes |
|--------|------|-------|
| `Ai` | int (PK) | Auto-increment |
| `LeagueId` | Guid (FK) | |
| `Season` | string | |
| `TCnt` | int? | Team count this applies to |
| `GameNumber` | int | Sequence number |
| `Rnd` | int | Round number |
| `T1` | int | Team 1 position/rank |
| `T2` | int | Team 2 position/rank |
| `T1Type` | string | T, Q, S, F, X, Z, Y, C, RRD1–8 |
| `T2Type` | string | Same options |
| `T1GnoRef` | int? | Game # that T1 comes from (brackets) |
| `T2GnoRef` | int? | Game # that T2 comes from (brackets) |
| `T1CalcType` | string? | W(inner), L(oser), 1st–8th Place |
| `T2CalcType` | string? | Same options |
| `T1Annotation` | string? | Free text (e.g., "Winner of Pool A") |
| `T2Annotation` | string? | Free text |
| `GCnt` | int? | Game count reference |
| `LebUserId` | string | Audit |
| `Modified` | DateTime | Audit |

### BracketDataSingleElimination (read-only seed data)
Templates for bracket matchups.

| Column | Type | Notes |
|--------|------|-------|
| `Id` | int (PK) | |
| `RoundType` | string | Z(R64), Y(R32), X(R16), Q(QF), S(SF), F(Finals) |
| `T1` | int? | Seed position 1 |
| `T2` | int? | Seed position 2 |
| `SortOrder` | int? | Display ordering |

---

## 6. Business Rules — Pairing Algorithms

### Round-Robin Generation (`AddBlock`)

```
Input:  teamCount (from division), noRounds (admin selects 1–14)
Output: N new PairingsLeagueSeason records

1. Find current maxGameNumber and maxRnd in PairingsLeagueSeason
   WHERE LeagueId + Season + TCnt
2. Query Masterpairingtable WHERE TCnt = teamCount AND Rnd <= noRounds
3. For each master record:
   - GameNumber = master.GNo + maxGameNumber
   - Rnd = master.Rnd + maxRnd
   - T1Type = "T", T2Type = "T"
   - T1 = master.T1, T2 = master.T2
4. Bulk insert
```

### Single-Elimination Generation (`AddSingleEliminationGames`)

```
Input:  teamCount, startKey (Z/Y/X/Q/S/F)
Output: Complete bracket from startKey through Finals

Algorithm (recursive):
1. Find current maxGameNumber and maxRnd
2. Query BracketDataSingleElimination WHERE RoundType = key
3. For each bracket record:
   - GameNumber = auto-incremented
   - T1 = bracket.T1, T2 = bracket.T2
   - T1Type = key, T2Type = key
   - T1GnoRef/T2GnoRef = game references for advancement
4. Insert records
5. Cascade to next key: Z→Y→X→Q→S→F
   - Each level's round number = previous + 1
   - Stop at "F" (Finals)
```

### Key Cascade Chain
```
Z (Round of 64, 32 games) →
Y (Round of 32, 16 games) →
X (Round of 16, 8 games) →
Q (Quarterfinals, 4 games) →
S (Semifinals, 2 games) →
F (Finals, 1 game)
```

Admin can start at any level — if they click "Q→F", only QF through Finals are generated.

### Game Reference System
Bracket pairings use `T1GnoRef`/`T2GnoRef` + `T1CalcType`/`T2CalcType` to define advancement:
- `T1GnoRef = 5, T1CalcType = "W"` → "Winner of Game 5 plays here"
- `T2GnoRef = 6, T2CalcType = "L"` → "Loser of Game 6 plays here" (consolation)
- `T1CalcType = "First Place"` → "1st place from standings plays here"

---

## 7. Implementation Steps

### Phase 1: Backend — DTOs

**File:** `TSIC.Contracts/Dtos/Scheduling/PairingDtos.cs`

```csharp
// ── Response DTOs ──

public record PairingDto
{
    public required int Ai { get; init; }
    public required int GameNumber { get; init; }
    public required int Rnd { get; init; }
    public required int T1 { get; init; }
    public required int T2 { get; init; }
    public required string T1Type { get; init; }
    public required string T2Type { get; init; }
    public int? T1GnoRef { get; init; }
    public int? T2GnoRef { get; init; }
    public string? T1CalcType { get; init; }
    public string? T2CalcType { get; init; }
    public string? T1Annotation { get; init; }
    public string? T2Annotation { get; init; }
    public required bool BAvailable { get; init; }  // false if already scheduled
}

public record DivisionPairingsResponse
{
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }
    public required int TeamCount { get; init; }
    public required List<PairingDto> Pairings { get; init; }
}

public record WhoPlaysWhoResponse
{
    public required int TeamCount { get; init; }
    /// N×N matrix: matrix[i][j] = number of games between team i and team j
    public required int[][] Matrix { get; init; }
}

// ── Request DTOs ──

public record AddPairingBlockRequest
{
    /// Number of rounds to generate (1–14)
    public required int NoRounds { get; init; }
    /// Team count for this division
    public required int TeamCount { get; init; }
}

public record AddSingleEliminationRequest
{
    /// Starting bracket key: Z, Y, X, Q, S, or F
    public required string StartKey { get; init; }
    /// Team count for this division
    public required int TeamCount { get; init; }
}

public record AddSinglePairingRequest
{
    public required int TeamCount { get; init; }
}

public record EditPairingRequest
{
    public required int Ai { get; init; }
    public int? GameNumber { get; init; }
    public int? Rnd { get; init; }
    public int? T1 { get; init; }
    public int? T2 { get; init; }
    public string? T1Type { get; init; }
    public string? T2Type { get; init; }
    public int? T1GnoRef { get; init; }
    public int? T2GnoRef { get; init; }
    public string? T1CalcType { get; init; }
    public string? T2CalcType { get; init; }
    public string? T1Annotation { get; init; }
    public string? T2Annotation { get; init; }
}

public record RemoveAllPairingsRequest
{
    public required int TeamCount { get; init; }
}
```

### Phase 2: Backend — Repository

**Interface:** `TSIC.Contracts/Repositories/IPairingsRepository.cs`

```
Methods:
- GetPairingsAsync(Guid leagueId, string season, int teamCount) → List<PairingsLeagueSeason>
- GetMasterPairingsAsync(int teamCount, int maxRounds) → List<Masterpairingtable>
- GetBracketDataAsync(string roundType) → List<BracketDataSingleElimination>
- GetMaxGameNumberAsync(Guid leagueId, string season, int teamCount) → (int maxGame, int maxRnd)
- AddPairingsAsync(List<PairingsLeagueSeason> pairings) → void
- UpdatePairingAsync(PairingsLeagueSeason pairing) → void
- DeletePairingAsync(int ai) → void
- DeleteAllPairingsAsync(Guid leagueId, string season, int teamCount) → void
- IsPairingScheduledAsync(Guid leagueId, string season, int rnd, int t1, int t2) → bool
```

**Implementation:** `TSIC.Infrastructure/Repositories/PairingsRepository.cs`

### Phase 3: Backend — Service

**Interface:** `TSIC.Contracts/Services/IPairingsService.cs`
**Implementation:** `TSIC.API/Services/Scheduling/PairingsService.cs`

```
Methods:
- GetAgegroupsWithDivisionsAsync() → List<AgegroupWithDivisionsDto>
- GetDivisionPairingsAsync(Guid divId) → DivisionPairingsResponse
- GetWhoPlaysWhoAsync(int teamCount) → WhoPlaysWhoResponse
- AddPairingBlockAsync(AddPairingBlockRequest) → List<PairingDto>
- AddSingleEliminationAsync(AddSingleEliminationRequest) → List<PairingDto>
- AddSinglePairingAsync(AddSinglePairingRequest) → PairingDto
- EditPairingAsync(EditPairingRequest) → void
- DeletePairingAsync(int ai) → void
- RemoveAllPairingsAsync(RemoveAllPairingsRequest) → void
```

The `AddPairingBlockAsync` and `AddSingleEliminationAsync` methods contain the core algorithms described in Section 6. These must be ported faithfully from the legacy controller.

### Phase 4: Backend — Controller

**File:** `TSIC.API/Controllers/PairingsController.cs`

```
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/[controller]")]

GET    /api/pairings/agegroups           → GetAgegroupsWithDivisionsAsync()
GET    /api/pairings/division/{divId}    → GetDivisionPairingsAsync(divId)
GET    /api/pairings/who-plays-who?teamCount=N → GetWhoPlaysWhoAsync(teamCount)
POST   /api/pairings/add-block           → AddPairingBlockAsync(request)
POST   /api/pairings/add-elimination     → AddSingleEliminationAsync(request)
POST   /api/pairings/add-single          → AddSinglePairingAsync(request)
PUT    /api/pairings                     → EditPairingAsync(request)
DELETE /api/pairings/{ai}                → DeletePairingAsync(ai)
POST   /api/pairings/remove-all          → RemoveAllPairingsAsync(request)
```

### Phase 5: Frontend — Generate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

### Phase 6: Frontend — Service

**File:** `src/app/views/admin/scheduling/pairings/services/pairings.service.ts`

### Phase 7: Frontend — Components

**Main:** `src/app/views/admin/scheduling/pairings/manage-pairings.component.ts`

Key signals:
- `agegroups` — signal<AgegroupWithDivisionsDto[]>
- `selectedDivision` — signal<DivisionSummaryDto | null>
- `pairings` — signal<PairingDto[]>
- `whoPlaysWho` — signal<int[][] | null>
- `isLoading` — signal<boolean>

Key child components:
- `division-navigator.component.ts` — reusable agegroup → division tree (shared with 009-3, 009-4)
- `who-plays-who-matrix.component.ts` — N×N grid with highlighting

### Phase 8: Frontend — Route

```typescript
{
  path: 'admin/scheduling/pairings',
  loadComponent: () => import('./views/admin/scheduling/pairings/manage-pairings.component')
    .then(m => m.ManagePairingsComponent),
  canActivate: [authGuard],
  data: { roles: ['SuperUser', 'Director', 'SuperDirector'] }
}
```

### Phase 9: Frontend — Agegroup Filtering

The agegroup navigator filters out non-schedulable entries on the frontend:
- **Excluded:** "Dropped Teams" agegroup (exact match, case-insensitive)
- **Excluded:** Any agegroup starting with "WAITLIST" (e.g., WAITLIST, WAITLISTxxx)
- **Sorted:** Remaining agegroups sorted alphabetically by name

### Phase 10: Testing

- Verify round-robin generation matches `Masterpairingtable` templates exactly
- Verify bracket cascade: click "Q→F" generates QF (4 games) + SF (2 games) + F (1 game)
- Verify game number offset logic when adding blocks incrementally
- Verify "Remove ALL" deletes only for the specified team count, not other divisions
- Verify inline edit preserves game reference integrity
- Verify Who Plays Who matrix is symmetric and accurate
- Verify availability status (BAvailable) correctly reflects scheduled games
