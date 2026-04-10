# Migration Plan: ClubRep/ClubTeamRosters → Club Rosters

## Context

The legacy TSIC-Unify-2024 project has a `ClubRep/ClubTeamRosters` page that lets club reps manage
player rosters on their teams. It uses jqGrid with inline editing for uniform numbers, positions,
team reassignment, and row deletion. We are migrating the core use case — **moving self-rostered
players between teams and deleting bogus registrations** — to a modern Angular component.

Uniform number and position editing are NOT in scope (those are covered by the admin Roster Swapper).

---

## 1. Legacy Pain Points

- **jqGrid dependency** — Heavy jQuery plugin, dated look, poor mobile experience
- **Inline editing** — Cramped, no validation feedback, confusing UX for team moves
- **No confirmation on delete** — Deleting a registration is irreversible; legacy has a basic browser confirm()
- **No player count visibility** — Club rep can't see how team sizes change during moves
- **Full page reload after edits** — Loses scroll position and selected team context
- **Direct DbContext queries** — No repository pattern, business logic in controller

## 2. Modern Vision

A clean, two-panel layout:
- **Team selector** dropdown showing all club rep's teams with player counts
- **Syncfusion grid** showing the selected team's roster
- **Move action** — Select player(s), pick target team from dropdown, confirm move
- **Delete action** — Select player(s), confirm deletion of bogus registrations
- **Live counts** — Team dropdown updates player counts after mutations
- **Club-rep-scoped** — Backend enforces that the club rep only sees/edits their own teams

## 3. User Value

- **Roster control**: Club reps can fix self-rostering mistakes without contacting the director
- **Fewer errors**: Confirmation dialogs prevent accidental deletions
- **Better awareness**: Player counts per team visible at a glance
- **Mobile access**: Responsive grid works on any device
- **Modern feel**: Consistent with rest of Angular app

## 4. Design Alignment

- Syncfusion grid (`GridAllModule`) — consistent with all other grids in the app
- CSS variables (all 8 palettes)
- Signal-based state, OnPush change detection
- Toast notifications via `ToastService`
- Confirmation via `ConfirmDialogComponent`
- WCAG AA compliant

## 5. UI Standards Created / Employed

### CREATED (new patterns)
- **ClubRep route guard flag** — `requireClubRep: true` in route data, checked by `authGuard`

### EMPLOYED (existing patterns reused)
- Syncfusion `<ejs-grid>` with column templates, sorting, paging
- `ConfirmDialogComponent` for destructive actions
- `ToastService` for success/error feedback
- Signal-based state management
- `field-select` / `field-label` form classes
- `@if` / `@for` template syntax
- OnPush change detection
- `inject()` dependency injection

---

## 6. Implementation Steps

### Step 1: Backend — DTOs
**Status**: [x] Complete
**Files to create**:
- `TSIC.Contracts/Dtos/ClubRoster/ClubRosterDtos.cs`

**DTOs**:
```csharp
public record ClubRosterTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgegroupName { get; init; }
    public required int PlayerCount { get; init; }
}

public record ClubRosterPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string AgegroupName { get; init; }
    public required string TeamName { get; init; }
}

public record MovePlayersRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required Guid TargetTeamId { get; init; }
}

public record DeletePlayersRequest
{
    public required List<Guid> RegistrationIds { get; init; }
}

public record ClubRosterMutationResultDto
{
    public required int AffectedCount { get; init; }
    public required string Message { get; init; }
}
```

### Step 2: Backend — Repository Methods
**Status**: [x] Complete
**Files to modify**:
- `TSIC.Contracts/Repositories/ITeamRepository.cs` — add method
- `TSIC.Infrastructure/Repositories/TeamRepository.cs` — implement

**New method**:
```csharp
Task<List<ClubRosterTeamDto>> GetClubRosterTeamsAsync(
    Guid clubRepRegistrationId,
    Guid jobId,
    CancellationToken cancellationToken = default);
```

Returns club rep's teams with player counts, excluding "Dropped Teams" agegroup.
Uses existing `ClubrepRegistrationid` FK on Teams entity.

**Existing methods to reuse**:
- `IRegistrationRepository.GetRosterByTeamIdAsync()` — already returns roster with User+Role
- `IRegistrationRepository.GetByIdAsync()` / `GetByIdsAsync()` — for mutation targets

### Step 3: Backend — Service
**Status**: [x] Complete
**Files to create**:
- `TSIC.Contracts/Services/IClubRosterService.cs`
- `TSIC.API/Services/ClubRosterService.cs`

**Methods**:
- `GetTeamsAsync(Guid clubRepRegistrationId, Guid jobId)` → `List<ClubRosterTeamDto>`
- `GetRosterAsync(Guid teamId, Guid clubRepRegistrationId, Guid jobId)` → `List<ClubRosterPlayerDto>`
  - Validates club rep owns the team
  - Returns active players with signed waivers
- `MovePlayersAsync(MovePlayersRequest request, Guid clubRepRegistrationId, Guid jobId)` → `ClubRosterMutationResultDto`
  - Validates club rep owns BOTH source and target teams
  - Updates `AssignedTeamId` on each registration
  - Updates `Assignment` field (format: `{ClubName}:{AgegroupName}:{TeamName}`)
- `DeletePlayersAsync(DeletePlayersRequest request, Guid clubRepRegistrationId, Guid jobId)` → `ClubRosterMutationResultDto`
  - Validates club rep owns the team
  - Checks for accounting records (block deletion if balance exists)
  - Removes `ApiRosterPlayersAccessed` records
  - Removes registration

### Step 4: Backend — Controller
**Status**: [x] Complete
**Files to create**:
- `TSIC.API/Controllers/ClubRosterController.cs`

**Endpoints** (all `[Authorize]`, derive clubRepRegistrationId from JWT `regId` claim):
- `GET    /api/club-rosters/teams` → List club rep's teams
- `GET    /api/club-rosters/teams/{teamId}/roster` → Team roster
- `PUT    /api/club-rosters/move-players` → Move players to different team
- `DELETE /api/club-rosters/delete-players` → Delete bogus registrations

### Step 5: Backend — DI Registration
**Status**: [x] Complete
**Files to modify**:
- `TSIC.API/Program.cs` — `AddScoped<IClubRosterService, ClubRosterService>()`

### Step 6: Backend — Regenerate API Models
**Status**: [x] Complete
**Action**: Run `.\scripts\2-Regenerate-API-Models.ps1`

### Step 7: Frontend — Route & Guard
**Status**: [x] Complete
**Files to modify**:
- `src/app/app.routes.ts` — add route `/:jobPath/club-rosters`
- `src/app/infrastructure/guards/auth.guard.ts` — add `requireClubRep` flag support

**Route**:
```typescript
{
  path: 'club-rosters',
  loadComponent: () => import('./views/club-rosters/club-rosters.component')
    .then(m => m.ClubRostersComponent),
  canActivate: [authGuard],
  data: { requireClubRep: true },
  title: 'Club Rosters'
}
```

**Guard addition**: Check `isClubRep()` predicate when `requireClubRep` is set.
Add `isClubRep()` to `roles.constants.ts` if not present.

### Step 8: Frontend — Service
**Status**: [x] Complete
**Files to create**:
- `src/app/views/club-rosters/club-rosters.service.ts`

**Methods** (all return Observables):
- `getTeams()` → `ClubRosterTeamDto[]`
- `getRoster(teamId: string)` → `ClubRosterPlayerDto[]`
- `movePlayers(request: MovePlayersRequest)` → `ClubRosterMutationResultDto`
- `deletePlayers(request: DeletePlayersRequest)` → `ClubRosterMutationResultDto`

### Step 9: Frontend — Component
**Status**: [x] Complete
**Files to create**:
- `src/app/views/club-rosters/club-rosters.component.ts`
- `src/app/views/club-rosters/club-rosters.component.html`
- `src/app/views/club-rosters/club-rosters.component.scss`

**Layout**:
- Page header: "Club Rosters"
- Team selector: `<select class="field-select">` with teams + player counts
- Syncfusion grid:
  - Columns: Checkbox, Player Name, Agegroup, Team
  - Sorting enabled
  - Multi-select with checkbox column
- Action toolbar (appears when rows selected):
  - "Move to..." dropdown (other teams) + confirm button
  - "Delete Selected" button → confirmation dialog

**Signals**:
- `teams` — `signal<ClubRosterTeamDto[]>([])`
- `selectedTeamId` — `signal<string | null>(null)`
- `roster` — `signal<ClubRosterPlayerDto[]>([])`
- `isLoading` — `signal(false)`
- `errorMessage` — `signal<string | null>(null)`

**Computed**:
- `otherTeams` — teams excluding the selected one (for move target dropdown)
- `selectedTeam` — current team object

### Step 10: Frontend — Nav Seed Update
**Status**: [x] Complete
**Files to modify**:
- `scripts/seed-nav-defaults.sql` — add `Club Rosters` item to ClubRep menu

Under ClubRep's "Registration" section, add:
```sql
INSERT INTO nav.NavItem (...) VALUES (..., N'Club Rosters', N'people', N'club-rosters', ...);
```

### Step 11: Styling & Polish
**Status**: [x] Complete
**Details**:
- All colors via CSS variables
- Player count badges in team selector
- Empty state when no teams / no players
- Mobile: grid scrolls horizontally
- Test all 8 palettes

---

## 7. Dependencies

- Existing `ITeamRepository` — team queries
- Existing `IRegistrationRepository` — roster queries, registration mutations
- Existing `ConfirmDialogComponent` (shared-ui)
- Existing `ToastService` (shared-ui)
- Existing `AuthService` (core) — for regId/jobId context
- Existing `RoleConstants` (Domain) — for ClubRep role ID
- Existing Syncfusion `GridAllModule`
- Existing `Teams` and `Registrations` entities

## 8. Verification

- [ ] Backend builds (`dotnet build`)
- [ ] API endpoints respond correctly (test via Swagger)
- [ ] TypeScript models generated (run regeneration script)
- [ ] Frontend compiles (`ng build`)
- [ ] Club rep sees only their teams in dropdown
- [ ] Selecting team loads roster in grid
- [ ] Multi-select + move players to different team works
- [ ] Player counts update after move
- [ ] Delete bogus registration with confirmation works
- [ ] Registrations with accounting balances cannot be deleted
- [ ] Non-ClubRep roles cannot access the route
- [ ] Toast notifications on success/error
- [ ] Responsive layout on mobile viewport
- [ ] Test with all 8 color palettes
