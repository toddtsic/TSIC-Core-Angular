# Migration Plan 023 — US Lacrosse Rankings (USLaxRankings/Index)

> **Legacy Route**: `USLaxRankings/Index`
> **New Route**: `/:jobPath/admin/uslax-rankings`
> **Scope**: Girls National rankings only, 2025 season data from usclublax.com
> **Priority**: High — directors rely on this for pool seeding
> **Status**: NOT STARTED

---

## Purpose

Screen-scrapes **Girls National** rankings from [usclublax.com](https://www.usclublax.com/rankings) and aligns them with registered TSIC teams via intelligent fuzzy matching. The matched national ranking is written into `Teams.TeamComments` as `{rank:D3}:{clubLaxTeamName}`, which directors see in the Pool Assignment tool — the primary use case.

The legacy UI is free-license Syncfusion grids + jQuery. The functionality is **solid and battle-tested** — the rewrite replaces the UI shell with Angular standalone components while preserving the proven matching algorithm.

---

## What Exists Today

### Already Ported to New Codebase
| Artifact | Location | Status |
|----------|----------|--------|
| DTOs | `TSIC.Contracts/Dtos/Rankings/RankingsDtos.cs` | Complete — 8 DTOs: `RankingEntryDto`, `ScrapeResultDto`, `AgeGroupOptionDto`, `RankingsTeamDto`, `AlignedTeamDto`, `AlignmentResultDto`, `ImportCommentsRequest`, `ImportCommentsResultDto`, `UpdateTeamCommentRequest` |
| Team entity | `Teams.TeamComments` (`string?`) | Exists in domain model |

### Legacy (reference only — do NOT copy verbatim)
| Artifact | Location | Notes |
|----------|----------|-------|
| Controller | `reference/.../USLaxRankingsController.cs` | ~900 lines, uses `SqlDbContext` directly, has matching logic embedded |
| Scraping service | `reference/.../USLaxScrapingService.cs` | HtmlAgilityPack, scrapes `usclublax.com/rank?v=&alpha=&yr=` |
| Interface | `reference/.../IUSLaxScrapingService.cs` | 2 methods: `GetAvailableAgeGroupsAsync`, `ScrapeGirlsNationalRankingsAsync` |
| Model | `reference/.../USLaxRanking.cs` | `Rank, Team, State, Record, Rating, Agd, Sched` |
| View | `reference/.../Views/USLaxRankings/Index.cshtml` | Syncfusion grids, Bootstrap 4, jQuery tabs |
| Scripts | `reference/.../Views/USLaxRankings/_Scripts.cshtml` | ~1500 lines of jQuery for grid binding, matching, import |

---

## Architecture

### Layer Diagram
```
USLaxRankingsController (API)
  ├─ IUSLaxScrapingService      → scrape usclublax.com (HttpClient + HtmlAgilityPack)
  ├─ IUSLaxMatchingService      → fuzzy matching algorithm (pure logic, no DB)
  ├─ ITeamRepository            → read teams for job+agegroup, write TeamComments
  └─ IAgeGroupRepository        → list active agegroups for job
```

### Key Design Decisions

1. **Extract matching logic into its own service** (`IUSLaxMatchingService`). The legacy controller has the entire matching algorithm inline (~400 lines of regex, Levenshtein, word-by-word comparison, color/year gates). This should be a standalone, testable service.

2. **Keep scraping service thin** — HtmlAgilityPack parses the HTML table. If usclublax.com changes structure, only this service needs updating.

3. **Girls National only** — no boys, no regional. Confirmed scope.

4. **Year hardcoded to 2025** in legacy; make configurable via query param with sensible default (current year).

5. **Repository pattern for all DB access** — legacy uses `SqlDbContext` directly. New code goes through `ITeamRepository` (existing) + `IAgeGroupRepository` (may need new method).

---

## Phase 1 — Backend: Scraping Service

### New Files
| File | Layer |
|------|-------|
| `TSIC.Contracts/Services/IUSLaxScrapingService.cs` | Contracts |
| `TSIC.Infrastructure/Services/USLaxScrapingService.cs` | Infrastructure |

### Interface
```csharp
public interface IUSLaxScrapingService
{
    Task<List<AgeGroupOptionDto>> GetAvailableAgeGroupsAsync(CancellationToken ct = default);
    Task<ScrapeResultDto> ScrapeRankingsAsync(string v, string alpha, string yr, CancellationToken ct = default);
}
```

### Implementation Notes
- Port from legacy `USLaxScrapingService.cs` — same HtmlAgilityPack approach
- Target: `https://www.usclublax.com/rank?v={v}&alpha={alpha}&yr={yr}`
- `v=20` = Girls Overall, `v=21` = Girls National (legacy uses `v=20`)
- Parse `div.desc-container-table table` for ranking rows
- Column mapping (hardcoded, matches usclublax.com 2025 structure):
  - 0=Rank, 1=Team (nested `span.uscl-team-cell__body a`), 2=State, 3=Record, 4=Rating, 5=AGD, 6=Sched
- Set `User-Agent` header to avoid blocks
- Return `ScrapeResultDto` (already defined)
- Add `HtmlAgilityPack` NuGet to `TSIC.Infrastructure`

### DI Registration
```csharp
builder.Services.AddHttpClient<IUSLaxScrapingService, USLaxScrapingService>();
```

---

## Phase 2 — Backend: Matching Service

### New Files
| File | Layer |
|------|-------|
| `TSIC.Contracts/Services/IUSLaxMatchingService.cs` | Contracts |
| `TSIC.API/Services/Rankings/USLaxMatchingService.cs` | API (business logic) |

### Interface
```csharp
public interface IUSLaxMatchingService
{
    AlignmentResultDto AlignRankingsWithTeams(
        List<RankingEntryDto> rankings,
        List<RankingsTeamDto> registeredTeams,
        int clubWeight = 75,
        int teamWeight = 25);
}
```

### Matching Algorithm (port from legacy controller)
The legacy matching is word-by-word with strict binary gates:

1. **Normalize** both team names: strip colors, years, common suffixes
2. **Binary gates** (instant rejection if fail):
   - Color mismatch → reject (Red ≠ Blue, colored ≠ colorless)
   - Year mismatch → reject (2026 ≠ 2027)
3. **Club name** (first word) must match ≥85% Levenshtein similarity
4. **Remaining words** scored: exact=100%, abbreviation=100%, fuzzy(≥75%)=proportional
5. **Final score** = `clubWeight * clubScore + teamWeight * teamWordsScore`
6. **Confidence categories**: Excellent (85-100%), Good (65-84%), Basic (50-64%), Low (25-49%), Unmatched (<25%)

### Static Data to Port
- Color HashSet (40+ color names)
- State abbreviation dictionary (all US states + territories)
- Abbreviation dictionary (e.g., "YJ" → "Yellow Jackets", "LC" → "Lacrosse Club")
- Compiled regex patterns for year extraction, color stripping, etc.

---

## Phase 3 — Backend: Controller + Repository Methods

### New File
| File | Layer |
|------|-------|
| `TSIC.API/Controllers/USLaxRankingsController.cs` | API |

### Endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `api/uslax-rankings/age-groups` | List available scraped age groups from usclublax.com |
| `GET` | `api/uslax-rankings/registered-age-groups` | List active age groups for current job (excluding DROPPED/WAITLIST) |
| `GET` | `api/uslax-rankings/scrape` | Scrape rankings for given `?v=&alpha=&yr=` params |
| `GET` | `api/uslax-rankings/align` | Scrape + match against registered teams in given agegroup |
| `POST` | `api/uslax-rankings/import-comments` | Bulk write `TeamComments` for matched teams by confidence filter |
| `PUT` | `api/uslax-rankings/team-comment/{teamId}` | Update single team's comment (manual override dropdown) |
| `DELETE` | `api/uslax-rankings/team-comments/{agegroupId}` | Clear all `TeamComments` for teams in given agegroup |
| `GET` | `api/uslax-rankings/export-csv` | Export scraped rankings as CSV download |

### Repository Methods Needed

**ITeamRepository** (extend existing):
```csharp
Task<List<RankingsTeamDto>> GetTeamsForRankingsAsync(Guid jobId, Guid agegroupId, CancellationToken ct = default);
Task<int> BulkUpdateTeamCommentsAsync(Dictionary<Guid, string?> teamComments, CancellationToken ct = default);
Task<int> ClearTeamCommentsForAgegroupAsync(Guid jobId, Guid agegroupId, CancellationToken ct = default);
```

**IAgeGroupRepository** (extend existing or create if needed):
```csharp
Task<List<AgeGroupOptionDto>> GetActiveAgeGroupsForJobAsync(Guid jobId, CancellationToken ct = default);
```

### Authorization
- `[Authorize]` — admin only (same as legacy `[Authorize(Policy = "AdminOnly")]`)
- JobId resolved via `User.GetJobIdFromRegistrationAsync(_jobLookupService)`

---

## Phase 4 — Frontend: Angular Component

### New Files
```
src/app/views/admin/scheduling/uslax-rankings/
├── uslax-rankings.component.ts
├── uslax-rankings.component.html
├── uslax-rankings.component.scss
└── services/
    └── uslax-rankings.service.ts
```

### Route
```typescript
{
  path: 'uslax-rankings',
  title: 'US Lacrosse Rankings',
  loadComponent: () => import('./views/admin/scheduling/uslax-rankings/uslax-rankings.component')
    .then(m => m.USLaxRankingsComponent)
}
```
Under `/:jobPath/admin/scheduling/uslax-rankings` (or directly under `admin/` — TBD based on nav structure).

### UI Structure — 3-Step Tabbed Flow (matches legacy)

**Step 1: Age Group Mapping**
- Left panel: Two dropdowns
  1. USClubLax.com age group (populated from scrape)
  2. TSIC registered age group (populated from API)
- Right panel: Info card explaining the system
- Both must be selected before proceeding

**Step 2: Rankings Data**
- Table showing scraped rankings: Rank, Team, State, Record, Rating, AGD, Schedule
- "Match Teams" button to proceed
- Excel export button

**Step 3: Matched Teams**
- Combined grid: all registered teams in agegroup, showing:
  - Status (matched/unmatched badge)
  - TSIC Team name
  - TSIC Team Comments (editable — dropdown with all ranked team options for manual override)
  - Confidence level + score
  - Rank, ClubLax Team, State, Record, Rating, AGD, Schedule
- Confidence overview bar (Excellent/Good/Basic/Low/Unmatched counts)
- Action menu: "Import to Team Comments" with confidence filter (High only, Medium+, All)
- "Clear All Comments" button with confirmation modal

### Signals Architecture
```typescript
// Service
scrapedAgeGroups = signal<AgeGroupOptionDto[]>([]);
registeredAgeGroups = signal<AgeGroupOptionDto[]>([]);

// Component
selectedScrapedAgeGroup = signal<string>('');
selectedRegisteredAgeGroup = signal<Guid | null>(null);
rankings = signal<RankingEntryDto[]>([]);
alignmentResult = signal<AlignmentResultDto | null>(null);
activeTab = signal<'age-groups' | 'rankings' | 'matched-teams'>('age-groups');
isLoading = signal(false);
isImporting = signal(false);
```

### No Syncfusion Dependency
Legacy uses Syncfusion grids (free license). New Angular component uses:
- Standard HTML tables with `@for` rendering
- Built-in sorting/filtering via signals + `computed()`
- Bootstrap 5 styling with design system CSS variables
- Native `<dialog>` for confirmation modals (TsicDialogComponent pattern)

---

## Phase 5 — Regenerate API Models & Wire Up

1. Run `.\scripts\2-Regenerate-API-Models.ps1`
2. Verify generated TypeScript types match DTOs
3. Add route to `app.routes.ts`
4. Add nav item via Nav Editor (or seed SQL)

---

## Key Differences from Legacy

| Aspect | Legacy | New |
|--------|--------|-----|
| Architecture | SqlDbContext in controller | Repository pattern + services |
| Matching logic | Inline in controller (~400 lines) | Extracted `IUSLaxMatchingService` |
| UI framework | jQuery + Syncfusion grids | Angular standalone + signals + HTML tables |
| State management | jQuery DOM manipulation | Signal-based reactive state |
| Styling | Hardcoded CSS | Design system CSS variables |
| Auth | `RouteData["jSeg"]` → `_userService.GetJobIdFromJSeg` | JWT claim → `User.GetJobIdFromRegistrationAsync` |
| Gender scope | Girls only (hardcoded) | Girls only (confirmed requirement) |
| Year | Hardcoded `yr=2025` | Param with current-year default |
| Bulk update | `foreach` + `SaveChangesAsync()` | Single `BulkUpdateTeamCommentsAsync` repository method |
| Error handling | `try/catch` → `Json(error)` | Proper HTTP status codes + ProblemDetails |

---

## NuGet Dependencies

| Package | Project | Purpose |
|---------|---------|---------|
| `HtmlAgilityPack` | TSIC.Infrastructure | HTML parsing for screen scraping |

Already present in legacy; verify if already in new `.csproj` or add to `Directory.Packages.props`.

---

## Testing Checklist

- [ ] Scraping returns correct data for Girls National age groups
- [ ] Age group dropdown populates from both sources
- [ ] Matching algorithm produces same results as legacy (compare on known dataset)
- [ ] Color gate: colored team never matches colorless team
- [ ] Year gate: 2026 never matches 2027
- [ ] Club name gate: first-word mismatch → immediate rejection
- [ ] Import writes `{rank:D3}:{teamName}` to `Teams.TeamComments`
- [ ] Clear comments nulls `TeamComments` for all teams in agegroup
- [ ] Manual override dropdown allows picking any ranked team
- [ ] CSV export downloads correctly
- [ ] Pool Assignment tool displays imported comments
- [ ] Works across all 8 palettes (design system compliance)
- [ ] Mobile-responsive (admin tools are desktop-primary but should not break on tablet)

---

## Estimated Effort

| Phase | Effort |
|-------|--------|
| Phase 1 — Scraping service | Small — straightforward port |
| Phase 2 — Matching service | Medium — careful port of regex + Levenshtein logic |
| Phase 3 — Controller + repo | Medium — 8 endpoints, 3 new repo methods |
| Phase 4 — Angular component | Large — 3-tab UI with grids, dropdowns, modals |
| Phase 5 — Wiring | Small — regenerate, route, nav |

---

## Reference Files

- Legacy controller: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/USLax/USLaxRankingsController.cs`
- Legacy service: `reference/TSIC-Unify-2024/TSIC-Unify-Services/USLaxScrapingService.cs`
- Legacy view: `reference/TSIC-Unify-2024/TSIC-Unify/Views/USLaxRankings/Index.cshtml`
- Legacy scripts: `reference/TSIC-Unify-2024/TSIC-Unify/Views/USLaxRankings/_Scripts.cshtml`
- Existing DTOs: `TSIC.Contracts/Dtos/Rankings/RankingsDtos.cs`
- Existing team entity: `TSIC.Domain/Entities/Teams.cs` → `TeamComments` property
