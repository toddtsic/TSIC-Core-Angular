# 024 — Master Schedule Page Migration

**Status**: PLANNED
**Date**: 2026-02-26
**Legacy Endpoint**: `MasterSchedule/Show`
**Priority**: High — Excel export is critical; grid content correct, UI modernization needed

---

## Overview

Migrate the legacy **MasterSchedule/Show** page to a standalone Angular 21 page. The legacy page renders a dense date×field pivot grid showing all scheduled games, color-coded by agegroup, with optional referee names for admin users. The Excel export is the most important feature — directors print individual day sheets or distribute the master schedule as a spreadsheet.

**What's already built**: The 009-5 View Schedule system (`ViewScheduleController`, `IScheduleRepository`, `ViewScheduleService`) provides the underlying game data and repository methods. The Master Schedule reuses this data layer but is a **separate page with its own purpose** — a print-friendly cross-tab pivot, not a filterable search tool.

**Key differences from View Schedule (009-5)**:
- **Standalone page** with its own route and nav menu entry (Schedules → Master Schedule)
- **No CADT filtering** — loads ALL games for the job
- **Day tabs** — each game day is its own tab, showing one self-contained grid (easier to print/export individually)
- **Read-only** — no inline score editing; that lives in View Schedule
- **Excel-first** — the grid is optimized to match what the Excel export produces

### Legacy URL → New Route

| Legacy | New Route | Purpose |
|--------|-----------|---------|
| `MasterSchedule/Show?jSeg={segment}` | `/:jobPath/scheduling/master-schedule` | Date×field pivot grid with Excel export |

### Nav Menu Entry

Director nav → Schedules group:
```sql
INSERT INTO [nav].[NavItem] (NavId, ParentNavItemId, Text, IconName, RouterLink, SortOrder, Active, Modified)
VALUES (@schedulingNavId, @schedulingParentId, N'Master Schedule', N'grid-3x3-gap', N'scheduling/master-schedule', 8, 1, GETUTCDATE());
```

---

## Phase 1 — Backend: DTOs + Service + Export + Controller

The existing `IScheduleRepository.GetFilteredGamesAsync()` provides game data. We pass an empty `ScheduleFilterRequest` to get all games, then pivot server-side.

### 1A. DTOs (`TSIC.Contracts/Dtos/Scheduling/MasterScheduleDtos.cs` — new file)

```csharp
public record MasterScheduleResponse
{
    public required List<MasterScheduleDay> Days { get; init; }
    public required List<string> FieldColumns { get; init; }
    public required int TotalGames { get; init; }
}

public record MasterScheduleDay
{
    public required string DayLabel { get; init; }           // "Saturday, March 14, 2026"
    public required string ShortLabel { get; init; }         // "Sat Mar 14" (for tabs)
    public required int GameCount { get; init; }
    public required List<MasterScheduleRow> Rows { get; init; }
}

public record MasterScheduleRow
{
    public required string TimeLabel { get; init; }          // "9:00 AM"
    public required DateTime SortKey { get; init; }
    public required List<MasterScheduleCell?> Cells { get; init; }  // One per field column (null = empty slot)
}

public record MasterScheduleCell
{
    public required int Gid { get; init; }
    public required string T1Name { get; init; }
    public required string T2Name { get; init; }
    public required string AgDiv { get; init; }              // "U14:Gold"
    public required string? Color { get; init; }             // Agegroup hex color
    public required string? ContrastColor { get; init; }     // Computed: "#000" or "#fff"
    public required int? T1Score { get; init; }
    public required int? T2Score { get; init; }
    public required string? T1Ann { get; init; }
    public required string? T2Ann { get; init; }
    public required int? GStatusCode { get; init; }
    public required List<string>? Referees { get; init; }    // Admin-only; null for non-admin
}

public record MasterScheduleExportRequest
{
    public required bool IncludeReferees { get; init; }
    public required int? DayIndex { get; init; }             // null = all days, 0..N = single day sheet
}
```

### 1B. Contrast Color Utility (`TSIC.API/Utilities/ColorUtility.cs` — new)

Server-side contrast calculation matching the frontend `contrastColor()` logic in `games-tab.component.ts:674` (luminance threshold at 150):

```csharp
public static class ColorUtility
{
    /// <summary>
    /// Returns "#000" or "#fff" for readable text on the given background color.
    /// Uses ITU-R BT.601 luminance — same formula as frontend.
    /// </summary>
    public static string GetContrastColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor)) return "#000";

        var hex = hexColor.TrimStart('#');
        if (hex.Length < 6) return "#000";

        var r = Convert.ToInt32(hex[..2], 16);
        var g = Convert.ToInt32(hex[2..4], 16);
        var b = Convert.ToInt32(hex[4..6], 16);

        var luminance = 0.299 * r + 0.587 * g + 0.114 * b;
        return luminance > 150 ? "#000" : "#fff";
    }
}
```

### 1C. Repository Extension (`IScheduleRepository` — extend)

```csharp
Task<Dictionary<int, List<string>>> GetRefereeAssignmentsForGamesAsync(
    List<int> gids, CancellationToken ct = default);
```

Implementation joins `RefGameAssigments → Registrations → Users`:
```csharp
public async Task<Dictionary<int, List<string>>> GetRefereeAssignmentsForGamesAsync(
    List<int> gids, CancellationToken ct = default)
{
    return await _context.RefGameAssigments
        .AsNoTracking()
        .Where(r => gids.Contains(r.GameId) && r.RefRegistration != null)
        .Select(r => new {
            r.GameId,
            Name = r.RefRegistration!.User.LastName + ", " + r.RefRegistration.User.FirstName
        })
        .GroupBy(r => r.GameId)
        .ToDictionaryAsync(
            g => g.Key,
            g => g.Select(x => x.Name).OrderBy(n => n).ToList(),
            ct);
}
```

### 1D. Service (`TSIC.Contracts/Services/IMasterScheduleService.cs` — new)

Separate service — this is its own page, not an extension of View Schedule.

```csharp
public interface IMasterScheduleService
{
    Task<MasterScheduleResponse> GetMasterScheduleAsync(
        Guid jobId, bool includeReferees, CancellationToken ct = default);

    Task<byte[]> ExportExcelAsync(
        Guid jobId, bool includeReferees, int? dayIndex, CancellationToken ct = default);
}
```

**`GetMasterScheduleAsync` implementation** (`TSIC.API/Services/Scheduling/MasterScheduleService.cs`):
1. Call `IScheduleRepository.GetFilteredGamesAsync(jobId, new ScheduleFilterRequest(), ct)` — empty filter = all games
2. Extract distinct field names (sorted alphabetically) → `fieldColumns`
3. Group games by date (day boundary), sort days chronologically
4. Within each day, group by time → rows sorted by time
5. For each row, build `MasterScheduleCell?[]` indexed by field column position
6. Compute `ContrastColor` for each cell via `ColorUtility.GetContrastColor(color)`
7. If `includeReferees`, call `GetRefereeAssignmentsForGamesAsync(allGids)` and merge
8. Return `MasterScheduleResponse` with all days

**`ExportExcelAsync` implementation**:
1. Call `GetMasterScheduleAsync()` to get pivot structure
2. EPPlus workbook with `ExcelPackage.License.SetNonCommercialPersonal("Todd Greenwald")`
3. If `dayIndex` is null → one worksheet per day; if set → single worksheet for that day
4. Per worksheet: Row 1 headers, data rows with formatted cells (see Phase 5)
5. Return `package.GetAsByteArray()`

### 1E. Controller (`TSIC.API/Controllers/MasterScheduleController.cs` — new)

```csharp
[ApiController]
[Route("api/master-schedule")]
[Authorize]
public class MasterScheduleController : ControllerBase
{
    // GET  api/master-schedule          → MasterScheduleResponse (all days)
    // POST api/master-schedule/export   → .xlsx file download
}
```

**`GET /api/master-schedule`**:
- Resolves jobId from JWT via `User.GetJobIdFromRegistrationAsync(_jobLookupService)`
- `includeReferees` = `IsAdmin()` (server decides, no client toggle needed for GET)
- Returns `MasterScheduleResponse`

**`POST /api/master-schedule/export`**:
- Body: `MasterScheduleExportRequest` (includeReferees + optional dayIndex)
- Referee inclusion gated by `IsAdmin()` on server regardless of client request
- Returns `File(bytes, contentType, fileName)`
- File name: `MasterSchedule-{dayShortLabel}.xlsx` (single day) or `MasterSchedule-Full.xlsx` (all days)

### 1F. DI Registration (`Program.cs`)

```csharp
builder.Services.AddScoped<IMasterScheduleService, MasterScheduleService>();
```

### 1G. Verify Backend

```bash
dotnet build
```

---

## Phase 2 — Frontend: API Models + Service

### 2A. Regenerate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

### 2B. Service (`master-schedule.service.ts` — new)

```typescript
@Injectable({ providedIn: 'root' })
export class MasterScheduleService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/master-schedule`;

    getMasterSchedule(): Observable<MasterScheduleResponse> {
        return this.http.get<MasterScheduleResponse>(this.apiUrl);
    }

    exportExcel(includeReferees: boolean, dayIndex?: number): Observable<Blob> {
        return this.http.post(this.apiUrl + '/export',
            { includeReferees, dayIndex: dayIndex ?? null },
            { responseType: 'blob' });
    }
}
```

---

## Phase 3 — Frontend: Master Schedule Page

### 3A. Component Structure

```
views/admin/scheduling/master-schedule/
├── master-schedule.component.ts
├── master-schedule.component.html
├── master-schedule.component.scss
└── services/
    └── master-schedule.service.ts
```

### 3B. Route (`app.routes.ts`)

```typescript
{
    path: 'scheduling/master-schedule',
    canActivate: [authGuard],
    data: { requirePhase2: true },
    loadComponent: () => import('./views/admin/scheduling/master-schedule/master-schedule.component')
        .then(m => m.MasterScheduleComponent)
}
```

### 3C. Signals

```typescript
masterData = signal<MasterScheduleResponse | null>(null);
isLoading = signal(true);
isExporting = signal(false);
activeDayIndex = signal(0);
includeReferees = signal(false);   // Admin-only toggle
isAdmin = signal(false);
```

### 3D. Template Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│  Master Schedule                                                    │
│  48 games · 6 fields                                                │
│                                                                     │
│  [Export This Day ↓]  [Export All Days ↓]  [□ Include Referees]     │
│                                                                     │
│  ┌─ Day Tabs ─────────────────────────────────────────────────────┐ │
│  │ [Sat Mar 14 (24)] [Sun Mar 15 (18)] [Mon Mar 16 (6)]          │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                     │
│  ┌─────────┬────────────┬────────────┬────────────┬──────────────┐ │
│  │  Time   │  Field A   │  Field B   │  Field C   │  Field D     │ │
│  ├─────────┼────────────┼────────────┼────────────┼──────────────┤ │
│  │         │ ┌────────┐ │ ┌────────┐ │            │ ┌────────┐   │ │
│  │ 8:00 AM │ │U14:Gold│ │ │U12:Silv│ │            │ │U16:Blue│   │ │
│  │         │ │Eagles  │ │ │Hawks   │ │            │ │Strikers│   │ │
│  │         │ │  vs    │ │ │  vs    │ │     —      │ │  vs    │   │ │
│  │         │ │Falcons │ │ │Sharks  │ │            │ │Thunder │   │ │
│  │         │ │[Smith] │ │ │        │ │            │ │[Jones] │   │ │
│  │         │ └────────┘ │ └────────┘ │            │ └────────┘   │ │
│  ├─────────┼────────────┼────────────┼────────────┼──────────────┤ │
│  │ 9:00 AM │ ...        │ ...        │ ...        │ ...          │ │
│  └─────────┴────────────┴────────────┴────────────┴──────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### 3E. Day Tabs

Each game day is its own tab. The tab label shows the short date + game count badge:

```html
<div class="day-tabs">
    @for (day of masterData()!.days; track day.dayLabel; let i = $index) {
        <button class="day-tab"
                [class.active]="activeDayIndex() === i"
                (click)="activeDayIndex.set(i)">
            {{ day.shortLabel }}
            <span class="badge">{{ day.gameCount }}</span>
        </button>
    }
</div>
```

Only the active day's grid is rendered. This keeps the DOM light and makes it obvious which day you're looking at — critical when directors print individual days.

### 3F. Cell Rendering

Each game cell gets the full agegroup background color with server-computed contrasting text:

```html
<div class="ms-cell"
     [style.background-color]="cell.color"
     [style.color]="cell.contrastColor">
    <span class="ms-agdiv">{{ cell.agDiv }}</span>
    <span class="ms-team">{{ cell.t1Name }}</span>
    <span class="ms-vs">vs</span>
    <span class="ms-team">{{ cell.t2Name }}</span>
    @if (cell.t1Score != null) {
        <span class="ms-score">{{ cell.t1Score }}–{{ cell.t2Score }}</span>
    }
    @if (cell.referees?.length) {
        <span class="ms-refs">{{ cell.referees.join(', ') }}</span>
    }
</div>
```

**Agegroup colors**: `color` is the hex background, `contrastColor` is pre-computed `"#000"` or `"#fff"` from the server. No client-side computation needed.

### 3G. Two Export Buttons

| Button | Behavior |
|--------|----------|
| **Export This Day** | Downloads Excel with single worksheet for the active tab's day |
| **Export All Days** | Downloads Excel with one worksheet per day |

```typescript
exportDay(): void {
    this.isExporting.set(true);
    this.masterScheduleService
        .exportExcel(this.includeReferees(), this.activeDayIndex())
        .subscribe({
            next: (blob) => this.downloadBlob(blob,
                `MasterSchedule-${this.activeDay().shortLabel}.xlsx`),
            error: () => this.toastService.error('Export failed'),
            complete: () => this.isExporting.set(false)
        });
}

exportAll(): void {
    this.isExporting.set(true);
    this.masterScheduleService
        .exportExcel(this.includeReferees())
        .subscribe({
            next: (blob) => this.downloadBlob(blob, 'MasterSchedule-Full.xlsx'),
            error: () => this.toastService.error('Export failed'),
            complete: () => this.isExporting.set(false)
        });
}
```

### 3H. Responsive Behavior

- **Desktop (≥992px)**: Full pivot grid; horizontal scroll if many fields
- **Tablet (768–991px)**: Grid with horizontal scroll, sticky time column
- **Mobile (<768px)**: Card-per-timeslot layout — each card shows time + field + matchup stacked vertically

---

## Phase 4 — Styling

All values from design system tokens — NO hardcoded colors.

```scss
:host {
    display: block;
    padding: var(--space-4);
}

.page-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    flex-wrap: wrap;
    gap: var(--space-3);
    margin-bottom: var(--space-4);
}

// ── Day Tabs ──
.day-tabs {
    display: flex;
    gap: var(--space-1);
    overflow-x: auto;
    border-bottom: 2px solid var(--border-color);
    margin-bottom: var(--space-4);
}

.day-tab {
    padding: var(--space-2) var(--space-4);
    border: none;
    background: transparent;
    cursor: pointer;
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
    color: var(--text-secondary);
    border-bottom: 2px solid transparent;
    margin-bottom: -2px;
    white-space: nowrap;
    transition: all 0.15s cubic-bezier(0.4, 0, 0.2, 1);

    &.active {
        color: var(--bs-primary);
        border-bottom-color: var(--bs-primary);
        font-weight: var(--font-weight-semibold);
    }

    .badge {
        font-size: var(--font-size-xs);
        background: var(--bg-elevated);
        padding: 1px var(--space-1);
        border-radius: var(--radius-full);
        margin-left: var(--space-1);
    }

    &.active .badge {
        background: rgba(var(--bs-primary-rgb), 0.15);
        color: var(--bs-primary);
    }
}

// ── Pivot Grid ──
.ms-grid {
    display: grid;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-md);
    overflow-x: auto;
    font-size: var(--font-size-sm);
    background: var(--bs-card-bg);
}

.ms-header-cell {
    background: var(--bg-elevated);
    font-weight: var(--font-weight-semibold);
    padding: var(--space-2) var(--space-3);
    border-bottom: 2px solid var(--border-color);
    text-align: center;
    position: sticky;
    top: 0;
    z-index: 2;
}

.ms-time-cell {
    font-weight: var(--font-weight-medium);
    padding: var(--space-2) var(--space-3);
    background: var(--bg-elevated);
    position: sticky;
    left: 0;
    z-index: 1;
    white-space: nowrap;
    border-right: 1px solid var(--border-color);
}

.ms-cell {
    padding: var(--space-1) var(--space-2);
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    margin: var(--space-1);
    display: flex;
    flex-direction: column;
    gap: 2px;
    font-size: var(--font-size-xs);
    line-height: var(--line-height-tight);
    min-width: 120px;
    // background-color and color set inline from agegroup values
}

.ms-agdiv {
    font-weight: var(--font-weight-semibold);
    font-size: 10px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
}

.ms-vs {
    font-size: 10px;
    opacity: 0.7;
    text-align: center;
}

.ms-score {
    font-weight: var(--font-weight-bold);
    text-align: center;
}

.ms-refs {
    font-size: 10px;
    opacity: 0.85;
    border-top: 1px solid currentColor;
    padding-top: 2px;
    margin-top: 2px;
}

.ms-empty {
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-secondary);
    opacity: 0.3;
    padding: var(--space-3);
}

// ── Responsive ──
@media (max-width: 767.98px) {
    .ms-grid { display: none; }
    .ms-mobile-cards { display: block; }
}
@media (min-width: 768px) {
    .ms-mobile-cards { display: none; }
}
```

---

## Phase 5 — Excel Export Formatting (Detail)

The Excel file is the most important deliverable. Directors print individual days and blow them up at the printer.

### 5A. Worksheet Structure

- `dayIndex` = null → one worksheet per game day (sheet names: "Sat Mar 14", "Sun Mar 15", etc.)
- `dayIndex` = N → single worksheet for that day only (sheet name: day's short label)
- Sheet names truncated to 31 chars (Excel limit)

### 5B. Column Layout

| Col A | Col B | Col C | Col D | ... |
|-------|-------|-------|-------|-----|
| Time  | Field A | Field B | Field C | ... |

### 5C. Cell Content (multi-line, wrap text)

```
U14:Gold
Eagles
  vs
Falcons
(3–1)
[Smith, Jones]     ← only if includeReferees
```

### 5D. Cell Formatting (EPPlus)

```csharp
// Game cell — agegroup color background + contrasting text
cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
cell.Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml(agColor));
cell.Style.Font.Color.SetColor(ColorTranslator.FromHtml(contrastColor));
cell.Style.WrapText = true;
cell.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
cell.Style.Font.Size = 9;
cell.Style.Border.BorderAround(ExcelBorderStyle.Thin);

// Header row
headerCell.Style.Font.Bold = true;
headerCell.Style.Font.Size = 10;
headerCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
headerCell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(230, 230, 230));
```

### 5E. Print Setup (per worksheet)

```csharp
worksheet.PrinterSettings.Orientation = eOrientation.Landscape;
worksheet.PrinterSettings.FitToPage = true;
worksheet.PrinterSettings.FitToWidth = 1;    // Fit all columns on one page width
worksheet.PrinterSettings.FitToHeight = 0;   // As many pages tall as needed
worksheet.PrinterSettings.RepeatRows = new ExcelAddress("1:1");  // Repeat header on each page
```

This ensures each day's sheet prints cleanly on its own — directors can hand individual day sheets to field marshals.

---

## Key Design Decisions

### 1. Standalone Page (Not a Tab in View Schedule)
Directors want a top-level nav item: **Schedules → Master Schedule**. The page loads all games for the job with no filters — it's a full-event overview, not a search tool. This also makes the route bookmarkable and shareable.

### 2. Day Tabs (Not Day Pills or Single Scroll)
Each game day is its own tab with its own grid. Benefits:
- Lighter DOM (one grid at a time)
- Clear mental model for multi-day events
- **Maps 1:1 to Excel worksheets** — what you see on screen is what prints
- "Export This Day" downloads exactly the active tab's grid

### 3. No Filtering
The master schedule shows everything. If a director wants to filter by agegroup or field, they use View Schedule (009-5). This page is the "print the whole thing" page.

### 4. Server-Side Pivot + Contrast Colors
Pivot grouping (date → time → field columns) and contrast color computation happen on the server because both the UI and Excel export need them. `ColorUtility.GetContrastColor()` uses the same ITU-R BT.601 luminance formula as `games-tab.component.ts:674`.

### 5. Separate Service (Not Extension of ViewScheduleService)
This is its own page with its own controller. Keeps `ViewScheduleService` focused on the filtered search use case. `MasterScheduleService` depends on `IScheduleRepository` directly.

### 6. Referee Toggle (Admin-Only)
Referees require a join through `RefGameAssigments → Registrations → Users`. The toggle is visible only to admin users. Server enforces: non-admin callers get `referees: null` regardless of request.

---

## Deferred Items

| Item | Reason |
|------|--------|
| Bracket seed display (`Home1 (Gold#2)`) | Only needed when schedule has unresolved bracket slots; add later if needed |
| Click-to-edit from master grid | View Schedule already has inline edit; master grid is read-only |
| Public (unauthenticated) access | Directors-only for now; public users use View Schedule |
| Browser print CSS | Excel export covers the print use case; browser `@media print` is lower priority |

---

## Dependencies

| Dependency | Status |
|------------|--------|
| `IScheduleRepository` / `ScheduleRepository` | ✅ Exists (009-5) — reuse `GetFilteredGamesAsync` with empty filter |
| `ViewScheduleDtos.cs` (`ViewGameDto`, `ScheduleFilterRequest`) | ✅ Exists (009-5) — used internally by service |
| `EPPlus` (OfficeOpenXml) | ✅ Already referenced in `ReportingService` |
| `RefGameAssigments` entity | ✅ Exists with nav properties to Registrations/Users |
| `IJobLookupService` | ✅ For jobId resolution from JWT |
| `ToastService` | ✅ For export error feedback |

---

## Files Created/Modified

### Backend — New Files
| File | Description |
|------|-------------|
| `TSIC.Contracts/Dtos/Scheduling/MasterScheduleDtos.cs` | 5 DTOs: Response, Day, Row, Cell, ExportRequest |
| `TSIC.Contracts/Services/IMasterScheduleService.cs` | Service interface (2 methods) |
| `TSIC.API/Services/Scheduling/MasterScheduleService.cs` | Pivot logic + EPPlus export |
| `TSIC.API/Controllers/MasterScheduleController.cs` | 2 endpoints: GET grid, POST export |
| `TSIC.API/Utilities/ColorUtility.cs` | Static `GetContrastColor()` — luminance-based text color |

### Backend — Extended Files
| File | Changes |
|------|---------|
| `TSIC.Contracts/Repositories/IScheduleRepository.cs` | +1 method: `GetRefereeAssignmentsForGamesAsync` |
| `TSIC.Infrastructure/Repositories/ScheduleRepository.cs` | +1 method implementation |
| `TSIC.API/Program.cs` | +1 DI registration: `IMasterScheduleService` |

### Frontend — New Files
| File | Description |
|------|-------------|
| `master-schedule/master-schedule.component.ts` | Standalone page: day tabs, pivot grid, export buttons |
| `master-schedule/master-schedule.component.html` | Grid template + mobile card layout |
| `master-schedule/master-schedule.component.scss` | Day tabs, grid cells, responsive breakpoints |
| `master-schedule/services/master-schedule.service.ts` | HTTP service (2 methods) |

### Frontend — Extended Files
| File | Changes |
|------|---------|
| `app.routes.ts` | +1 route: `scheduling/master-schedule` |

---

## Execution Order

| Step | Phase | Description | Depends On |
|------|-------|-------------|------------|
| 1 | 1A | Create MasterScheduleDtos.cs | — |
| 2 | 1B | Create ColorUtility.cs | — |
| 3 | 1C | Add referee lookup to IScheduleRepository + impl | — |
| 4 | 1D | Create IMasterScheduleService + MasterScheduleService | 1, 2, 3 |
| 5 | 1E | Create MasterScheduleController | 4 |
| 6 | 1F | Register in Program.cs | 5 |
| 7 | 1G | `dotnet build` — verify 0 errors | 6 |
| 8 | 2A | Regenerate API models | 7 |
| 9 | 2B | Create master-schedule.service.ts | 8 |
| 10 | 3 | Build master-schedule component (page + grid + day tabs) | 9 |
| 11 | 3 | Add route to app.routes.ts | 10 |
| 12 | 4 | SCSS styling + responsive | 10 |
| 13 | — | Add nav menu entry (SQL) | 11 |
| 14 | — | Full integration test | 12, 13 |

---

## Testing Checklist

- [ ] Navigate to `/{jobPath}/scheduling/master-schedule` — page loads
- [ ] All games for the job appear (no filters applied)
- [ ] Day tabs show one tab per distinct game date with game count badges
- [ ] Switching day tabs renders that day's grid only
- [ ] Grid columns = "Time" + one per field (sorted alphabetically)
- [ ] Grid rows = one per distinct game time within the day (sorted chronologically)
- [ ] Cells show agegroup background color with readable contrasting text (black or white)
- [ ] Empty slots (no game at that time/field) render as blank cells
- [ ] "Include Referees" toggle visible only for admin users
- [ ] Toggling referees on shows referee names in cells
- [ ] "Export This Day" downloads `.xlsx` with single worksheet matching active tab
- [ ] "Export All Days" downloads `.xlsx` with one worksheet per day
- [ ] Excel cells have agegroup background fill + contrasting font color
- [ ] Excel prints cleanly in landscape (fit-to-width, header repeats)
- [ ] Single-day events show no tab bar (just the grid)
- [ ] Mobile layout switches to card view below 768px
- [ ] Horizontal scroll works on tablet with sticky time column
- [ ] Test all 8 color palettes — page chrome adapts correctly
- [ ] No hardcoded colors in SCSS (CSS variables only)
- [ ] Nav menu entry appears under Schedules for Director role

---

**Document Version**: 1.1
**Author**: Claude Code
**Last Updated**: 2026-02-26
