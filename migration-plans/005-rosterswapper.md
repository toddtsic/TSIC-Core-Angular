# Migration Plan: Rosters/Swapper → Roster Swapper

## Context

The legacy `Rosters/Swapper` page is an admin tool for moving players between team rosters. Admins select a source team and a target team, see both rosters side by side in jqGrid tables, then click a "Swap" button on any player row to reassign that player to the other team. Moving a player triggers fee recalculation — the player's fees must reflect the fee structure of their new team/agegroup (single source of truth: Agegroup fees → Team fee overrides).

**Critical business rule — three distinct transfer flows based on role:**
1. **Player → Team**: Standard swap — edits `AssignedTeamId` + recalculates fees
2. **Unassigned Adult → Team**: CREATES a new "Staff" registration cloned from the Unassigned Adult, assigned to the target team. The Unassigned Adult registration is **never modified** — it remains in the pool as a reusable template, allowing one coach to be assigned to multiple teams.
3. **Staff → Unassigned pool**: DELETES the Staff registration (the original Unassigned Adult master record still exists in the pool)

**Legacy URL**: `/Rosters/Swapper` (Controller=Rosters, Action=Swapper)

---

## 1. Legacy Strengths (Preserve These!)

- **Side-by-side roster comparison** — see both source and target team's full rosters simultaneously
- **One-click transfer** — single swap button per row, immediate AJAX round-trip
- **Full player detail in grid** — Name, Role, School, Grade, GradYear, Position, DOB, SkillLevel, YrsExp, PrevCoach, Requests, Gender, RegistrationDate
- **Dropdowns grouped by AgeGroup:Division** — teams organized hierarchically for quick navigation
- **Active status toggle** — double-click to toggle player Active flag inline
- **Unassigned adult staff pool** — special pool for coach/adult registrations (role: "Unassigned Adult") that acts as a reusable template. "Swapping" from this pool to a team creates a NEW Staff registration, allowing one coach to be rostered to multiple teams without consuming the original
- **Automatic fee recalculation** — player fees updated to match new team's agegroup fee structure after swap

## 2. Legacy Pain Points (Fix These!)

- **jqGrid dependency** — dated look, heavy jQuery, poor mobile experience, frozen columns via plugin hack
- **No multi-select** — can only move one player at a time; moving 10 players = 10 clicks + 10 AJAX round-trips
- **No fee impact preview** — player is moved first, fees recalculated silently; admin has no visibility into fee change BEFORE confirming
- **No capacity indicators** — no visual cue for how full a roster is (e.g., 14/16 players), easy to overfill
- **No search/filter within rosters** — must scroll through large rosters to find a specific player
- **Full page state loss** — dropdowns reset if page is refreshed mid-workflow
- **Anti-forgery token plumbing** — boilerplate in every AJAX call
- **No undo** — accidental swaps require re-swapping manually
- **Swap button confusion** — button always points one direction (source → target), no left ← right option; actually supports both but UX doesn't make this obvious
- **1000 rows per page** — entire roster loaded at once regardless of size

## 3. Modern Vision

**Recommended UI: Dual-Panel Transfer Layout**

The legacy side-by-side pattern is actually the right mental model for this task — admins need to see both rosters simultaneously to make informed decisions. The modernization preserves this layout while fixing every pain point.

```
┌──────────────────────────────────────────────────────────────────────┐
│  Roster Swapper                                              [⟳]    │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌─ SOURCE ─────────────────────┐   ┌─ TARGET ─────────────────────┐│
│  │ Pool: [▼ U12 Boys Gold / A ] │   │ Pool: [▼ U12 Boys Gold / B ] ││
│  │       14/16 players  ██████░░│   │       11/16 players  █████░░░ ││
│  │                              │   │                               ││
│  │  Dropdown options:           │   │  (same dropdown options)      ││
│  │  ── Unassigned Adults ──     │   │                               ││
│  │     ★ Unassigned Adults (5)  │   │                               ││
│  │  ── U12 Boys / Gold ──       │   │                               ││
│  │     Team A (14/16)           │   │                               ││
│  │     Team B (11/16)           │   │                               ││
│  │  ── U12 Boys / Silver ──     │   │                               ││
│  │     Team C (8/16)            │   │                               ││
│  │                              │   │                               ││
│  │ 🔍 [Filter players...     ] │   │ 🔍 [Filter players...     ]  ││
│  │                              │   │                               ││
│  │ ☐  Name         Role  Grade  │   │  Name         Role  Grade  ☐ ││
│  │ ☐  Smith, John  Player  8   →│   │← Jones, Mike  Player  7   ☐ ││
│  │ ☐  Davis, Emma  Player  7   →│   │← Lee, Sarah   Player  8   ☐ ││
│  │ ☐  Brown, Alex  Player  8   →│   │← Chen, Wei    Player  7   ☐ ││
│  │ ...                          │   │ ...                           ││
│  │                              │   │                               ││
│  │ [Move Selected →]            │   │           [← Move Selected]  ││
│  └──────────────────────────────┘   └───────────────────────────────┘│
│                                                                      │
│  ┌─ Fee Impact Preview (appears after selecting players) ──────────┐│
│  │  Moving 2 players from U12 Gold/A → U12 Gold/B                  ││
│  │  Smith, John:  $150.00 → $175.00  (+$25.00)                     ││
│  │  Davis, Emma:  $150.00 → $175.00  (+$25.00)                     ││
│  │                                                                   ││
│  │  [Cancel]                                    [Confirm Transfer]  ││
│  └───────────────────────────────────────────────────────────────────┘│
│                                                                      │
│  ── OR when assigning from Unassigned Adults → Team: ──              │
│                                                                      │
│  ┌─ Staff Assignment Confirmation ─────────────────────────────────┐│
│  │  Assigning 1 coach to U12 Gold / Team A                         ││
│  │  Coach Williams → NEW Staff registration will be created         ││
│  │  (Original "Unassigned Adult" record is preserved)               ││
│  │                                                                   ││
│  │  [Cancel]                                  [Confirm Assignment]  ││
│  └───────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────┘
```

**Why this approach over alternatives considered:**

| Alternative | Why Not |
|---|---|
| **Single table with "Move to" dropdown column** | Loses the simultaneous view of both rosters — admin can't see target roster capacity/composition before moving |
| **Kanban board (team = column, player = card)** | Doesn't scale — a job with 50 teams creates 50 columns. Also loses the focused 2-team comparison that makes the legacy tool effective |
| **Drag-and-drop only** | Accessibility concern (keyboard users, screen readers). CDK drag-drop between separate lists is also fragile with scroll containers. Arrow buttons + multi-select is more reliable and accessible |
| **Modal-based transfer** | This IS the primary workflow, not a secondary action. Modals would add friction to a repetitive task |

**Key improvements over legacy:**
- ✅ **Multi-select transfer** — check multiple players, move them all in one operation
- ✅ **Fee impact preview** — shows old fee → new fee per player BEFORE confirming the transfer
- ✅ **Capacity bar** — visual progress indicator showing roster fill level (current/max), color-coded (green → yellow → red as it fills)
- ✅ **Inline search/filter** — quickly find players in large rosters by name
- ✅ **Grouped team dropdown** — teams organized under Agegroup > Division headers using `<optgroup>` — mirrors legacy's AgDiv grouping
- ✅ **Responsive layout** — panels stack vertically on mobile (< 768px), source on top, target below
- ✅ **No page reloads** — signal-driven state updates after each transfer, preserves dropdown selections
- ✅ **Toast feedback** — success/error toast after each transfer with player count and fee summary
- ✅ **Active status toggle** — inline switch per player row (not hidden behind double-click)

## 4. User Value

- **Faster workflows**: Multi-select moves 10 players in 1 click instead of 10
- **Fewer errors**: Fee preview prevents surprise fee changes; capacity bar prevents overfilling rosters
- **Better awareness**: See roster composition and capacity at a glance before transferring
- **Mobile access**: Stack layout works on tablets for field-side roster adjustments
- **Transparency**: Admin sees exact fee impact before confirming — no silent recalculations

## 5. Design Alignment

- Bootstrap tables + CSS variables (all 8 palettes)
- Signal-based state, OnPush change detection
- Toast notifications via existing `ToastService`
- `ConfirmDialogComponent` for transfer confirmation (with fee impact detail)
- WCAG AA compliant (keyboard-navigable multi-select, ARIA labels, focus management)

## 6. UI Standards Created / Employed

### CREATED (new patterns this module introduces)
- **Dual-Panel Transfer Layout** — two side-by-side panels with independent dropdowns, multi-select tables, and directional transfer buttons. Responsive: stacks vertically on mobile.
- **Capacity Progress Bar** — colored bar showing current/max with numeric label (e.g., "14/16"). Thresholds: green (< 75%), yellow (75-90%), red (> 90%).
- **Fee Impact Preview Panel** — expandable panel below transfer area showing per-player old fee → new fee with delta. Only appears after selecting players and clicking "Move".
- **Transfer Confirmation with Fee Detail** — confirmation dialog that includes financial impact summary, requiring explicit "Confirm Transfer" click.
- **Grouped Select Dropdown** — `<optgroup>` labels grouping teams by Agegroup > Division for large team lists.

### EMPLOYED (existing patterns reused)
- Signal-based state management (all component state as signals)
- CSS variable design system tokens (all colors, spacing, borders)
- `@if` / `@for` template syntax
- OnPush change detection
- `inject()` dependency injection
- Repository pattern (RegistrationRepository, TeamRepository, AgeGroupRepository)
- `ConfirmDialogComponent` for transfer confirmation
- `ToastService` for success/error feedback
- `FormsModule` with `[(ngModel)]` for dropdowns/filters

---

## 7. Security Requirements

**CRITICAL**: All endpoints must derive `jobId` from JWT claims, NOT from route parameters.

- **Route**: `/:jobPath/admin/roster-swapper` (jobPath for routing only)
- **API Endpoints**: Must use `ClaimsPrincipalExtensions.GetJobIdFromRegistrationAsync()` to derive `jobId` from the authenticated user's `regId` claim
- **NO route parameters containing sensitive IDs**: All `[Authorize]` endpoints must extract job context from JWT token
- **Policy**: `[Authorize(Policy = "AdminOnly")]` — Directors, SuperDirectors, and Superusers can swap rosters
- **Validation**: Server must verify that both source and target teams belong to the user's job before any swap operation
- **Validation**: Server must verify that the player registration belongs to the source team before moving

---

## 8. Database Entities (Existing — No Schema Changes)

### Role-Based Transfer Semantics:

Three distinct roles participate in roster swapping, each with different behavior:

```
┌─────────────────────────────────────────────────────────────────────┐
│  ROLE: "Unassigned Adult"                                           │
│  ─────────────────────────                                          │
│  The master coach/adult registration. Lives in the Unassigned       │
│  Adults pool (AssignedTeamId = Guid.Empty / null).                  │
│  NEVER modified or moved by a swap. Acts as a reusable template.    │
│                                                                     │
│  "Swap" to team → CREATES new "Staff" registration (see below)     │
│  One Unassigned Adult can spawn N Staff registrations (one per team)│
├─────────────────────────────────────────────────────────────────────┤
│  ROLE: "Staff"                                                      │
│  ─────────────                                                      │
│  A team-specific registration created FROM an Unassigned Adult.     │
│  Tied to exactly one team via AssignedTeamId.                       │
│                                                                     │
│  "Swap" back to Unassigned Adults pool → DELETES this registration │
│  (the original Unassigned Adult master record still exists)         │
│  "Swap" to different team → UPDATE AssignedTeamId (like a Player)  │
├─────────────────────────────────────────────────────────────────────┤
│  ROLE: "Player"                                                     │
│  ─────────────                                                      │
│  Standard player registration. Standard swap behavior:              │
│  UPDATE AssignedTeamId + recalculate fees.                          │
│  Cannot be moved to the Unassigned Adults pool.                     │
└─────────────────────────────────────────────────────────────────────┘

Example flow:
  Coach registers → 1 "Unassigned Adult" reg (no team)
  Admin assigns to Team A → creates Staff reg #1 (Team A)
  Admin assigns to Team B → creates Staff reg #2 (Team B)
  Admin assigns to Team C → creates Staff reg #3 (Team C)
  Result: 1 Unassigned Adult + 3 Staff regs (one per team)
  Admin removes from Team B → deletes Staff reg #2
  Result: 1 Unassigned Adult + 2 Staff regs (Team A, Team C)
```

### Key Entities Involved:

**Registrations** (player/staff being moved):
- `RegistrationId` (Guid, PK) — the player registration
- `UserId` (string, FK) — the user this registration belongs to (shared between Unassigned Adult and its Staff clones)
- `AssignedTeamId` (Guid, FK) — **the field that gets updated** when swapping players/staff; null/Guid.Empty for Unassigned Adults
- `AssignedAgegroupId` (Guid) — updated to match new team's agegroup
- `AssignedDivId` (Guid) — updated to match new team's division
- `RoleId` (string, FK) — determines transfer behavior ("Unassigned Adult", "Staff", "Player", etc.)
- `FeeBase`, `FeeProcessing`, `FeeDiscount`, `FeeTotal`, `OwedTotal`, `PaidTotal` — **recalculated after swap** (players only; staff fees typically $0)
- `BActive` (bool) — can be toggled inline
- `JobId` (Guid, FK) — the job this registration belongs to
- `FamilyUserId` (string) — family group (copied to Staff clone)
- `LebUserId` (string) — admin who last modified

**Teams** (source and target):
- `TeamId` (Guid, PK)
- `AgegroupId` (Guid, FK) — determines fee structure
- `DivId` (Guid, FK) — determines division
- `LeagueId` (Guid, FK)
- `MaxCount` (int) — roster capacity limit
- `PerRegistrantFee` (decimal) — team-level fee override (if set)
- `FeeBase` (decimal) — team-level base fee override (if set)
- `TeamName`, `Active`

**Agegroups** (fee source of truth):
- `AgegroupId` (Guid, PK)
- `TeamFee` (decimal) — default team fee
- `RosterFee` (decimal) — default per-player roster fee
- `PlayerFeeOverride` (decimal) — override for player-level fees

### Fee Recalculation Logic (Single Source of Truth):

After a player is moved to a new team, fees are recalculated using the coalescing hierarchy:

```
Player FeeBase = COALESCE(
    Team.PerRegistrantFee,    -- team-level override (highest priority)
    Agegroup.PlayerFeeOverride, -- agegroup-level override
    Agegroup.RosterFee,       -- agegroup default
    0                         -- fallback
)
```

Then:
```
FeeProcessing = FeeBase × Job.ProcessingFeePercent (if Job.BAddProcessingFees)
FeeTotal = FeeBase + FeeProcessing - FeeDiscount - FeeDonation
OwedTotal = FeeTotal - PaidTotal
```

Uses existing `PlayerFeeCalculator` / `RegistrationRecordFeeCalculatorService` for computation.

---

## 9. Implementation Steps

### Phase 1: Backend — DTOs

**Status**: [ ] Pending

**File to create**:
- `TSIC.Contracts/Dtos/RosterSwapper/RosterSwapperDtos.cs`

**DTOs**:
```csharp
// Pool dropdown option (teams grouped by agegroup/division + special Unassigned Adults pool)
public record SwapperPoolOptionDto
{
    public required Guid PoolId { get; init; }          // TeamId or Guid.Empty for Unassigned Adults
    public required string PoolName { get; init; }       // TeamName or "Unassigned Adults"
    public required bool IsUnassignedAdultsPool { get; init; }
    public required string? AgegroupName { get; init; }  // null for Unassigned Adults
    public required string? DivName { get; init; }       // null for Unassigned Adults
    public required Guid? AgegroupId { get; init; }
    public required Guid? DivId { get; init; }
    public required int RosterCount { get; init; }
    public required int MaxCount { get; init; }          // 0 for Unassigned Adults (unlimited)
    public required bool Active { get; init; }
}

// Player row in roster table
public record SwapperPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string RoleName { get; init; }
    public required bool BActive { get; init; }
    public string? School { get; init; }
    public short? Grade { get; init; }
    public int? GradYear { get; init; }
    public string? Position { get; init; }
    public DateOnly? Dob { get; init; }
    public string? Gender { get; init; }
    public string? SkillLevel { get; init; }
    public int? YrsExp { get; init; }
    public string? Requests { get; init; }
    public string? PrevCoach { get; init; }
    public decimal FeeBase { get; init; }
    public decimal FeeTotal { get; init; }
    public decimal OwedTotal { get; init; }
    public DateTime? RegistrationTs { get; init; }
}

// Transfer request (supports multi-select)
// Backend detects transfer type from source/target pool IDs and registration roles
public record RosterTransferRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required Guid SourcePoolId { get; init; }   // Guid.Empty = Unassigned Adults pool
    public required Guid TargetPoolId { get; init; }   // Guid.Empty = Unassigned Adults pool
}

// Fee impact preview per player
public record RosterTransferFeePreviewDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required decimal CurrentFeeBase { get; init; }
    public required decimal CurrentFeeTotal { get; init; }
    public required decimal NewFeeBase { get; init; }
    public required decimal NewFeeTotal { get; init; }
    public required decimal FeeDelta { get; init; }
}

// Fee preview request
public record RosterTransferPreviewRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required Guid TargetTeamId { get; init; }
}

// Transfer result — describes what happened (role-dependent)
public record RosterTransferResultDto
{
    public required int PlayersTransferred { get; init; }    // Players with AssignedTeamId updated
    public required int StaffCreated { get; init; }          // New Staff regs created from Unassigned Adults
    public required int StaffDeleted { get; init; }          // Staff regs deleted (returned to unassigned pool)
    public required int FeesRecalculated { get; init; }
    public required string Message { get; init; }
}

// Active status toggle
public record UpdatePlayerActiveRequest
{
    public required bool BActive { get; init; }
}
```

### Phase 2: Backend — Repository Extensions

**Status**: [ ] Pending

**Files to modify**:
- `TSIC.Contracts/Repositories/IRegistrationRepository.cs` (add methods)
- `TSIC.Infrastructure/Repositories/RegistrationRepository.cs` (implement)
- `TSIC.Contracts/Repositories/ITeamRepository.cs` (add methods)
- `TSIC.Infrastructure/Repositories/TeamRepository.cs` (implement)

**Files to create**:
- `TSIC.Contracts/Repositories/IDeviceRepository.cs`
- `TSIC.Infrastructure/Repositories/DeviceRepository.cs`

**New IRegistrationRepository methods**:
```
GetRosterByTeamIdAsync(Guid teamId, Guid jobId) → List<SwapperPlayerDto>
    -- Returns all registrations for team (active + inactive), joined with User + Role for display names
    -- AsNoTracking for read-only roster display

GetUnassignedAdultsAsync(Guid jobId) → List<SwapperPlayerDto>
    -- Returns all registrations with Role = "Unassigned Adult" for the job
    -- These are the "template" coach registrations that can be cloned to teams
    -- AsNoTracking for read-only display

GetRegistrationsForTransferAsync(List<Guid> registrationIds, Guid sourcePoolId) → List<Registrations>
    -- Returns tracked registrations for bulk update
    -- If sourcePoolId = Guid.Empty: validates each has Role = "Unassigned Adult"
    -- Otherwise: validates each belongs to source team (AssignedTeamId = sourcePoolId)
    -- Tracked entities for update/clone

GetExistingStaffAssignmentAsync(string userId, Guid teamId, Guid jobId) → Registrations?
    -- Checks if a Staff registration already exists for this user on this team
    -- Used to prevent duplicate staff assignments (coach already on that team)
    -- AsNoTracking
```

**New ITeamRepository methods**:
```
GetSwapperPoolOptionsAsync(Guid jobId) → List<SwapperPoolOptionDto>
    -- Returns all teams for job with agegroup/division names and roster counts
    -- PLUS a synthetic "Unassigned Adults" entry (PoolId=Guid.Empty, IsUnassignedAdultsPool=true)
    --   with RosterCount = COUNT of Unassigned Adult role registrations for the job
    -- Grouped/ordered: Unassigned Adults first, then by AgegroupName, DivName, TeamName
    -- AsNoTracking

GetTeamWithFeeContextAsync(Guid teamId) → (Teams team, Agegroups agegroup)?
    -- Returns team + its parent agegroup (for fee coalescing)
    -- AsNoTracking
```

**New IDeviceRepository** (CREATE — no device repository exists yet):
```
GetDeviceTeamsByRegistrationAndTeamAsync(Guid registrationId, Guid teamId) → List<DeviceTeams>
    -- Returns tracked DeviceTeams records for a specific registration + team combo
    -- Used in Flows 1, 3, 4 to find device-team links to update or delete

GetDeviceIdsByRegistrationAsync(Guid registrationId) → List<string>
    -- Returns distinct DeviceIds linked to registration via DeviceRegistrationIds (where Active=true)
    -- AsNoTracking — used in Flow 2 to discover which devices to create DeviceTeams for

GetDeviceRegistrationIdsByRegistrationAsync(Guid registrationId) → List<DeviceRegistrationIds>
    -- Returns tracked DeviceRegistrationIds records for a registration
    -- Used in Flow 3 to delete device-registration links when Staff reg is deleted

AddDeviceTeam(DeviceTeams entity)
AddDeviceRegistrationId(DeviceRegistrationIds entity)
    -- Add new device mapping records (Flow 2: staff creation)

RemoveDeviceTeams(IEnumerable<DeviceTeams> entities)
RemoveDeviceRegistrationIds(IEnumerable<DeviceRegistrationIds> entities)
    -- Bulk remove device mapping records (Flow 3: staff deletion)

SaveChangesAsync()
```

### Phase 3: Backend — Service

**Status**: [ ] Pending

**Files to create**:
- `TSIC.Contracts/Services/IRosterSwapperService.cs`
- `TSIC.API/Services/Admin/RosterSwapperService.cs`

**Dependencies**:
- `IRegistrationRepository`
- `ITeamRepository`
- `IAgeGroupRepository`
- `IDeviceRepository` (new — for device record sync during transfers)
- `IRegistrationRecordFeeCalculatorService` (existing — for fee computation)
- `IJobRepository` (for processing fee settings)

**Methods**:

```
GetPoolOptionsAsync(Guid jobId) → List<SwapperPoolOptionDto>
    -- Delegates to TeamRepository, returns teams + Unassigned Adults pool organized for dropdown

GetRosterAsync(Guid poolId, Guid jobId) → List<SwapperPlayerDto>
    -- If poolId = Guid.Empty: loads Unassigned Adults via GetUnassignedAdultsAsync
    -- Otherwise: loads team roster via GetRosterByTeamIdAsync
    -- Validates team belongs to job (for real teams)

PreviewTransferAsync(Guid jobId, RosterTransferPreviewRequest request) → List<RosterTransferFeePreviewDto>
    -- Detects transfer type from source/target pool IDs and registration roles:
    --
    -- FLOW 1: Player → Team (standard)
    --   For each registration:
    --     1. Look up current fees from registration
    --     2. Look up target team's fee context (team + agegroup)
    --     3. Compute new FeeBase via coalescing hierarchy
    --     4. Compute new FeeProcessing via RegistrationRecordFeeCalculatorService
    --     5. Compute new FeeTotal, delta
    --
    -- FLOW 2: Unassigned Adult → Team (staff creation)
    --   No fee preview needed (staff fees are typically $0)
    --   Returns preview with informational message: "N new Staff registrations will be created"
    --   Checks for duplicate: if coach already has Staff reg on target team, flag in preview
    --
    -- FLOW 3: Staff → Unassigned pool (staff removal)
    --   No fee preview needed
    --   Returns preview with informational message: "N Staff registrations will be deleted"
    --
    -- Returns preview DTOs WITHOUT persisting any changes

ExecuteTransferAsync(Guid jobId, string userId, RosterTransferRequest request) → RosterTransferResultDto
    -- Validates:
    --   1. Source and target pools are valid (belong to job, or Guid.Empty for unassigned)
    --   2. All registrations belong to source pool
    --   3. Source ≠ Target
    --
    -- FLOW 1: Player → Team (SourcePoolId != Empty, TargetPoolId != Empty, Role = Player)
    --   Validate target team has capacity (roster count + transfer count ≤ MaxCount)
    --   For each registration:
    --     1. Update AssignedTeamId → target team
    --     2. Update AssignedAgegroupId → target team's agegroup
    --     3. Update AssignedDivId → target team's division
    --     4. Recalculate fees using coalescing hierarchy + PlayerFeeCalculator
    --     5. Update Modified, LebUserId
    --     6. Device sync: Update DeviceTeams records (RegistrationId + old TeamId) → set TeamId to target team
    --   SaveChangesAsync (single transaction)
    --
    -- FLOW 2: Unassigned Adult → Team (SourcePoolId = Empty, TargetPoolId = team)
    --   Validate target team has capacity
    --   For each Unassigned Adult registration:
    --     1. Check for existing Staff reg for this user on target team (prevent duplicates)
    --     2. CREATE new Registrations entity with:
    --        - New RegistrationId (Guid.NewGuid())
    --        - UserId = source Unassigned Adult's UserId (same person)
    --        - FamilyUserId = source's FamilyUserId
    --        - JobId = source's JobId
    --        - RoleId = "Staff" role ID
    --        - AssignedTeamId = target team
    --        - AssignedAgegroupId = target team's agegroup
    --        - AssignedDivId = target team's division
    --        - AssignedLeagueId = target team's league
    --        - BActive = true
    --        - All fee fields = 0 (staff don't pay)
    --        - Copy relevant personal fields from source (name, contact info, etc.)
    --        - Modified, LebUserId = current admin
    --     3. Do NOT modify the source Unassigned Adult registration
    --     4. Device sync: For each device linked to the source Unassigned Adult (via DeviceRegistrationIds),
    --        create a DeviceTeams record mapping that device → target team with new Staff RegistrationId.
    --        Also create DeviceRegistrationIds records linking each device → new Staff registration.
    --   SaveChangesAsync
    --
    -- FLOW 3: Staff → Unassigned pool (SourcePoolId = team, TargetPoolId = Empty, Role = Staff)
    --   For each Staff registration:
    --     1. Device sync: Delete all DeviceTeams records where RegistrationId = Staff reg
    --     2. Device sync: Delete all DeviceRegistrationIds records where RegistrationId = Staff reg
    --     3. DELETE the Staff registration entity
    --   SaveChangesAsync
    --   (The original Unassigned Adult master record and its device records remain in the pool)
    --
    -- FLOW 4: Staff → Different Team (SourcePoolId = team, TargetPoolId = team, Role = Staff)
    --   Treated like a Player swap: UPDATE AssignedTeamId on the Staff registration
    --   No fee recalculation needed (staff fees = $0)
    --   Device sync: Update DeviceTeams records (RegistrationId + old TeamId) → set TeamId to target team
    --
    -- Returns result with counts per flow type + summary message

TogglePlayerActiveAsync(Guid registrationId, Guid jobId, bool active, string userId) → void
    -- Validates registration belongs to job
    -- Updates BActive, Modified, LebUserId
```

### Phase 4: Backend — Controller

**Status**: [ ] Pending

**File to create**:
- `TSIC.API/Controllers/RosterSwapperController.cs`

**Endpoints**:
- `GET api/roster-swapper/pools` → `List<SwapperPoolOptionDto>` (all teams + Unassigned Adults pool for dropdown)
- `GET api/roster-swapper/roster/{poolId:guid}` → `List<SwapperPlayerDto>` (roster for selected pool; Guid.Empty = Unassigned Adults)
- `POST api/roster-swapper/preview` → `List<RosterTransferFeePreviewDto>` (fee impact preview, body: `RosterTransferPreviewRequest`)
- `POST api/roster-swapper/transfer` → `RosterTransferResultDto` (execute transfer — backend auto-detects flow from roles, body: `RosterTransferRequest`)
- `PUT api/roster-swapper/players/{registrationId:guid}/active` → `void` (toggle active, body: `UpdatePlayerActiveRequest`)

**Authorization**: All endpoints `[Authorize(Policy = "AdminOnly")]`, derive `jobId` from JWT via `GetJobIdFromRegistrationAsync()`.

### Phase 5: Backend — DI Registration

**Status**: [ ] Pending

**File to modify**:
- `TSIC.API/Program.cs`

**Add registrations**:
```csharp
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IRosterSwapperService, RosterSwapperService>();
```

### Phase 6: Frontend — Service

**Status**: [ ] Pending

**File to create**:
- `src/app/views/admin/roster-swapper/services/roster-swapper.service.ts`

**Methods** (all return Observables):
- `getPoolOptions(): Observable<SwapperPoolOptionDto[]>`
- `getRoster(poolId: string): Observable<SwapperPlayerDto[]>` (Guid.Empty string for Unassigned Adults)
- `previewTransfer(request: RosterTransferPreviewRequest): Observable<RosterTransferFeePreviewDto[]>`
- `executeTransfer(request: RosterTransferRequest): Observable<RosterTransferResultDto>`
- `togglePlayerActive(registrationId: string, active: boolean): Observable<void>`

### Phase 7: Frontend — Roster Swapper Component

**Status**: [ ] Pending

**Files to create**:
- `src/app/views/admin/roster-swapper/roster-swapper.component.ts`
- `src/app/views/admin/roster-swapper/roster-swapper.component.html`
- `src/app/views/admin/roster-swapper/roster-swapper.component.scss`

**Component state** (signals):
```typescript
// Pool options (teams + Unassigned Adults)
poolOptions = signal<SwapperPoolOptionDto[]>([]);
groupedPoolOptions = computed(() => groupByCategory(this.poolOptions()));
// Grouping: "Unassigned Adults" group first, then Agegroup > Division groups

// Source panel
sourcePoolId = signal<string | null>(null);
sourcePool = computed(() => this.poolOptions().find(p => p.poolId === this.sourcePoolId()));
sourceRoster = signal<SwapperPlayerDto[]>([]);
sourceSelected = signal<Set<string>>(new Set());
sourceFilter = signal('');
filteredSourceRoster = computed(() => filterPlayers(this.sourceRoster(), this.sourceFilter()));
isSourceUnassigned = computed(() => this.sourcePool()?.isUnassignedAdultsPool ?? false);

// Target panel
targetPoolId = signal<string | null>(null);
targetPool = computed(() => this.poolOptions().find(p => p.poolId === this.targetPoolId()));
targetRoster = signal<SwapperPlayerDto[]>([]);
targetSelected = signal<Set<string>>(new Set());
targetFilter = signal('');
filteredTargetRoster = computed(() => filterPlayers(this.targetRoster(), this.targetFilter()));
isTargetUnassigned = computed(() => this.targetPool()?.isUnassignedAdultsPool ?? false);

// Transfer type detection (derived from source/target pool types and selected roles)
transferType = computed<'player-swap' | 'staff-create' | 'staff-delete' | 'staff-move'>(() => {
  // Unassigned Adults → Team = staff-create
  // Team (Staff role) → Unassigned Adults = staff-delete
  // Team (Staff role) → Team = staff-move
  // Team (Player role) → Team = player-swap
});

// Transfer state
feePreview = signal<RosterTransferFeePreviewDto[] | null>(null);
isTransferring = signal(false);
isLoadingPreview = signal(false);
transferDirection = signal<'source-to-target' | 'target-to-source'>('source-to-target');

// General
isLoading = signal(false);
```

**Component methods**:
- `loadPoolOptions()` — fetch all pools (teams + Unassigned Adults) for dropdowns
- `onSourcePoolChange(poolId)` — load source roster, clear selection
- `onTargetPoolChange(poolId)` — load target roster, clear selection
- `toggleSourceSelect(regId)` — toggle player selection in source
- `toggleTargetSelect(regId)` — toggle player selection in target
- `selectAllSource()` / `deselectAllSource()` — bulk select
- `moveSourceToTarget()` — preview, show confirmation panel (context-aware messaging based on `transferType`)
- `moveTargetToSource()` — preview (reverse direction)
- `confirmTransfer()` — execute transfer, reload both panels, show toast with role-aware summary
- `cancelTransfer()` — clear preview
- `toggleActive(regId, active)` — toggle player active status

**Context-aware confirmation messages based on transfer type:**
- `player-swap`: "Move N players from [Source] → [Target]. Fees will be recalculated."
- `staff-create`: "Assign N coaches to [Target]. New Staff registrations will be created. Original Unassigned Adult records are preserved."
- `staff-delete`: "Remove N staff from [Source]. Staff registrations will be deleted. Original Unassigned Adult records remain in the pool."
- `staff-move`: "Move N staff from [Source] → [Target]."

**Layout structure** (responsive dual-panel):
```html
<div class="roster-swapper-container">
  <div class="swapper-header">
    <h5>Roster Swapper</h5>
    <button class="btn btn-sm btn-outline-secondary" (click)="loadPoolOptions()">
      <i class="bi bi-arrow-clockwise"></i>
    </button>
  </div>

  <div class="swapper-panels">
    <!-- Source Panel -->
    <div class="swapper-panel">
      <div class="panel-header">
        <label class="form-label fw-semibold">Source Pool</label>
        <select class="form-select" [ngModel]="sourcePoolId()" (ngModelChange)="onSourcePoolChange($event)">
          @for (group of groupedPoolOptions(); track group.label) {
            <optgroup [label]="group.label">
              @for (pool of group.pools; track pool.poolId) {
                <option [value]="pool.poolId">{{ pool.poolName }} ({{ pool.rosterCount }}{{ pool.maxCount ? '/' + pool.maxCount : '' }})</option>
              }
            </optgroup>
          }
        </select>
        <!-- Capacity bar -->
        <!-- Filter input -->
      </div>
      <div class="panel-body">
        <!-- Player table with checkboxes -->
      </div>
      <div class="panel-footer">
        <button class="btn btn-primary btn-sm" [disabled]="sourceSelected().size === 0"
                (click)="moveSourceToTarget()">
          Move Selected <i class="bi bi-arrow-right"></i> ({{ sourceSelected().size }})
        </button>
      </div>
    </div>

    <!-- Target Panel -->
    <div class="swapper-panel">
      <!-- Mirror of source panel with reversed arrow -->
    </div>
  </div>

  <!-- Fee Impact Preview (conditional) -->
  @if (feePreview()) {
    <div class="fee-preview-panel">
      <!-- Per-player fee impact table -->
      <!-- Confirm / Cancel buttons -->
    </div>
  }
</div>
```

**CSS** (responsive):
```scss
.roster-swapper-container {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  height: calc(100vh - 200px);
}

.swapper-panels {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-3);
  flex: 1;
  min-height: 0;
}

.swapper-panel {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--bs-border-color);
  border-radius: var(--radius-md);
  background: var(--bs-body-bg);
  overflow: hidden;
}

.panel-header {
  padding: var(--space-3);
  border-bottom: 1px solid var(--bs-border-color);
}

.panel-body {
  flex: 1;
  overflow-y: auto;
}

.panel-footer {
  padding: var(--space-2) var(--space-3);
  border-top: 1px solid var(--bs-border-color);
  display: flex;
  justify-content: flex-end;
}

.capacity-bar {
  height: 6px;
  border-radius: 3px;
  background: var(--bs-secondary-bg);
  margin-top: var(--space-1);
}

.capacity-fill {
  height: 100%;
  border-radius: 3px;
  transition: width 0.3s ease;
  // Thresholds set via [style.background]
  // < 75%: var(--bs-success)
  // 75-90%: var(--bs-warning)
  // > 90%: var(--bs-danger)
}

.fee-preview-panel {
  border: 1px solid var(--bs-warning-border-subtle);
  background: var(--bs-warning-bg-subtle);
  border-radius: var(--radius-md);
  padding: var(--space-3);
}

// Mobile: stack panels vertically
@media (max-width: 767.98px) {
  .swapper-panels {
    grid-template-columns: 1fr;
  }

  .swapper-panel {
    max-height: 50vh;
  }
}
```

### Phase 8: Frontend — Routing

**Status**: [ ] Pending

**File to modify**:
- `src/app/app.routes.ts`

**Add routes**:
```typescript
{
  path: 'admin/roster-swapper',
  canActivate: [authGuard],
  data: { requirePhase2: true, requiresPolicy: 'AdminOnly' },
  loadComponent: () => import('./views/admin/roster-swapper/roster-swapper.component')
    .then(m => m.RosterSwapperComponent)
}
// Legacy-compatible route
{
  path: 'rosters/swapper',
  canActivate: [authGuard],
  data: { requirePhase2: true, requiresPolicy: 'AdminOnly' },
  loadComponent: () => import('./views/admin/roster-swapper/roster-swapper.component')
    .then(m => m.RosterSwapperComponent)
}
```

### Phase 9: Post-Build — API Model Regeneration

**Status**: [ ] Pending

**Action**: Run `.\scripts\2-Regenerate-API-Models.ps1`
- Generates TypeScript types from DTOs
- Switch imports in frontend service from local types to `@core/api`

### Phase 10: Testing & Polish

**Status**: [ ] Pending

**Critical tests**:

**Pool selection:**
1. **Pool dropdown**: "Unassigned Adults" appears first, then teams grouped by Agegroup > Division with roster counts
2. **Load roster**: Selecting a team loads its roster; selecting "Unassigned Adults" loads all Unassigned Adult role registrations
3. **Capacity bar**: Shown for teams (current/max); hidden or no-max for Unassigned Adults pool

**Flow 1 — Player → Team (standard swap):**
4. **Multi-select**: Check individual players, select all, deselect all
5. **Fee preview**: Selecting players + clicking "Move" shows per-player fee impact before confirming
6. **Transfer execution**: Confirming transfer updates AssignedTeamId, recalculates fees, refreshes both rosters
7. **Capacity enforcement**: Cannot transfer if target roster would exceed MaxCount (button disabled + tooltip)

**Flow 2 — Unassigned Adult → Team (staff creation):**
8. **Staff creation**: Selecting Unassigned Adults in source, a team in target, then moving → creates NEW Staff registrations
9. **Unassigned Adult preserved**: After creating Staff registrations, the Unassigned Adult records remain in the pool (same count)
10. **Multi-team assignment**: Same Unassigned Adult can be assigned to multiple teams (each creates a separate Staff reg)
11. **Duplicate prevention**: If coach already has a Staff reg on target team, show warning in preview / block transfer
12. **Confirmation text**: Shows "N new Staff registrations will be created" (not "N players moved")

**Flow 3 — Staff → Unassigned pool (staff removal):**
13. **Staff deletion**: Moving Staff role registrations to Unassigned Adults pool → deletes the Staff registrations
14. **Confirmation text**: Shows "N Staff registrations will be deleted. Original records preserved in pool."
15. **Unassigned Adult untouched**: After deletion, the Unassigned Adult master records still appear in the pool

**Flow 4 — Staff → Different Team (staff move):**
16. **Staff move**: Moving Staff role registrations between teams → updates AssignedTeamId (like a player swap, no fee recalc)

**Device record sync (push notifications):**
17. **Player swap device sync**: After moving a player, their DeviceTeams records point to the new team (score notifications follow the player)
18. **Staff creation device sync**: After assigning an Unassigned Adult to a team, DeviceTeams + DeviceRegistrationIds records are created for the new Staff reg, mirroring the source's device links
19. **Staff deletion device cleanup**: After removing a Staff reg, all DeviceTeams + DeviceRegistrationIds for that registration are deleted (Unassigned Adult's device records remain intact)
20. **Staff move device sync**: After moving Staff between teams, their DeviceTeams records point to the new team

**General:**
21. **Validation**: Cannot select same pool as source and target
22. **Active toggle**: Clicking active switch toggles player's BActive, reflected in UI
23. **Filter**: Typing in filter box narrows player list by name (case-insensitive)
24. **Responsive**: Panels stack vertically on mobile, each scrollable independently
25. **Error handling**: Network errors show toast with retry option
26. **All 8 palettes**: CSS variable themed throughout
27. **Edge cases**: Empty roster, single player roster, full roster (at MaxCount), Unassigned Adults pool with zero coaches, player with no device records (no-op for device sync)

---

## 10. Files Summary

### Backend Files

| File | Action | LOC (est.) |
|------|--------|------------|
| `TSIC.Contracts/Dtos/RosterSwapper/RosterSwapperDtos.cs` | Create | ~100 |
| `TSIC.Contracts/Repositories/IRegistrationRepository.cs` | Edit (add methods) | +20 |
| `TSIC.Infrastructure/Repositories/RegistrationRepository.cs` | Edit (implement) | +80 |
| `TSIC.Contracts/Repositories/ITeamRepository.cs` | Edit (add methods) | +10 |
| `TSIC.Infrastructure/Repositories/TeamRepository.cs` | Edit (implement) | +50 |
| `TSIC.Contracts/Repositories/IDeviceRepository.cs` | Create | ~30 |
| `TSIC.Infrastructure/Repositories/DeviceRepository.cs` | Create | ~80 |
| `TSIC.Contracts/Services/IRosterSwapperService.cs` | Create | ~25 |
| `TSIC.API/Services/Admin/RosterSwapperService.cs` | Create | ~400 |
| `TSIC.API/Controllers/RosterSwapperController.cs` | Create | ~110 |
| `TSIC.API/Program.cs` | Edit (2 DI lines) | +2 |

### Frontend Files

| File | Action | LOC (est.) |
|------|--------|------------|
| `views/admin/roster-swapper/services/roster-swapper.service.ts` | Create | ~50 |
| `views/admin/roster-swapper/roster-swapper.component.ts` | Create | ~300 |
| `views/admin/roster-swapper/roster-swapper.component.html` | Create | ~200 |
| `views/admin/roster-swapper/roster-swapper.component.scss` | Create | ~120 |
| `app.routes.ts` | Edit (2 routes) | +12 |
| `core/api/models/` (auto-generated) | Auto | ~6 files |

---

## 11. Key Design Decisions

1. **Dual-panel transfer over single table** — preserves the legacy mental model of comparing two rosters side by side, which is essential for informed transfer decisions. Admin needs to see both roster compositions simultaneously.

2. **Fee preview BEFORE transfer** — the biggest UX improvement. Legacy silently recalculated fees after the swap; admin had no visibility. New design shows exact fee impact per player with delta, requiring explicit confirmation. This prevents surprise billing changes.

3. **Multi-select batch transfer** — single biggest efficiency gain. Legacy required one click per player × one AJAX call per player. New design: check N players → one preview → one confirmation → one API call.

4. **Capacity bar with color thresholds** — visual indicator prevents overfilling rosters without requiring admin to mentally compare numbers. Green/yellow/red matches universal UX conventions.

5. **Grouped `<optgroup>` dropdowns** — teams organized by Agegroup > Division mirrors the LADT hierarchy and matches the legacy's "AgDiv" grouping pattern. Scales to 100+ teams per job.

6. **No drag-and-drop** — considered CDK drag-drop between lists but rejected: (a) accessibility issues for keyboard/screen reader users, (b) unreliable with scroll containers, (c) multi-select drag is complex. Arrow buttons + checkboxes are more reliable and accessible.

7. **Single transaction for batch transfers** — all player updates within one `SaveChangesAsync()` call. If any validation fails, no players are moved. Ensures data integrity.

8. **Service reuses existing fee calculators** — `RegistrationRecordFeeCalculatorService` and `PlayerFeeCalculator` are the single source of truth for fee computation. No duplication of fee logic.

9. **Active toggle inline** — moved from legacy's hidden double-click to explicit switch control. More discoverable, better accessibility.

10. **Authorization: AdminOnly (not SuperUserOnly)** — roster management is a director-level task, not limited to superusers. Matches legacy's `[Authorize(Roles = "Director,SuperDirector,Superuser")]`.

11. **Unassigned Adult / Staff pattern is first-class, not deferred** — this is core business logic, not a special case. The Roster Swapper implements four distinct transfer flows based on source/target pool type and registration role. The backend auto-detects the correct flow from the request context — the frontend only needs to know about pool IDs, not the underlying role mechanics. The "Unassigned Adults" pool appears as the first option in the dropdown, visually distinguished from team entries.

12. **Staff creation clones from Unassigned Adult, never modifies it** — the Unassigned Adult registration is immutable with respect to swaps. It acts as a reusable template. This enables one coach to be rostered to N teams simultaneously (one Staff registration per team). Deleting a Staff registration (returning to pool) does not affect the master record or other Staff registrations for the same coach.

13. **No fee recalculation for staff** — Staff registrations have $0 fees. Only Player role transfers trigger the fee coalescing hierarchy and recalculation via PlayerFeeCalculator.

14. **Device record sync is integral to every transfer flow** — `DeviceTeams` and `DeviceRegistrationIds` records must be maintained during all four transfer flows to ensure push notifications follow the player/staff to their new team. Without this, coaches and players would stop receiving score notifications for their new team's games. A new `IDeviceRepository` is created (no device repository existed previously) with targeted methods for the specific CRUD operations each flow requires.

15. **Device records for Staff creation mirror the source Unassigned Adult's devices** — when creating a Staff registration from an Unassigned Adult, the new Staff reg inherits device links from the source. This means if a coach has the mobile app installed and follows teams, their new Staff assignment will automatically receive push notifications for the target team's games.

---

## 12. Special Cases — Unassigned Adult / Staff Pattern (BUSINESS CRITICAL)

### The Pattern

The Roster Swapper is NOT just a simple "update AssignedTeamId" tool. It implements a **registration cloning pattern** for coach/adult staff that enables one person to be assigned to multiple teams simultaneously:

```
Unassigned Adult (Role)     Staff (Role)
─────────────────────       ─────────────
Master template record.     Team-specific clone.
Never modified by swaps.    Created/deleted by swaps.
Lives in unassigned pool.   Lives on a specific team.
One per coach per job.      One per coach per team.
```

### Why This Exists

Coaches/staff frequently work with multiple teams (e.g., a head coach for U12 Gold who also assists U10 Silver). The Unassigned Adult acts as a "stamp" — each time admin assigns them to a team, a new Staff registration is created. This avoids the limitation of a single `AssignedTeamId` field per registration.

### The Four Transfer Flows

| Source | Target | Role | Action | Fee Impact |
|--------|--------|------|--------|------------|
| Team A | Team B | Player | UPDATE `AssignedTeamId` | Recalculate from target team's agegroup |
| Unassigned Pool | Team | Unassigned Adult | CREATE new Staff registration | Staff fees = $0 |
| Team | Unassigned Pool | Staff | DELETE Staff registration | N/A — master record preserved |
| Team A | Team B | Staff | UPDATE `AssignedTeamId` | None (staff fees = $0) |

### Fields Copied When Creating Staff Registration

When an Unassigned Adult is "assigned" to a team, a new Staff registration is created by copying these fields from the source:

**Copied from Unassigned Adult:**
- `UserId` — same person
- `FamilyUserId` — same family group
- `JobId` — same job

**Set by the swap operation:**
- `RegistrationId` — new `Guid.NewGuid()`
- `RoleId` — "Staff" role ID (NOT "Unassigned Adult")
- `AssignedTeamId` — target team
- `AssignedAgegroupId` — target team's agegroup
- `AssignedDivId` — target team's division
- `AssignedLeagueId` — target team's league
- `BActive` — true
- `LebUserId` — admin who performed the swap
- `Modified` — current timestamp
- All fee fields — $0 (staff don't pay registration fees)

**Device records created for new Staff registration:**
- `DeviceTeams` — for each device linked to the source Unassigned Adult (via `DeviceRegistrationIds`), a new `DeviceTeams` record is created mapping that device → target team with the new Staff `RegistrationId`
- `DeviceRegistrationIds` — for each device linked to the source Unassigned Adult, a new `DeviceRegistrationIds` record is created linking that device → the new Staff registration (Active=true)

### Guard: Duplicate Prevention

Before creating a Staff registration, the service must check if a Staff registration already exists for the same `UserId` on the target team. If so, the transfer is blocked with a descriptive error: "Coach [Name] is already assigned to [TeamName]."

### Guard: Role Validation

- Only "Unassigned Adult" role registrations can be cloned to create Staff
- Only "Staff" role registrations can be deleted (returned to pool)
- "Player" role registrations follow the standard AssignedTeamId update path
- Players CANNOT be moved to the Unassigned Adults pool (that pool is exclusively for coach/adult roles)

### Device Record Management (Push Notifications)

The mobile app uses push notifications to alert users about game score results for teams they follow. Three device tables maintain this mapping:

```
DeviceTeams          — Links a phone device to a team (for score push notifications)
  Id (Guid PK)
  DeviceId (string FK → Devices)
  TeamId (Guid FK → Teams)
  RegistrationId (Guid? FK → Registrations)  ← ties the device-team link to a specific registration
  Modified (DateTime)

DeviceRegistrationIds — Links a phone device to a registration (for general notifications)
  Id (Guid PK)
  DeviceId (string FK → Devices)
  RegistrationId (Guid FK → Registrations)
  Modified (DateTime)
  Active (bool)
```

**Each transfer flow must maintain device records:**

| Flow | Device Action |
|------|--------------|
| **Player → Team** (UPDATE) | Update `DeviceTeams.TeamId` on all DeviceTeams records matching the player's `RegistrationId` + old `TeamId` → set to new `TeamId`. The device follows the player to their new team. |
| **Unassigned Adult → Team** (CREATE Staff) | Create new `DeviceTeams` records: for each device linked to the source Unassigned Adult registration (via `DeviceRegistrationIds`), create a `DeviceTeams` entry mapping that device to the target team with the new Staff `RegistrationId`. Also create `DeviceRegistrationIds` entries for the new Staff registration. |
| **Staff → Unassigned pool** (DELETE Staff) | Delete all `DeviceTeams` records where `RegistrationId` = the deleted Staff registration. Delete all `DeviceRegistrationIds` records for the deleted Staff registration. (The Unassigned Adult's own device records are untouched.) |
| **Staff → Different Team** (UPDATE) | Update `DeviceTeams.TeamId` on all DeviceTeams records matching the Staff's `RegistrationId` + old `TeamId` → set to new `TeamId`. |

**Why this matters**: Without device record sync, a coach moved to a new team would stop receiving score notifications for their new team's games and continue receiving notifications for the old team's games.

### New Repository: IDeviceRepository

A new `IDeviceRepository` is required (no device repository currently exists):

```
GetDeviceTeamsByRegistrationAndTeamAsync(Guid registrationId, Guid teamId) → List<DeviceTeams>
    -- Tracked entities for update/delete
    -- Used in Flow 1 (Player), Flow 3 (Staff delete), Flow 4 (Staff move)

GetDeviceIdsByRegistrationAsync(Guid registrationId) → List<string>
    -- Returns device IDs linked to a registration via DeviceRegistrationIds
    -- AsNoTracking — used to discover which devices to link for new Staff regs

GetDeviceRegistrationIdsByRegistrationAsync(Guid registrationId) → List<DeviceRegistrationIds>
    -- Tracked entities for deletion in Flow 3

AddDeviceTeam(DeviceTeams entity) / AddDeviceRegistrationId(DeviceRegistrationIds entity)
    -- For creating new records in Flow 2

RemoveDeviceTeams(IEnumerable<DeviceTeams> entities)
RemoveDeviceRegistrationIds(IEnumerable<DeviceRegistrationIds> entities)
    -- For bulk deletion in Flow 3

SaveChangesAsync()
```

---

## 13. Amendments Log

| # | Change | Reason |
|---|--------|--------|
| 1 | Unassigned Adult / Staff pattern promoted from "deferred special case" to first-class implementation | Core business rule: coaches need multi-team assignment via registration cloning. Unassigned Adult = immutable template; "swapping" to a team CREATES a new Staff registration; "swapping" back DELETES the Staff registration. Enables one coach on N teams simultaneously. DTOs renamed from `SwapperTeamOptionDto` → `SwapperPoolOptionDto` to reflect that the dropdown includes both teams and the Unassigned Adults pool. Service `ExecuteTransferAsync` now handles 4 distinct flows based on role detection. Transfer result DTO extended with `StaffCreated` and `StaffDeleted` counts. Frontend `transferType` computed signal drives context-aware confirmation messaging. |
| 2 | Device record management added to all 4 transfer flows | Push notification integrity: `DeviceTeams` and `DeviceRegistrationIds` must be maintained during roster swaps so players/coaches continue receiving score notifications for the correct teams. New `IDeviceRepository` / `DeviceRepository` created (no device repo existed). Flow 1 (Player→Team): update DeviceTeams.TeamId. Flow 2 (Unassigned Adult→Team): create DeviceTeams + DeviceRegistrationIds for new Staff reg by mirroring source's device links. Flow 3 (Staff→Unassigned): delete all DeviceTeams + DeviceRegistrationIds for the Staff reg. Flow 4 (Staff→Team): update DeviceTeams.TeamId. DI registration added for IDeviceRepository. Service LOC estimate increased from ~350 to ~400. |

---

**Status**: Planning complete. Ready for implementation.
