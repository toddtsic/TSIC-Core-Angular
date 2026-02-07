# Port ReportingController & Enable Angular Report Menu Items

## Context
The legacy `TSIC-Unify-2024` submodule has a `ReportingController` with ~97 endpoints. Most (~90) are thin wrappers around an external Crystal Reports service. A few handle local exports (stored procedure Excel exports, iCal). The new Angular + .NET Core architecture needs this functionality ported following clean architecture (repository pattern, proper auth, DTOs). The Angular menu system is already database-driven — menu items with `Controller`/`Action` or `NavigateUrl` already exist in `JobMenuItems`. We need the backend endpoints and an Angular "report launcher" component to handle authenticated file downloads.

## Plan

### Phase 1: Backend — DTOs & Configuration

**File: `src/backend/TSIC.Contracts/Dtos/ReportingDtos.cs`** (new)
```csharp
// CrystalReportRequest - sent to external CR service
// ExportHistoryDto - for tracking
// ReportExportFormat enum (Pdf=1, Rtf=2, Xls=3)
```

**File: `src/backend/TSIC.API/appsettings.json`** + `appsettings.Development.json`
- Add `"Reporting": { "CrystalReportsBaseUrl": "..." }` section
  - Dev: `https://localhost:44395/api/`
  - Prod: `https://cr2025.teamsportsinfo.com/api/`
- Create `ReportingSettings.cs` configuration class

### Phase 2: Backend — Repository Layer

**File: `src/backend/TSIC.Contracts/Repositories/IReportingRepository.cs`** (new)
- `ExecuteStoredProcedureToDataReaderAsync(spName, jobId, ...)` — for SP-based Excel exports
- `RecordExportHistoryAsync(regId, spName?, reportName?)` — audit trail
- `GetScheduleGamesForICalAsync(gameIds)` — for iCal export

**File: `src/backend/TSIC.Infrastructure/Repositories/ReportingRepository.cs`** (new)
- Implements above interface using `SqlDbContext`
- Uses `FromSqlRaw`/raw `DbCommand` for SP execution (as agreed — keep SPs)
- Uses EF Core for export history and schedule queries

### Phase 3: Backend — Service Layer

**File: `src/backend/TSIC.API/Services/Reporting/IReportingService.cs`** (new)
**File: `src/backend/TSIC.API/Services/Reporting/ReportingService.cs`** (new)
- `ExportCrystalReportAsync(reportName, exportFormat, jobId, regId, userId, strGids?)` — proxies to external CR service via `IHttpClientFactory`
- `ExportStoredProcedureToExcelAsync(spName, jobId, useJobId, useDateUnscheduled)` — delegates to repository, builds Excel bytes
- `ExportMonthlyReconciliationAsync(month, year, isMerch)` — dedicated SP export
- `ExportScheduleToICalAsync(gameIds)` — builds .ics file from schedule data
- `BuildLastMonthsJobInvoicesAsync()` — calls external CR invoice builder
- Uses `IHttpClientFactory` (not static `HttpClient`) for CR service calls
- Records export history via repository

### Phase 4: Backend — Controller

**File: `src/backend/TSIC.API/Controllers/ReportingController.cs`** (new)
- `[ApiController]`, `[Route("api/[controller]")]`
- Individual endpoints for each report, preserving legacy authorization attributes:
  - `[Authorize(Policy = "AdminOnly")]` endpoints (~60 methods)
  - `[Authorize(Policy = "StoreAdmin")]` endpoints (~4 methods)
  - `[Authorize(Roles = "Superuser")]` endpoints (~3 methods)
  - `[Authorize(Roles = "Club Rep")]` endpoints (~1 method)
  - `[Authorize(Roles = "Superuser,Director,Player")]` endpoints (~2 methods)
  - `[AllowAnonymous]` endpoints (~13 methods)
  - Unauthenticated endpoints (~7 methods)
- Each method is a one-liner delegating to `IReportingService`
- SP export endpoints: `ExportStoredProcedureResults`, `ExportMonthlyReconciliation*`
- iCal endpoint: `Schedule_Export_Public_ICAL`
- Returns `FileStreamResult` / `FileContentResult` for downloads

### Phase 5: Backend — DI Registration

**File: `src/backend/TSIC.API/Program.cs`**
- Register `IReportingRepository` / `ReportingRepository`
- Register `IReportingService` / `ReportingService`
- Register `ReportingSettings` configuration
- Register named `HttpClient` for Crystal Reports service

### Phase 6: Frontend — Report Launcher Component

**File: `src/app/views/reporting/report-launcher/report-launcher.component.ts`** (new)
- Reads `:controller/:action` from route params (matches legacy menu item Controller/Action pattern)
- Calls the corresponding API endpoint `GET /api/reporting/{action}?exportFormat=X` with JWT auth via `HttpClient`
- Streams the response blob and triggers browser file download
- Shows loading spinner during report generation
- Handles errors (401 → redirect to login, 403 → access denied, 500 → error message)
- Standalone component, OnPush, signals for state

### Phase 7: Frontend — Reporting Service

**File: `src/app/infrastructure/services/reporting.service.ts`** (new)
- `downloadReport(action: string, exportFormat?: number, params?: Record<string, string>): Observable<Blob>`
- Handles the HTTP call with `responseType: 'blob'`
- Extracts filename from `Content-Disposition` header
- Triggers browser download via temporary anchor element

### Phase 8: Frontend — Routes & Menu Integration

**File: `src/app/app.routes.ts`** — add routes:
```typescript
// Under :jobPath children:
{
    path: 'reporting/:action',
    loadComponent: () => import('./views/reporting/report-launcher/report-launcher.component')
        .then(m => m.ReportLauncherComponent)
},
```
- This single route handles ALL report menu items that use `Controller=Reporting` + `Action=X`
- The `isRouteImplemented()` check in `ClientMenuComponent` will match because `reporting/:action` covers any `reporting/xxx` path
- Menu items with `navigateUrl` (external CR links) continue working as-is via `<a href>`

**File: `src/app/layouts/components/client-menu/client-menu.component.ts`**
- Update `buildKnownRoutes()` to handle wildcard/parameterized routes so `reporting/:action` matches `reporting/get_netusers` etc. Currently it collects literal paths — needs to recognize `:param` segments as wildcards.

### Phase 9: Frontend — OpenAPI Model Regeneration
- Run `.\scripts\2-Regenerate-API-Models.ps1` after backend is complete
- Verify new DTOs appear in `src/app/core/api/models/`

## Key Architectural Decisions

1. **Individual endpoints** (not generic) — preserves per-report authorization attributes
2. **Server-side CR proxy** — keeps CR service URL private, auth handled by API
3. **Keep stored procedures** — use `FromSqlRaw`/`DbCommand` in repository
4. **`IHttpClientFactory`** — replaces legacy static `HttpClient` (proper lifetime management)
5. **Single Angular route** (`reporting/:action`) — handles all report menu items without individual components per report
6. **No database menu changes needed** — existing `Controller=Reporting` + `Action=X` menu items map directly to the new Angular route

## Files Modified/Created

| File | Action |
|------|--------|
| `src/backend/TSIC.Contracts/Dtos/ReportingDtos.cs` | Create |
| `src/backend/TSIC.Contracts/Repositories/IReportingRepository.cs` | Create |
| `src/backend/TSIC.Infrastructure/Repositories/ReportingRepository.cs` | Create |
| `src/backend/TSIC.API/Services/Reporting/IReportingService.cs` | Create |
| `src/backend/TSIC.API/Services/Reporting/ReportingService.cs` | Create |
| `src/backend/TSIC.API/Controllers/ReportingController.cs` | Create |
| `src/backend/TSIC.API/Configuration/ReportingSettings.cs` | Create |
| `src/backend/TSIC.API/Program.cs` | Edit (DI registration) |
| `src/backend/TSIC.API/appsettings.json` | Edit (add Reporting section) |
| `src/backend/TSIC.API/appsettings.Development.json` | Edit (add Reporting section) |
| `src/app/views/reporting/report-launcher/report-launcher.component.ts` | Create |
| `src/app/views/reporting/report-launcher/report-launcher.component.html` | Create |
| `src/app/views/reporting/report-launcher/report-launcher.component.scss` | Create |
| `src/app/infrastructure/services/reporting.service.ts` | Create |
| `src/app/app.routes.ts` | Edit (add reporting route) |
| `src/app/layouts/components/client-menu/client-menu.component.ts` | Edit (wildcard route matching) |

## Verification

1. `dotnet build` — backend compiles
2. `ng build` — frontend compiles
3. Verify OpenAPI spec includes all reporting endpoints with correct auth policies
4. Test a CR report endpoint returns file download with correct content type
5. Test an SP export endpoint returns Excel file
6. Test menu items render and link correctly (both internal routes and external URLs)
7. Test "Coming Soon" badge disappears for reporting menu items
8. Test auth enforcement — admin reports return 403 for non-admin users
