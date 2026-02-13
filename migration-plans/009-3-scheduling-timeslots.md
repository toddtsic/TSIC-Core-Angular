# Migration Plan 009-3: Timeslots/Index → Manage Timeslots

## Context

The Timeslots page is **step 3** of the scheduling pipeline. After fields are defined (009-1) and pairings are set (009-2), administrators configure *when* and *where* each agegroup can play by defining:

1. **Timeslot Dates** (`TimeslotsLeagueSeasonDates`) — which calendar dates are available for games, mapped to round numbers
2. **Timeslot Fields** (`TimeslotsLeagueSeasonFields`) — which fields are available on which days, with start times, game intervals, and max games per field

This is the most endpoint-heavy controller in the scheduling suite (20+ actions) because of its extensive **cloning** capabilities. Admins configure one agegroup's timeslots and then clone dates, fields, and day-of-week settings to other agegroups — a critical time-saver when leagues have 10+ agegroups with similar schedules.

**Legacy URL:** `/Timeslots/Index` (Controller=Timeslots, Action=Index)

**Legacy Controller:** `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Scheduling/TimeslotsController.cs`
**Legacy View:** `reference/TSIC-Unify-2024/TSIC-Unify/Views/Timeslots/Index.cshtml`

---

## 1. Legacy Strengths (Preserve These!)

- **Two-data-model design** — dates (when) and fields (where/how) are managed independently per agegroup
- **Powerful cloning** — clone dates between agegroups, clone field configs between agegroups, clone by field, clone by division, clone by day-of-week (with optional start time override)
- **Quick date generation** — clone +1 day, clone +1 week, clone same date with round+1
- **Cartesian product creation** — when adding field timeslots, system auto-creates entries for all fields × all divisions × all days of week
- **Division-level granularity** — timeslots can be agegroup-wide OR division-specific, allowing different schedules per division within the same agegroup
- **Day-of-week cycling** — clone timeslot configs to next day (Mon→Tue→...→Sun→Mon)
- **Field capacity model** — `MaxGamesPerField` + `GamestartInterval` + `StartTime` defines exact scheduling capacity per field per day
- **Batch operations** — delete all dates, delete all fields, delete by agegroup — efficient bulk management

## 2. Legacy Pain Points (Fix These!)

- **20+ AJAX endpoints** — hard to discover features; too many buttons, no clear workflow
- **Two separate jqGrid tables** — dates and fields grids share the page but have no visual connection
- **No preview of resulting schedule capacity** — admin can't see "this configuration yields 48 game slots on Saturday" until they try to auto-schedule (009-4)
- **Clone operations require multiple clicks** — select source, select target, click clone; no drag-and-drop or visual feedback
- **No validation** — can create overlapping timeslots, or timeslots that exceed field capacity
- **Dropdown menus as semicolon-delimited strings** — legacy returns `"id1:name1;id2:name2"` for field/division dropdowns
- **Direct SqlDbContext** — all 20+ actions access database directly

## 3. Modern Vision

**Recommended UI: Agegroup Selector + Tabbed Dates/Fields Panels + Capacity Preview**

Keep the two-data-model approach (dates and fields are conceptually separate) but present them in tabs within a single agegroup context. Add a capacity preview that shows the total game slots available.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Manage Timeslots                                                            │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│ ┌─ Agegroups ──────┐  ┌─ Timeslot Configuration ────────────────────────────┐│
│ │                   │  │                                                     ││
│ │  ● U8             │  │  U10  •  Gold/Silver/Bronze                         ││
│ │  ● U10  ◄─────── │  │                                                     ││
│ │  ● U12            │  │  Clone To: [U12 ▼]  [Clone Dates] [Clone Fields]   ││
│ │  ● U14            │  │                                                     ││
│ │  ● U16            │  │  [Dates]  [Fields]  [Capacity Preview]              ││
│ │                   │  │  ─────────────────────────────────────               ││
│ │                   │  │                                                     ││
│ │                   │  │  ── Dates Tab ──────────────────────────────────── ││
│ │                   │  │                                                     ││
│ │                   │  │  [+ Add Date]  [Delete All Dates]                   ││
│ │                   │  │                                                     ││
│ │                   │  │  ┌────────────────────────────────────────────┐     ││
│ │                   │  │  │ Date         │ Rnd │ Day │ Actions        │     ││
│ │                   │  │  ├────────────────────────────────────────────┤     ││
│ │                   │  │  │ Mar 1, 2026  │  1  │ Sat │ [+D][+W][+R]  │     ││
│ │                   │  │  │ Mar 2, 2026  │  1  │ Sun │ [+D][+W][+R]  │     ││
│ │                   │  │  │ Mar 8, 2026  │  2  │ Sat │ [+D][+W][+R]  │     ││
│ │                   │  │  │ Mar 9, 2026  │  2  │ Sun │ [+D][+W][+R]  │     ││
│ │                   │  │  │ Mar 15, 2026 │  3  │ Sat │ [+D][+W][+R]  │     ││
│ │                   │  │  │ ...                                        │     ││
│ │                   │  │  └────────────────────────────────────────────┘     ││
│ │                   │  │                                                     ││
│ │                   │  │  [+D] = Clone +1 Day  [+W] = Clone +1 Week         ││
│ │                   │  │  [+R] = Clone Same Date, Rnd+1                      ││
│ │                   │  │                                                     ││
│ └───────────────────┘  └────────────────────────────────────────────────────┘│
│                                                                              │
│  ── Fields Tab ──────────────────────────────────────────────────────────── │
│                                                                              │
│  [+ Add Field-Timeslot]  [Delete All Fields]                                │
│  Clone: [Field ▼] → [Field ▼] [Go]   [Div ▼] → [Div ▼] [Go]             │
│         [DOW ▼] → [DOW ▼] StartTime: [    ] [Go]                           │
│                                                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │ Field        │ DOW  │ Start │ Interval │ Max Games │ Division │ Del  │   │
│  ├──────────────────────────────────────────────────────────────────────┤   │
│  │ Cedar Park A │ Sat  │ 8:00  │ 60 min   │ 6         │ Gold     │ [✕] │   │
│  │ Cedar Park A │ Sat  │ 8:00  │ 60 min   │ 6         │ Silver   │ [✕] │   │
│  │ Cedar Park A │ Sun  │ 9:00  │ 50 min   │ 5         │ Gold     │ [✕] │   │
│  │ Lakeline     │ Sat  │ 8:00  │ 60 min   │ 6         │ Gold     │ [✕] │   │
│  │ ...                                                                  │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                                                              │
│  ── Capacity Preview Tab ────────────────────────────────────────────────── │
│                                                                              │
│  ┌──────────────────────────────────────────────────┐                       │
│  │ Day       │ Fields │ Game Slots │ Games Needed   │                       │
│  ├──────────────────────────────────────────────────┤                       │
│  │ Saturday  │ 4      │ 24         │ 12 (Round 1)   │                       │
│  │ Sunday    │ 3      │ 15         │ 12 (Round 1)   │                       │
│  │ TOTAL     │ 7      │ 39         │ 24             │                       │
│  └──────────────────────────────────────────────────┘                       │
│  ✅ Sufficient capacity for all rounds                                      │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Key improvements:**
- ✅ **Capacity preview** — shows total available game slots vs. games needed (from pairing count) before scheduling
- ✅ **Tabbed interface** — dates, fields, and preview in organized tabs instead of cramped dual-grid
- ✅ **Clone dropdowns with confirmation** — source → target pattern with clear visual feedback
- ✅ **Quick-add row actions** — +1 Day, +1 Week, +Round buttons inline per row
- ✅ **Grouped field timeslots** — visual grouping by field name for scanability
- ✅ **Validation** — warn on overlapping timeslots, highlight capacity shortfalls

**Design alignment:** Glassmorphic card tabs, Syncfusion Grid for both dates and fields tables, CSS variable colors.

---

## 4. Security

- **Authorization:** `[Authorize(Policy = "AdminOnly")]`
- **Scoping:** JWT `regId` → `jobId` → `leagueId` + `season` + `year` (via `ResolveLeagueSeasonAsync` pattern)
- **Agegroup filtering:** Only active agegroups shown (excludes "Dropped", "WAITLIST")

---

## 5. Database Entities

### TimeslotsLeagueSeasonDates
| Column | Type | Notes |
|--------|------|-------|
| `Ai` | int (PK) | Auto-increment |
| `AgegroupId` | Guid (FK) | |
| `DivId` | Guid? (FK) | Optional division-specific |
| `Season` | string | |
| `Year` | string | |
| `GDate` | DateTime | Game date |
| `Rnd` | int | Round number (1–20) |
| `LebUserId` | string | Audit |
| `Modified` | DateTime | Audit |

### TimeslotsLeagueSeasonFields
| Column | Type | Notes |
|--------|------|-------|
| `Ai` | int (PK) | Auto-increment |
| `AgegroupId` | Guid (FK) | |
| `Season` | string | |
| `Year` | string | |
| `FieldId` | Guid (FK) | |
| `StartTime` | string | e.g., "08:00" |
| `GamestartInterval` | int | Minutes between games |
| `MaxGamesPerField` | int | Capacity per day |
| `Dow` | string | Day of week name |
| `DivId` | Guid? (FK) | Optional division-specific |
| `LebUserId` | string | Audit |
| `Modified` | DateTime | Audit |

### FieldOverridesStartTimeMaxMinGames
| Column | Type | Notes |
|--------|------|-------|
| `Ai` | int (PK) | |
| `LeagueId` | Guid? | |
| `Season` | string | |
| `Year` | string | |
| `FieldId` | Guid? | |
| `StartTime` | string | Override start time |
| `MinGamesPerField` | int? | Override minimum |
| `MaxGamesPerField` | int? | Override maximum |
| `Dow` | string | Day of week |

---

## 6. Business Rules

### Data Hierarchy
```
League/Season/Year
  └─ Agegroup
       └─ Division (optional — if null, applies to all divisions in agegroup)
            └─ Field + DayOfWeek
                 └─ StartTime, GamestartInterval, MaxGamesPerField
```

### Cloning Rules

| Clone Type | Source | Target | Behavior |
|-----------|--------|--------|----------|
| Clone Dates (agegroup→agegroup) | All dates from source AG | Target AG | Deletes existing target dates first |
| Clone Fields (agegroup→agegroup) | All field timeslots from source AG | Target AG | Fails if target already has timeslots |
| Clone by Field | Field A in AG | Field B in AG | Copies all DOW/time/capacity settings |
| Clone by Division | Div A in AG | Div B in AG | Copies all field configs |
| Clone by DOW | Monday in AG | Tuesday in AG | Copies all field configs, optionally overrides StartTime |
| Clone Date +1 Day | Single date record | New record | GDate + 1 day, Rnd + 1 |
| Clone Date +1 Week | Single date record | New record | GDate + 7 days, Rnd + 1 |
| Clone Date Same (Rnd+1) | Single date record | New record | Same GDate, Rnd + 1 |
| Clone Field DOW | Single field timeslot | New record | Next day of week (Mon→Tue→...→Sun→Mon) |

### Cartesian Product Creation
When adding a new field timeslot with no specific field/division selected:
- Creates entries for ALL active fields in league-season × ALL active divisions in agegroup × ALL selected days of week.
- This is the bulk-initialization path.

### Capacity Calculation
```
GameSlots per field per day = MaxGamesPerField
Total slots for a day = Σ (MaxGamesPerField for each field configured for that DOW)
Games needed per round = ceil(teamCount / 2)
```

---

## 7. Implementation Steps

### Phase 1: Backend — DTOs

**File:** `TSIC.Contracts/Dtos/Scheduling/TimeslotDtos.cs`

```csharp
// ── Response DTOs ──

public record TimeslotDateDto
{
    public required int Ai { get; init; }
    public required Guid AgegroupId { get; init; }
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
    public Guid? DivId { get; init; }
    public string? DivName { get; init; }
}

public record TimeslotFieldDto
{
    public required int Ai { get; init; }
    public required Guid AgegroupId { get; init; }
    public required Guid FieldId { get; init; }
    public required string FieldName { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string Dow { get; init; }
    public Guid? DivId { get; init; }
    public string? DivName { get; init; }
}

public record TimeslotConfigurationResponse
{
    public required List<TimeslotDateDto> Dates { get; init; }
    public required List<TimeslotFieldDto> Fields { get; init; }
}

public record CapacityPreviewDto
{
    public required string Dow { get; init; }
    public required int FieldCount { get; init; }
    public required int TotalGameSlots { get; init; }
    public required int GamesNeeded { get; init; }
    public required bool IsSufficient { get; init; }
}

// ── Request DTOs ──

public record AddTimeslotDateRequest
{
    public required Guid AgegroupId { get; init; }
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
    public Guid? DivId { get; init; }
}

public record EditTimeslotDateRequest
{
    public required int Ai { get; init; }
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
}

public record AddTimeslotFieldRequest
{
    public required Guid AgegroupId { get; init; }
    public Guid? FieldId { get; init; }         // null = all fields
    public Guid? DivId { get; init; }            // null = all divisions
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string Dow { get; init; }
}

public record EditTimeslotFieldRequest
{
    public required int Ai { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string Dow { get; init; }
    public Guid? FieldId { get; init; }
    public Guid? DivId { get; init; }
}

public record CloneDatesRequest
{
    public required Guid SourceAgegroupId { get; init; }
    public required Guid TargetAgegroupId { get; init; }
}

public record CloneFieldsRequest
{
    public required Guid SourceAgegroupId { get; init; }
    public required Guid TargetAgegroupId { get; init; }
}

public record CloneByFieldRequest
{
    public required Guid AgegroupId { get; init; }
    public required Guid SourceFieldId { get; init; }
    public required Guid TargetFieldId { get; init; }
}

public record CloneByDivisionRequest
{
    public required Guid AgegroupId { get; init; }
    public required Guid SourceDivId { get; init; }
    public required Guid TargetDivId { get; init; }
}

public record CloneByDowRequest
{
    public required Guid AgegroupId { get; init; }
    public required string SourceDow { get; init; }
    public required string TargetDow { get; init; }
    public string? NewStartTime { get; init; }   // null = keep source start time
}

public record CloneDateRecordRequest
{
    public required int Ai { get; init; }
    /// "day" (+1 day), "week" (+7 days), or "round" (same date, rnd+1)
    public required string CloneType { get; init; }
}

public record CloneFieldDowRequest
{
    public required int Ai { get; init; }
}
```

### Phase 2: Backend — Repository

**Interface:** `TSIC.Contracts/Repositories/ITimeslotRepository.cs`

```
Methods:
// Dates
- GetDatesAsync(Guid agegroupId, string season, string year) → List<TimeslotsLeagueSeasonDates>
- AddDateAsync(TimeslotsLeagueSeasonDates date) → TimeslotsLeagueSeasonDates
- UpdateDateAsync(TimeslotsLeagueSeasonDates date) → void
- DeleteDateAsync(int ai) → void
- DeleteAllDatesAsync(Guid leagueId, string season, string year) → void
- DeleteAgegroupDatesAsync(Guid agegroupId, string season, string year) → void
- CloneDateRecordAsync(int ai, string cloneType) → TimeslotsLeagueSeasonDates

// Fields
- GetFieldTimeslotsAsync(Guid agegroupId, string season, string year) → List<TimeslotsLeagueSeasonFields>
- AddFieldTimeslotAsync(TimeslotsLeagueSeasonFields timeslot) → TimeslotsLeagueSeasonFields
- BulkAddFieldTimeslotsAsync(List<TimeslotsLeagueSeasonFields> timeslots) → void
- UpdateFieldTimeslotAsync(TimeslotsLeagueSeasonFields timeslot) → void
- DeleteFieldTimeslotAsync(int ai) → void
- DeleteAllFieldTimeslotsAsync(Guid leagueId, string season, string year) → void
- DeleteAgegroupFieldTimeslotsAsync(Guid agegroupId, string season, string year) → void
- RemoveFieldFromAgegroupAsync(Guid agegroupId, Guid fieldId, string season, string year) → void
- AddFieldToAgegroupAsync(Guid agegroupId, Guid fieldId, string season, string year) → void

// Cloning
- CloneDatesAsync(Guid sourceAg, Guid targetAg, ...) → void
- CloneFieldsAsync(Guid sourceAg, Guid targetAg, ...) → void
- CloneByFieldAsync(Guid agId, Guid sourceField, Guid targetField, ...) → void
- CloneByDivisionAsync(Guid agId, Guid sourceDiv, Guid targetDiv, ...) → void
- CloneByDowAsync(Guid agId, string sourceDow, string targetDow, string? newStartTime, ...) → void
- CloneFieldDowAsync(int ai) → TimeslotsLeagueSeasonFields

// Capacity
- GetCapacityPreviewAsync(Guid agegroupId, string season, string year) → (List<TimeslotsLeagueSeasonFields>, int pairingCount)
```

**Implementation:** `TSIC.Infrastructure/Repositories/TimeslotRepository.cs`

### Phase 3: Backend — Service

**Interface:** `TSIC.Contracts/Services/ITimeslotService.cs`
**Implementation:** `TSIC.API/Services/Scheduling/TimeslotService.cs`

Orchestrates the repository calls, handles cartesian product logic for bulk field timeslot creation, and computes capacity previews.

### Phase 4: Backend — Controller

**File:** `TSIC.API/Controllers/TimeslotController.cs`

```
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/[controller]")]

GET    /api/timeslot/agegroups                  → GetAgegroupsAsync()
GET    /api/timeslot/{agegroupId}/dates          → GetDatesAsync(agegroupId)
GET    /api/timeslot/{agegroupId}/fields          → GetFieldTimeslotsAsync(agegroupId)
GET    /api/timeslot/{agegroupId}/capacity        → GetCapacityPreviewAsync(agegroupId)

POST   /api/timeslot/date                        → AddDateAsync(request)
PUT    /api/timeslot/date                        → EditDateAsync(request)
DELETE /api/timeslot/date/{ai}                   → DeleteDateAsync(ai)
POST   /api/timeslot/date/clone                  → CloneDateRecordAsync(request)

POST   /api/timeslot/field                       → AddFieldTimeslotAsync(request)
PUT    /api/timeslot/field                       → EditFieldTimeslotAsync(request)
DELETE /api/timeslot/field/{ai}                  → DeleteFieldTimeslotAsync(ai)

POST   /api/timeslot/clone-dates                 → CloneDatesAsync(request)
POST   /api/timeslot/clone-fields                → CloneFieldsAsync(request)
POST   /api/timeslot/clone-by-field              → CloneByFieldAsync(request)
POST   /api/timeslot/clone-by-division           → CloneByDivisionAsync(request)
POST   /api/timeslot/clone-by-dow                → CloneByDowAsync(request)
POST   /api/timeslot/clone-field-dow             → CloneFieldDowAsync(request)

DELETE /api/timeslot/dates/all                   → DeleteAllDatesAsync()
DELETE /api/timeslot/dates/{agegroupId}          → DeleteAgegroupDatesAsync(agId)
DELETE /api/timeslot/fields/all                  → DeleteAllFieldTimeslotsAsync()
DELETE /api/timeslot/fields/{agegroupId}         → DeleteAgegroupFieldTimeslotsAsync(agId)

POST   /api/timeslot/field/add-to-agegroup       → AddFieldToAgegroupAsync(request)
POST   /api/timeslot/field/remove-from-agegroup  → RemoveFieldFromAgegroupAsync(request)
```

### Phase 5: Frontend — Generate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

### Phase 6: Frontend — Components

**Main:** `src/app/views/admin/scheduling/timeslots/manage-timeslots.component.ts`

Tabbed layout with three child components:
- `timeslot-dates.component.ts` — dates grid with clone action buttons
- `timeslot-fields.component.ts` — field timeslots grid with inline editing
- `timeslot-capacity.component.ts` — computed capacity vs. demand preview

Key signals:
- `selectedAgegroup` — signal<AgegroupSummaryDto | null>
- `dates` — signal<TimeslotDateDto[]>
- `fieldTimeslots` — signal<TimeslotFieldDto[]>
- `capacityPreview` — signal<CapacityPreviewDto[]>

### Phase 7: Frontend — Route

```typescript
{
  path: 'admin/scheduling/timeslots',
  loadComponent: () => import('./views/admin/scheduling/timeslots/manage-timeslots.component')
    .then(m => m.ManageTimeslotsComponent),
  canActivate: [authGuard],
  data: { roles: ['SuperUser', 'Director', 'SuperDirector'] }
}
```

### Phase 8: Frontend — Agegroup Filtering

The agegroup navigator filters out non-schedulable entries on the frontend:
- **Excluded:** "Dropped Teams" agegroup (exact match, case-insensitive)
- **Excluded:** Any agegroup starting with "WAITLIST" (e.g., WAITLIST, WAITLISTxxx)
- **Sorted:** Remaining agegroups sorted alphabetically by name

This is identical to the filtering in Pairings (009-2, Phase 9) and applies to all scheduling pages that use the shared agegroup navigator.

### Phase 9: Testing

- Verify date cloning: +1 day, +1 week, +1 round all produce correct values
- Verify DOW cycling: Mon→Tue→Wed→Thu→Fri→Sat→Sun→Mon
- Verify cartesian product: adding field timeslot with no specific field/div creates all combinations
- Verify clone-dates deletes target agegroup dates before inserting
- Verify clone-fields fails if target agegroup already has timeslots
- Verify capacity preview accurately reflects MaxGamesPerField × field count
- Verify field override integration with FieldOverridesStartTimeMaxMinGames
- Verify all delete-all operations require confirmation
