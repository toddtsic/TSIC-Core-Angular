# Migration Plan 009: Scheduling Tools Suite — Overview & Dependency Map

## Context

The scheduling module is the crown jewel of the TSIC platform — customers consistently rate it as "the best in the business." It follows an orderly pipeline where each tool feeds the next:

```
1. Manage Fields        →  Define where games are played
2. Manage Pairings      →  Define who plays whom (round-robin + brackets)
3. Manage Timeslots     →  Define when/where each agegroup plays
4. Schedule by Division →  Auto-place pairings into timeslots = games
5. View Schedule        →  Read-only multi-view (grid, standings, brackets)
6. Rescheduler          →  Move/swap games, weather adjustments, bulk email
```

### Legacy URLs
| Step | Legacy Route | Controller |
|------|-------------|------------|
| 1 | `/Fields/Index` | `FieldsController` |
| 2 | `/Pairings/Index` | `PairingsController` |
| 3 | `/Timeslots/Index` | `TimeslotsController` |
| 4 | `/ScheduleDivision/Index` | `ScheduleDivisionController` |
| 5 | `/Schedules/Index` | `SchedulesController` |
| 6 | `/Rescheduler/Index` | `ReschedulerController` |

### Guiding Principle
**The scheduling logic is finely tuned and battle-tested.** Customers love the functional behavior. The migration modernizes the UI (Angular + Syncfusion, glassmorphic design system, signals), enforces the repository pattern, and adds quality-of-life improvements — but the core algorithms (pairing generation, auto-scheduling, bracket advancement) are preserved faithfully.

---

## Document Index

| Document | Scope | Backend Endpoints | Frontend Components |
|----------|-------|-------------------|---------------------|
| [009-1](009-1-scheduling-fields.md) | Manage Fields | ~6 | 1 page + 1 modal |
| [009-2](009-2-scheduling-pairings.md) | Manage Pairings | ~7 | 1 page |
| [009-3](009-3-scheduling-timeslots.md) | Manage Timeslots | ~20 | 1 page |
| [009-4](009-4-scheduling-division.md) | Schedule by Division | ~12 | 1 page + dynamic grid |
| [009-5](009-5-scheduling-view-schedule.md) | View Schedule (public + admin) | ~11 | 1 page + 7 tab components |
| [009-6](009-6-scheduling-rescheduler.md) | Rescheduler (admin-only) | ~5 | 1 page + 3 modals |

**Total estimated endpoints:** ~61
**Total estimated Angular components:** ~9 pages + supporting components

---

## Shared Backend Infrastructure

All 6 documents share a common backend foundation that should be built first.

### Shared Repositories Needed

| Repository | Primary Entity | Used By |
|-----------|---------------|---------|
| `IFieldRepository` | `Fields`, `FieldsLeagueSeason` | 009-1, 009-3, 009-4, 009-5 |
| `IPairingsRepository` | `PairingsLeagueSeason`, `Masterpairingtable`, `BracketDataSingleElimination` | 009-2, 009-4 |
| `ITimeslotRepository` | `TimeslotsLeagueSeasonFields`, `TimeslotsLeagueSeasonDates`, `FieldOverridesStartTimeMaxMinGames` | 009-3, 009-4 |
| `IScheduleRepository` *(extend existing)* | `Schedule`, `BracketSeeds` | 009-4, 009-5 |

The existing `IScheduleRepository` has only 4 synchronization methods. It will be significantly extended with CRUD and query operations across 009-4 and 009-5.

### Shared Domain Entities (Already Migrated)

All domain entities are already in `TSIC.Domain/Entities/`:
- `Fields`, `FieldsLeagueSeason`, `FieldOverridesStartTimeMaxMinGames`
- `Masterpairingtable`, `PairingsLeagueSeason`, `BracketDataSingleElimination`, `BracketSeeds`
- `TimeslotsLeagueSeasonFields`, `TimeslotsLeagueSeasonDates`
- `Schedule`, `ScheduleTeamTypes`, `Divisions`, `Agegroups`

No entity migration needed — only repositories, services, DTOs, and controllers.

### Shared Frontend Infrastructure

| Concern | Approach |
|---------|----------|
| **Routing** | `admin/scheduling/fields`, `admin/scheduling/pairings`, `admin/scheduling/timeslots`, `admin/scheduling/schedule-division`, `admin/scheduling/view-schedule`, `admin/scheduling/rescheduler` |
| **Navigation** | Scheduling sidebar group with sequential numbered steps |
| **Grid Library** | Syncfusion Grid (licensed, already integrated) |
| **Design System** | Glassmorphic cards, CSS variables, 8px grid — per `DESIGN-SYSTEM.md` |
| **State** | Angular signals for component state, observables for HTTP only |

### Shared DTO Patterns

All scheduling DTOs reference common types repeatedly:

```csharp
// Reused across all scheduling modules
public record AgegroupSummaryDto
{
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public required string AgegroupColor { get; init; }
}

public record DivisionSummaryDto
{
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }
    public required int TeamCount { get; init; }
}

public record AgegroupWithDivisionsDto
{
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public required string AgegroupColor { get; init; }
    public required List<DivisionSummaryDto> Divisions { get; init; }
}

public record FieldSummaryDto
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
}
```

These shared DTOs go in `TSIC.Contracts/Dtos/Scheduling/SharedSchedulingDtos.cs`.

---

## Implementation Order

The tools have a natural dependency chain. Build them in this order:

```
Phase 1: 009-1 (Fields)            — No dependencies, pure CRUD
Phase 2: 009-2 (Pairings)          — No dependencies, pure CRUD + algorithm
Phase 3: 009-3 (Timeslots)         — Depends on Fields being queryable
Phase 4: 009-4 (Schedule Division)  — Depends on Fields, Pairings, Timeslots
Phase 5: 009-5 (View Schedule)      — Depends on Schedule data existing
Phase 6: 009-6 (Rescheduler)        — Depends on Schedule data existing
```

Phases 1 and 2 can be built in parallel. Phase 3 can start once the `IFieldRepository` from Phase 1 exists (even before the frontend). Phases 5 and 6 can be built in parallel — they share the `schedule-filters.component.ts` but are otherwise independent.

---

## Security Model

All scheduling tools are **Admin-only** (`[Authorize(Policy = "AdminOnly")]`). The `jobId`, `leagueId`, `season`, and `year` are always derived from the authenticated user's JWT claims — never passed as route parameters.

**Exception:** View Schedule (009-5) has a public-access mode controlled by `Job.BScheduleAllowPublicAccess`. When enabled, unauthenticated users can view schedules and standings (but not edit scores or access contacts).

---

## Legacy Technology → Modern Replacement

| Legacy | Modern |
|--------|--------|
| jqGrid (jQuery) | Syncfusion Grid (`ejs-grid`) |
| Razor Server-Side Rendering | Angular standalone components |
| `SqlDbContext` direct access | Repository pattern (interface + implementation) |
| AJAX with anti-forgery tokens | Angular `HttpClient` with JWT interceptor |
| Syncfusion Diagram (brackets) | Syncfusion Diagram (keep — already licensed) |
| SignalR for live scores | SignalR (keep — already integrated in .NET 9) |
| CKEditor (email composition) | Syncfusion Rich Text Editor (licensed) |
| Toast notifications (NToast) | Angular toast service |
