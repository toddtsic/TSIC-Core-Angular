# Migration Plan: LADT/Admin → League/Age-Group/Division/Team Editor

## Context

The legacy `LADT/Admin` page is **one of the most heavily used and critical** administrative tools in TSIC. It manages the 4-level hierarchy of Leagues → Age Groups → Divisions (Pools) → Teams for a given job. The current implementation uses jqGrid TreeGrid with a master-detail pattern: left side shows the expandable/collapsible hierarchy tree, right side shows editable detail grid for the selected level. This port will modernize the UI while **preserving the excellent UX** that makes this tool so effective.

**Legacy URL**: `/LADT/Admin` (Controller=LADT, Action=Admin)

---

## 1. Legacy Strengths (Preserve These!)

- **Master-detail layout** - Left tree navigation + right detail editor is intuitive and space-efficient
- **Expand/collapse hierarchy** - Critical for navigating large structures with 100+ teams
- **Live team/player counts** - Aggregated counts visible in tree (essential for directors)
- **Context-sensitive actions** - Add buttons change based on selection (Add Agegroup, Add Division, Add Team)
- **Inline editing** - Edit records directly in grid without navigation
- **Quick add lower level** - One click to add child entities (e.g., add division while viewing agegroup)
- **Breadth of fields** - Teams have 40+ editable fields (dates, fees, roster limits, LOP, DOB ranges, etc.)
- **Auto-create stub division** - When adding agegroup, auto-creates first division/pool
- **Batch operations** - Add waitlists, update all player fees to agegroup fees

## 2. Legacy Pain Points (Fix These!)

- **UX friction creating entities**:
  - ❌ Must be at league level to add agegroup (can't add from empty parent)
  - ❌ Must be at agegroup level to add division (can't add from division list)
  - ❌ Must select existing division to add new division (counterintuitive)
- **jqGrid dependency** - Dated look, heavy jQuery, poor mobile experience
- **Right-click context menus** - Hidden affordances, not discoverable
- **No drag-drop reordering** - Can't reorder teams/divisions within parent
- **Team grid has 40+ columns** - Horizontal scroll nightmare, hard to find fields
- **No bulk import** - Creating 50 teams requires 50 manual adds
- **Full page reload on tree add** - Loses expand/collapse state, jarring
- **CKEditor inline in grid** - Team comments field crashes on some browsers
- **No validation until save** - Users waste time on invalid data entry
- **Anti-forgery token plumbing** - Boilerplate in every AJAX call

## 3. Modern Vision

**Layout**: Master-detail preserved, but modernized:
- **Left panel (320px)**: **Angular CDK Tree** with signal-driven expand/collapse, live counts, add-child buttons per node
- **Right panel (flex)**: Sibling comparison grid (top) + detail edit form (bottom)

**Key improvements**:
- ✅ **Sibling comparison grid** - Clicking a node shows ALL siblings at that level (e.g., all agegroups for the league) in a horizontally-scrollable native HTML table with frozen name column — replicates the legacy's most powerful UX for comparing properties across entities
- ✅ **Add buttons at every level** - "+" button visible on league/agegroup/division rows to add child entities
- ✅ **Inline detail forms** - Edit forms below the comparison grid (no modals needed — detail form updates, grid refreshes)
- ✅ **Optimistic UI updates** - Tree updates instantly without full reload, preserves expand state
- ✅ **Mobile responsive** - Off-canvas drawer for tree on mobile, detail panel takes full width, breadcrumb bar for context
- ✅ **Signal-driven expansion** - CDK Tree flat mode with custom `visibleNodes` computed signal (bypasses CDK expansion model which doesn't filter visibility for flat trees)

## 4. Design Alignment

- **Angular CDK Tree** (`@angular/cdk/tree`) for left tree panel — signal-driven expansion (`expandedIds` signal + `visibleNodes` computed) bypasses CDK's built-in expansion model which doesn't filter visibility for flat trees
- **Native HTML table** for sibling comparison grid — `position: sticky` frozen name column, `overflow: auto` scrolling, CSS variable themed. Deliberately NOT Syncfusion: the grid is read-only for comparison; editing happens in the detail form below. Native table is lighter, zero `::ng-deep` hacks, fully CSS-variable themed
- **Off-canvas drawer** on mobile (< 768px) - tree slides in from left, detail panel takes full width, breadcrumb bar shows current selection path
- Bootstrap forms + CSS variables (all 8 palettes)
- Signal-based state, OnPush change detection
- WCAG AA compliant (keyboard nav, ARIA labels, focus management)

## 5. Database Entities

### 4-Level Hierarchy:
1. **Leagues** (`Leagues` table) - Top level, linked to Job via `JobLeagues`
2. **Agegroups** (`Agegroups` table) - Child of League, has fees, date ranges, roster rules
3. **Divisions** (`Divisions` table) - Child of Agegroup (synonymous with "Pool"), has MaxRoundNumberToShow
4. **Teams** (`Teams` table) - Leaf level, has 40+ fields (fees, dates, roster limits, field assignments, etc.)

### Key Fields by Level:

**Leagues** (editable fields):
- LeagueId (Guid, PK), LeagueName, SportId (dropdown from Sports entity), BHideContacts, BHideStandings, RescheduleEmailsToAddon, PlayerFeeOverride
- *Removed from editing*: BAllowCoachScoreEntry, BShowScheduleToTeamMembers, BTakeAttendance, BTrackPenaltyMinutes, BTrackSportsmanshipScores, PointsMethod, StrLop, StrGradYears, StandingsSortProfileId

**Agegroups** (editable fields):
- AgegroupId (Guid, PK), LeagueId (FK), AgegroupName, Color (HTML color dropdown with swatches), Gender, TeamFee, TeamFeeLabel, RosterFee, RosterFeeLabel, DiscountFee, DiscountFeeStart, DiscountFeeEnd, LateFee, LateFeeStart, LateFeeEnd, MaxTeams, MaxTeamsPerClub, BAllowSelfRostering, BChampionsByDivision, BAllowApiRosterAccess, BHideStandings, PlayerFeeOverride, SortAge
- *Removed from editing*: Season (immutable — set from Job on creation), DobMin, DobMax, GradYearMin, GradYearMax, SchoolGradeMin, SchoolGradeMax

**Divisions**:
- DivId (Guid, PK), AgegroupId (FK), DivName, MaxRoundNumberToShow

**Teams** (editable fields):
- TeamId (Guid, PK), DivId (FK), AgegroupId (FK), LeagueId (FK), JobId (FK), TeamName, Active, DivRank, DivisionRequested, LastLeagueRecord, MaxCount, BAllowSelfRostering, BHideRoster, FeeBase, PerRegistrantFee, PerRegistrantDeposit, DiscountFee, DiscountFeeStart, DiscountFeeEnd, LateFee, LateFeeStart, LateFeeEnd, Startdate, Enddate, Effectiveasofdate, Expireondate, DobMin, DobMax, GradYearMin, GradYearMax, SchoolGradeMin, SchoolGradeMax, Gender, Dow, Dow2, FieldId1, FieldId2, FieldId3, LevelOfPlay, Requests, KeywordPairs, TeamComments
- *Removed from editing*: Season, Year (immutable — set from Job on creation), Color (managed at agegroup level)

## 6. UI Standards Created / Employed

### CREATED (new patterns this module introduces)
- **CDK Tree with Signal-Driven Expansion** - Angular CDK Tree (`@angular/cdk/tree`) with `expandedIds` signal + `visibleNodes` computed signal. Bypasses CDK's built-in expansion model (which doesn't filter flat node visibility). Expand all/collapse all/toggle per node all update a single `Set<string>` signal.
- **Sibling Comparison Grid** - Native HTML `<table>` with `position: sticky; left: 0` frozen name column, `overflow: auto` horizontal+vertical scrolling, type-aware cell rendering (checkmarks for booleans, formatted currency/dates, ellipsis for long text). Click a row to select that entity in the tree. Driven by `LadtColumnDef[]` configs per hierarchy level.
- **Master-Detail Admin Layout** - 320px tree panel + flex detail panel (sibling grid on top, detail form on bottom), responsive split
- **Mobile Drawer Pattern** - Off-canvas tree drawer on mobile (< 768px), breadcrumb bar for current selection path, detail panel takes full width
- **Context-Sensitive Add Buttons** - Per-node "+" buttons in tree visible on hover (add agegroup on leagues, add division on agegroups, add team on divisions)
- **Live Aggregate Counts** - Blue team count + green player count badges at all tree levels
- **Collapsible Tree Persistence** - Expand/collapse state survives CRUD operations (signal-based, not CDK-managed)
- **Two-Line Tree Nodes** - Team nodes with club rep show club name (primary, bold) + team name (secondary, smaller muted text) via flex-column label group
- **Special Entity Visual Distinction** - `isSpecial` flag on `LadtFlatNode` applies muted opacity + italic styling for segregated entities (Dropped Teams, WAITLIST age groups, "Unassigned" divisions)
- **Protected Entity Pattern** - "Unassigned" division demonstrates the pattern: backend `InvalidOperationException` guards + frontend disabled fields + info banner explaining the restriction
- **Client-Side Duplicate Name Validation** - Division detail component checks `siblingNames` input before calling API, preventing unnecessary round-trips
- **Collision-Safe Stub Naming** - Auto-generated names ("Pool A/B/C...") skip existing names using a HashSet lookup loop

### EMPLOYED (existing patterns reused)
- Signal-based state management (all component state as signals)
- CSS variable design system tokens (all colors, spacing, borders)
- `@if` / `@for` / `@switch` template syntax
- OnPush change detection
- `inject()` dependency injection
- Repository pattern (LeagueRepository, AgegroupRepository, DivisionRepository, TeamRepository)
- `FormsModule` with `[(ngModel)]` for detail edit forms

---

## 7. Security Requirements

**CRITICAL**: All endpoints must derive `jobId` from JWT claims, NOT from route parameters.

- **Route**: `/:jobPath/ladt/admin` (jobPath for routing only)
- **API Endpoints**: Must use `ClaimsPrincipalExtensions.GetJobIdFromRegistrationAsync()` to derive `jobId` from the authenticated user's `regId` claim
- **NO route parameters containing sensitive IDs**: All `[Authorize]` endpoints must extract job context from JWT token
- **Policy**: `[Authorize(Policy = "AdminOnly")]` - Directors, SuperDirectors, and Superusers can manage LADT hierarchy
- **Validation**: Server must verify all entities belong to the user's job before any CRUD operation

---

## 8. Implementation Steps

### Phase 1: Backend - DTOs

**Status**: [x] Complete

**Files to create**:
- `TSIC.Contracts/Dtos/Ladt/LadtTreeDtos.cs` (tree structure DTOs)
- `TSIC.Contracts/Dtos/Ladt/LeagueDtos.cs`
- `TSIC.Contracts/Dtos/Ladt/AgegroupDtos.cs`
- `TSIC.Contracts/Dtos/Ladt/DivisionDtos.cs`
- `TSIC.Contracts/Dtos/Ladt/TeamDtos.cs`

**Tree Structure DTOs** (for left panel CDK Tree):
```csharp
public record LadtTreeRootDto
{
    public required List<LadtTreeNodeDto> Leagues { get; init; }
    public required int TotalTeams { get; init; }
    public required int TotalPlayers { get; init; }
}

public record LadtTreeNodeDto
{
    public required Guid Id { get; init; }
    public required Guid? ParentId { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; } // 0=League, 1=Agegroup, 2=Division, 3=Team
    public required bool IsLeaf { get; init; }
    public required int TeamCount { get; init; }
    public required int PlayerCount { get; init; }
    public required bool Expanded { get; init; }
    public List<LadtTreeNodeDto>? Children { get; init; } // For hierarchical binding
    // Metadata for context actions
    public string? LeagueName { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? TeamName { get; init; }
}
```

**League DTOs** (streamlined — removed 9 rarely-edited fields):
```csharp
public record LeagueDetailDto
{
    public required Guid LeagueId { get; init; }
    public required string LeagueName { get; init; }
    public required Guid? SportId { get; init; }
    public string? SportName { get; init; }
    public bool? BHideContacts { get; init; }
    public bool? BHideStandings { get; init; }
    public string? RescheduleEmailsToAddon { get; init; }
    public decimal? PlayerFeeOverride { get; init; }
}

public record UpdateLeagueRequest
{
    public required string LeagueName { get; init; }
    public Guid? SportId { get; init; }
    public bool? BHideContacts { get; init; }
    public bool? BHideStandings { get; init; }
    public string? RescheduleEmailsToAddon { get; init; }
    public decimal? PlayerFeeOverride { get; init; }
}

public record SportOptionDto
{
    public required Guid SportId { get; init; }
    public required string SportName { get; init; }
}
```

**Agegroup DTOs** (Season read-only — set from Job on creation, never updated):
```csharp
public record AgegroupDetailDto
{
    public required Guid AgegroupId { get; init; }
    public required Guid LeagueId { get; init; }
    public string? AgegroupName { get; init; }
    public string? Season { get; init; }        // Read-only (from Job)
    public string? Color { get; init; }          // HTML color name dropdown
    public string? Gender { get; init; }
    public DateOnly? DobMin { get; init; }       // In DTO for grid display, not in edit form
    public DateOnly? DobMax { get; init; }
    // ... fees, limits, toggles
    public required int MaxTeams { get; init; }
    public required int MaxTeamsPerClub { get; init; }
    public required byte SortAge { get; init; }
}

public record CreateAgegroupRequest
{
    public required Guid LeagueId { get; init; }
    public required string AgegroupName { get; init; }
    // NO Season — backend sets from Job automatically
    public string? Color { get; init; }
    public string? Gender { get; init; }
    // ... fees, limits, toggles (NO eligibility fields)
}

public record UpdateAgegroupRequest
{
    // Same as Create minus LeagueId. NO Season — immutable.
}
```

**Division DTOs**:
```csharp
public record DivisionDetailDto
{
    public required Guid DivId { get; init; }
    public required Guid AgegroupId { get; init; }
    public required string DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}

public record CreateDivisionRequest
{
    public required string DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}

public record UpdateDivisionRequest
{
    public required string DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}
```

**Team DTOs** (Season/Year read-only — set from Job on creation, never updated):
```csharp
public record TeamDetailDto
{
    // Identity
    public required Guid TeamId { get; init; }
    public Guid? DivId { get; init; }
    public required Guid AgegroupId { get; init; }
    public required Guid LeagueId { get; init; }
    public required Guid JobId { get; init; }

    // Basic Info
    public string? TeamName { get; init; }
    public string? ClubName { get; init; }
    public bool? Active { get; init; }
    public required int DivRank { get; init; }
    public string? DivisionRequested { get; init; }
    public string? LastLeagueRecord { get; init; }
    public string? Color { get; init; }

    // Roster Limits
    public required int MaxCount { get; init; }
    public bool? BAllowSelfRostering { get; init; }
    public required bool BHideRoster { get; init; }

    // Fees, Dates, Eligibility (all present in DTO for grid/read)
    // ...

    // Immutable (read-only — set from Job on creation)
    public string? Season { get; init; }
    public string? Year { get; init; }

    // ... schedule, advanced, club context, player count
}

public record CreateTeamRequest
{
    public required Guid DivId { get; init; }
    public required string TeamName { get; init; }
    // NO Season, Year — backend sets from Job automatically
    // ... all other fields
}

public record UpdateTeamRequest
{
    // NO Season, Year — immutable, not overwritable
    // ... all other editable fields
}
```

### Phase 2: Backend - Repository Interfaces & Implementations

**Status**: [x] Complete

**Files to create**:
- `TSIC.Contracts/Repositories/ILeagueRepository.cs`
- `TSIC.Infrastructure/Repositories/LeagueRepository.cs`
- `TSIC.Contracts/Repositories/IAgegroupRepository.cs`
- `TSIC.Infrastructure/Repositories/AgegroupRepository.cs`
- `TSIC.Contracts/Repositories/IDivisionRepository.cs`
- `TSIC.Infrastructure/Repositories/DivisionRepository.cs`
- `TSIC.Contracts/Repositories/ITeamRepository.cs`
- `TSIC.Infrastructure/Repositories/TeamRepository.cs`

**ILeagueRepository methods**:
- `GetLeaguesByJobIdAsync(Guid jobId)` → `List<Leagues>` (with Sport navigation)
- `GetLeagueByIdAsync(Guid leagueId)` → `Leagues?` (tracked)
- `GetJobLeaguesAsync(Guid jobId)` → `List<JobLeagues>` (for linking)
- `Add(Leagues league)` / `Remove(Leagues league)` / `SaveChangesAsync()`

**IAgegroupRepository methods**:
- `GetAgegroupsByLeagueIdAsync(Guid leagueId, string season)` → `List<Agegroups>`
- `GetAgegroupByIdAsync(Guid agegroupId)` → `Agegroups?` (tracked)
- `GetAgegroupsWithCountsAsync(Guid jobId)` → `List<AgegroupWithCountsDto>` (includes team/player counts via joins)
- `Add(Agegroups agegroup)` / `Remove(Agegroups agegroup)` / `SaveChangesAsync()`

**IDivisionRepository methods**:
- `GetDivisionsByAgegroupIdAsync(Guid agegroupId)` → `List<Divisions>`
- `GetDivisionByIdAsync(Guid divId)` → `Divisions?` (tracked)
- `GetDivisionsWithCountsAsync(Guid jobId)` → `List<DivisionWithCountsDto>` (team/player counts)
- `Add(Divisions division)` / `Remove(Divisions division)` / `SaveChangesAsync()`

**ITeamRepository methods**:
- `GetTeamsByDivisionIdAsync(Guid divId)` → `List<Teams>`
- `GetTeamByIdAsync(Guid teamId)` → `Teams?` (tracked)
- `GetTeamsWithCountsAsync(Guid jobId)` → `List<TeamWithCountsDto>` (player counts from Registrations)
- `GetMaxDivRankAsync(Guid divId)` → `int` (for new team ordering)
- `IsTeamScheduledAsync(Guid teamId, Guid jobId)` → `bool` (check if team is on any Schedule via T1Id/T2Id)
- `GetScheduledTeamIdsAsync(Guid jobId)` → `HashSet<Guid>` (bulk schedule check for all teams in job)
- `Add(Teams team)` / `Remove(Teams team)` / `SaveChangesAsync()`

**IRegistrationRepository** (extended for Drop Team):
- `ZeroFeesForTeamAsync(Guid teamId, Guid jobId)` → `int` (zeros all 8 fee fields for registrations assigned to team, returns count affected)

### Phase 3: Backend - Service Interfaces & Implementations

**Status**: [x] Complete

**Files to create**:
- `TSIC.Contracts/Services/ILadtService.cs`
- `TSIC.Application/Services/Ladt/LadtService.cs`

**ILadtService methods** (single service for entire hierarchy):

**Tree Loading**:
- `GetLadtTreeAsync(Guid jobId)` → `LadtTreeRootDto`
  - Returns full 4-level hierarchy with counts (Leagues → Agegroups → Divisions → Teams)
  - Includes team/player counts at each level (aggregated from child entities)
  - AsNoTracking for performance

**League CRUD**:
- `GetLeagueDetailAsync(Guid leagueId)` → `LeagueDetailDto`
- `UpdateLeagueAsync(Guid leagueId, UpdateLeagueRequest request)` → `LeagueDetailDto`
- `AddLeagueAgegroupAsync(Guid leagueId, Guid jobId)` → `Guid` (creates stub agegroup + stub division)

**Agegroup CRUD**:
- `GetAgegroupDetailAsync(Guid agegroupId)` → `AgegroupDetailDto`
- `CreateAgegroupAsync(Guid leagueId, CreateAgegroupRequest request)` → `AgegroupDetailDto` (auto-creates stub division)
- `UpdateAgegroupAsync(Guid agegroupId, UpdateAgegroupRequest request)` → `AgegroupDetailDto`
- `DeleteAgegroupAsync(Guid agegroupId)` → `bool` (cascade delete divisions/teams or prevent if teams exist)
- `AddAgegroupDivisionAsync(Guid agegroupId)` → `Guid` (creates stub division)
- `AddWaitlistAgegroupsAsync(Guid jobId)` → `int` (batch creates WAITLIST agegroups for all leagues)
- `UpdatePlayerFeesToAgegroupFeesAsync(Guid agegroupId, Guid jobId)` → `int` (recalculates player registration fees using coalescing hierarchy: Team.FeeBase → Team.PerRegistrantFee → AG.TeamFee → AG.RosterFee; handles processing fee non-CC discount, PaidTotal refresh from RegistrationAccounting)

**Division CRUD**:
- `GetDivisionDetailAsync(Guid divId)` → `DivisionDetailDto`
- `CreateDivisionAsync(Guid agegroupId, CreateDivisionRequest request)` → `DivisionDetailDto`
- `UpdateDivisionAsync(Guid divId, UpdateDivisionRequest request)` → `DivisionDetailDto`
- `DeleteDivisionAsync(Guid divId)` → `bool` (prevent if teams exist)
- `AddDivisionTeamAsync(Guid divId)` → `Guid` (creates stub team)

**Team CRUD**:
- `GetTeamDetailAsync(Guid teamId)` → `TeamDetailDto`
- `CreateTeamAsync(Guid divId, CreateTeamRequest request)` → `TeamDetailDto`
- `UpdateTeamAsync(Guid teamId, UpdateTeamRequest request)` → `TeamDetailDto`
- `DeleteTeamAsync(Guid teamId)` → `DeleteTeamResultDto` (prevent if players rostered, or soft delete via Active=false)
- `DropTeamAsync(Guid teamId, Guid jobId, string userId)` → `DropTeamResultDto` (move to "Dropped Teams" agegroup/division, deactivate, zero player fees — blocked if team is on a schedule)
- `CloneTeamAsync(Guid teamId, CloneTeamRequest request)` → `TeamDetailDto` (duplicates team with admin-chosen name; optionally links to source club and creates new ClubTeam entry, with club rep financial sync)

**Validation Helpers**:
- `ValidateJobOwnershipAsync(Guid entityId, string entityType, Guid jobId)` → `bool` (ensures league/agegroup/division/team belongs to job)

### Phase 4: Backend - Controller

**Status**: [x] Complete

**File to create**:
- `TSIC.API/Controllers/LadtController.cs`

**Endpoints**:

**Tree Loading**:
- `GET api/ladt/tree` → `LadtTreeRootDto` (full hierarchy with counts)

**Lookups**:
- `GET api/ladt/sports` → `List<SportOptionDto>` (all sports for league dropdown)

**League**:
- `GET api/ladt/leagues/{leagueId:guid}` → `LeagueDetailDto`
- `PUT api/ladt/leagues/{leagueId:guid}` → `LeagueDetailDto` (update)
- `POST api/ladt/leagues/{leagueId:guid}/agegroups` → `Guid` (add stub agegroup)

**Agegroup**:
- `GET api/ladt/agegroups/{agegroupId:guid}` → `AgegroupDetailDto`
- `POST api/ladt/agegroups` → `AgegroupDetailDto` (create, body: CreateAgegroupRequest)
- `PUT api/ladt/agegroups/{agegroupId:guid}` → `AgegroupDetailDto` (update)
- `DELETE api/ladt/agegroups/{agegroupId:guid}` → `void`
- `POST api/ladt/agegroups/{agegroupId:guid}/divisions` → `Guid` (add stub division)
- `POST api/ladt/agegroups/waitlists` → `int` (batch add waitlists)
- `POST api/ladt/agegroups/{agegroupId:guid}/update-player-fees` → `int` (batch update player fees)

**Division**:
- `GET api/ladt/divisions/{divId:guid}` → `DivisionDetailDto`
- `POST api/ladt/divisions` → `DivisionDetailDto` (create)
- `PUT api/ladt/divisions/{divId:guid}` → `DivisionDetailDto` (update)
- `DELETE api/ladt/divisions/{divId:guid}` → `void`
- `POST api/ladt/divisions/{divId:guid}/teams` → `Guid` (add stub team)

**Team**:
- `GET api/ladt/teams/{teamId:guid}` → `TeamDetailDto`
- `POST api/ladt/teams` → `TeamDetailDto` (create)
- `PUT api/ladt/teams/{teamId:guid}` → `TeamDetailDto` (update)
- `DELETE api/ladt/teams/{teamId:guid}` → `DeleteTeamResultDto`
- `POST api/ladt/teams/{teamId:guid}/drop` → `DropTeamResultDto` (move to "Dropped Teams", deactivate, zero fees)
- `POST api/ladt/teams/{teamId:guid}/clone` → `TeamDetailDto`

**Sibling Batch Queries** (for comparison grid):
- `GET api/ladt/leagues/siblings` → `List<LeagueDetailDto>` (all leagues for job)
- `GET api/ladt/agegroups/by-league/{leagueId:guid}` → `List<AgegroupDetailDto>` (all agegroups in league)
- `GET api/ladt/divisions/by-agegroup/{agegroupId:guid}` → `List<DivisionDetailDto>` (all divisions in agegroup)
- `GET api/ladt/teams/by-division/{divId:guid}` → `List<TeamDetailDto>` (all teams in division, with player counts)

**Batch Operations**:
- `POST api/ladt/batch/waitlist-agegroups` → `int` (creates WAITLIST agegroups for all leagues)
- `POST api/ladt/batch/update-fees/{agegroupId:guid}` → `int` (recalculates player registration fees for teams in agegroup using coalescing fee hierarchy)

**Authorization**: All endpoints `[Authorize(Policy = "AdminOnly")]`, derive `jobId` from JWT via `GetJobIdFromRegistrationAsync()`.

### Phase 5: Backend - DI Registration

**Status**: [x] Complete

**File to modify**:
- `TSIC.API/Program.cs`

**Add registrations**:
```csharp
builder.Services.AddScoped<ILeagueRepository, LeagueRepository>();
builder.Services.AddScoped<IAgegroupRepository, AgegroupRepository>();
builder.Services.AddScoped<IDivisionRepository, DivisionRepository>();
builder.Services.AddScoped<ITeamRepository, TeamRepository>();
builder.Services.AddScoped<ILadtService, LadtService>();
```

### Phase 6: Frontend - LADT Service

**Status**: [x] Complete

**File to create**:
- `src/app/core/services/ladt.service.ts`

**Methods** (all return Observables):
- `getLadtTree(): Observable<LadtTreeRootDto>`
- `getLeagueDetail(leagueId: string): Observable<LeagueDetailDto>`
- `updateLeague(leagueId: string, request: UpdateLeagueRequest): Observable<LeagueDetailDto>`
- `addLeagueAgegroup(leagueId: string): Observable<string>` (returns new agegroupId)
- `getAgegroupDetail(agegroupId: string): Observable<AgegroupDetailDto>`
- `createAgegroup(request: CreateAgegroupRequest): Observable<AgegroupDetailDto>`
- `updateAgegroup(agegroupId: string, request: UpdateAgegroupRequest): Observable<AgegroupDetailDto>`
- `deleteAgegroup(agegroupId: string): Observable<void>`
- `addAgegroupDivision(agegroupId: string): Observable<string>`
- `addWaitlistAgegroups(): Observable<number>`
- `updatePlayerFeesToAgegroupFees(agegroupId: string): Observable<number>`
- `getDivisionDetail(divId: string): Observable<DivisionDetailDto>`
- `createDivision(request: CreateDivisionRequest): Observable<DivisionDetailDto>`
- `updateDivision(divId: string, request: UpdateDivisionRequest): Observable<DivisionDetailDto>`
- `deleteDivision(divId: string): Observable<void>`
- `addDivisionTeam(divId: string): Observable<string>`
- `getTeamDetail(teamId: string): Observable<TeamDetailDto>`
- `createTeam(request: CreateTeamRequest): Observable<TeamDetailDto>`
- `updateTeam(teamId: string, request: UpdateTeamRequest): Observable<TeamDetailDto>`
- `deleteTeam(teamId: string): Observable<void>`
- `cloneTeam(teamId: string): Observable<TeamDetailDto>`

### Phase 7: Frontend - Master-Detail Layout Component

**Status**: [x] Complete

**File to create**:
- `src/app/views/ladt-admin/ladt-admin.component.ts`
- `src/app/views/ladt-admin/ladt-admin.component.html`
- `src/app/views/ladt-admin/ladt-admin.component.scss`

**Layout structure** (responsive split with mobile drawer):
```html
<div class="ladt-admin-container">
  <!-- Mobile breadcrumb bar -->
  <div class="ladt-breadcrumb-bar d-md-none">
    <button class="btn btn-sm btn-outline-secondary" (click)="treeDrawerOpen.set(true)">
      <i class="bi bi-diagram-3"></i>
    </button>
    <nav aria-label="breadcrumb">
      <!-- League > Agegroup > Division > Team -->
    </nav>
  </div>

  <!-- Tree panel (off-canvas drawer on mobile) -->
  <div class="ladt-tree-panel" [class.drawer-open]="treeDrawerOpen()">
    <app-ladt-tree
      [treeData]="treeData()"
      (nodeSelect)="onNodeSelect($event)"
      (nodeAction)="onNodeAction($event)" />
  </div>

  <!-- Backdrop for mobile drawer -->
  @if (treeDrawerOpen()) {
    <div class="ladt-drawer-backdrop d-md-none" (click)="treeDrawerOpen.set(false)"></div>
  }

  <!-- Detail panel -->
  <div class="ladt-detail-panel">
    <!-- Context-sensitive detail view -->
  </div>
</div>
```

**Component state** (signals):
- `treeData = signal<LadtTreeRootDto | null>(null)`
- `selectedNode = signal<LadtTreeNodeDto | null>(null)`
- `selectedLevel = computed(() => this.selectedNode()?.Level ?? -1)`
- `isLoading = signal(false)`
- `expandedNodes = signal<Set<string>>(new Set())`
- `treeDrawerOpen = signal(false)` (mobile drawer state)

**Component methods**:
- `loadTree()` - Fetch full hierarchy
- `onNodeSelect(node: LadtTreeNodeDto)` - Update selectedNode, load detail, close drawer on mobile
- `onNodeExpand(nodeId: string)` - Track expansion state
- `refreshTree(preserveSelection: boolean)` - Reload tree, restore expand state + selection
- `addLowerLevel()` - Context-aware add (calls addLeagueAgegroup/addAgegroupDivision/addDivisionTeam)

**CSS** (responsive with off-canvas drawer):
```scss
.ladt-admin-container {
  display: grid;
  grid-template-columns: 30% 70%;
  gap: var(--space-2);
  height: calc(100vh - 200px);
}

.ladt-tree-panel {
  border-right: 1px solid var(--bs-border-color);
  overflow-y: auto;
}

.ladt-detail-panel {
  overflow-y: auto;
  padding: var(--space-2);
}

// Mobile drawer pattern
@media (max-width: 767.98px) {
  .ladt-admin-container {
    grid-template-columns: 1fr;
  }

  .ladt-tree-panel {
    position: fixed;
    top: 0;
    left: 0;
    bottom: 0;
    width: 85vw;
    max-width: 360px;
    z-index: 1045;
    background: var(--bs-body-bg);
    transform: translateX(-100%);
    transition: transform 0.3s ease-in-out;
    border-right: 1px solid var(--bs-border-color);

    &.drawer-open {
      transform: translateX(0);
    }
  }

  .ladt-drawer-backdrop {
    position: fixed;
    inset: 0;
    z-index: 1040;
    background: rgba(0, 0, 0, 0.5);
  }

  .ladt-breadcrumb-bar {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-2);
    border-bottom: 1px solid var(--bs-border-color);
  }
}
```

### Phase 8: Frontend - CDK Tree Navigation Component

**Status**: [x] Complete

**File to create**:
- `src/app/views/ladt-admin/components/ladt-tree.component.ts`

**Angular CDK Tree features**:
- **Nested tree** using `CdkTree` with `CdkNestedTreeNode`
- **Expand/collapse per node**: Chevron toggle per parent node
- **Expand All / Collapse All**: Toolbar buttons using `treeControl.expandAll()` / `treeControl.collapseAll()`
- **Selection**: Single node selection via click, highlighted with CSS
- **Search/filter**: Signal-based text filter over tree data
- **Inline actions**: Icon buttons per row (add child, edit, delete, clone) visible on hover
- **Keyboard navigation**: Arrow keys, Enter to select, Space to expand/collapse (CDK built-in ARIA tree)
- **Live counts**: Team count and player count badges per node

**CDK Tree template**:
```html
<div class="ladt-tree-toolbar">
  <input type="text" class="form-control form-control-sm" placeholder="Search..."
    [ngModel]="searchTerm()" (ngModelChange)="searchTerm.set($event)" />
  <div class="btn-group btn-group-sm">
    <button class="btn btn-outline-secondary" (click)="treeControl.expandAll()" title="Expand All">
      <i class="bi bi-arrows-expand"></i>
    </button>
    <button class="btn btn-outline-secondary" (click)="treeControl.collapseAll()" title="Collapse All">
      <i class="bi bi-arrows-collapse"></i>
    </button>
  </div>
</div>

<cdk-tree [dataSource]="filteredTreeData()" [treeControl]="treeControl">
  <cdk-nested-tree-node *cdkTreeNodeDef="let node">
    <div class="tree-node" [class.selected]="selectedNode()?.Id === node.Id"
         (click)="selectNode(node)">
      <button cdkTreeNodeToggle class="btn btn-sm btn-link tree-toggle"
              [style.visibility]="node.IsLeaf ? 'hidden' : 'visible'">
        <i class="bi" [class.bi-chevron-right]="!treeControl.isExpanded(node)"
           [class.bi-chevron-down]="treeControl.isExpanded(node)"></i>
      </button>
      <span class="tree-node-name">{{ node.Name }}</span>
      @if (!node.IsLeaf) {
        <span class="badge bg-secondary-subtle text-secondary ms-2">{{ node.TeamCount }} teams</span>
        <span class="badge bg-info-subtle text-info ms-1">{{ node.PlayerCount }} players</span>
      }
      <div class="tree-node-actions">
        @if (!node.IsLeaf) {
          <button class="btn btn-sm btn-icon" (click)="addChild.emit(node); $event.stopPropagation()" title="Add Child">
            <i class="bi bi-plus-circle"></i>
          </button>
        }
        <button class="btn btn-sm btn-icon" (click)="edit.emit(node); $event.stopPropagation()" title="Edit">
          <i class="bi bi-pencil"></i>
        </button>
        <button class="btn btn-sm btn-icon" (click)="delete.emit(node); $event.stopPropagation()" title="Delete">
          <i class="bi bi-trash"></i>
        </button>
        @if (node.Level === 3) {
          <button class="btn btn-sm btn-icon" (click)="clone.emit(node); $event.stopPropagation()" title="Clone">
            <i class="bi bi-files"></i>
          </button>
        }
      </div>
    </div>
    <div [style.margin-left.px]="24" *cdkTreeNodeOutlet></div>
  </cdk-nested-tree-node>
</cdk-tree>
```

### Phase 9: Frontend - Detail Panel Components

**Status**: [x] Complete

**Files to create**:
- `src/app/views/ladt-admin/components/league-detail.component.ts` (league editor form)
- `src/app/views/ladt-admin/components/agegroup-detail.component.ts` (agegroup list + editor)
- `src/app/views/ladt-admin/components/division-detail.component.ts` (division list + editor)
- `src/app/views/ladt-admin/components/team-detail.component.ts` (team list + editor)

**Context-sensitive detail view** (in ladt-admin.component.html):
```html
<div class="ladt-detail-panel">
  @switch (selectedLevel()) {
    @case (0) {
      <app-league-detail [leagueId]="selectedNode()!.Id" (updated)="refreshTree(true)" />
    }
    @case (1) {
      <app-agegroup-detail [agegroupId]="selectedNode()!.Id" (updated)="refreshTree(true)" />
    }
    @case (2) {
      <app-division-detail [divId]="selectedNode()!.Id" (updated)="refreshTree(true)" />
    }
    @case (3) {
      <app-team-detail [teamId]="selectedNode()!.Id" (updated)="refreshTree(true)" />
    }
    @default {
      <div class="empty-state">
        <i class="bi bi-diagram-3 fs-1 text-muted"></i>
        <p>Select a league, agegroup, division, or team to view details</p>
      </div>
    }
  }
</div>
```

**Each detail component**:
- Loads entity detail via service
- Displays read-only fields + Edit button
- Edit button opens modal (see Phase 10)
- Emits `updated` event on successful save (triggers tree refresh)

### Phase 10: Frontend - Modal Dialogs (Add/Edit Forms)

**Status**: [ ] Deferred — detail components handle editing inline via form panels

**Files to create**:
- `src/app/views/ladt-admin/modals/league-form-modal.component.ts`
- `src/app/views/ladt-admin/modals/agegroup-form-modal.component.ts`
- `src/app/views/ladt-admin/modals/division-form-modal.component.ts`
- `src/app/views/ladt-admin/modals/team-form-modal.component.ts`

**League Form Modal** (simple):
- Fields: LeagueName, SportId (dropdown), 5 checkboxes, PlayerFeeOverride, RescheduleEmailsToAddon, StandingsSortProfileId
- Uses `TsicDialogComponent` wrapper
- Reactive form with validation

**Agegroup Form Modal** (complex - 25+ fields):
- **Organized into tabs/accordion**:
  - **Basic Info**: Name, Color (color picker), Gender (radio), Sort Age
  - **Eligibility**: DOB Min/Max (date pickers), Grad Year Min/Max, School Grade Min/Max
  - **Fees**: Team Fee, Roster Fee, Discount Fee (start/end dates), Late Fee (start/end dates)
  - **Roster Rules**: Max Teams, Max Teams Per Club, Allow Self Rostering, Champions By Division, Allow API Roster Access
- Smart defaults: Inherit fees/dates from previous agegroup in league (if exists)

**Division Form Modal** (simple):
- Fields: DivName, MaxRoundNumberToShow
- Option to "Clone from existing division" (copies name + MaxRoundNumberToShow)

**Team Form Modal** (very complex - 40+ fields):
- **Organized into accordion sections**:
  1. **Basic Info**: TeamName, Active, Division Requested, Last League Record
  2. **Roster Limits**: Max Count, Allow Self Rostering, Hide Roster
  3. **Fees**: Fee Base, Per Registrant Fee, Per Registrant Deposit, Discount Fee (dates), Late Fee (dates)
  4. **Dates**: Start/End/Effective/Expire dates (date pickers)
  5. **Eligibility**: DOB Min/Max, Grad Year Min/Max, School Grade Min/Max, Gender, Season, Year
  6. **Schedule Preferences**: DOW, DOW2, Field 1/2/3 (dropdowns)
  7. **Advanced**: Level of Play (dropdown), Requests, Keyword Pairs, Team Comments (textarea, NOT CKEditor)
- Smart defaults: Inherit agegroup fees/dates/eligibility
- Validation: Max Count > 0, Start Date < End Date, etc.

### Phase 11: Frontend - Routing

**Status**: [x] Complete

**File to modify**:
- `src/app/app.routes.ts`

**Add route**:
```typescript
{
  path: 'ladt/admin',
  canActivate: [authGuard],
  data: { requirePhase2: true, requiresPolicy: 'AdminOnly' },
  loadComponent: () => import('./views/ladt-admin/ladt-admin.component').then(m => m.LadtAdminComponent)
}
```

### Phase 12: Backend - Post-Build API Model Regeneration

**Status**: [x] Complete

**Action**: Run `.\scripts\2-Regenerate-API-Models.ps1`
- Generates TypeScript types from DTOs
- Switch imports in frontend service from local types to `@core/api`

### Phase 13: Testing & Polish

**Status**: [ ] In progress

**Critical tests**:
1. **Tree navigation**: Expand/collapse, expand all/collapse all, selection persists after CRUD
2. **Add agegroup**: Auto-creates stub division, tree updates without reload
3. **Add division**: Adds to correct agegroup, tree counts update
4. **Add team**: Adds to correct division, inherits parent defaults
5. **Edit agegroup**: Updates fees, dates propagate to new teams
6. **Delete validation**: Prevents deleting agegroup/division with children
7. **Clone team**: Creates duplicate with " (Copy)" suffix
8. **Mobile drawer**: Tree slides in/out, breadcrumb shows current path, backdrop closes drawer
9. **Palette test**: All 8 palettes render correctly (CDK Tree uses CSS variables natively)
10. **Batch operations**: Add Waitlists, Update Player Fees work correctly
11. **Performance**: Tree with 500+ teams loads in < 2s, scroll is smooth
12. **Keyboard nav**: Tab through tree, Enter to select, Arrow keys to navigate (CDK ARIA tree)
13. **Search/filter**: Text filter narrows tree nodes in real-time
14. **UX improvements verified**: Can add agegroup from league level (no friction), can add division from division list (no friction)

---

## 9. Files Summary

### Backend Files (all created)

| File | Status | LOC |
|------|--------|-----|
| `TSIC.Contracts/Dtos/Ladt/LadtTreeDtos.cs` | Done | ~30 |
| `TSIC.Contracts/Dtos/Ladt/LeagueDtos.cs` | Done | 41 |
| `TSIC.Contracts/Dtos/Ladt/AgegroupDtos.cs` | Done | 100 |
| `TSIC.Contracts/Dtos/Ladt/DivisionDtos.cs` | Done | 22 |
| `TSIC.Contracts/Dtos/Ladt/TeamDtos.cs` | Done | 161 |
| `TSIC.Contracts/Repositories/ILeagueRepository.cs` | Done | ~20 |
| `TSIC.Infrastructure/Repositories/LeagueRepository.cs` | Done | ~60 |
| `TSIC.Contracts/Repositories/IAgegroupRepository.cs` | Done | ~25 |
| `TSIC.Infrastructure/Repositories/AgegroupRepository.cs` | Done | ~80 |
| `TSIC.Contracts/Repositories/IDivisionRepository.cs` | Done | ~20 |
| `TSIC.Infrastructure/Repositories/DivisionRepository.cs` | Done | ~60 |
| `TSIC.Contracts/Repositories/ITeamRepository.cs` | Done | ~30 |
| `TSIC.Infrastructure/Repositories/TeamRepository.cs` | Done | ~120 |
| `TSIC.Contracts/Repositories/IScheduleRepository.cs` | Done | ~10 |
| `TSIC.Infrastructure/Repositories/ScheduleRepository.cs` | Done | ~67 |
| `TSIC.Contracts/Services/ILadtService.cs` | Done | 59 |
| `TSIC.API/Services/Admin/LadtService.cs` | Done | ~1,060 |
| `TSIC.API/Controllers/LadtController.cs` | Done | ~445 |
| `TSIC.API/Program.cs` | Modified | +11 |

### Frontend Files (all created)

| File | Status | LOC |
|------|--------|-----|
| `ladt-editor/ladt-editor.component.ts` | Done | ~370 |
| `ladt-editor/ladt-editor.component.html` | Done | ~225 |
| `ladt-editor/ladt-editor.component.scss` | Done | ~300 |
| `ladt-editor/services/ladt.service.ts` | Done | 141 |
| `ladt-editor/configs/ladt-grid-columns.ts` | Done | 143 |
| `ladt-editor/components/ladt-sibling-grid.component.ts` | Done | ~350 |
| `ladt-editor/components/league-detail.component.ts` | Done | ~120 |
| `ladt-editor/components/agegroup-detail.component.ts` | Done | ~200 |
| `ladt-editor/components/division-detail.component.ts` | Done | ~180 |
| `ladt-editor/components/team-detail.component.ts` | Done | ~350 |
| `core/api/models/` (auto-generated) | Done | ~15 files |
| `app.routes.ts` | Modified | +7 |

---

## 10. Key Design Decisions

1. **Angular CDK Tree over Syncfusion TreeGrid** - CDK Tree is free, lightweight (~5KB vs ~200KB), has zero CSS conflicts with our 8-palette design system, and provides full template control. The hierarchy is only 4 levels deep — a navigation tree, not a data grid.

2. **Signal-driven expansion (not CDK's built-in)** - CDK Tree with `levelAccessor` + flat nodes does NOT manage node visibility — it renders all nodes always (`_computeRenderingData` returns `renderNodes: nodes`). The `cdkTreeNodeToggle` directive also caused double-toggle bugs. Solution: custom `expandedIds` signal + `visibleNodes` computed signal that filters flat nodes before passing to CdkTree as `[dataSource]`. CDK just renders what it's given.

3. **Native HTML table over Syncfusion Grid for sibling comparison** - The comparison grid is read-only; editing happens in the detail form below. A plain `<table>` with CSS `position: sticky` achieves frozen columns natively, with zero bundle cost, zero `::ng-deep` hacks, and full CSS variable theming. Syncfusion Grid would be overkill.

4. **Single service for all 4 levels** - `LadtService` handles all CRUD instead of 4 separate services. Simpler DI, easier to share validation/authorization logic. Private `MapLeague/MapAgegroup/MapDivision/MapTeam` helpers reused by both single-entity and batch-sibling methods.

5. **Sibling batch endpoints** - 4 dedicated GET endpoints (`leagues/siblings`, `agegroups/by-league/{id}`, etc.) avoid N+1 detail fetches when loading the comparison grid. Each reuses existing Map helpers.

6. **Master-detail layout preserved** with sibling grid enhancement - Right panel splits vertically: sibling comparison grid (top, scrollable) + detail edit form (bottom, max 50%). Click a row in the grid to switch selected entity without going back to the tree.

7. **Mobile off-canvas drawer** - On mobile (< 768px), the tree slides in as a drawer from the left. Detail panel takes full width. Breadcrumb bar at top shows current selection path. Drawer auto-closes on node selection.

8. **Context-sensitive add buttons** - Per-node "+" buttons visible on hover: leagues get "Add Age Group", agegroups get "Add Division", divisions get "Add Team". No modals needed — stub entities created server-side with sensible defaults.

9. **Smart Drop/Delete** - The "-" button triggers a "Remove" operation with dual-path backend logic. **Clean teams** (no players, no payments, no schedule history) are permanently hard-deleted. **Teams with history** are soft-dropped: moved to "Dropped Teams" agegroup/division (auto-created per league), deactivated (`Active=false`), all player fees zeroed (8 fields). Both paths recalculate club rep financials via `SynchronizeClubRepFinancialsAsync`. Scheduled teams are blocked. The API returns `DropTeamResultDto` with `wasDropped`, `wasDeleted`, `message`, and `playersAffected`. Frontend confirmation text explains both possible outcomes since the decision is made server-side.

10. **Authorization via JWT** - ALL endpoints derive jobId from token via `GetJobIdFromRegistrationAsync()`, never from route params. Each entity validated for job ownership before any CRUD operation.

11. **Single tree fetch** - `GetLadtTreeAsync()` returns full 4-level hierarchy in one query (with counts). Reduces N+1 queries, improves performance.

12. **No CKEditor in team comments** - Legacy inline CKEditor in jqGrid caused crashes. Use plain textarea instead.

---

## 11. Verification Checklist

- [x] Backend compiles (`dotnet build`) — 0 errors, warnings only
- [x] All 4+2 repositories implement CRUD correctly (League, Agegroup, Division, Team + existing)
- [x] `LadtService` returns hierarchical tree with accurate counts
- [x] Sibling batch endpoints return all siblings at each level
- [x] TypeScript models generated (run regeneration script)
- [x] Frontend compiles (`ng build`) — 0 errors
- [x] CDK Tree loads full hierarchy with signal-driven expand/collapse
- [x] Expand All / Collapse All buttons work correctly
- [x] Tree selection updates detail panel AND loads sibling comparison grid
- [x] Add agegroup from league level (+ button on hover)
- [x] Add division from agegroup level (+ button on hover)
- [x] Add team from division level (+ button on hover)
- [x] Auto-create stub division when adding agegroup
- [x] Blue team count + green player count badges at all hierarchy levels
- [x] Inactive nodes dimmed with strikethrough + red "Inactive" badge
- [x] Sibling comparison grid: frozen name column stays visible during horizontal scroll
- [x] Sibling comparison grid: click row selects entity in tree + detail form
- [x] Sibling comparison grid: boolean checkmarks, currency formatting, date formatting
- [x] Sibling comparison grid: row numbers (frozen) + sortable column headers
- [x] Edit detail form saves correctly, grid + tree both refresh
- [x] Delete agegroup/division with children shows validation error
- [x] Remove team: clean teams (no players/payments/schedule) are hard-deleted permanently
- [x] Remove team: teams with history are soft-dropped to "Dropped Teams" agegroup/division
- [x] Remove team: both paths recalculate club rep financials
- [x] Remove team: scheduled teams blocked (backend returns 400 with message)
- [x] Remove team: confirmation text explains both possible outcomes
- [x] "Dropped Teams" agegroup/division auto-created per league on first drop
- [x] Dropped teams appear dimmed in tree under "Dropped Teams" special agegroup
- [x] Clone team: dialog prompts for name + "Add to club library" checkbox
- [x] Clone team: with club library checked → creates ClubTeam + links to club rep + syncs financials
- [x] Clone team: without club library → pure league team (no club association)
- [x] Add Waitlists batch operation creates WAITLIST agegroups for all leagues
- [x] Update Player Fees batch operation updates all player registrations
- [x] Tree expand/collapse state persists after CRUD operations
- [x] Team/player counts aggregate correctly at all levels
- [x] Header shows total teams/players across all leagues
- [x] Mobile drawer: tree slides in/out, breadcrumb shows current path
- [x] Mobile: backdrop closes drawer on tap, node selection closes drawer
- [ ] All 8 color palettes render correctly (all CSS variable themed)
- [ ] Performance test: 500+ teams load in < 2s, smooth scrolling
- [x] Route accessible to Directors/SuperDirectors/Superusers only
- [x] Club name display: teams with club rep show two-line layout (club name + team name)
- [x] Club name column in team sibling grid (frozen)
- [x] Tree tooltips show full club/team name on hover
- [x] Inactive badge flush right in tree nodes
- [x] Age groups sorted: regular alpha first, specials (Dropped Teams, WAITLIST*) last
- [x] Special age groups visually distinguished (muted/italic)
- [x] Initial tree state: leagues expanded on first load
- [x] Collapse All keeps leagues expanded (showing age groups)
- [x] "Unassigned" division auto-created with every age group (not "Pool A")
- [x] "Unassigned" division: cannot be deleted (backend + frontend protection)
- [x] "Unassigned" division: cannot be renamed (backend + frontend protection)
- [x] "Unassigned" division: visual distinction in tree (muted/italic)
- [x] "Unassigned" division: sorted first among sibling divisions in tree
- [x] Division duplicate name prevention (client + server validation)
- [x] Stub division naming collision-safe ("Pool A", "Pool B", etc., skipping existing names)
- [x] Move Team to Club: "Change Club" button visible only for teams with `clubRepRegistrationId`
- [x] Move Team to Club: "Change Club" button hidden for league teams / admin-created stubs (no club context)
- [x] Move Team to Club: target club dropdown excludes current club
- [x] Move Team to Club: single team mode updates one team, batch mode updates all teams from same club
- [x] Move Team to Club: `ClubrepRegistrationid`, `ClubrepId`, `ClubTeamId` (if applicable) updated in DB
- [x] Move Team to Club: source club rep Registration fees decrease, target club rep fees increase (via `SynchronizeClubRepFinancialsAsync`)
- [x] Move Team to Club: batch move all teams → source club rep fees go to $0
- [x] Move Team to Club: schedule round-robin game names (`T1Name`/`T2Name` where `T1Type == "T"`) reflect new club name prefix
- [x] Move Team to Club: `BShowTeamNameOnlyInSchedules` flag respected (no club prefix when true)
- [x] Move Team to Club: championship/bracket games (`T1Type != "T"`) unchanged
- [x] Move Team to Club: tree refreshes after move showing updated club name labels
- [ ] Move Team to Club: edge case — only one club registered → dropdown empty, Move button disabled
- [x] Move Team to Club: "Change Club" in overflow menu (three-dots) behind warning modal gate
- [x] Tree toolbar: gear icon dropdown for Actions (signal-toggled, not Bootstrap JS)
- [x] Tree toolbar: "Add WAITLIST Age Groups" in Actions dropdown with `bi-collection` icon

---

## 12. Post-Migration Tasks

1. **User training**: Create video walkthrough demonstrating new UX improvements (add buttons at every level, smart defaults, mobile drawer)
2. **Performance monitoring**: Track page load time, tree render time, API response times for large jobs (500+ teams)
3. **Data migration**: No schema changes needed (uses existing entities), but consider adding indexes on DivRank, AgegroupId, etc. for performance
4. **Feature flag**: Consider soft-launch to SuperUsers first, then rollout to Directors
5. **Legacy deprecation**: After 2 months of stable usage, deprecate legacy `/LADT/Admin` route (show "Use new LADT Editor" banner)

---

## 13. Estimated Effort

- **Backend**: 24-30 hours (5 DTO files, 4 repos, 1 service (~1,200 LOC), 1 controller, DI registration)
- **Frontend - Layout & Tree**: 12-16 hours (CDK Tree setup, master-detail layout, mobile drawer, breadcrumb)
- **Frontend - Detail Components**: 12-16 hours (4 detail components, context switching)
- **Frontend - Modals**: 16-20 hours (4 modal dialogs, accordion/tabs, validation, smart defaults)
- **Testing & Polish**: 10-14 hours (CRUD testing, mobile responsive, palette testing, performance)

**Total**: 74-96 hours

---

## 14. Success Metrics

- **Adoption rate**: 90%+ of directors use new editor within 1 month
- **Support tickets**: 50% reduction in "how do I add agegroup/division/team?" tickets
- **User satisfaction**: NPS score > 8 from directors (survey after 1 month)
- **Performance**: < 2s page load for jobs with 500+ teams
- **Mobile usage**: 20%+ of LADT edits happen on mobile devices (currently 0% due to jqGrid)

---

## 15. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Performance with 1000+ teams | MEDIUM | CDK Tree handles nested rendering efficiently; add DB indexes on FK columns for count queries |
| Complex team form UX | MEDIUM | User testing with 3 directors before launch, iterate on accordion section grouping |
| Data loss during CRUD | HIGH | Server-side validation, optimistic UI with rollback on error, confirmation dialogs for delete |
| Legacy data inconsistencies | MEDIUM | Run data integrity audit before launch (orphaned divisions, teams with missing agegroups) |
| Mobile drawer UX | LOW | Test on iOS Safari + Android Chrome; breadcrumb provides fallback navigation if drawer has issues |

---

## 16. Amendments Log

| # | Change | Reason |
|---|--------|--------|
| 1 | Angular CDK Tree replaces Syncfusion TreeGrid | Zero CSS conflicts with 8-palette system, lightweight (~5KB vs ~200KB), full template control, no license dependency on critical page |
| 2 | Phase order: DTOs → Repos → Service → Controller | Repos reference DTO types; defining shapes first prevents compilation errors |
| 3 | Bulk CSV import removed entirely | Not a needed function |
| 4 | Drag-drop reordering removed entirely | Not a needed function |
| 5 | Mobile layout: off-canvas drawer + breadcrumb bar | Better UX than stacked layout or dropdown; detail panel gets full width, tree available on demand |
| 6 | Route confirmed as `/:jobPath/ladt/admin` | Consistent with existing admin route patterns |
| 7 | LadtService LOC estimate raised to ~1,200 | Original 800 LOC underestimated for 4-entity CRUD + tree + batch ops + validation |
| 8 | All templates use `@if`/`@for` (not `*ngIf`/`*ngFor`) | Project standard: modern Angular control flow syntax |
| 9 | Soft delete returns `DeleteTeamResultDto` with distinct frontend handling | Admin must clearly see whether team was deleted vs deactivated — dimmed tree node + alert + Active toggle update for soft delete; disappears for hard delete |
| 10 | Signal-driven expansion replaces CDK's built-in expansion model | CDK Tree with `levelAccessor` + flat nodes does NOT manage visibility — renders all nodes always. `expandedIds` signal + `visibleNodes` computed signal filters flat nodes before passing to CdkTree. Also fixes double-toggle bug from `cdkTreeNodeToggle` directive. |
| 11 | Sibling comparison grid added (native HTML table, NOT Syncfusion) | Replicates legacy's most powerful UX: click a node → see ALL siblings in a scrollable table with frozen name column. Native `<table>` + CSS `position: sticky` is lighter, zero dependencies, zero `::ng-deep` hacks, fully CSS-variable themed. Grid is read-only for comparison; editing stays in detail form. |
| 12 | Backend sibling batch endpoints added | 4 GET endpoints (`leagues/siblings`, `agegroups/by-league/{id}`, `divisions/by-agegroup/{id}`, `teams/by-division/{id}`) avoid N+1 detail fetches for comparison grid. Reuse existing Map helpers. |
| 13 | Detail panel split: grid (top) + form (bottom) | Right panel splits vertically — sibling grid fills available space, detail form capped at 50% height with own scroll. Click grid row to switch selected entity. |
| 14 | Modal dialogs deferred | Inline detail forms handle all editing. Modals may be added later for bulk operations or "focus mode" editing. |
| 15 | Club name display on teams | Teams registered by a club rep show two-line layout in tree (club name primary, team name secondary) and a frozen "Club" column in the team sibling grid. Uses `ClubRepRegistrationId → Registrations.ClubName` join via `TeamRepository.GetClubNamesByJobAsync()` bulk dictionary. |
| 16 | Sibling grid: row numbers + sortable columns | Frozen row-number column (#) as first column, all headers sortable (click to toggle asc/desc). Row numbers reflect current sort order. Sort state stored as signals, reset when data source changes. |
| 17 | Tree tooltips + flush-right Inactive badge | All tree nodes have `[title]` showing full club + team name on hover. "Inactive" badge moved into `node-badges ms-auto` group for consistent right-alignment. |
| 18 | Age group sorting with special segregation | Age groups sorted alphabetically within each league, with "Dropped Teams" and "WAITLIST*" groups pushed to the bottom. Specials visually distinguished with muted opacity + italic text. |
| 19 | Initial expansion state: leagues expanded | On first load, `collapseAll()` runs which keeps all league-level nodes expanded (showing age groups). Collapse All button behaves the same way. |
| 20 | "Unassigned" division business rule | Every age group MUST always have an "Unassigned" division. Cannot be deleted or renamed (backend `InvalidOperationException` + frontend disabled fields). Auto-created when age groups are created. Division duplicate name prevention (client + server). Stub division naming changed from count-based to collision-safe. "Unassigned" divisions visually muted in tree and sorted first among siblings. |
| 21 | Update Player Fees rewrite | Original implementation incorrectly copied `ag.TeamFee` down to `team.FeeBase`, violating coalescing principle and not updating player registrations at all. Rewritten to: (1) resolve per-team base fee via in-memory coalescing (Team.FeeBase → Team.PerRegistrantFee → AG.TeamFee → AG.RosterFee → 0), (2) recalculate FeeProcessing with non-CC payment discount (processing applies only to CC-payable portion), (3) refresh PaidTotal from RegistrationAccounting records, (4) recompute FeeTotal/OwedTotal. Added `GetPaymentSummariesAsync` to IRegistrationAccountingRepository and `GetActivePlayerRegistrationsByTeamIdsAsync` to IRegistrationRepository. LadtService gained 4 new deps: IRegistrationRepository, IRegistrationAccountingRepository, IJobRepository, IRegistrationRecordFeeCalculatorService. Frontend button renamed "Push Fees" → "Update Player Fees" with updated tooltip and success message. |
| 22 | Smart Drop/Delete replaces simple team delete | The "-" button triggers a "Remove" operation with dual-path logic. **Hard delete**: teams with no players, no payments, and no schedule history are permanently deleted (`_teamRepo.Remove`). **Soft drop**: teams with any history are moved to "Dropped Teams" agegroup/division (auto-created per league), deactivated, all 8 player fee fields zeroed. Both paths recalculate club rep financials via `SynchronizeClubRepFinancialsAsync`. Scheduled teams blocked at backend (checks `Schedule.T1Id`/`T2Id`). New repository method: `HasPaymentsForTeamAsync` (IRegistrationAccountingRepository). Extended DTO: `DropTeamResultDto` gained `WasDeleted` bool (true = hard deleted, false = soft dropped). Frontend confirmation text updated: "If the team has no players, payments, or schedule history it will be permanently deleted. Otherwise it will be moved to Dropped Teams and deactivated." Button label changed from "Drop" to "Remove". |
| 23 | Enhanced Clone with Club Library | Clone button now opens an inline dialog instead of firing immediately. Admin enters a team name (pre-filled with "Name (Copy)") and optionally checks "Add to club's team library" (only shown for club-registered teams, checked by default). When checked: backend copies `ClubrepRegistrationid` + `ClubrepId` from source, creates a new `ClubTeams` row (cloned from source's ClubTeam with the new name), links it to the clone, and recalculates club rep financials via `SynchronizeClubRepFinancialsAsync`. When unchecked: pure league team with no club association (original behavior). New DTO: `CloneTeamRequest` with `TeamName` + `AddToClubLibrary`. Updated endpoint: `POST teams/{teamId}/clone` now accepts `[FromBody] CloneTeamRequest`. Frontend: `showCloneDialog`, `cloneName`, `cloneAddToClub` signals; `openCloneDialog()` and `doClone()` methods replace the old fire-and-forget `clone()`. |
| 24 | Move Team to Different Club | Tournament teams registered by club reps can be reassigned to a different club. Two modes: single team or batch (all teams from same club). Updates `ClubrepRegistrationid`, `ClubrepId`, `ClubTeamId` (if non-null, finds/creates matching ClubTeam under target club), and audit fields. After move: recalculates financials for BOTH source and target club reps via `SynchronizeClubRepFinancialsAsync`, and syncs denormalized schedule team names via new `SynchronizeScheduleNamesForTeamAsync` (reusable "single point of truth" — recomposes `T1Name`/`T2Name` from Teams.TeamName + Registrations.ClubName + Jobs.BShowTeamNameOnlyInSchedules; only updates round-robin games where `T1Type/T2Type == "T"`). New files: `IScheduleRepository.cs`, `ScheduleRepository.cs`. New DTOs: `MoveTeamToClubRequest`, `MoveTeamToClubResultDto`, `ClubRegistrationDto`. Extended `TeamDetailDto` with `ClubRepRegistrationId` and `ClubTeamId`. New endpoints: `GET clubs-for-job`, `POST teams/{teamId}/change-club`. LadtService gained 3 new deps: `IClubTeamRepository`, `IClubRepository`, `IScheduleRepository`. Frontend: "Change Club" moved to overflow menu (three-dots `⋮` button) behind a warning modal gate (`ConfirmDialogComponent` with `confirmVariant="warning"`). Inline panel with scope toggle (single/all), target club dropdown (current club filtered out), and move action. Tree refreshes after move to reflect new club name labels. |
| 25 | Tree toolbar: gear Actions dropdown | Replaced the ambiguous "+" dropdown button (which used broken Bootstrap JS `data-bs-toggle`) with a gear icon (`bi-gear`) dropdown using signal-toggled `@if (actionsOpen())` pattern. Contains "Add WAITLIST Age Groups" item with `bi-collection` icon. Bootstrap dropdown JS is not loaded in Angular — all dropdowns in this module use signal-toggled visibility. |
| 26 | Detail panel streamlining — League | Removed 9 rarely-edited fields (BAllowCoachScoreEntry, BShowScheduleToTeamMembers, BTakeAttendance, BTrackPenaltyMinutes, BTrackSportsmanshipScores, PointsMethod, StrLop, StrGradYears, StandingsSortProfileId) from detail form and sibling grid. Sport field changed from disabled text input to `<select>` dropdown populated from `GET /api/ladt/sports` (new endpoint). Added helper text to Reschedule Emails: "Separate emails with semi-colon, no spaces". New backend: `SportOptionDto`, `ILeagueRepository.GetAllSportsAsync()`, `ILadtService.GetSportsAsync()`, `LadtController.GetSports()`. |
| 27 | Detail panel streamlining — Agegroup | Removed eligibility fields (DobMin, DobMax, GradYearMin, GradYearMax, SchoolGradeMin, SchoolGradeMax) and Season from detail form and sibling grid. Color field changed from text input to `<select>` dropdown of 20 HTML color names with inline color swatch squares. Agegroup name column in sibling grid now renders a color dot (`colorField` property on `LadtColumnDef`). |
| 28 | Detail panel streamlining — Team | Removed Color (managed at agegroup level), DobMin, DobMax, Season, Year from detail form and sibling grid. Removed entire Schedule Preferences section (Dow, Dow2). Remaining eligibility: Gender, Level of Play only. |
| 29 | Season/Year immutability enforcement | `Season` and `Year` are Job-level properties that MUST be consistent across all LADT entities. **Updates**: Removed from `UpdateAgegroupRequest` and `UpdateTeamRequest` DTOs entirely — backend update methods no longer touch these fields. **Creates/Stubs**: Backend now fetches `Season`/`Year` from `_jobRepo.GetJobSeasonYearAsync(jobId)` instead of trusting the request. This prevents null-writes from frontend forms that no longer have these fields. New: `JobSeasonYear` record, `IJobRepository.GetJobSeasonYearAsync()`, `JobRepository.GetJobSeasonYearAsync()`. **Clones**: Copy from source team (already correct since source is in same Job). |
| 30 | Team Status KPI badges (frontend-computed) | Replicates legacy LADT tree header's 3 status lines: **Active Waitlisted Teams** (red dot), **Active Non Waitlisted Teams** (green dot), **Active Scheduled Teams** (blue dot). **Design decision**: counts are computed on the **frontend** via a `teamStatusKpis` computed signal, NOT as backend aggregate fields. This makes counts reactive — they update instantly when the tree data changes (add/clone/drop) without requiring a separate API round-trip for counts. **Backend**: `LadtTreeRootDto` gained `ScheduledTeamIds: List<Guid>` (raw data, not computed count) from existing `ITeamRepository.GetScheduledTeamIdsAsync`. The 3 aggregate count fields (`ActiveWaitlistedTeams`, `ActiveNonWaitlistedTeams`, `ActiveScheduledTeams`) that were briefly added to the DTO were **reverted** in favor of frontend computation. **Frontend**: `scheduledTeamIds` signal (`Set<string>`) populated from root DTO. `teamStatusKpis` computed signal traverses `rawTree()` hierarchy, classifying active teams by agegroup name (WAITLIST\* → waitlisted, DROPPED TEAMS → excluded, else → nonWaitlisted) and checking against `scheduledTeamIds` set. HTML: 3 color-coded KPI lines in `tree-header` with dot + label + right-aligned count. SCSS: `.tree-kpis` column layout, `.kpi-badge` flex row, `.kpi-dot` colored circles using `--bs-danger`/`--bs-success`/`--bs-primary` CSS vars. |

---

**Implementation is substantially complete. Core features working:**
- Angular CDK Tree with signal-driven expand/collapse (bypasses CDK limitations with flat trees)
- Sibling comparison grid using native HTML table with frozen name column + horizontal scroll (no Syncfusion)
- Sibling grid enhancements: row numbers (frozen), sortable columns, club name column for teams, color dot for agegroup name
- 4 backend batch endpoints for sibling data, reusing existing Map helpers
- Detail edit forms for all 4 entity levels, integrated below the comparison grid
- **Detail panel streamlining**: League (Sport dropdown, 9 fields removed), Agegroup (HTML color dropdown, eligibility section removed), Team (color/schedule/season/year removed)
- **Season/Year immutability**: enforced server-side — set from Job on create/stub, never overwritten on update, removed from Update/Create DTOs
- Club name display: two-line tree nodes for teams with club rep, bulk lookup via repository
- Age group sorting: regular alpha first, specials (Dropped Teams, WAITLIST*) last with visual distinction
- "Unassigned" division business rule: auto-created, never deletable/renameable, duplicate name prevention
- Tree tooltips, flush-right Inactive badges, initial expansion state (leagues expanded)
- Mobile off-canvas drawer, breadcrumb bar, responsive layout
- All 8 palettes supported via CSS variable theming throughout
- Update Player Fees: recalculates player registration fees using coalescing hierarchy, processing fee non-CC discount, PaidTotal refresh from accounting records
- Smart Drop/Delete: clean teams (no players/payments/schedule) are permanently deleted; teams with history are soft-dropped to "Dropped Teams" agegroup/division; both paths recalculate club rep financials; scheduled teams blocked at backend (no TOCTOU race)
- Enhanced Clone with Club Library: dialog prompts for team name + "Add to club library" checkbox; when checked, creates new ClubTeam + copies club rep association + syncs financials; supports "admin adds team for club rep" workflow
- Move Team to Different Club: single or batch reassignment with financial recalculation for both clubs, schedule name sync via reusable `SynchronizeScheduleNamesForTeamAsync`, ClubTeamId migration; "Change Club" moved to overflow menu behind warning modal gate
- UI polish: gear icon Actions dropdown (signal-toggled, not Bootstrap JS), overflow menu for dangerous operations, warning modal gates for irreversible actions

**Remaining work**: Manual testing across all 8 palettes, performance testing with large datasets (see verification checklist).
