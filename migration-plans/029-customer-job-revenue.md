# 029 — Customer Job Revenue Dashboard

## Legacy Route
`CustomerJobRevenue/Index`

## Overview
SuperUser/SuperDirector financial dashboard showing cross-job revenue rollups, player/team monthly counts, admin fees, and payment records. Uses two stored procedures (`[reporting].[CustomerJobRevenueRollups]` and `[reporting].[CustomerJobRevenueRollups_NotTSICADN]`) that return 6 result sets. The UI is 5 Syncfusion tabs: a PivotView for revenue rollups + 4 Grids (counts, admin fees, CC records, check records). Monthly counts grid is inline-editable by SuperUsers.

## Legacy Analysis

### Auth
- `[Authorize(Policy = "SuperUserOnly")]` + `[Authorize(Roles = "Superuser,SuperDirector")]`

### Stored Procedures
Two sprocs selected based on whether the customer uses TSIC's ADN account or their own:
- `[reporting].[CustomerJobRevenueRollups]` — TSIC ADN customers (PayAmount is `decimal`)
- `[reporting].[CustomerJobRevenueRollups_NotTSICADN]` — customer's own ADN (PayAmount is `double`, cast to `decimal`)

Parameters: `@jobId`, `@startDate`, `@endDate`, `@listJobsString` (comma-delimited)

### 6 Result Sets
1. **JobRevenueRecords** — JobName, Year, Month, PayMethod, PayAmount (pivot source)
2. **JobMonthlyCounts** — aid, JobName, Year, Month, 6 count columns (editable)
3. **JobAdminFees** — JobName, Year, Month, ChargeType, ChargeAmount, Comment
4. **JobCCRecords** — JobName, Year, Month, Registrant, PaymentMethod, PaymentDate, PaymentAmount
5. **JobCheckRecords** — same shape as CC records
6. **ListAvailableJobs** — JobName (for multi-select filter)

### UI Tabs
| Tab | Syncfusion Component | Features |
|-----|---------------------|----------|
| Revenue Rollup | `ejs-pivotview` | Rows: Job→Year→Month, Cols: PayMethod, Values: Sum(PayAmount), expand/collapse all, Excel+PDF export |
| Player/Team Counts | `ejs-grid` | Editable by SuperUser (RemoteSaveAdaptor), Excel export |
| Admin Fee Records | `ejs-grid` | Read-only, Excel export |
| Credit Card Records | `ejs-grid` | Read-only, Excel export |
| Check Records | `ejs-grid` | Read-only, Excel export |

### Date Filter
- Start/End date dropdowns — monthly buckets from current month-1 back to Jan 2022
- Multi-select job filter populated from result set 6
- POST to `RefreshJobRevenueData` redirects back to Index with new params

---

## Migration Plan

### Phase 1: Backend — DTOs

**File: `TSIC.Contracts/Dtos/CustomerJobRevenue/CustomerJobRevenueDtos.cs`** (new)

```csharp
public record JobRevenueQueryRequest
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public List<string> JobNames { get; init; } = [];
}

public record JobRevenueDataDto
{
    public required List<JobRevenueRecordDto> RevenueRecords { get; init; }
    public required List<JobMonthlyCountDto> MonthlyCounts { get; init; }
    public required List<JobAdminFeeDto> AdminFees { get; init; }
    public required List<JobPaymentRecordDto> CreditCardRecords { get; init; }
    public required List<JobPaymentRecordDto> CheckRecords { get; init; }
    public required List<string> AvailableJobs { get; init; }
}

public record JobRevenueRecordDto
{
    public required string JobName { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required string PayMethod { get; init; }
    public required decimal PayAmount { get; init; }
}

public record JobMonthlyCountDto
{
    public required int Aid { get; init; }
    public required string JobName { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required int CountActivePlayersToDate { get; init; }
    public required int CountActivePlayersToDateLastMonth { get; init; }
    public required int CountNewPlayersThisMonth { get; init; }
    public required int CountActiveTeamsToDate { get; init; }
    public required int CountActiveTeamsToDateLastMonth { get; init; }
    public required int CountNewTeamsThisMonth { get; init; }
}

public record JobAdminFeeDto
{
    public required string JobName { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required string ChargeType { get; init; }
    public required decimal ChargeAmount { get; init; }
    public required string Comment { get; init; }
}

public record JobPaymentRecordDto
{
    public required string JobName { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required string Registrant { get; init; }
    public required string PaymentMethod { get; init; }
    public required DateTime PaymentDate { get; init; }
    public required decimal PaymentAmount { get; init; }
}

public record UpdateMonthlyCountRequest
{
    public required int Aid { get; init; }
    public required int CountActivePlayersToDate { get; init; }
    public required int CountActivePlayersToDateLastMonth { get; init; }
    public required int CountNewPlayersThisMonth { get; init; }
    public required int CountActiveTeamsToDate { get; init; }
    public required int CountActiveTeamsToDateLastMonth { get; init; }
    public required int CountNewTeamsThisMonth { get; init; }
}
```

### Phase 2: Backend — Repository

**File: `TSIC.Contracts/Repositories/ICustomerJobRevenueRepository.cs`** (new)

```csharp
public interface ICustomerJobRevenueRepository
{
    /// Executes the appropriate CustomerJobRevenueRollups sproc and reads all 6 result sets.
    Task<JobRevenueDataDto> GetRevenueDataAsync(
        Guid jobId, DateTime startDate, DateTime endDate,
        string listJobsString, bool isTsicAdn,
        CancellationToken ct = default);

    /// Updates a single MonthlyJobStats row.
    Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default);
}
```

**File: `TSIC.Infrastructure/Repositories/CustomerJobRevenueRepository.cs`** (new)
- Injects `SqlDbContext`
- `GetRevenueDataAsync`: Opens raw `DbCommand`, calls `[reporting].[CustomerJobRevenueRollups]` or `[reporting].[CustomerJobRevenueRollups_NotTSICADN]` based on `isTsicAdn` flag, reads 6 result sets into DTO lists
- `UpdateMonthlyCountAsync`: Loads `MonthlyJobStats` entity by `aid`, updates fields, saves
- All `PayAmount` values normalized to `decimal` (cast from `double` for non-TSIC ADN sproc)

**Decision**: The TSIC-vs-non-TSIC ADN determination currently happens in the controller via `IAdnApiService`. Move that check into the **service layer** — the repository just receives the `bool isTsicAdn` flag.

### Phase 3: Backend — Service

**File: `TSIC.API/Services/Admin/ICustomerJobRevenueService.cs`** (new)
**File: `TSIC.API/Services/Admin/CustomerJobRevenueService.cs`** (new)

```csharp
public interface ICustomerJobRevenueService
{
    Task<JobRevenueDataDto> GetRevenueDataAsync(
        Guid jobId, DateTime startDate, DateTime endDate,
        List<string> jobNames, CancellationToken ct = default);

    Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default);
}
```

Service responsibilities:
- Calls `IAdnApiService.GetJobAdnCredentials_FromJobId()` to determine TSIC vs non-TSIC ADN
- Builds comma-delimited `listJobsString` from `List<string>`
- Delegates to repository
- Default date logic (previous month) lives in the **frontend**, not the service

### Phase 4: Backend — Controller

**File: `TSIC.API/Controllers/CustomerJobRevenueController.cs`** (new)

```csharp
[ApiController]
[Route("api/customer-job-revenue")]
[Authorize(Policy = "SuperUserOnly")]
public class CustomerJobRevenueController : ControllerBase
{
    // GET api/customer-job-revenue?startDate=...&endDate=...&jobNames=...&jobNames=...
    [HttpGet]
    public async Task<ActionResult<JobRevenueDataDto>> GetRevenueData(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] List<string> jobNames) { ... }

    // PUT api/customer-job-revenue/monthly-counts/{aid}
    [HttpPut("monthly-counts/{aid:int}")]
    public async Task<IActionResult> UpdateMonthlyCount(
        int aid, [FromBody] UpdateMonthlyCountRequest request) { ... }
}
```

- Resolves `jobId` via `User.GetJobIdFromRegistrationAsync(_jobLookupService)`
- Resolves `userId` via `User.FindFirstValue(ClaimTypes.NameIdentifier)`
- Single GET returns all 6 datasets in one DTO (mirrors sproc behavior — one round trip)
- PUT for inline monthly count edits

### Phase 5: Backend — DI Registration

**File: `TSIC.API/Program.cs`** — add:
```csharp
builder.Services.AddScoped<ICustomerJobRevenueRepository, CustomerJobRevenueRepository>();
builder.Services.AddScoped<ICustomerJobRevenueService, CustomerJobRevenueService>();
```

### Phase 6: Frontend — Install PivotView Package

```bash
npm install @syncfusion/ej2-angular-pivotview@32.1.24
```

Must match existing Syncfusion version (32.1.24). No other new packages needed — `ej2-angular-grids`, `ej2-angular-navigations` (tabs) already installed.

### Phase 7: Frontend — Regenerate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

### Phase 8: Frontend — Component

**File: `src/app/views/admin/customer-job-revenue/customer-job-revenue.component.ts`** (new)
**File: `src/app/views/admin/customer-job-revenue/customer-job-revenue.component.html`** (new)
**File: `src/app/views/admin/customer-job-revenue/customer-job-revenue.component.scss`** (new)

Standalone, OnPush, signals for state.

**State signals:**
- `revenueData = signal<JobRevenueDataDto | null>(null)`
- `isLoading = signal(false)`
- `errorMessage = signal('')`
- `startDate = signal<Date>(...)` — defaults to 1st of previous month
- `endDate = signal<Date>(...)` — defaults to last day of previous month
- `selectedJobs = signal<string[]>([])`
- `availableJobs = computed(() => this.revenueData()?.availableJobs ?? [])`

**Date range generation:**
- Monthly buckets from current month-1 back to Jan 2022 (matches legacy)
- Computed in component, fed to two `<select>` elements

**Tab structure** (Syncfusion `TabModule`):
1. **Revenue Rollup** — `PivotViewModule`
   - dataSource: `revenueData().revenueRecords`
   - rows: JobName → Year → Month
   - columns: PayMethod
   - values: Sum(PayAmount), format C2
   - toolbar: Export, Expand All, Collapse All
2. **Player/Team Counts** — `GridModule`
   - dataSource: `revenueData().monthlyCounts`
   - Editable if SuperUser (check role from auth)
   - On save: PUT to `api/customer-job-revenue/monthly-counts/{aid}`
   - Toolbar: Edit, Cancel, Update, ExcelExport (SuperUser) or just ExcelExport
3. **Admin Fee Records** — `GridModule`, read-only, ExcelExport
4. **Credit Card Records** — `GridModule`, read-only, ExcelExport
5. **Check Records** — `GridModule`, read-only, ExcelExport

**Common patterns to reuse:**
- `environment.apiUrl` for all HTTP calls (never relative `/api/...`)
- Loading/error signal pattern from existing admin components
- Design system CSS variables for all styling

### Phase 9: Frontend — Route

**File: `src/app/app.routes.ts`** — add under `:jobPath/admin` children:
```typescript
{
    path: 'customer-job-revenue',
    loadComponent: () => import('./views/admin/customer-job-revenue/customer-job-revenue.component')
        .then(m => m.CustomerJobRevenueComponent),
    data: { title: 'Customer Job Revenue' }
}
```

### Phase 10: Nav Integration
- Legacy `Controller=CustomerJobRevenue`, `Action=Index` → new route `admin/customer-job-revenue`
- Add to `LegacyRouteMap` in `NavEditorService.cs` if not already there

---

## Stored Procedure Decision

**Recommendation: Keep the sprocs.** They aggregate cross-job financial data across 6 result sets with date-range filtering. This is firmly SQL-domain work. The repository calls them via raw `DbCommand` and maps results to DTOs.

If the sprocs need changes (e.g., consolidating the two variants), that's a SQL-side task.

---

## Files Summary

| File | Action |
|------|--------|
| `TSIC.Contracts/Dtos/CustomerJobRevenue/CustomerJobRevenueDtos.cs` | Create |
| `TSIC.Contracts/Repositories/ICustomerJobRevenueRepository.cs` | Create |
| `TSIC.Infrastructure/Repositories/CustomerJobRevenueRepository.cs` | Create |
| `TSIC.API/Services/Admin/ICustomerJobRevenueService.cs` | Create |
| `TSIC.API/Services/Admin/CustomerJobRevenueService.cs` | Create |
| `TSIC.API/Controllers/CustomerJobRevenueController.cs` | Create |
| `TSIC.API/Program.cs` | Edit (DI) |
| `src/app/views/admin/customer-job-revenue/*` | Create (3 files) |
| `src/app/app.routes.ts` | Edit (route) |
| `TSIC.API/Services/NavEditorService.cs` | Edit (legacy route map) |
| `package.json` | Edit (add pivotview package) |

## Verification

1. `dotnet build` — 0 errors
2. `ng build` — 0 errors
3. Hit `GET api/customer-job-revenue?startDate=2026-01-01&endDate=2026-01-31` — verify all 6 datasets populate
4. Verify PivotView renders revenue rollup with expand/collapse
5. Verify inline edit on Monthly Counts grid saves via PUT
6. Verify Excel export on all 5 tabs
7. Test with multiple job filter selections
8. Test date range switching
