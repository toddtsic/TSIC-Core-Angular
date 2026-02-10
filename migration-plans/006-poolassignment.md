# Migration Plan: TeamPoolAssignment/Index → Team Pool Assignment

## Context

The legacy `TeamPoolAssignment/Index` page is an admin tool for moving teams between age groups and divisions (pools). Admins select a source division and a target division, see both division's team rosters side by side in jqGrid tables, then click a "Swap" button on any team row to reassign that team to the other division. Moving a team has cascading financial impacts: the team's fees must be recalculated based on the target agegroup's fee structure, and the team's club rep's financial totals must be re-synchronized. **Critical constraint**: a team's agegroup and division must be consistent — a team cannot belong to one agegroup's division while being assigned to a different agegroup. When moving between divisions of different agegroups, BOTH `AgegroupId` AND `DivId` change atomically.

**Legacy URL**: `/TeamPoolAssignment/Index` (Controller=TeamPoolAssignment, Action=Index)

---

## 1. Legacy Strengths (Preserve These!)

- **Side-by-side division comparison** — see both source and target division's teams simultaneously
- **One-click transfer** — single swap button per team, immediate AJAX round-trip
- **Team detail in grid** — Active status, TeamName, Club, ClubRep, LevelOfPlay, RegDate, Comments, DivRank
- **Dropdowns grouped by AgeGroup** — divisions organized under their parent agegroup
- **DivRank editing** — inline rank editing with dynamic dropdown populated from current division
- **Active status toggle** — inline toggle per team row
- **Team name editing** — inline edit with immediate server persist
- **Automatic fee recalculation** — team fees updated to match target agegroup's fee structure
- **Auto-deactivation on drop** — moving to "Dropped Teams" division automatically sets `Active=false`
- **Club rep financial sync** — after any move, club rep's aggregated financial totals are recalculated
- **Division rank renumbering** — both source and target divisions re-rank teams after swap

## 2. Legacy Pain Points (Fix These!)

- **jqGrid dependency** — dated look, heavy jQuery, poor mobile experience
- **No multi-select** — can only move one team at a time; moving an entire club's teams = N clicks
- **No fee impact preview** — team is moved first, fees recalculated silently; admin has no visibility into fee changes (team fees AND club rep totals) BEFORE confirming
- **No capacity indicators** — no visual cue for how many teams are in a division vs the agegroup's `MaxTeams` limit
- **DivRank dropdown via separate AJAX call** — requires extra server round-trip just to populate rank options
- **Full page state loss** — dropdowns reset if page is refreshed mid-workflow
- **Anti-forgery token plumbing** — boilerplate in every AJAX call
- **No undo** — accidental swaps require re-swapping manually
- **No batch "move all from club" option** — common use case (club drops out, move all their teams) requires N individual swaps
- **Team comments via plain text** — no formatting, hard to read in grid cell

## 3. Modern Vision

**Recommended UI: Dual-Panel Transfer Layout with Agegroup-Aware Selectors**

Same dual-panel pattern as the Roster Swapper (005), but adapted for the team/division/agegroup context. The key difference: the selector is **Agegroup > Division** (not just team), because moving between divisions of different agegroups triggers fee recalculation.

```
┌──────────────────────────────────────────────────────────────────────┐
│  Team Pool Assignment                                        [⟳]    │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌─ SOURCE ─────────────────────┐   ┌─ TARGET ─────────────────────┐│
│  │ Pool: [▼ U12 Boys / Gold    ]│   │ Pool: [▼ U14 Boys / Silver  ]││
│  │       6/8 teams  ██████░░░░  │   │       3/8 teams  ███░░░░░░░  ││
│  │ 🔍 [Filter teams...       ] │   │ 🔍 [Filter teams...       ]  ││
│  │                              │   │                               ││
│  │ ☐  #  Team         Club  LOP │   │  #  Team         Club  LOP ☐ ││
│  │ ☐  1  Storm FC     ABC   A  →│   │← 1  Thunder FC  XYZ   B   ☐ ││
│  │ ☐  2  Lightning    ABC   A  →│   │← 2  Cyclones    DEF   B   ☐ ││
│  │ ☐  3  Wildcats     DEF   B  →│   │← 3  Falcons     GHI   A   ☐ ││
│  │ ...                          │   │ ...                           ││
│  │                              │   │                               ││
│  │ [Move Selected →]            │   │           [← Move Selected]  ││
│  └──────────────────────────────┘   └───────────────────────────────┘│
│                                                                      │
│  ┌─ Fee Impact Preview (appears when agegroups differ) ────────────┐│
│  │  ⚠ Moving 2 teams from U12 Boys / Gold → U14 Boys / Silver     ││
│  │     Agegroup change detected — fees will be recalculated         ││
│  │                                                                   ││
│  │  Team          Current TeamFee  New TeamFee   Delta               ││
│  │  Storm FC      $500.00          $650.00       +$150.00            ││
│  │  Lightning     $500.00          $650.00       +$150.00            ││
│  │                                                                   ││
│  │  Club Rep Impact:                                                 ││
│  │  ABC Club Rep:  $1,000.00 → $1,300.00  (+$300.00)               ││
│  │                                                                   ││
│  │  [Cancel]                                    [Confirm Transfer]  ││
│  └───────────────────────────────────────────────────────────────────┘│
│                                                                      │
│  ── OR if moving within same agegroup: ──                            │
│                                                                      │
│  ┌─ Transfer Confirmation ─────────────────────────────────────────┐│
│  │  Moving 1 team from U12 Boys / Gold → U12 Boys / Silver         ││
│  │  ✓ Same agegroup — no fee changes                                ││
│  │                                                                   ││
│  │  [Cancel]                                    [Confirm Transfer]  ││
│  └───────────────────────────────────────────────────────────────────┘│
│                                                                      │
│  ── OR if source team has scheduled games (symmetrical swap): ──     │
│                                                                      │
│  ┌─ Symmetrical Swap Required ───────────────────────────────────┐  │
│  │  ⚠ Storm FC has scheduled games — a symmetrical swap is        │  │
│  │    required. Select a team from the target division to swap.   │  │
│  │                                                                 │  │
│  │  Source → Target:  Storm FC       →  U14 Boys / Silver         │  │
│  │  Target → Source:  Thunder FC     →  U12 Boys / Gold           │  │
│  │                                                                 │  │
│  │  Fee Impact (cross-agegroup):                                   │  │
│  │  Storm FC:     $500.00 → $650.00  (+$150.00)                   │  │
│  │  Thunder FC:   $650.00 → $500.00  (-$150.00)                   │  │
│  │                                                                 │  │
│  │  [Cancel]                              [Confirm Swap]          │  │
│  └─────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────┘
```

**Why same dual-panel approach as Roster Swapper:**

The mental model is identical — admin compares two pools, selects items, and transfers between them. Using the same layout pattern across both tools means:
1. Zero new UX learning curve if admin has used Roster Swapper
2. Shared component patterns reduce development time
3. Consistent admin experience across all "swapper" tools

**Key differences from Roster Swapper (005):**

| Aspect | Roster Swapper | Pool Assignment |
|--------|---------------|-----------------|
| **Selector** | Team dropdown | Agegroup > Division dropdown |
| **Items** | Player registrations | Teams |
| **Fee trigger** | Always recalculates (different team = potentially different fee) | Only recalculates when agegroup changes |
| **Secondary impact** | None | Club rep financial sync |
| **Auto-deactivation** | No | Yes — moving to "Dropped Teams" division sets `Active=false` |
| **Rank management** | N/A | DivRank renumbered in both source and target divisions |
| **Inline editing** | Active toggle only | Active toggle, TeamName, DivRank |

**Key improvements over legacy:**
- ✅ **Multi-select transfer** — check multiple teams, move them all in one operation
- ✅ **Fee impact preview** — shows old fee → new fee per team AND club rep totals BEFORE confirming. Only shown when agegroups differ (same-agegroup moves skip fee preview since fees don't change).
- ✅ **Capacity bar** — visual indicator showing team count / MaxTeams for the agegroup, color-coded
- ✅ **Inline search/filter** — quickly find teams by name or club in large divisions
- ✅ **Grouped division dropdown** — divisions organized under Agegroup headers using `<optgroup>`
- ✅ **"Dropped Teams" visual distinction** — "Dropped Teams" division in dropdown shows with warning badge; moving there triggers deactivation warning in confirmation
- ✅ **Responsive layout** — panels stack vertically on mobile (< 768px)
- ✅ **No page reloads** — signal-driven state, preserves dropdown selections
- ✅ **Toast feedback** — success/error toast with team count and fee summary
- ✅ **DivRank auto-assignment** — moved teams get next available rank in target division (no manual rank entry needed)

## 4. User Value

- **Faster workflows**: Multi-select moves entire clubs in 1 click instead of N
- **Fewer errors**: Fee preview prevents surprise billing changes; deactivation warning prevents accidental team drops
- **Better awareness**: Capacity bars show division fill level; fee impact shown before confirming
- **Transparency**: Admin sees fee impact to BOTH team AND club rep before confirming
- **Consistency**: Same dual-panel UX as Roster Swapper — no new patterns to learn

## 5. Design Alignment

- Bootstrap tables + CSS variables (all 8 palettes)
- Signal-based state, OnPush change detection
- Toast notifications via existing `ToastService`
- `ConfirmDialogComponent` for transfer confirmation (with fee impact detail)
- Shared layout pattern with Roster Swapper (005)
- WCAG AA compliant (keyboard-navigable multi-select, ARIA labels, focus management)

## 6. UI Standards Created / Employed

### CREATED (new patterns this module introduces)
- **Agegroup-Division Grouped Selector** — `<optgroup>` dropdown where groups are agegroups and options are divisions, with team counts per division. "Dropped Teams" division shown with warning icon.
- **Conditional Fee Impact Panel** — fee preview only shown when source and target agegroups differ (since same-agegroup moves don't change fees). Includes both team-level AND club rep-level impact.
- **Club Rep Impact Summary** — within fee preview, shows aggregated impact on club rep's financial totals (sum of all their teams' fees after the move).
- **Deactivation Warning** — when target is the "Dropped Teams" division, confirmation dialog shows explicit deactivation warning: "Teams moved here will be deactivated (Active=false)."
- **Inline Team Property Editing** — Active toggle + TeamName edit within the transfer panel table (preserves legacy's inline editing capability without jqGrid).

### EMPLOYED (existing patterns reused)
- **Dual-Panel Transfer Layout** (from 005-rosterswapper) — same grid layout, capacity bars, filter inputs, multi-select
- **Capacity Progress Bar** (from 005-rosterswapper) — same color thresholds, applied to agegroup MaxTeams
- Signal-based state management
- CSS variable design system tokens (all colors, spacing, borders)
- `@if` / `@for` template syntax
- OnPush change detection
- `inject()` dependency injection
- Repository pattern (TeamRepository, AgeGroupRepository, DivisionRepository, RegistrationRepository)
- `ConfirmDialogComponent` for transfer confirmation
- `ToastService` for success/error feedback
- `FormsModule` with `[(ngModel)]` for dropdowns/filters

---

## 7. Security Requirements

**CRITICAL**: All endpoints must derive `jobId` from JWT claims, NOT from route parameters.

- **Route**: `/:jobPath/admin/pool-assignment` (jobPath for routing only)
- **API Endpoints**: Must use `ClaimsPrincipalExtensions.GetJobIdFromRegistrationAsync()` to derive `jobId` from the authenticated user's `regId` claim
- **NO route parameters containing sensitive IDs**: All `[Authorize]` endpoints must extract job context from JWT token
- **Policy**: `[Authorize(Policy = "AdminOnly")]` — Directors, SuperDirectors, and Superusers can reassign team pools
- **Validation**: Server must verify that both source and target divisions belong to the user's job before any transfer
- **Validation**: Server must verify that each team belongs to the source division before moving

---

## 8. Database Entities (Existing — No Schema Changes)

### Key Entities Involved:

**Teams** (entity being moved):
- `TeamId` (Guid, PK) — the team
- `AgegroupId` (Guid, FK) — **updated when agegroup changes**
- `DivId` (Guid, FK) — **always updated** (this is the primary move)
- `LeagueId` (Guid, FK) — unchanged (teams stay in same league)
- `DivRank` (int) — **reset to next available rank** in target division
- `Active` (bool) — **set to false** if target is "Dropped Teams" division
- `FeeBase`, `FeeProcessing`, `FeeTotal`, `OwedTotal`, `PaidTotal` — **recalculated if agegroup changes**
- `ClubrepRegistrationid` (Guid) — used to identify club rep for financial sync
- `TeamName`, `LevelOfPlay`, `TeamComments`

**Divisions** (source and target containers):
- `DivId` (Guid, PK)
- `AgegroupId` (Guid, FK) — determines which agegroup the division belongs to
- `DivName` (string)

**Agegroups** (fee source of truth):
- `AgegroupId` (Guid, PK)
- `TeamFee` (decimal) — base team fee
- `RosterFee` (decimal) — per-player roster fee
- `AgegroupName`, `MaxTeams`, `MaxTeamsPerClub`

**Registrations** (club rep — financial sync target):
- `RegistrationId` (Guid, PK) — club rep's registration
- `FeeBase`, `FeeProcessing`, `FeeTotal`, `OwedTotal`, `PaidTotal` — **recalculated as SUM of all their active teams' fees**

### Fee Recalculation Logic (Only When Agegroup Changes):

When a team moves to a division in a **different agegroup**, team fees are recalculated:

```
Team.FeeBase = recalculated via TeamFeeCalculator using:
    - NewAgegroup.RosterFee
    - NewAgegroup.TeamFee
    - Job.BTeamsFullPaymentRequired
    - Job.BAddProcessingFees
    - Job.BApplyProcessingFeesToTeamDeposit
    - Job.ProcessingFeePercent

Team.FeeProcessing = recalculated based on above
Team.FeeTotal = FeeBase + FeeProcessing - FeeDiscount
Team.OwedTotal = FeeTotal - PaidTotal
```

**Club Rep Financial Sync** (after ANY move, regardless of agegroup change):
```
ClubRepReg.FeeBase = SUM(Team.FeeBase WHERE Team.ClubrepRegistrationid = ClubRepReg.RegistrationId AND Team.Active = 1)
ClubRepReg.FeeProcessing = SUM(Team.FeeProcessing ...)
ClubRepReg.FeeTotal = SUM(Team.FeeTotal ...)
ClubRepReg.OwedTotal = ClubRepReg.FeeTotal - ClubRepReg.PaidTotal
// ... all 8 fee fields synchronized
```

Uses existing `TeamFeeCalculator` for team fee computation and `IRegistrationRepository.SynchronizeClubRepFinancialsAsync()` for club rep sync.

### "Dropped Teams" Auto-Deactivation:

When target division belongs to an agegroup with name containing "DROPPED" (case-insensitive):
1. `Team.Active = false`
2. Fees recalculated to $0 (or based on Dropped agegroup's fee structure — typically $0)
3. Club rep financials re-synced (team's fees now $0, reducing club rep totals)

---

## 9. Implementation Steps

### Phase 1: Backend — DTOs

**Status**: [ ] Pending

**File to create**:
- `TSIC.Contracts/Dtos/PoolAssignment/PoolAssignmentDtos.cs`

**DTOs**:
```csharp
// Division dropdown option (grouped by agegroup)
public record PoolOptionDto
{
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public required int TeamCount { get; init; }
    public required int MaxTeams { get; init; }
    public required bool IsDroppedTeams { get; init; }
}

// Team row in division table
public record PoolTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required bool Active { get; init; }
    public required int DivRank { get; init; }
    public required bool HasScheduledGames { get; init; }   // true if team appears in Schedule table — drives symmetrical swap enforcement
    public string? ClubName { get; init; }
    public string? ClubRepName { get; init; }
    public string? LevelOfPlay { get; init; }
    public string? TeamComments { get; init; }
    public decimal FeeBase { get; init; }
    public decimal FeeProcessing { get; init; }
    public decimal FeeTotal { get; init; }
    public decimal OwedTotal { get; init; }
    public int RosterCount { get; init; }
    public Guid? ClubrepRegistrationId { get; init; }
    public DateTime? RegistrationTs { get; init; }
}

// Transfer request (supports multi-select + symmetrical swap)
public record PoolTransferRequest
{
    public required List<Guid> TeamIds { get; init; }       // Source teams moving → target division
    public required Guid SourceDivId { get; init; }
    public required Guid TargetDivId { get; init; }
    public List<Guid>? SwapTeamIds { get; init; }           // Target teams moving → source division (symmetrical swap)
                                                             // Required when any source team has scheduled games
}

// Fee impact preview per team (only for cross-agegroup moves)
public record PoolTransferFeePreviewDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required decimal CurrentFeeBase { get; init; }
    public required decimal CurrentFeeTotal { get; init; }
    public required decimal NewFeeBase { get; init; }
    public required decimal NewFeeTotal { get; init; }
    public required decimal FeeDelta { get; init; }
    public required bool WillBeDeactivated { get; init; }
}

// Club rep impact summary
public record ClubRepImpactDto
{
    public required string ClubRepName { get; init; }
    public required Guid ClubrepRegistrationId { get; init; }
    public required decimal CurrentFeeTotal { get; init; }
    public required decimal NewFeeTotal { get; init; }
    public required decimal FeeDelta { get; init; }
}

// Preview response
public record PoolTransferPreviewResponseDto
{
    public required bool AgegroupChanged { get; init; }
    public required string SourceAgegroupName { get; init; }
    public required string TargetAgegroupName { get; init; }
    public required bool TargetIsDropped { get; init; }
    public required bool RequiresSymmetricalSwap { get; init; }  // true if any source teams have scheduled games
    public required List<Guid> ScheduledTeamIds { get; init; }   // which source teams have games (for UI highlighting)
    public required List<PoolTransferFeePreviewDto> TeamPreviews { get; init; }
    public required List<ClubRepImpactDto> ClubRepImpacts { get; init; }
}

// Fee preview request
public record PoolTransferPreviewRequest
{
    public required List<Guid> TeamIds { get; init; }       // Source teams
    public required Guid SourceDivId { get; init; }
    public required Guid TargetDivId { get; init; }
    public List<Guid>? SwapTeamIds { get; init; }           // Target teams (for symmetrical swap preview)
}

// Transfer result
public record PoolTransferResultDto
{
    public required int TeamsTransferred { get; init; }       // Total teams moved (both directions in symmetrical swap)
    public required int TeamsSwapped { get; init; }           // Teams moved in reverse direction (0 if one-directional)
    public required int FeesRecalculated { get; init; }
    public required int ClubRepsUpdated { get; init; }
    public required int SchedulesSynced { get; init; }        // Schedule records with updated T1Name/T2Name
    public required bool AnyDeactivated { get; init; }
    public required string Message { get; init; }
}

// Inline team property update
public record UpdatePoolTeamRequest
{
    public string? TeamName { get; init; }
    public bool? Active { get; init; }
    public int? DivRank { get; init; }
}
```

### Phase 2: Backend — Repository Extensions

**Status**: [ ] Pending

**Files to modify**:
- `TSIC.Contracts/Repositories/ITeamRepository.cs` (add methods)
- `TSIC.Infrastructure/Repositories/TeamRepository.cs` (implement)
- `TSIC.Contracts/Repositories/IDivisionRepository.cs` (add methods)
- `TSIC.Infrastructure/Repositories/DivisionRepository.cs` (implement)
- `TSIC.Contracts/Repositories/IScheduleRepository.cs` (add methods)
- `TSIC.Infrastructure/Repositories/ScheduleRepository.cs` (implement)

**New ITeamRepository methods**:
```
GetTeamsByDivisionForPoolAsync(Guid divId, Guid jobId) → List<PoolTeamDto>
    -- Returns all teams in division with club name, club rep name, roster count
    -- Joins: Teams → ClubrepRegistration → User (for club rep name)
    --        Teams → ClubTeam → Club (for club name)
    --        Subquery: COUNT(Registrations) for roster count
    -- AsNoTracking

GetTeamsForTransferAsync(List<Guid> teamIds, Guid divId) → List<Teams>
    -- Returns tracked teams for bulk update (validates each belongs to source division)
    -- Tracked entities

GetMaxDivRankInDivisionAsync(Guid divId) → int
    -- Returns highest DivRank in division (for assigning next rank to moved teams)
    -- AsNoTracking

RenumberDivRanksAsync(Guid divId) → void
    -- Renumbers DivRank sequentially (1, 2, 3...) for all active teams in division
    -- Ordered by current DivRank
    -- Tracked update + SaveChangesAsync
```

**New IDivisionRepository methods**:
```
GetPoolOptionsAsync(Guid jobId) → List<PoolOptionDto>
    -- Returns all divisions for job with agegroup names, team counts, MaxTeams
    -- Joins: Divisions → Agegroups → Leagues → JobLeagues
    -- Includes "IsDroppedTeams" flag (agegroup name contains "DROPPED")
    -- Ordered by AgegroupName, DivName
    -- AsNoTracking

GetDivisionWithAgegroupAsync(Guid divId) → (Divisions div, Agegroups agegroup)?
    -- Returns division + parent agegroup (for fee context)
    -- AsNoTracking
```

**New IScheduleRepository methods** (extend existing — already has `SynchronizeScheduleNamesForTeamAsync`):
```
HasScheduledGamesAsync(List<Guid> teamIds, Guid jobId) → Dictionary<Guid, bool>
    -- For each teamId, check if Schedule records exist where (T1Id = teamId OR T2Id = teamId) AND JobId = jobId
    -- Returns map of teamId → hasGames
    -- AsNoTracking

UpdateScheduleAgegroupDivForTeamAsync(Guid teamId, Guid newAgegroupId, string newAgegroupName,
                                       Guid newDivId, string newDivName, Guid jobId, CancellationToken ct = default)
    -- Finds all Schedule records where (T1Id = teamId OR T2Id = teamId) AND JobId = jobId
    -- Updates AgegroupId, AgegroupName, DivId, DivName to the new values
    -- Tracked entities + SaveChangesAsync
    -- Only called for cross-agegroup moves
```

**Note**: `SynchronizeScheduleNamesForTeamAsync(Guid teamId, Guid jobId)` already exists and handles T1Name/T2Name sync for round-robin games. No changes needed to that method.

### Phase 3: Backend — Service

**Status**: [ ] Pending

**Files to create**:
- `TSIC.Contracts/Services/IPoolAssignmentService.cs`
- `TSIC.API/Services/Admin/PoolAssignmentService.cs`

**Dependencies**:
- `ITeamRepository`
- `IDivisionRepository`
- `IAgeGroupRepository`
- `IRegistrationRepository` (for `SynchronizeClubRepFinancialsAsync`)
- `IScheduleRepository` (for `SynchronizeScheduleNamesForTeamAsync` + new `UpdateScheduleAgegroupDivForTeamAsync`)
- `IJobRepository` (for processing fee settings)
- `TeamFeeCalculator` (existing — for team fee computation)

**Methods**:

```
GetPoolOptionsAsync(Guid jobId) → List<PoolOptionDto>
    -- Delegates to DivisionRepository
    -- Returns divisions organized for dropdown with agegroup grouping

GetPoolTeamsAsync(Guid divId, Guid jobId) → List<PoolTeamDto>
    -- Loads teams for selected division
    -- Validates division belongs to job

PreviewTransferAsync(Guid jobId, PoolTransferPreviewRequest request) → PoolTransferPreviewResponseDto
    -- Determines if agegroups differ between source and target divisions
    -- Checks if any source teams have scheduled games (HasScheduledGamesAsync)
    -- If any teams are scheduled AND no target teams provided → return error/flag: RequiresSymmetricalSwap=true
    -- If same agegroup:
    --   Returns AgegroupChanged=false, empty fee previews, no club rep impacts
    -- If different agegroup:
    --   For each team:
    --     1. Look up current fees from team entity
    --     2. Look up target agegroup's fee structure
    --     3. Compute new fees via TeamFeeCalculator
    --     4. Compute delta
    --     5. Check if target is "Dropped Teams" (WillBeDeactivated)
    --   For each affected club rep:
    --     1. Calculate current total from all their active teams
    --     2. Calculate projected total after removing moved teams' old fees and adding new fees
    --     3. Compute delta
    -- Returns preview WITHOUT persisting any changes

ExecuteTransferAsync(Guid jobId, string userId, PoolTransferRequest request) → PoolTransferResultDto
    -- Validates:
    --   1. Source and target divisions belong to job
    --   2. All source teams belong to source division
    --   3. Source ≠ Target
    --   4. If any source teams have scheduled games: SwapTeamIds must be provided (symmetrical swap)
    --      and all swap teams must belong to target division, count must match source team count
    --   5. (No MaxTeams enforcement — admin may intentionally exceed for tournament brackets)
    --
    -- FORWARD DIRECTION (source → target): For each source team:
    --   1. Update DivId → target division
    --   2. Update AgegroupId → target division's agegroup (if different)
    --   3. DivRank → next available in target division (maxRank + 1, incrementing)
    --   4. If target agegroup differs: recalculate fees via TeamFeeCalculator
    --   5. If target is "Dropped Teams": set Active = false
    --   6. Update Modified, LebUserId
    --
    -- REVERSE DIRECTION (target → source, symmetrical swap only): For each swap team:
    --   1. Update DivId → source division
    --   2. Update AgegroupId → source division's agegroup (if different)
    --   3. DivRank → next available in source division
    --   4. If source agegroup differs from swap team's current: recalculate fees via TeamFeeCalculator
    --   5. Update Modified, LebUserId
    --
    -- SaveChangesAsync (single transaction — all moves atomic)
    -- Renumber DivRanks in both source and target divisions (close gaps)
    --
    -- Post-transfer sync (all moved teams — both directions):
    -- For each distinct ClubrepRegistrationId among ALL moved teams:
    --   Call SynchronizeClubRepFinancialsAsync
    -- For each moved team that has scheduled games:
    --   Call SynchronizeScheduleNamesForTeamAsync(teamId, jobId)
    --   If agegroup changed: Call UpdateScheduleAgegroupDivForTeamAsync(teamId, newAgegroupId, newAgegroupName, newDivId, newDivName, jobId)
    --
    -- Returns result with counts + summary

UpdatePoolTeamAsync(Guid teamId, Guid jobId, UpdatePoolTeamRequest request, string userId) → PoolTeamDto
    -- Validates team belongs to job
    -- Updates provided fields (TeamName, Active, DivRank)
    -- If DivRank changed: renumber ranks in division
    -- Update Modified, LebUserId
    -- Returns updated PoolTeamDto
```

### Phase 4: Backend — Controller

**Status**: [ ] Pending

**File to create**:
- `TSIC.API/Controllers/PoolAssignmentController.cs`

**Endpoints**:
- `GET api/pool-assignment/pools` → `List<PoolOptionDto>` (all divisions for dropdown)
- `GET api/pool-assignment/teams/{divId:guid}` → `List<PoolTeamDto>` (teams in division)
- `POST api/pool-assignment/preview` → `PoolTransferPreviewResponseDto` (fee impact preview + schedule detection, body: `PoolTransferPreviewRequest`)
- `POST api/pool-assignment/transfer` → `PoolTransferResultDto` (execute transfer — supports symmetrical swap, body: `PoolTransferRequest`)
- `PUT api/pool-assignment/teams/{teamId:guid}` → `PoolTeamDto` (inline edit team properties, body: `UpdatePoolTeamRequest`)

**Authorization**: All endpoints `[Authorize(Policy = "AdminOnly")]`, derive `jobId` from JWT via `GetJobIdFromRegistrationAsync()`.

### Phase 5: Backend — DI Registration

**Status**: [ ] Pending

**File to modify**:
- `TSIC.API/Program.cs`

**Add registration**:
```csharp
builder.Services.AddScoped<IPoolAssignmentService, PoolAssignmentService>();
```

### Phase 6: Frontend — Service

**Status**: [ ] Pending

**File to create**:
- `src/app/views/admin/pool-assignment/services/pool-assignment.service.ts`

**Methods** (all return Observables):
- `getPoolOptions(): Observable<PoolOptionDto[]>`
- `getPoolTeams(divId: string): Observable<PoolTeamDto[]>`
- `previewTransfer(request: PoolTransferPreviewRequest): Observable<PoolTransferPreviewResponseDto>`
- `executeTransfer(request: PoolTransferRequest): Observable<PoolTransferResultDto>`
- `updateTeam(teamId: string, request: UpdatePoolTeamRequest): Observable<PoolTeamDto>`

### Phase 7: Frontend — Pool Assignment Component

**Status**: [ ] Pending

**Files to create**:
- `src/app/views/admin/pool-assignment/pool-assignment.component.ts`
- `src/app/views/admin/pool-assignment/pool-assignment.component.html`
- `src/app/views/admin/pool-assignment/pool-assignment.component.scss`

**Component state** (signals):
```typescript
// Pool options
poolOptions = signal<PoolOptionDto[]>([]);
groupedPoolOptions = computed(() => groupByAgegroup(this.poolOptions()));

// Source panel
sourceDivId = signal<string | null>(null);
sourceTeams = signal<PoolTeamDto[]>([]);
sourceSelected = signal<Set<string>>(new Set());
sourceFilter = signal('');
filteredSourceTeams = computed(() => filterTeams(this.sourceTeams(), this.sourceFilter()));

// Target panel
targetDivId = signal<string | null>(null);
targetTeams = signal<PoolTeamDto[]>([]);
targetSelected = signal<Set<string>>(new Set());
targetFilter = signal('');
filteredTargetTeams = computed(() => filterTeams(this.targetTeams(), this.targetFilter()));

// Transfer state
transferPreview = signal<PoolTransferPreviewResponseDto | null>(null);
isTransferring = signal(false);
isLoadingPreview = signal(false);
transferDirection = signal<'source-to-target' | 'target-to-source'>('source-to-target');

// Schedule-aware symmetrical swap state
requiresSymmetricalSwap = computed(() => this.transferPreview()?.requiresSymmetricalSwap ?? false);
scheduledTeamIds = computed(() => new Set(this.transferPreview()?.scheduledTeamIds ?? []));
// When symmetrical swap is required, admin must also select teams from the target panel
symmetricalSwapReady = computed(() => {
  if (!this.requiresSymmetricalSwap()) return true; // no constraint
  return this.targetSelected().size > 0 && this.targetSelected().size === this.sourceSelected().size;
});

// Inline editing
editingTeamId = signal<string | null>(null);
editingField = signal<string | null>(null);

// General
isLoading = signal(false);
```

**Component methods**:
- `loadPoolOptions()` — fetch all divisions for dropdowns
- `onSourcePoolChange(divId)` — load source teams, clear selection
- `onTargetPoolChange(divId)` — load target teams, clear selection
- `toggleSourceSelect(teamId)` / `toggleTargetSelect(teamId)` — toggle team selection
- `selectAllSource()` / `deselectAllSource()` — bulk select
- `moveSourceToTarget()` — preview fees/impacts + schedule detection, show confirmation panel. If `requiresSymmetricalSwap`, prompt admin to also select target teams before confirming.
- `moveTargetToSource()` — preview fees (reverse direction) + same schedule detection
- `confirmTransfer()` — execute transfer (includes SwapTeamIds if symmetrical swap), reload both panels, show toast with schedule sync summary
- `cancelTransfer()` — clear preview
- `startEdit(teamId, field)` / `saveEdit(teamId)` / `cancelEdit()` — inline editing
- `toggleActive(teamId, active)` — toggle team active status

**Layout** (same dual-panel structure as Roster Swapper):

Reuses the same responsive grid layout with these differences:
1. Dropdown shows divisions grouped by agegroup (not teams grouped by agegroup/division)
2. Table columns: `#`, `Active`, `Team Name`, `Club`, `Club Rep`, `LOP`, `Roster`, `Fee`, `Owed`
3. Fee preview panel shows club rep impact in addition to team-level impact
4. "Dropped Teams" division option shows with warning styling

**CSS** (extends Roster Swapper patterns):
```scss
// Reuses .swapper-panels, .swapper-panel, .panel-header, .panel-body, .panel-footer patterns
// from 005-rosterswapper via shared admin swapper styles or copy

.pool-assignment-container {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  height: calc(100vh - 200px);
}

// Dropped teams visual distinction
.pool-option-dropped {
  color: var(--bs-danger);
  font-style: italic;
}

// Inline editing
.inline-edit-input {
  width: 100%;
  padding: var(--space-1);
  border: 1px solid var(--bs-primary);
  border-radius: var(--radius-sm);
  background: var(--bs-body-bg);
  color: var(--bs-body-color);
  font-size: inherit;
}

// Club rep impact section in fee preview
.club-rep-impact {
  margin-top: var(--space-3);
  padding-top: var(--space-3);
  border-top: 1px solid var(--bs-border-color);
}

// Same responsive breakpoint as Roster Swapper
@media (max-width: 767.98px) {
  .pool-panels {
    grid-template-columns: 1fr;
  }
  .pool-panel {
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
  path: 'admin/pool-assignment',
  canActivate: [authGuard],
  data: { requirePhase2: true, requiresPolicy: 'AdminOnly' },
  loadComponent: () => import('./views/admin/pool-assignment/pool-assignment.component')
    .then(m => m.PoolAssignmentComponent)
}
// Legacy-compatible route
{
  path: 'teampoolassignment/index',
  canActivate: [authGuard],
  data: { requirePhase2: true, requiresPolicy: 'AdminOnly' },
  loadComponent: () => import('./views/admin/pool-assignment/pool-assignment.component')
    .then(m => m.PoolAssignmentComponent)
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
**Pool selection & display:**
1. **Pool dropdown**: All divisions appear grouped by Agegroup, team counts shown, "Dropped Teams" styled distinctly
2. **Load teams**: Selecting a division loads its teams with club info, fees, roster counts
3. **Multi-select**: Check individual teams, select all, deselect all

**Standard transfers (no scheduled games):**
4. **Same-agegroup transfer**: No fee preview (just simple confirmation), DivId updated, DivRank reassigned
5. **Cross-agegroup transfer**: Fee preview shown with per-team fee impact + club rep impact, requires confirmation
6. **"Dropped Teams" transfer**: Deactivation warning in confirmation, teams set Active=false after transfer

**Schedule-aware symmetrical swap:**
7. **Scheduled team detection**: Preview response includes `requiresSymmetricalSwap=true` and highlights which teams have games
8. **Swap enforcement**: Cannot confirm transfer of scheduled teams without selecting equal count of target teams
9. **Symmetrical swap execution**: Both directions execute atomically — source teams move to target div, target teams move to source div
10. **Schedule name sync**: After swap, `SynchronizeScheduleNamesForTeamAsync` updates T1Name/T2Name for each moved team
11. **Schedule agegroup/div sync**: After cross-agegroup swap, `UpdateScheduleAgegroupDivForTeamAsync` updates denormalized AgegroupId/AgegroupName/DivId/DivName on Schedule records
12. **Mixed scheduled/unscheduled**: If any selected source teams are scheduled, entire batch requires symmetrical swap
13. **Unscheduled teams free move**: Teams with no scheduled games can be moved freely (one-directional, no swap required)

**Post-transfer sync:**
14. **DivRank management**: Moved teams get next available rank; both source and target division gaps closed via renumbering
15. **Club rep sync**: After transfer, club rep's FeeBase/FeeTotal/OwedTotal recalculated as SUM of active teams

**General:**
16. **Validation**: Cannot select same division as source and target
17. **Inline editing**: TeamName editable via click-to-edit, Active toggle via switch, DivRank via dropdown
18. **Filter**: Typing in filter box narrows team list by name or club (case-insensitive)
19. **Responsive**: Panels stack vertically on mobile, each scrollable independently
20. **Error handling**: Network errors show toast with retry option
21. **All 8 palettes**: CSS variable themed throughout
22. **Edge cases**: Empty division, single team, division at MaxTeams capacity, team with scheduled games but no games in opposite panel's division

---

## 10. Files Summary

### Backend Files

| File | Action | LOC (est.) |
|------|--------|------------|
| `TSIC.Contracts/Dtos/PoolAssignment/PoolAssignmentDtos.cs` | Create | ~120 |
| `TSIC.Contracts/Repositories/ITeamRepository.cs` | Edit (add methods) | +15 |
| `TSIC.Infrastructure/Repositories/TeamRepository.cs` | Edit (implement) | +60 |
| `TSIC.Contracts/Repositories/IDivisionRepository.cs` | Edit (add methods) | +10 |
| `TSIC.Infrastructure/Repositories/DivisionRepository.cs` | Edit (implement) | +40 |
| `TSIC.Contracts/Repositories/IScheduleRepository.cs` | Edit (add 2 methods) | +15 |
| `TSIC.Infrastructure/Repositories/ScheduleRepository.cs` | Edit (implement) | +50 |
| `TSIC.Contracts/Services/IPoolAssignmentService.cs` | Create | ~20 |
| `TSIC.API/Services/Admin/PoolAssignmentService.cs` | Create | ~380 |
| `TSIC.API/Controllers/PoolAssignmentController.cs` | Create | ~100 |
| `TSIC.API/Program.cs` | Edit (1 DI line) | +1 |

### Frontend Files

| File | Action | LOC (est.) |
|------|--------|------------|
| `views/admin/pool-assignment/services/pool-assignment.service.ts` | Create | ~45 |
| `views/admin/pool-assignment/pool-assignment.component.ts` | Create | ~280 |
| `views/admin/pool-assignment/pool-assignment.component.html` | Create | ~220 |
| `views/admin/pool-assignment/pool-assignment.component.scss` | Create | ~130 |
| `app.routes.ts` | Edit (2 routes) | +12 |
| `core/api/models/` (auto-generated) | Auto | ~8 files |

---

## 11. Key Design Decisions

1. **Same dual-panel layout as Roster Swapper** — consistency between the two "swapper" admin tools. Admins learn one pattern and apply it to both player transfers and team transfers. Same responsive behavior, same multi-select, same capacity bars.

2. **Conditional fee preview** — only shown when source and target agegroups differ. Same-agegroup moves (e.g., moving a team from Pool A → Pool B within U12 Boys) don't change fees, so showing a fee preview would be confusing noise. Cross-agegroup moves ALWAYS show the preview because fee changes are guaranteed.

3. **Club rep impact in fee preview** — the legacy system updated club rep financials silently. The new design makes this explicit: admin sees "ABC Club Rep: $1,000 → $1,300 (+$300)" before confirming. This transparency prevents billing disputes.

4. **No MaxTeams enforcement on transfer** — unlike roster MaxCount (which is a hard capacity limit), agegroup MaxTeams is a soft guideline. Tournament admins frequently exceed it for bracket play. The capacity bar provides visual awareness, but the transfer is not blocked.

5. **"Dropped Teams" as a first-class concept** — instead of hiding the deactivation behavior (legacy auto-set Active=false), the new design: (a) visually distinguishes "Dropped Teams" in the dropdown, (b) shows a deactivation warning in the confirmation panel, (c) explicitly labels the preview as "Will be deactivated". No surprises.

6. **DivRank auto-assignment** — legacy required manual rank editing via a separate AJAX-populated dropdown. New design: moved teams automatically get the next available rank. Source division gaps are closed via renumbering. Admin can fine-tune ranks after the move via inline editing.

7. **Inline editing preserved** — legacy's inline TeamName and Active editing in the grid was useful. Preserved as click-to-edit pattern (not jqGrid's double-click) with explicit save/cancel.

8. **Single transaction for batch transfers** — all team updates + all club rep syncs within one `SaveChangesAsync()` scope. If validation fails for any team, no teams are moved.

9. **Service reuses existing fee calculators** — `TeamFeeCalculator` is the single source of truth for team fee computation. `SynchronizeClubRepFinancialsAsync()` is the single source of truth for club rep financial aggregation. No duplication.

10. **Authorization: AdminOnly** — matches legacy's authorization. Directors and SuperDirectors need this tool regularly during tournament setup when reorganizing brackets and pools.

11. **Symmetrical swap enforcement for scheduled teams** — a team with scheduled games cannot be freely moved to another division without a replacement team taking its slot. This prevents orphaned schedule records and maintains bracket integrity. The UI detects scheduled teams during preview and prompts the admin to select equal numbers of teams from both panels. This matches real-world practice where admins perform symmetrical swaps.

12. **Schedule sync uses existing `SynchronizeScheduleNamesForTeamAsync`** — the existing single-source-of-truth method in `IScheduleRepository` correctly handles T1Name/T2Name updates for round-robin games after a team moves. For cross-agegroup moves, a new companion method `UpdateScheduleAgegroupDivForTeamAsync` updates the denormalized `AgegroupId`, `AgegroupName`, `DivId`, `DivName` fields on Schedule records. Both methods are called post-transfer for each moved team.

13. **Schedule denormalized fields are updated, not schedule structure** — moving a team between divisions does NOT delete or reassign games. The `T1Id`/`T2Id` foreign keys still point to the same team entities — only the denormalized display names and agegroup/division metadata are refreshed. The game structure remains intact, which is exactly what admins expect in a symmetrical swap scenario.

---

## 12. Relationship to Roster Swapper (005)

These two tools are conceptual siblings — both use the dual-panel transfer pattern for admin reassignment operations. They share:

| Shared Concept | Roster Swapper | Pool Assignment |
|---|---|---|
| Layout | Dual-panel grid, 1fr 1fr | Same |
| Selector | `<optgroup>` dropdown | Same (different grouping) |
| Capacity bar | current/max with color thresholds | Same |
| Multi-select | Checkbox per row + select all | Same |
| Filter | Text search within panel | Same |
| Fee preview | Always shown | Conditional (only cross-agegroup) |
| Confirmation | Dialog with fee detail | Dialog with fee + club rep detail |
| Responsive | Stack on mobile | Same |

**Potential shared component extraction** (defer to implementation):
If the dual-panel layout, capacity bar, and multi-select table patterns prove sufficiently similar during implementation, consider extracting a shared `SwapperPanelComponent` that both tools compose. However, don't prematurely abstract — implement both independently first, then extract if the duplication is egregious.

---

## 13. Special Cases

### Schedule-Aware Transfer — Symmetrical Swap Enforcement

Teams that have scheduled games (`Schedule` records where `T1Id` or `T2Id` references the team) **cannot be freely moved** between divisions — they require a **symmetrical swap**. A team from the target division must take the moving team's place in the source division. This preserves schedule integrity: game records' `T1Id`/`T2Id` still point to valid teams, and the existing `SynchronizeScheduleNamesForTeamAsync` method correctly updates the denormalized `T1Name`/`T2Name` after the swap.

**Business rule**: This is done in practice — admins perform symmetrical swaps when scheduled teams need to change divisions.

#### Detection

The service must check whether any selected teams have scheduled games:
```
HasScheduledGamesAsync(List<Guid> teamIds, Guid jobId) → Dictionary<Guid, bool>
    -- For each teamId, check if any Schedule records exist where
    --   (T1Id = teamId OR T2Id = teamId) AND JobId = jobId
    -- Returns map of teamId → hasGames
```

#### Enforcement Rules

| Source Teams Scheduled? | Target Teams Selected? | Allowed? | Behavior |
|---|---|---|---|
| None scheduled | N/A | Yes | Free one-directional move (existing behavior) |
| Some scheduled | No target teams selected | **Blocked** | UI shows: "These teams have scheduled games. Select teams from the target division to swap with." |
| Some scheduled | Target teams selected (equal count) | Yes | Symmetrical swap — source teams move to target div, target teams move to source div |
| Some scheduled | Target teams selected (unequal count) | **Blocked** | UI shows: "Scheduled teams require a 1-for-1 swap. Select equal numbers of teams from each panel." |

**Important**: Only the scheduled teams enforce the symmetrical swap constraint. If an admin selects a mix of scheduled and unscheduled teams, the entire batch must follow the stricter rule (symmetrical swap) for simplicity.

#### Frontend UX

When the admin clicks "Move Selected →" and any source teams have scheduled games:
1. The fee preview / confirmation panel shows an additional banner: **"⚠ Scheduled teams detected — symmetrical swap required"**
2. The target panel's "← Move Selected" button is highlighted, prompting the admin to select target teams
3. The confirmation dialog shows both sides of the swap:
   ```
   Symmetrical Swap:
     Source → Target:  Storm FC, Lightning  →  U14 Boys / Silver
     Target → Source:  Thunder FC, Cyclones  →  U12 Boys / Gold
   ```
4. Both transfers execute atomically in a single transaction

#### Schedule Sync After Transfer

After any team transfer (whether symmetrical swap or free move), the service must update the `Schedule` table:

**Step 1: Call `SynchronizeScheduleNamesForTeamAsync` for each moved team**
- This updates `T1Name` / `T2Name` on all round-robin games (T1Type/T2Type == "T") for the moved team
- Uses the existing single-source-of-truth method in `IScheduleRepository`
- Composes display name from `Teams.TeamName` + `Registrations.ClubName` + `Jobs.BShowTeamNameOnlyInSchedules`

**Step 2: Update denormalized agegroup/division fields on Schedule records (cross-agegroup moves only)**
When a team moves to a division in a different agegroup, the `Schedule` records referencing that team have stale `AgegroupId`, `AgegroupName`, `DivId`, `DivName`. These must be updated:

```
UpdateScheduleAgegroupDivForTeamAsync(Guid teamId, Guid newAgegroupId, string newAgegroupName,
                                       Guid newDivId, string newDivName, Guid jobId)
    -- Find all Schedule records where (T1Id = teamId OR T2Id = teamId) AND JobId = jobId
    -- Update AgegroupId, AgegroupName, DivId, DivName to the new values
    -- NOTE: In a symmetrical swap, each team's schedule records get updated to their respective
    --   new agegroup/div. Since T1Id and T2Id point to specific teams (not to agegroups),
    --   each game record may end up with the agegroup of whichever team "owns" that schedule slot.
    --   This matches legacy behavior where Schedule.AgegroupId follows the game's primary agegroup context.
    -- SaveChangesAsync
```

**Add to `IScheduleRepository`**: `UpdateScheduleAgegroupDivForTeamAsync(...)` — new method alongside existing `SynchronizeScheduleNamesForTeamAsync`.

### "Unassigned" Division
The LADT editor (004) established that every agegroup has an "Unassigned" division. Teams in the "Unassigned" division are teams not yet assigned to a pool/bracket. The Pool Assignment tool should treat "Unassigned" the same as any other division — teams can be moved into and out of it freely. **Unassigned teams typically have no scheduled games**, so the symmetrical swap constraint would not apply to them.

---

## 14. Amendments Log

| # | Change | Reason |
|---|--------|--------|
| 1 | Replaced "Schedule Impact — Deferred" with full schedule-aware implementation | Schedule integrity is critical: teams with scheduled games require symmetrical swaps to prevent orphaned game records. Added `HasScheduledGamesAsync` for detection, `UpdateScheduleAgegroupDivForTeamAsync` for denormalized field sync, and reuse of existing `SynchronizeScheduleNamesForTeamAsync` for T1Name/T2Name sync. DTOs extended: `PoolTransferRequest.SwapTeamIds`, `PoolTransferPreviewResponseDto.RequiresSymmetricalSwap/ScheduledTeamIds`, `PoolTransferResultDto.TeamsSwapped/SchedulesSynced`. Frontend adds `requiresSymmetricalSwap` computed signal and enforces equal team selection from both panels. IScheduleRepository extended with 2 new methods. Service LOC estimate increased from ~280 to ~380. |

---

**Status**: Planning complete. Ready for implementation.
