# New Widget Design Specs: Event Contact + YTD Registration Comparison

> **Status**: Design Spec — Ready for Review
> **Date**: 2026-02-17
> **Depends on**: Widget dashboard foundation (Phases 1-3 complete)

---

## Widget 1: Event Contact

### The Problem

Players, club reps, and families need a quick way to reach the person running the event. Today there's no obvious "who do I contact?" surface on the dashboard.

### What It Shows

A small, friendly card:

```
+------------------------------------------------------+
|                                                      |
|  For questions about this event, please contact:     |
|                                                      |
|  Jane Smith                                          |
|  jane.smith@lijsl.org                                |
|                                                      |
+------------------------------------------------------+
```

That's it. Name and a clickable `mailto:` link.

### Who Is the Contact?

The event contact is the **administrator with the earliest `RegistrationTs`** for the job — the same logic used by the legacy Job Administrators page. This is the person who set up the event.

```csharp
// Admin role IDs: SuperUser, SuperDirector, Director, ApiAuthorized,
//                 RefAssignor, StoreAdmin, STPAdmin
Registrations
    .Where(r => r.JobId == jobId
             && adminRoleIds.Contains(r.RoleId)
             && r.BActive == true)
    .OrderBy(r => r.RegistrationTs)
    .Select(r => new { r.User.FirstName, r.User.LastName, r.User.Email })
    .FirstOrDefault()
```

### Backend Design

#### New DTO (`TSIC.Contracts/Dtos/Widgets/EventContactDto.cs`)

```csharp
namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// The primary contact for an event — the earliest-registered administrator.
/// </summary>
public record EventContactDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
}
```

#### Repository Method (`WidgetRepository.cs`)

```csharp
public async Task<EventContactDto?> GetEventContactAsync(Guid jobId, CancellationToken ct)
{
    var adminRoleIds = new[]
    {
        RoleConstants.Superuser,
        RoleConstants.SuperDirector,
        RoleConstants.Director,
        RoleConstants.ApiAuthorized,
        RoleConstants.RefAssignor,
        RoleConstants.StoreAdmin,
        RoleConstants.StpAdmin,
    };

    return await _context.Registrations
        .AsNoTracking()
        .Where(r => r.JobId == jobId
                  && adminRoleIds.Contains(r.RoleId)
                  && r.BActive == true)
        .OrderBy(r => r.RegistrationTs)
        .Select(r => new EventContactDto
        {
            FirstName = r.User.FirstName ?? "",
            LastName = r.User.LastName ?? "",
            Email = r.User.Email ?? "",
        })
        .FirstOrDefaultAsync(ct);
}
```

#### Service + Controller

- `IWidgetDashboardService.GetEventContactAsync(Guid jobId, CancellationToken ct)`
- `GET /api/widget-dashboard/event-contact` -> returns `EventContactDto`

#### Frontend Component

- **Component**: `event-contact-widget/event-contact-widget.component.ts`
- **ComponentKey**: `event-contact`
- **Widget type**: `content`
- **Pattern**: Signal-based state, HTTP fetch in `ngOnInit`
- **Template**: Static label text + name + `mailto:` link. No card chrome needed — just a clean text block within the dashboard layout.
- **Styling**: `--brand-text` for the label, `--bs-primary` for the email link, `--space-*` grid for padding.
- **Empty state**: If no admin registration exists, hide the widget entirely (don't show an empty card).

#### Seed Data

```sql
INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
VALUES ('Event Contact', 'content', 'event-contact', @commCatId,
        'Primary contact for event questions');
```

Add to `widgets.WidgetDefault` for **all roles** (including Player, Club Rep, Staff, Anonymous/public) — everyone should be able to see who to contact.

### Role Visibility

| Role | Sees Widget? | Notes |
|------|-------------|-------|
| All roles | Yes | Everyone needs to know who to contact |
| Anonymous (public) | Yes | Visitors on public landing page need this too |

---

## Widget 2: YTD Registration Rate Comparison (Year-over-Year)

### The Problem

Directors want to know: "Are registrations coming in faster or slower than last year?" The current trend chart shows the absolute cumulative curve for the current job, but there's no reference line for prior years. Without that context, "350 registrations by February 15th" is just a number — is that ahead of pace or behind?

### What It Shows

An overlaid multi-line chart where each line represents one year's cumulative registration count, plotted against a **calendar X-axis** (Jan, Feb, Mar...). The current year is bold/highlighted; prior years are muted reference lines. Directors are seasonal — they think "where were we last January 15th?", not "where were we at Day 45?"

```
+--------------------------------------------------------------+
|  REGISTRATION PACE: YEAR OVER YEAR                           |
|                                                              |
|  +--------------------------------------------------------+  |
|  |                                            / 2026      |  |
|  | 800 - - - - - - - - - - - - - - - - - -/- - - - - - - |  |
|  |                                       /                |  |
|  |                                  /---- 2025 (742)      |  |
|  | 600 - - - - - - - - - - - -/-- ---- - - - - - - - - - |  |
|  |                          /  /                          |  |
|  |                       / /                              |  |
|  | 400 - - - - - - - -/-/- - - - - - - - - - - - - - - - |  |
|  |                 //              /------ 2024 (681)      |  |
|  |              //            /--                         |  |
|  | 200 - - -//- - - - - --/-- - - - - - - - - - - - - - |  |
|  |       //         /--                                   |  |
|  |    //       /--                                        |  |
|  | //     /--                                             |  |
|  +--------------------------------------------------------+  |
|    Nov     Dec     Jan     Feb     Mar     Apr     May       |
|                                                              |
|  Summary: 2026 is 12% ahead of 2025 at the same date        |
|           2025 finished with 742 total                       |
|                                                              |
|  * 2026 (current)  o 2025 (742 final)  o 2024 (681 final)   |
+--------------------------------------------------------------+
```

### Core Concept: "Sibling Jobs"

A year-over-year comparison requires identifying **the same event across multiple years**. In TSIC, a recurring event (e.g., "LIJSL Spring 2026") is a separate Job record each year. The linkage:

```
Same Customer + Same JobType + Same Sport + Same Season = Sibling Jobs
```

**Query**:
```sql
SELECT j.JobId, j.Year, j.JobName
FROM Jobs.Jobs j
WHERE j.CustomerId = @currentJob.CustomerId
  AND j.JobTypeId  = @currentJob.JobTypeId
  AND j.SportId    = @currentJob.SportId
  AND (j.Season    = @currentJob.Season OR (j.Season IS NULL AND @currentJob.Season IS NULL))
  AND j.Year       IS NOT NULL
ORDER BY j.Year DESC
```

### Calendar-Aligned X-Axis

Directors think in calendar time: "where were we last February 15th?" Registration seasons for the same event roughly align year-over-year (spring events open in late fall, summer events open in winter, etc.), so a calendar X-axis gives the intuitive comparison they want.

Each data point retains its **real calendar date** (month + day). The chart X-axis uses **month-day format** (e.g., "Nov 1", "Dec 15", "Jan 30"). Each year's series naturally occupies its own date range, and the chart renders them overlaid by mapping all dates onto a **shared month-day axis** (stripping the year component for alignment).

#### Date Alignment Strategy

To overlay lines from different calendar years onto the same axis, the frontend normalizes each date to a **synthetic shared year**:

```typescript
// Map all dates to a shared reference year (e.g., 2000) for axis alignment
// Nov 15, 2025  ->  Nov 15, 2000
// Nov 15, 2026  ->  Nov 15, 2000
const syntheticDate = new Date(2000, point.date.getMonth(), point.date.getDate());
```

This way Syncfusion's DateTime X-axis aligns Nov-to-May across all years. The legend and tooltips still show the real year. If a season spans a year boundary (e.g., Nov 2025 -> May 2026), the synthetic dates naturally wrap from Nov 2000 -> May 2001, which the axis handles correctly.

### Backend Design

#### New DTOs (`TSIC.Contracts/Dtos/Widgets/YearOverYearDtos.cs`)

```csharp
namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// Year-over-year registration pace comparison.
/// Each series represents one year's cumulative registration curve
/// plotted against calendar dates.
/// </summary>
public record YearOverYearComparisonDto
{
    /// <summary>
    /// One series per sibling job (year). Ordered most recent first.
    /// </summary>
    public required List<YearSeriesDto> Series { get; init; }

    /// <summary>
    /// The current job's year (highlighted series).
    /// </summary>
    public required string CurrentYear { get; init; }
}

/// <summary>
/// A single year's registration curve.
/// </summary>
public record YearSeriesDto
{
    public required string Year { get; init; }
    public required string JobName { get; init; }
    public required int FinalTotal { get; init; }

    /// <summary>
    /// Daily cumulative registration counts with real calendar dates.
    /// </summary>
    public required List<YearDayPointDto> DailyData { get; init; }
}

/// <summary>
/// A single data point on the year curve.
/// </summary>
public record YearDayPointDto
{
    /// <summary>
    /// The actual calendar date of this data point.
    /// </summary>
    public required DateTime Date { get; init; }

    /// <summary>
    /// Cumulative registration count as of this date.
    /// </summary>
    public required int CumulativeCount { get; init; }
}
```

#### Repository Method (`WidgetRepository.cs`)

```csharp
public async Task<YearOverYearComparisonDto> GetYearOverYearAsync(
    Guid currentJobId, CancellationToken ct)
{
    // 1. Get current job's identity fields
    var currentJob = await _context.Jobs
        .AsNoTracking()
        .Where(j => j.JobId == currentJobId)
        .Select(j => new
        {
            j.CustomerId,
            j.JobTypeId,
            j.SportId,
            j.Season,
            j.Year,
        })
        .FirstOrDefaultAsync(ct);

    if (currentJob?.Year == null)
        return new YearOverYearComparisonDto
        {
            Series = [],
            CurrentYear = "",
        };

    // 2. Find sibling jobs (same customer + type + sport + season)
    var siblings = await _context.Jobs
        .AsNoTracking()
        .Where(j => j.CustomerId == currentJob.CustomerId
                  && j.JobTypeId == currentJob.JobTypeId
                  && j.SportId == currentJob.SportId
                  && j.Season == currentJob.Season
                  && j.Year != null)
        .OrderByDescending(j => j.Year)
        .Select(j => new { j.JobId, j.Year, j.JobName })
        .ToListAsync(ct);

    // Cap at 4 most recent years for chart readability
    var recentSiblings = siblings.Take(4).ToList();

    // 3. For each sibling, get daily registration counts (sequential — shared DbContext)
    var series = new List<YearSeriesDto>();

    foreach (var sibling in recentSiblings)
    {
        var dailyRaw = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == sibling.JobId && r.BActive == true)
            .GroupBy(r => r.RegistrationTs.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        if (dailyRaw.Count == 0) continue;

        // Build cumulative totals — keep real calendar dates
        var cumulative = 0;
        var points = dailyRaw.Select(d =>
        {
            cumulative += d.Count;
            return new YearDayPointDto
            {
                Date = d.Date,
                CumulativeCount = cumulative,
            };
        }).ToList();

        series.Add(new YearSeriesDto
        {
            Year = sibling.Year!,
            JobName = sibling.JobName ?? sibling.Year!,
            FinalTotal = cumulative,
            DailyData = points,
        });
    }

    return new YearOverYearComparisonDto
    {
        Series = series,
        CurrentYear = currentJob.Year,
    };
}
```

> **Note on sequential awaits**: The `foreach` loop issues one query per sibling job. This is intentional — DbContext is not thread-safe. With typically 2-4 sibling years, this is 2-4 lightweight queries. Not a performance concern.

#### Service + Controller

- `IWidgetDashboardService.GetYearOverYearAsync(Guid jobId, CancellationToken ct)`
- `GET /api/widget-dashboard/year-over-year` -> returns `YearOverYearComparisonDto`

#### Frontend Component

- **Component**: `year-over-year-widget/year-over-year-widget.component.ts`
- **ComponentKey**: `year-over-year`
- **Widget type**: `chart`
- **Chart**: Syncfusion EJ2 Chart with:
  - **X-axis**: DateTime, label format = "MMM d" (month-day), using synthetic shared-year dates for alignment
  - **Y-axis**: Numeric (Cumulative Count), label = "Registrations"
  - **Series**: One `Spline` series per year
    - Current year: thick stroke (`width: 3`), uses `--bs-primary` color
    - Prior years: thin stroke (`width: 1.5`), muted palette colors (`--brand-accent`, `--bs-secondary`, etc.), dashed line style
  - **Legend**: Bottom, showing year + final total (e.g., "2025 (742 final)")
  - **Tooltip**: Shared crosshair tooltip showing all years' values at the hovered date, displaying real year in tooltip text (e.g., "Feb 15, 2025: 312 | Feb 15, 2026: 350")
  - **Annotation**: Summary text badge — "X% ahead/behind [prior year] at the same date"
- **Collapsible**: Yes, via `app-collapsible-chart-card`
- **Summary badges** (always visible even when collapsed):
  - Current year count so far
  - Pace vs. prior year (% ahead/behind at same calendar date)
  - Prior year final total

#### Date Alignment (Frontend)

```typescript
// Normalize all dates to a shared synthetic year for axis alignment
private toSyntheticDate(realDate: Date): Date {
  const month = realDate.getMonth();
  const day = realDate.getDate();
  // Use 2000 as base year; if month >= 7 (Jul+), keep 2000;
  // if month < 7, use 2001 — handles Nov->May season spans
  const syntheticYear = month >= 7 ? 2000 : 2001;
  return new Date(syntheticYear, month, day);
}
```

#### Pace Calculation (Frontend Computed Signal)

```typescript
// Compare current year to most recent prior year at the same calendar date
const currentSeries = data.series.find(s => s.year === data.currentYear);
const priorSeries = data.series.find(s => s.year !== data.currentYear);

if (currentSeries && priorSeries) {
  const latestDate = currentSeries.dailyData.at(-1)?.date;
  const currentCount = currentSeries.dailyData.at(-1)?.cumulativeCount ?? 0;

  // Find prior year's count at the same month-day
  const targetMonthDay = { month: latestDate.getMonth(), day: latestDate.getDate() };
  const priorAtSameDate = priorSeries.dailyData
    .filter(d => {
      const pd = new Date(d.date);
      return pd.getMonth() < targetMonthDay.month
        || (pd.getMonth() === targetMonthDay.month && pd.getDate() <= targetMonthDay.day);
    })
    .at(-1)?.cumulativeCount ?? 0;

  const pacePercent = priorAtSameDate > 0
    ? ((currentCount - priorAtSameDate) / priorAtSameDate) * 100
    : 0;
  // -> "+12% ahead" or "-8% behind"
}
```

### Seed Data

```sql
INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description)
VALUES ('Registration Pace YoY', 'chart', 'year-over-year',
        @dashChartCatId, 'Year-over-year registration pace comparison');
```

Add to `widgets.WidgetDefault` for Director + SuperDirector + SuperUser, placed in the `dashboard` workspace alongside existing chart widgets.

### Role Visibility

| Role | Sees Widget? | Notes |
|------|-------------|-------|
| SuperUser | Yes | Cross-year view |
| SuperDirector | Yes | Cross-year view |
| Director | Yes | Their events across years |
| Club Rep | No | Club reps don't manage multi-year events |
| Player/Staff | No | Not relevant |

### Edge Cases

| Scenario | Behavior |
|----------|----------|
| First-year event (no siblings) | Widget shows single line + "No prior year data for comparison" message |
| Job has no Year field set | Widget hidden (no data to compare) |
| Sibling job has zero registrations | Skip that year entirely (no empty series) |
| Season is NULL on some siblings | NULL = NULL match (IS NULL symmetry) |
| 5+ years of history | Show most recent 4 years max (keep chart readable) |
| Season spans year boundary (Nov -> May) | Synthetic year mapping handles this: Jul-Dec -> 2000, Jan-Jun -> 2001 |

---

## Implementation Status

> **Last updated**: 2026-02-17
> **Status**: Code complete — pending widget editor configuration + testing

### Phase A: Event Contact Widget
1. [x] Create `EventContactDto.cs` in Contracts — `TSIC.Contracts/Dtos/Widgets/EventContactDto.cs`
2. [x] Add `GetEventContactAsync` to `IWidgetRepository` + `WidgetRepository`
3. [x] Add `GetEventContactAsync` to `IWidgetDashboardService` + `WidgetDashboardService`
4. [x] Add endpoints to `WidgetDashboardController`:
   - `GET /api/widget-dashboard/event-contact` (authenticated)
   - `GET /api/widget-dashboard/public/{jobPath}/event-contact` (anonymous)
5. [x] Regenerate API models (`.\scripts\2-Regenerate-API-Models.ps1`)
6. [x] Create Angular component: `event-contact-widget/event-contact-widget.component.ts`
7. [x] Register in `@switch` (public mode, hub content, hub chart tiles sections)
8. [x] Add to `widget-dashboard.service.ts` (`getEventContact()`, `getPublicEventContact()`)
9. [x] Style with design system tokens (CSS vars only)
10. [ ] **Widget Editor**: Create widget definition (`componentKey: 'event-contact'`, type: `content`) and assign to desired workspaces/roles

### Phase B: YTD Registration Rate Comparison
1. [x] Create `YearOverYearDtos.cs` in Contracts — `TSIC.Contracts/Dtos/Widgets/YearOverYearDtos.cs`
2. [x] Add `GetYearOverYearAsync` to `IWidgetRepository` + `WidgetRepository`
3. [x] Add `GetYearOverYearAsync` to `IWidgetDashboardService` + `WidgetDashboardService`
4. [x] Add endpoint to `WidgetDashboardController`: `GET /api/widget-dashboard/year-over-year`
5. [x] Regenerate API models
6. [x] Create Angular component: `year-over-year-widget/year-over-year-widget.component.ts`
   - Syncfusion Spline chart with synthetic-year date alignment
   - Current year bold, prior years dashed/muted
   - Pace badge (% ahead/behind), collapsible card
7. [x] Register in `@switch` (hub chart tiles section)
8. [x] Add to `widget-dashboard.service.ts` (`getYearOverYear()`)
9. [x] Style with design system tokens (CSS vars, `_widget-summary.scss`)
10. [ ] **Widget Editor**: Create widget definition (`componentKey: 'year-over-year'`, type: `chart`) and assign to Dashboard Charts category for admin roles

### Files Created
| File | Description |
|------|-------------|
| `TSIC.Contracts/Dtos/Widgets/EventContactDto.cs` | Event contact DTO |
| `TSIC.Contracts/Dtos/Widgets/YearOverYearDtos.cs` | YoY comparison DTOs (3 records) |
| `event-contact-widget/event-contact-widget.component.ts` | Event contact Angular component |
| `event-contact-widget/event-contact-widget.component.html` | Event contact template |
| `event-contact-widget/event-contact-widget.component.scss` | Event contact styles |
| `year-over-year-widget/year-over-year-widget.component.ts` | YoY chart Angular component |
| `year-over-year-widget/year-over-year-widget.component.html` | YoY chart template |
| `year-over-year-widget/year-over-year-widget.component.scss` | YoY chart styles |

### Files Modified
| File | Changes |
|------|---------|
| `IWidgetRepository.cs` | +2 method signatures |
| `WidgetRepository.cs` | +2 method implementations (~100 lines) |
| `IWidgetDashboardService.cs` | +2 method signatures |
| `WidgetDashboardService.cs` | +2 passthrough methods |
| `WidgetDashboardController.cs` | +3 endpoints |
| `widget-dashboard.service.ts` | +3 HTTP methods |
| `widget-dashboard.component.ts` | +2 component imports |
| `widget-dashboard.component.html` | +3 `@case` entries |

### Widget Editor Configuration Needed

**Event Contact** (`event-contact`):
- Widget type: `content`
- Suggested category: Dashboard workspace (or new "Communications" category)
- Roles: All roles including Anonymous — everyone needs to know who to contact

**Registration Pace YoY** (`year-over-year`):
- Widget type: `chart`
- Suggested category: Dashboard Charts (alongside player-trend, team-trend, agegroup-distribution)
- Roles: Admin only (SuperUser, SuperDirector, Director)

### Resolved Decisions
1. **Season NULL handling**: Match NULL = NULL (implemented in sibling query)
2. **Max years**: 4 most recent (capped in repository)
3. **Revenue overlay on YoY chart**: Registrations only — keeps chart focused on pace
