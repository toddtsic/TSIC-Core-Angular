# 021 — Tournament Parking Per Site

## Context

Legacy feature `ParkingPerSite` shows tournament directors how many teams and estimated cars occupy each field complex throughout tournament days. Parking capacity constrains scheduling — this report is critical for validating schedules before events.

**Legacy**: MVC Razor + Syncfusion EJ2 + SQL stored procedure `[utility].[TournamentTeamsOnSiteReporting]`.
**Migration**: Port sproc logic to C# repository/service, new Angular component with Syncfusion charts/grids, highest polish UI.

**Key bug fix**: Legacy sproc's running total (`SUM() OVER(ORDER BY ...)`) crosses complex boundaries — our C# code will reset running totals per (complex, day).

---

## Architecture

```
TournamentParkingController → TournamentParkingService → TournamentParkingRepository → SqlDbContext
```

New dedicated repository (not expanding IScheduleRepository — that already has 20+ methods across 4 domains).

---

## Phase 1 — Backend DTOs

**New file**: `TSIC.Contracts/Dtos/Scheduling/TournamentParkingDtos.cs`

| DTO | Purpose |
|-----|---------|
| `TournamentParkingRequest` | `ArrivalBufferMinutes`, `DepartureBufferMinutes`, `CarMultiplier` |
| `TournamentParkingResponse` | `Rollup[]`, `ComplexDays[]`, `Summary` |
| `ParkingTimeslotDto` | One row: complex, day, time, teams±, cars±, running totals |
| `ParkingComplexDayDto` | Label + timeslot array for one complex+day slice |
| `ParkingSummaryDto` | KPIs: total complexes, peak teams/cars, peak complex name |
| `TeamGamePresenceDto` | Internal: fieldComplex, fieldId, teamId, agegroupId, gameDate |

All use `required` + `init` pattern per conventions.

---

## Phase 2 — Repository

**Interface**: `TSIC.Contracts/Repositories/ITournamentParkingRepository.cs`

```
GetTeamGamePresenceAsync(Guid jobId, CancellationToken) → List<TeamGamePresenceDto>
GetGameStartIntervalsAsync(Guid jobId, CancellationToken) → Dictionary<Guid agegroupId, int intervalMinutes>
```

**Implementation**: `TSIC.Infrastructure/Repositories/TournamentParkingRepository.cs`

Query 1 — team presence:
- `Schedule` where `JobId == jobId`, `GDate != null`, `FieldId != null`
- Union T1 side (where `T1Type == "T"` and `T1Id != null`) with T2 side
- `FName` is denormalized on `Schedule` entity (line 84) — extract complex as `FName.Substring(0, FName.IndexOf('-'))` (same as sproc)
- Uses `Schedule.Field` nav property for `FieldId` only
- `AgegroupId` from `Schedule.AgegroupId`

Query 2 — game intervals:
- `TimeslotsLeagueSeasonFields` filtered by job's schedule data
- Group by `AgegroupId`, take `Min(GamestartInterval)` per agegroup (matches sproc behavior)
- Returns dictionary for O(1) lookup in service

---

## Phase 3 — Service

**Interface**: `TSIC.Contracts/Services/ITournamentParkingService.cs`
**Implementation**: `TSIC.API/Services/Scheduling/TournamentParkingService.cs`

Core algorithm (ported from sproc):

1. `await repo.GetTeamGamePresenceAsync(jobId)` — all team-game records
2. `await repo.GetGameStartIntervalsAsync(jobId)` — interval lookup
3. Group presence by `(FieldComplex, TeamId, Day)` → for each group:
   - `firstGameStart = MIN(GameDate)`
   - `lastGameStart = MAX(GameDate)`
   - `interval = lookup[agegroupId]` (fallback 60)
   - `arrivalTime = firstGameStart - arrivalBuffer`
   - `departureTime = lastGameStart + interval + departureBuffer`
4. Emit arrival event `(+teams, +cars)` and departure event `(-teams, -cars)` at each time
5. Group by `(FieldComplex, Day, Time)`, sum arrival/departure counts
6. **Per-complex per-day running totals** (the bug fix — never cross boundaries)
7. Build `TournamentParkingResponse` with rollup, complex-day slices, and summary KPIs

---

## Phase 4 — Controller

**New file**: `TSIC.API/Controllers/TournamentParkingController.cs`

- Route: `api/tournament-parking`
- Auth: `[Authorize(Policy = "AdminOnly")]`
- Single endpoint: `POST report` accepting `TournamentParkingRequest`
- JobId resolution: `await User.GetJobIdFromRegistrationAsync(_jobLookupService)` + `using TSIC.API.Extensions`
- Pattern matches `SchedulingDashboardController` exactly

**DI in Program.cs** (2 lines):
```csharp
builder.Services.AddScoped<ITournamentParkingRepository, TournamentParkingRepository>();
builder.Services.AddScoped<ITournamentParkingService, TournamentParkingService>();
```

---

## Phase 5 — Build + Regenerate API Models

```bash
dotnet build TSIC-Core-Angular.sln
.\scripts\2-Regenerate-API-Models.ps1
```

---

## Phase 6 — Frontend Service

**New file**: `views/admin/scheduling/tournament-parking/services/tournament-parking.service.ts`

- `getReport(request): Observable<TournamentParkingResponse>`
- Uses `${environment.apiUrl}/tournament-parking/report`
- Imports from `@core/api`

---

## Phase 7 — Frontend Component (Highest Polish)

**New files**:
- `tournament-parking.component.ts` — standalone, OnPush, signals, inject()
- `tournament-parking.component.html`
- `tournament-parking.component.scss`

### UI Layout — Three Zones

**Zone 1 — Header + KPI Cards**
```
┌─────────────────────────────────────────────────────────┐
│  Tournament Parking Analysis                             │
│  Estimate vehicle load per field complex                 │
├──────────────┬──────────────┬───────────────────────────┤
│ 🏁 Peak Teams │ 🚗 Peak Cars │ 📍 Complexes × Days      │
│     42        │    1,008     │     3 × 2                 │
│  at Central   │  at Central  │                           │
└──────────────┴──────────────┴───────────────────────────┘
```
- Glassmorphic cards matching `scheduling-dashboard.component.scss` `.status-card` pattern
- KPI number in `--font-size-2xl`, detail in `--font-size-xs`

**Zone 2 — Parameter Bar**
```
┌─────────────────────────────────────────────────────────┐
│ Arrival Buffer [45 ▾]  Departure Buffer [30 ▾]  Cars/Team [24 ▾]  │
└─────────────────────────────────────────────────────────┘
```
- Glassmorphic bar with `var(--surface-elevated-bg)` + backdrop-filter
- Native `<select>` elements styled with design system
- Changes trigger immediate re-fetch

**Zone 3 — Tabbed Content**

Custom tab bar (matching project pattern — not Syncfusion tabs):
```
[ All Complexes | Central — Sat 3/14 | Eastside — Sat 3/14 | Central — Sun 3/15 | ... ]
```

**"All Complexes" tab**: Syncfusion `ejs-grid` with:
- Columns: Complex, Day, Time, Teams+, Teams-, Net, On-Site, Cars+, Cars-, Net, On-Site
- Excel export toolbar
- Alternating row styling via `queryCellInfo` event

**Per-complex/day tabs** — three sections vertically:

**A. Cars Chart** (Syncfusion `ejs-chart`):
- Grouped columns: "Arriving" (`--bs-primary` at 0.7 opacity), "Departing" (`--brand-danger` at 0.7 opacity)
- Line: "On-Site" (`--bs-success`, weight 2.5, circle markers)
- X-axis DateTime (time), Y-axis count
- Chart export/print buttons
- `cssVar()` pattern from `registration-trend-chart.component.ts`

**B. Teams Chart**: Same structure, different data series

**C. Data Grid**: Same columns as rollup, filtered to this complex/day, with Excel export

### SCSS Highlights
- All design system variables (`--space-*`, `--radius-*`, `--shadow-*`, `--brand-*`)
- Glassmorphic surfaces for cards and param bar
- Custom tab bar with primary-colored active indicator
- Responsive: collapses to single-column on mobile
- `prefers-reduced-motion` disables backdrop-filter

---

## Phase 8 — Routing

**Modified file**: `app.routes.ts`

Add as standalone route (sibling to `scheduling/view-schedule`):
```typescript
{
    path: 'scheduling/tournament-parking',
    canActivate: [authGuard],
    data: { requirePhase2: true },
    loadComponent: () => import('./views/admin/scheduling/tournament-parking/tournament-parking.component')
        .then(m => m.TournamentParkingComponent)
}
```

---

## File Inventory

| File | Action | Layer |
|------|--------|-------|
| `TSIC.Contracts/Dtos/Scheduling/TournamentParkingDtos.cs` | Create | Contracts |
| `TSIC.Contracts/Repositories/ITournamentParkingRepository.cs` | Create | Contracts |
| `TSIC.Contracts/Services/ITournamentParkingService.cs` | Create | Contracts |
| `TSIC.Infrastructure/Repositories/TournamentParkingRepository.cs` | Create | Infrastructure |
| `TSIC.API/Services/Scheduling/TournamentParkingService.cs` | Create | API |
| `TSIC.API/Controllers/TournamentParkingController.cs` | Create | API |
| `TSIC.API/Program.cs` | Edit (2 lines) | API |
| `frontend/.../tournament-parking/services/tournament-parking.service.ts` | Create | Frontend |
| `frontend/.../tournament-parking/tournament-parking.component.ts` | Create | Frontend |
| `frontend/.../tournament-parking/tournament-parking.component.html` | Create | Frontend |
| `frontend/.../tournament-parking/tournament-parking.component.scss` | Create | Frontend |
| `frontend/.../app.routes.ts` | Edit (1 route) | Frontend |

**10 new files, 2 modified files.**

---

## Verification

1. `dotnet build TSIC-Core-Angular.sln` — 0 errors
2. Regenerate API models — verify new DTO types appear in `@core/api`
3. `ng serve` — verify route resolves at `/:jobPath/scheduling/tournament-parking`
4. Manual test: select different buffer/multiplier values, verify charts re-render
5. Verify Excel export works from all grids
6. Test with all 8 palettes — chart colors must adapt
7. Test responsive at 375px, 768px, 1440px
8. Verify empty schedule returns empty report with zero KPIs (no error)
