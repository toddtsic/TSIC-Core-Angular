# 030 — Bracket Seeds Management

**Status**: PLANNED
**Date**: 2026-03-16
**Legacy Endpoint**: `BracketSeeds/Index`
**Priority**: High — prerequisite for schedule-hub Phase 2 (build results must show bracket game status)

---

## Overview

Migrate the legacy **BracketSeeds/Index** admin page. This page lets directors configure which division-ranked teams feed into bracket (championship/playoff) games. The legacy UI is two jqGrid panels:

1. **Games To Seed grid** — lists all non-round-robin (bracket) games for the job, each row showing T1/T2 type+rank and the assigned seed division + seed rank. Directors double-click to edit which division and rank feeds each bracket slot.
2. **Standings grid** — read-only division standings grouped by `Agegroup:Division`, showing W/L/T/GF/GV/Points/PPG so directors can see current rankings while assigning seeds.

### Business Logic

- **Bracket games** are Schedule rows where `T1Type == T2Type` and `T1Type != "T"` (not team-vs-team round-robin). Game types form a bracket hierarchy: `C`hampionship ← `F`inal ← `S`emi ← `Q`uarter ← `X` ← `Y` ← `Z`.
- Each bracket game has a `BracketSeeds` record linking it to source divisions + ranks (e.g., "T1 = Gold#1, T2 = Silver#2").
- `WhichSide` controls which side of the bracket row is editable (1 = T1 only, 2 = T2 only, null = both).
- **`GetSeededGames`** is a read+upsert hybrid: it queries all non-RR games, cleans up orphaned `BracketSeeds` rows, creates missing `BracketSeeds` rows, and returns the combined list. This side-effect-on-read pattern will be split into explicit read + ensure operations.
- **`PopulateBracketSeeds`** runs after scoring a game: if all RR games for a division are complete, it resolves standings → seed ranks → fills in bracket game T1Id/T2Id/T1Name/T2Name for downstream bracket games. This is the auto-advance logic.
- **`AutoadvanceSingleEliminationBracketGameWinner`** runs after scoring a bracket game: promotes the winner into the next round's bracket slot.
- **Available divisions** for seed assignment = all divisions in the bracket game's agegroup (excluding "Unassigned").

### Legacy URL → New Route

| Legacy | New Route | Purpose |
|--------|-----------|---------|
| `BracketSeeds/Index` | `/:jobPath/scheduling/bracket-seeds` | Assign division seed ranks to bracket/playoff games |

---

## Phase 1: Backend — DTOs

**File: `TSIC.Contracts/Dtos/Scheduling/BracketSeedDtos.cs`** (new)

```csharp
public record BracketSeedGameDto
{
    public required int Gid { get; init; }
    public required string AgegroupName { get; init; }
    public required int? WhichSide { get; init; }
    public required string T1Type { get; init; }
    public required int T1No { get; init; }
    public required Guid? T1SeedDivId { get; init; }
    public required string? T1SeedDivName { get; init; }
    public required int? T1SeedRank { get; init; }
    public required string T2Type { get; init; }
    public required int T2No { get; init; }
    public required Guid? T2SeedDivId { get; init; }
    public required string? T2SeedDivName { get; init; }
    public required int? T2SeedRank { get; init; }
}

public record UpdateBracketSeedRequest
{
    public required int Gid { get; init; }
    public Guid? T1SeedDivId { get; init; }
    public int? T1SeedRank { get; init; }
    public Guid? T2SeedDivId { get; init; }
    public int? T2SeedRank { get; init; }
}

public record BracketSeedDivisionOptionDto
{
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }
}
```

---

## Phase 2: Backend — Repository

**Files:** `TSIC.Contracts/Repositories/IBracketSeedRepository.cs` (new),
         `TSIC.Infrastructure/Repositories/BracketSeedRepository.cs` (new)

Dedicated repository — bracket seed logic is complex enough to warrant its own repo rather than extending `IScheduleRepository` further.

```csharp
public interface IBracketSeedRepository
{
    /// Get all non-round-robin games for the job with their bracket seed data.
    Task<List<BracketSeedGameDto>> GetBracketGamesAsync(
        Guid jobId, CancellationToken ct = default);

    /// Get the BracketSeeds record for a game (tracked, for update).
    Task<BracketSeeds?> GetByGidTrackedAsync(
        int gid, CancellationToken ct = default);

    /// Get all BracketSeeds records for a job (for cleanup).
    Task<List<BracketSeeds>> GetAllForJobAsync(
        Guid jobId, CancellationToken ct = default);

    /// Create a new BracketSeeds record.
    Task AddAsync(BracketSeeds entity, CancellationToken ct = default);

    /// Remove orphaned BracketSeeds records.
    void RemoveRange(IEnumerable<BracketSeeds> entities);

    /// Get divisions in the same agegroup as a game (for seed assignment dropdown).
    Task<List<BracketSeedDivisionOptionDto>> GetDivisionsForGameAsync(
        int gid, CancellationToken ct = default);

    /// Get the Schedule record (tracked) for updating T1Id/T1Name/T2Id/T2Name.
    Task<Schedule?> GetScheduleTrackedAsync(
        int gid, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
```

### Implementation notes

- `GetBracketGamesAsync`: Query `Schedule` where `T1Type == T2Type && T1Type != "T"`, join `BracketSeeds` (left join — record may not exist yet), join `Agegroups` for name + `BChampionsByDivision`, join `Divisions` for seed div names. Project to `BracketSeedGameDto`. `AsNoTracking()`.
- `GetDivisionsForGameAsync`: Resolve `AgegroupId` from `Schedule.Gid`, then query `Divisions` where `AgegroupId` matches and `DivName != "Unassigned"`, ordered by `DivName`. `AsNoTracking()`.
- Bracket game type sort needs to be handled in service layer (ordering by type hierarchy C→F→S→Q→X→Y→Z).

---

## Phase 3: Backend — Service

**Files:** `TSIC.Contracts/Services/IBracketSeedService.cs` (new),
         `TSIC.API/Services/Scheduling/BracketSeedService.cs` (new)

```csharp
public interface IBracketSeedService
{
    /// Get all bracket games with seed data. Creates missing BracketSeeds records
    /// and removes orphans as a side effect (matches legacy GetSeededGames behavior).
    Task<List<BracketSeedGameDto>> GetBracketGamesAsync(
        Guid jobId, string userId, CancellationToken ct = default);

    /// Update seed division + rank for a bracket game. Also updates
    /// Schedule.T1Name/T2Name to show "(DivName#Rank)" annotation.
    Task UpdateSeedAsync(
        UpdateBracketSeedRequest request, string userId,
        CancellationToken ct = default);

    /// Get available divisions for seed assignment (dropdown options).
    Task<List<BracketSeedDivisionOptionDto>> GetDivisionsForGameAsync(
        int gid, CancellationToken ct = default);
}
```

### `GetBracketGamesAsync` implementation

Splits the legacy `GetSeededGames` read+upsert hybrid into clean steps:

1. Call `_repo.GetBracketGamesAsync(jobId)` to get all non-RR games
2. Call `_repo.GetAllForJobAsync(jobId)` to get existing BracketSeeds records
3. **Remove orphans**: BracketSeeds rows where `Gid` is not in the bracket games list
4. **Determine seedable games**: For each bracket game, check if it has parent bracket games (using the type hierarchy). Games whose parents are RR games (type `"T"`) OR games with no parents in the bracket are seedable.
5. **Create missing records**: For seedable games without a BracketSeeds row, create one with null seed values
6. Save changes
7. Return the bracket games list sorted by AgegroupName → bracket type hierarchy (Z→Y→X→Q→S→F→C descending) → T1No

### `UpdateSeedAsync` implementation

1. Load `BracketSeeds` record by Gid (tracked)
2. Update T1SeedDivId/T1SeedRank and T2SeedDivId/T2SeedRank
3. Set `LebUserId` and `Modified`
4. Load Schedule record (tracked) — update `T1Name`/`T2Name` with `"{T1Type}{T1No} ({DivName}#{Rank})"` format
5. Save changes

### Bracket type hierarchy (constant)

```csharp
private static readonly Dictionary<string, int> BracketTypeOrder = new()
{
    ["C"] = 0, ["F"] = 1, ["S"] = 2, ["Q"] = 3,
    ["X"] = 4, ["Y"] = 5, ["Z"] = 6
};

private static readonly Dictionary<string, string> ParentTypeMap = new()
{
    ["C"] = "T", ["F"] = "S", ["S"] = "Q", ["Q"] = "X",
    ["X"] = "Y", ["Y"] = "Z"
};
```

---

## Phase 4: Backend — Controller

**File: `TSIC.API/Controllers/BracketSeedController.cs`** (new)

```csharp
[ApiController]
[Route("api/bracket-seeds")]
[Authorize]
public class BracketSeedController : ControllerBase
{
    // GET  api/bracket-seeds          → List<BracketSeedGameDto>
    // PUT  api/bracket-seeds          → UpdateBracketSeedRequest → 200
    // GET  api/bracket-seeds/divisions/{gid}  → List<BracketSeedDivisionOptionDto>
}
```

- `GET /api/bracket-seeds`: Resolves jobId via `User.GetJobIdFromRegistrationAsync(_jobLookupService)`, userId via `ClaimTypes.NameIdentifier`. Returns bracket games with seed data.
- `PUT /api/bracket-seeds`: Updates seed assignment for a single bracket game. Returns updated `BracketSeedGameDto` for that game.
- `GET /api/bracket-seeds/divisions/{gid}`: Returns division options for the seed assignment dropdown. Used when user opens the edit form for a specific bracket game.

---

## Phase 5: Backend — DI Registration

**File: `TSIC.API/Program.cs`** — add:

```csharp
builder.Services.AddScoped<IBracketSeedRepository, BracketSeedRepository>();
builder.Services.AddScoped<IBracketSeedService, BracketSeedService>();
```

---

## Phase 6: Backend — Verify

```bash
dotnet build
```

---

## Phase 7: Frontend — Regenerate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

---

## Phase 8: Frontend — Service

**File: `src/app/views/scheduling/bracket-seeds/services/bracket-seed.service.ts`** (new)

```typescript
@Injectable({ providedIn: 'root' })
export class BracketSeedService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/bracket-seeds`;

    getBracketGames(): Observable<BracketSeedGameDto[]> {
        return this.http.get<BracketSeedGameDto[]>(this.apiUrl);
    }

    updateSeed(request: UpdateBracketSeedRequest): Observable<BracketSeedGameDto> {
        return this.http.put<BracketSeedGameDto>(this.apiUrl, request);
    }

    getDivisionsForGame(gid: number): Observable<BracketSeedDivisionOptionDto[]> {
        return this.http.get<BracketSeedDivisionOptionDto[]>(
            `${this.apiUrl}/divisions/${gid}`);
    }
}
```

---

## Phase 9: Frontend — Component

**Location:** `src/app/views/scheduling/bracket-seeds/`

```
bracket-seeds/
├── bracket-seeds.component.ts
├── bracket-seeds.component.html
├── bracket-seeds.component.scss
└── services/
    └── bracket-seed.service.ts
```

Standalone, OnPush, signals for state.

### Signals

```typescript
bracketGames = signal<BracketSeedGameDto[]>([]);
isLoading = signal(true);
errorMessage = signal('');

// Edit modal state
editingGame = signal<BracketSeedGameDto | null>(null);
divisionOptions = signal<BracketSeedDivisionOptionDto[]>([]);
isSaving = signal(false);
isLoadingDivisions = signal(false);

// Standings (reuse existing ViewScheduleService)
standingsData = signal<StandingsByDivisionResponse | null>(null);
isLoadingStandings = signal(false);
showStandings = signal(true);
```

### Template Layout

Two-panel layout: bracket games table (primary) + standings reference (collapsible).

```
┌─────────────────────────────────────────────────────────────────────┐
│  Bracket Seeds                                                      │
│  Configure which division-ranked teams feed into playoff games      │
│                                                                     │
│  ┌─ Bracket Games ──────────────────────────────────────────────── │
│  │ Agegroup  │ Type│ Rank│ Seed Pool 1   │ Rank│ Side│ Type│ Rank│ │
│  │           │  -1 │  -1 │               │  -1 │     │  -2 │  -2 │ │
│  │           │     │     │ Seed Pool 2   │  -2 │     │     │     │ │
│  ├───────────┼─────┼─────┼───────────────┼─────┼─────┼─────┼─────┤ │
│  │ U14       │  C  │  1  │ Gold          │  1  │     │  C  │  2  │ │
│  │           │     │     │ Silver        │  1  │     │     │     │ │
│  │ U14       │  F  │  1  │ —             │  —  │     │  F  │  2  │ │
│  │           │     │     │ —             │  —  │     │     │     │ │
│  │ ...       │     │     │               │     │     │     │     │ │
│  └──────────────────────────────────────────────────────────────── │
│                                                                     │
│  ┌─ Division Standings (collapsible) ───────────────────────────── │
│  │ [U14:Gold]                                                       │
│  │ # │ Team         │ GP │ W │ L │ T │ GF │ GV │ Pts │ PPG        │
│  │ 1 │ Eagles       │  4 │ 3 │ 0 │ 1 │ 12 │  3 │ 10 │ 2.50      │
│  │ 2 │ Falcons      │  4 │ 2 │ 1 │ 1 │  8 │  5 │  7 │ 1.75      │
│  │ ...                                                              │
│  └──────────────────────────────────────────────────────────────── │
└─────────────────────────────────────────────────────────────────────┘
```

### Edit Flow

1. User clicks row → `editingGame` set, `getDivisionsForGame(gid)` called
2. Modal/inline form shows: T1 seed division dropdown + T1 seed rank dropdown, T2 seed division dropdown + T2 seed rank dropdown
3. `WhichSide` controls visibility: side 1 only → hide T2 fields, side 2 only → hide T1 fields, null → show both
4. Seed rank dropdown: 1–12 (matches legacy)
5. Save → `updateSeed()` → refresh row in `bracketGames` signal
6. Cancel → clear `editingGame`

### Standings Panel

Reuse existing `ViewScheduleService.getStandings()` endpoint (already in `ViewScheduleController`). Load on init with empty filter to get all standings. Group by `Agegroup:Division` with collapsible sections.

---

## Phase 10: Frontend — Route

**File: `src/app/app.routes.ts`** — add under scheduling children:

```typescript
{
    path: 'scheduling/bracket-seeds',
    canActivate: [authGuard],
    data: { requirePhase2: true },
    loadComponent: () => import('./views/scheduling/bracket-seeds/bracket-seeds.component')
        .then(m => m.BracketSeedsComponent)
}
```

---

## Phase 11: Nav Integration

- Legacy `Controller=BracketSeeds`, `Action=Index` → new route `scheduling/bracket-seeds`
- Add to `LegacyRouteMap` in `NavEditorService.cs`

---

## Existing Dependencies

| Dependency | Status |
|------------|--------|
| `BracketSeeds` entity | ✅ Exists — `TSIC.Domain/Entities/BracketSeeds.cs` |
| `Schedule` entity | ✅ Exists with nav properties |
| `Divisions` entity | ✅ Exists with `BracketSeedsT1SeedDiv` / `BracketSeedsT2SeedDiv` nav properties |
| `BracketDataSingleElimination` entity | ✅ Exists — bracket structure templates (used by auto-advance, not directly by this page) |
| `ViewScheduleController.GetStandings` | ✅ Exists — reuse for standings panel |
| `ScheduleRepository` cascade deletes | ✅ Already handle BracketSeeds cleanup on game deletion |
| `IJobLookupService` | ✅ For jobId resolution from JWT |

---

## What This Migration Does NOT Include

| Item | Reason |
|------|--------|
| `PopulateBracketSeeds` (auto-resolve after scoring) | Separate concern — triggered by score entry in View Schedule, not by this admin page. Will be migrated as part of score entry pipeline. |
| `AutoadvanceSingleEliminationBracketGameWinner` | Same — triggered by score entry, not bracket seed config. |
| `DivStandingsRankAdjust` (tiebreaker logic) | Already migrated as part of `ViewScheduleService.GetStandingsAsync`. Reuse that endpoint. |
| Bracket visualization (bracket diagram) | `IBracketsViewerService` is a separate feature — visual bracket rendering for public display. Out of scope. |

---

## Files Created/Modified

### Backend — New Files

| File | Description |
|------|-------------|
| `TSIC.Contracts/Dtos/Scheduling/BracketSeedDtos.cs` | 3 DTOs: BracketSeedGameDto, UpdateBracketSeedRequest, BracketSeedDivisionOptionDto |
| `TSIC.Contracts/Repositories/IBracketSeedRepository.cs` | Repository interface (7 methods) |
| `TSIC.Infrastructure/Repositories/BracketSeedRepository.cs` | Repository implementation |
| `TSIC.Contracts/Services/IBracketSeedService.cs` | Service interface (3 methods) |
| `TSIC.API/Services/Scheduling/BracketSeedService.cs` | Service: GetBracketGames (with cleanup/ensure), UpdateSeed, GetDivisions |
| `TSIC.API/Controllers/BracketSeedController.cs` | 3 endpoints: GET games, PUT seed, GET divisions |

### Backend — Modified Files

| File | Changes |
|------|---------|
| `TSIC.API/Program.cs` | +2 DI registrations |
| `TSIC.API/Services/NavEditorService.cs` | +1 LegacyRouteMap entry |

### Frontend — New Files

| File | Description |
|------|-------------|
| `bracket-seeds/bracket-seeds.component.ts` | Standalone page: bracket games table + edit modal + standings panel |
| `bracket-seeds/bracket-seeds.component.html` | Two-panel layout with collapsible standings |
| `bracket-seeds/bracket-seeds.component.scss` | Table styling, edit form, responsive |
| `bracket-seeds/services/bracket-seed.service.ts` | HTTP service (3 methods) |

### Frontend — Modified Files

| File | Changes |
|------|---------|
| `app.routes.ts` | +1 route: `scheduling/bracket-seeds` |

---

## Execution Order

| Step | Phase | Description | Depends On |
|------|-------|-------------|------------|
| 1 | 1 | Create BracketSeedDtos.cs | — |
| 2 | 2 | Create IBracketSeedRepository + BracketSeedRepository | 1 |
| 3 | 3 | Create IBracketSeedService + BracketSeedService | 2 |
| 4 | 4 | Create BracketSeedController | 3 |
| 5 | 5 | Register in Program.cs | 4 |
| 6 | 6 | `dotnet build` — verify 0 errors | 5 |
| 7 | 7 | Regenerate API models | 6 |
| 8 | 8 | Create bracket-seed.service.ts | 7 |
| 9 | 9 | Build bracket-seeds component | 8 |
| 10 | 9 | Add route to app.routes.ts | 9 |
| 11 | 11 | Add LegacyRouteMap entry | 10 |
| 12 | — | Full integration test | 11 |

---

## Testing Checklist

- [ ] Navigate to `/{jobPath}/scheduling/bracket-seeds` — page loads
- [ ] Bracket games table shows all non-RR games grouped/sorted by agegroup → bracket type
- [ ] Each row shows T1/T2 type, rank, seed pool name, and seed rank
- [ ] Click row → edit modal opens with division dropdown + rank dropdown
- [ ] `WhichSide=1` → only T1 fields editable; `WhichSide=2` → only T2 fields; null → both
- [ ] Division dropdown lists all divisions in the game's agegroup (excluding "Unassigned")
- [ ] Save updates seed assignment and refreshes row
- [ ] Schedule.T1Name/T2Name updated with `"{Type}{No} ({DivName}#{Rank})"` annotation
- [ ] Standings panel shows division standings grouped by Agegroup:Division
- [ ] Standings panel is collapsible
- [ ] Empty state: message shown when no bracket games exist for the job
- [ ] `dotnet build` — 0 errors
- [ ] `ng build` — 0 errors
- [ ] Test all 8 color palettes — page chrome adapts correctly
- [ ] No hardcoded colors in SCSS (CSS variables only)

---

**Document Version**: 1.0
**Author**: Claude Code
**Last Updated**: 2026-03-16
