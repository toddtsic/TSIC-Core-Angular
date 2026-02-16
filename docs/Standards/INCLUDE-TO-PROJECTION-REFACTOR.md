# `.Include()` → `.Select()` Projection Refactor Plan

## Executive Summary

The codebase has **~65 `.Include()` calls across 14 repositories**. Many return full entity graphs to service layers that only consume a subset of fields. This document categorizes every instance and recommends which to convert to `.Select()` projections, ordered by impact.

**Key principle:** `.Include()` generates `SELECT *` across all joined tables. `.Select()` projection generates a targeted `SELECT col1, col2, ...` — less data over the wire, less memory, faster queries.

---

## Current State

| Metric | Count |
|--------|-------|
| Repository files with `.Include()` | 14 |
| Total `.Include()` occurrences | ~65 |
| Already using `.Select()` projection | 7 methods (good examples) |
| Intentionally tracked (need entity for updates) | 10 methods — **leave as-is** |
| **Candidates for projection refactor** | **~19 methods** |

---

## Category A: Already Projecting (No Action Needed)

These methods already follow the preferred pattern — `.Include()` + `.Select()` or just `.Select()` with navigation access:

| Repository | Method | Notes |
|-----------|--------|-------|
| TeamRepository | `GetAvailableTeamsQueryResultsAsync` | Projects to `AvailableTeamQueryResult` |
| TeamRepository | `GetTeamFeeDataAsync` | Projects to `TeamFeeData` |
| TeamRepository | `GetRegisteredTeamsForPaymentAsync` | Projects to `RegisteredTeamInfo` |
| TeamRepository | `GetPoolAssignmentTeamsAsync` | Projects to anonymous type |
| DivisionRepository | `GetPoolAssignmentOptionsAsync` | Projects to anonymous type |
| ScheduleRepository | `GetContactsAsync` | Projects to `ContactDto` |
| FamiliesRepository | `GetEmailsForFamilyAndPlayersAsync` | Projects to `string` (email) |

> **Note:** Methods that use `.Include()` + `.Select()` can have the `.Include()` removed entirely — EF Core auto-joins when navigations are accessed inside `.Select()`.

---

## Category B: Tracked Methods — REQUIRES CLEANUP

These methods omit `.AsNoTracking()` — originally assumed to need change tracking. **Audit reveals 4 of 10 are purely read-only (bugs), 2 have simple scalar updates replaceable with `ExecuteUpdateAsync`, and only 4 legitimately need tracked entities.**

### B-Sub1: Missing `.AsNoTracking()` — Pure Read-Only (Bugs) ✅ DONE

Deep caller analysis revealed only 2 of the original 4 were truly read-only. The other 2 were reclassified to B-Sub3.

| Repository | Method | Fix Applied |
|-----------|--------|-------------|
| RegistrationRepository | `GetFamilyRegistrationsForPlayersAsync` | ✅ Added `.AsNoTracking()`, removed `.Include(r => r.AssignedTeam)` — callers only read scalar fields (UserId, AssignedTeamId, Modified) |
| ClubRepRepository | `GetClubRepForUserAndClubAsync` | ✅ Removed `.Include(cr => cr.Club)` — callers only do existence check or `Remove()`, never access `Club.ClubName` |

**Reclassified to B-Sub3 (callers DO modify entities via change tracking):**
- `TeamRepository.GetTeamsWithJobAndCustomerAsync` — `PaymentService.ProcessTeamPaymentAsync` and `TeamSearchService.ChargeCcInternalAsync` modify `PaidTotal`, `OwedTotal`, `FeeTotal`
- `TeamRepository.GetActiveClubTeamsOrderedByOwedAsync` — `TeamSearchService.RecordCheckOrCorrectionInternalAsync` modifies `FeeProcessing`, `OwedTotal`, `FeeTotal`, `PaidTotal`

### B-Sub2: Tracked Mutations That Over-Include ✅ DONE

| Repository | Method | Fix Applied |
|-----------|--------|-------------|
| AdministratorRepository | `GetByIdAsync` | ✅ Removed `.Include(r => r.User).Include(r => r.Role)` — now uses `FindAsync`. Added `GetAdminProjectionByIdAsync` for display. Callers updated. |
| RegistrationAccountingRepository | `GetByAIdAsync` | **Reclassified → B-Sub3.** One caller (`RegistrationSearchService.ProcessRefundAsync`) modifies `Registration.PaidTotal`/`OwedTotal` through the navigation — needs tracked Include. |

### B-Sub3: Complex Business Logic — Keep Tracked (With Narrower Includes)

These legitimately need tracked entities because they do complex in-memory fee calculations before saving. However, the **included navigation properties are only read, never modified** — so we can narrow the includes or split the read/write.

| Repository | Method | Included Navs | What's Modified | What Navs Are Used For |
|-----------|--------|---------------|-----------------|----------------------|
| TeamRepository | `GetTeamsWithDetailsForJobAsync` | `Job`, `Agegroup` | `FeeBase`, `FeeProcessing`, `FeeTotal`, `OwedTotal` | Fee calculation inputs (read-only) |
| TeamRepository | `GetTeamsForPoolTransferAsync` | `Agegroup`, `Div`, `Job` | `DivId`, `AgegroupId`, `DivRank`, `Active`, fees, audit | Division/agegroup context for pool transfer logic |
| TeamRepository | `GetTeamsWithJobAndCustomerAsync` | `Job.Customer` (2 levels) | `PaidTotal`, `OwedTotal`, `FeeTotal` | `Job.Customer.CustomerAi`, `Job.JobAi` for invoice number generation |
| TeamRepository | `GetActiveClubTeamsOrderedByOwedAsync` | `Agegroup`, `Job` | `FeeProcessing`, `OwedTotal`, `FeeTotal`, `PaidTotal` | `OwedTotal` for ordering/filtering |
| RegistrationRepository | `GetByJobAndFamilyWithUsersAsync` | `User` | ARB subscription fields, audit | `User.Email` for email sending |
| RegistrationRepository | `GetRegistrationsForTransferAsync` | `Role`, `User` | `AssignedTeamId`, `AssignedAgegroupId`, fees, audit | `Role.Name` for transfer flow branching |

**Action:** Keep tracked, but consider:
1. Loading the read-only context (Agegroup fees, Job settings) via a **separate lightweight query** with `.AsNoTracking()`, then loading only the entities being modified tracked
2. For `GetByJobAndFamilyWithUsersAsync`: the `User` include loads the full `AspNetUsers` table just for `Email` — consider a separate `Email` lookup

---

## Category C: Refactor Candidates (Ranked by Priority)

### Priority 1 — Hot Paths (Login / Dashboard / Every Page Load)

These are hit on nearly every user session. Maximum ROI for refactoring.

#### C1. `WidgetRepository.GetJobWidgetsAsync`
- **Includes:** `Widget`, `Category` (2 joins)
- **Current return:** `List<JobWidget>` with full Widget + Category entities
- **Consumer:** `WidgetDashboardService.GetDashboardAsync()`
- **Fields actually used:** `Widget.{Name, WidgetType, ComponentKey, Description}`, `Category.{Name, Icon, Section, DefaultOrder}`, `JobWidget.{DisplayOrder, Config, IsEnabled, WidgetId, CategoryId}`
- **Wasted columns:** `Widget.CategoryId`, all `Category` collection navs, `JobWidget.{JobId, RoleId, Job, Role}` nav props
- **Recommendation:** Project to a new `JobWidgetProjection` record, or restructure so the service `.Select()` maps directly to `WidgetItemDto`
- **Effort:** Low — single consumer, straightforward mapping

#### C2. `WidgetRepository.GetDefaultsAsync`
- **Includes:** `Widget`, `Category` (2 joins)
- **Consumer:** `WidgetDashboardService.GetDashboardAsync()` (same consumer)
- **Fields actually used:** Same subset as C1 but from `WidgetDefault` instead of `JobWidget`
- **Recommendation:** Same projection pattern as C1
- **Effort:** Low

#### C3. `RegistrationRepository.GetByUserIdAsync`
- **Includes:** Conditional — `Job`, `Job.JobDisplayOptions`, `Role` (up to 3 joins)
- **Consumer:** Auth flow — `AuthService`, `RegistrationLookupService`
- **Fields actually used:** Varies by flags, but typically `Job.{JobName, ExpiryAdmin, JobPath}`, `Role.{Name, Id}`, `JobDisplayOptions` (only when specifically requested)
- **Recommendation:** Split into separate methods or use conditional projection. The `JobDisplayOptions` include is especially wasteful — it's a wide table (20+ columns) used only for the public-facing landing page.
- **Effort:** Medium — conditional logic complicates projection

#### C4. `MenuRepository.GetAllMenusForJobAsync`
- **Includes:** `Role` (1 join)
- **Consumer:** Menu building — hit on every navigation
- **Fields actually used:** `Role.Name` (just one field from the entire `AspNetRoles` table)
- **Recommendation:** Replace with `.Select()` that pulls `RoleName = m.Role.Name` — trivial refactor, good savings since `AspNetRoles` has many columns
- **Effort:** Very Low

---

### Priority 2 — Medium Frequency (Feature Pages)

#### C5. `ScheduleRepository.GetFilteredGamesAsync`
- **Includes:** `Field`, `Agegroup` (2 joins)
- **Consumer:** `ScheduleDivisionService` → schedule grid views
- **Fields actually used:** `Field.FName`, `Agegroup.AgegroupName` + game columns
- **Wasted columns:** Full `Field` entity (address, coordinates, etc.), full `Agegroup` entity
- **Recommendation:** Project to a `ScheduleGameDto` with only needed fields
- **Effort:** Medium — complex query with date filtering, but mapping is straightforward

#### C6. `ScheduleRepository.GetTeamGamesAsync`
- **Includes:** `Field` (1 join)
- **Consumer:** Team schedule view
- **Fields actually used:** `Field.FName` + game columns
- **Recommendation:** Project to DTO with `FieldName` flattened in
- **Effort:** Low

#### C7. `ScheduleRepository.GetBracketGamesAsync`
- **Includes:** `Field` (1 join)
- **Consumer:** Bracket display
- **Fields actually used:** `Field.FName` + bracket game columns
- **Recommendation:** Same pattern as C6
- **Effort:** Low

#### C8. `ScheduleRepository.GetReschedulerGridAsync`
- **Includes:** `Agegroup` (1 join)
- **Consumer:** Admin rescheduler tool
- **Fields actually used:** `Agegroup.AgegroupName` + schedule columns
- **Recommendation:** Project to rescheduler DTO
- **Effort:** Low

#### C9. `LeagueRepository.GetLeaguesByJobIdAsync`
- **Includes:** `Sport` (1 join)
- **Consumer:** League listing
- **Fields actually used:** `Sport.SportName` (one field)
- **Recommendation:** Trivial — add `SportName = l.Sport.SportName` to projection, drop Include
- **Effort:** Very Low

#### C10. `LeagueRepository.GetByIdWithSportAsync`
- **Includes:** `Sport` (1 join)
- **Consumer:** League detail view
- **Fields actually used:** `Sport.SportName`
- **Recommendation:** Same as C9
- **Effort:** Very Low

#### C11. `AdministratorRepository.GetByJobIdAsync`
- **Includes:** `User`, `Role` (2 joins)
- **Consumer:** Admin list display
- **Fields actually used:** `User.{FirstName, LastName, Email}`, `Role.Name`
- **Wasted columns:** Full `AspNetUsers` (password hashes, security stamps, etc.), full `AspNetRoles`
- **Recommendation:** Project to `AdministratorListItemDto` — significant savings since Identity tables are wide
- **Effort:** Low

---

### Priority 3 — Lower Frequency / Smaller Payloads

#### C12. `TimeslotRepository.GetDatesAsync`
- **Includes:** `Div` (1 join)
- **Fields actually used:** `Div` for ordering
- **Effort:** Low

#### C13. `TimeslotRepository.GetFieldTimeslotsAsync`
- **Includes:** `Field`, `Div` (2 joins)
- **Fields actually used:** `Field.FName`, `Div.DivName`
- **Effort:** Low

#### C14. `FieldRepository.GetLeagueSeasonFieldsAsync`
- **Includes:** `Field` (1 join)
- **Fields actually used:** `Field.FName` for display
- **Effort:** Very Low

#### C15. `PairingsRepository.GetAgegroupsWithDivisionsAsync`
- **Includes:** `Divisions` (collection navigation)
- **Fields actually used:** Hierarchical tree — most Division fields needed
- **Recommendation:** Lower priority — collection includes are harder to project and the entity is relatively narrow
- **Effort:** Medium

#### C16. `FamilyRepository.GetFamilyRegistrationsForJobAsync` (both overloads)
- **Includes:** `User` (+ `Job` in second overload)
- **Fields actually used:** User demographic fields for family profile
- **Effort:** Low–Medium

#### C17. `ClubRepRepository.GetClubsForUserAsync`
- **Includes:** `Club` (1 join)
- **Fields actually used:** `Club.ClubName`
- **Recommendation:** Trivial — project `ClubName` as string
- **Effort:** Very Low

#### C18. `ProfileMetadataRepository.GetRegistrationWithJobAsync`
- **Includes:** `Job` (1 join)
- **Fields actually used:** `Job.{JobPath, Season, Year}` subset
- **Effort:** Low

---

### Priority 4 — High Risk / High Effort

#### C19. `RegistrationRepository.GetRegistrationDetailAsync`
- **Includes:** `User`, `Role`, `AssignedTeam`, `Job`, `FamilyUser`, `RegistrationAccounting.ThenInclude(PaymentMethod)` — **6 joins, 3 levels deep**
- **Consumer:** Registration detail view — complex DTO assembly
- **Fields actually used:** Extensive — builds `RegistrationDetailDto` with profile values, accounting records, family contact info
- **Recommendation:** This is the biggest entity graph in the codebase. Refactoring requires careful DTO design to flatten the 3-level hierarchy. Recommend tackling this last, after patterns are established from simpler refactors.
- **Effort:** High

---

## Bonus: Remove Redundant `.Include()` from Category A

Methods that already use `.Select()` projection don't need `.Include()` at all — EF Core generates the JOINs automatically when navigations are accessed inside `.Select()`:

```csharp
// BEFORE — Include is redundant when Select is used
.Include(t => t.Agegroup)
.Include(t => t.Div)
.Select(t => new AvailableTeamQueryResult {
    AgegroupName = t.Agegroup.AgegroupName,
    DivName = t.Div.DivName,
    ...
})

// AFTER — cleaner, same SQL generated
.Select(t => new AvailableTeamQueryResult {
    AgegroupName = t.Agegroup.AgegroupName,
    DivName = t.Div!.DivName,
    ...
})
```

**Applies to:** All 7 methods in Category A. Quick cleanup.

---

## Implementation Strategy

### Recommended Approach: Inside-Out by Priority

1. **Phase 0 (Quick Wins):** Remove redundant `.Include()` from Category A methods that already project. Zero-risk, immediate cleanup.

2. **Phase 1a (Bugs — B-Sub1):** Add `.AsNoTracking()` + projections to the 4 methods that are purely read-only but missing it. These are correctness fixes, not just performance.

3. **Phase 1b (Hot Paths — C1–C4):** Refactor Widget, Menu, Registration auth. Hit on every session — biggest performance improvement per method.

4. **Phase 2 (ExecuteUpdate — B-Sub2):** Convert 2 simple scalar-update methods to `ExecuteUpdateAsync`. Stops loading `AspNetUsers` password hashes just to flip a boolean.

5. **Phase 3 (Schedule + League — C5–C11):** Medium frequency, straightforward pattern.

6. **Phase 4 (Long Tail — C12–C18):** Lower impact but establishes consistency.

7. **Phase 5 (Narrow Tracked Includes — B-Sub3):** Split read-only context from tracked entity loading for 4 complex business logic methods.

8. **Phase 6 (Registration Detail — C19):** Tackle last — highest complexity, needs careful DTO design.

### Per-Method Refactor Pattern

For each method:

1. **Create a projection DTO** (if one doesn't exist) in `TSIC.Contracts/Dtos/[Feature]/`:
   ```csharp
   public record ScheduleGameProjection
   {
       public required int ScheduleId { get; init; }
       public required string FieldName { get; init; }
       // ... only fields the consumer actually uses
   }
   ```

2. **Replace `.Include()` + `.ToListAsync()` with `.Select()` + `.ToListAsync()`:**
   ```csharp
   // Before
   .Include(s => s.Field)
   .Where(...)
   .ToListAsync(ct);

   // After
   .Where(...)
   .Select(s => new ScheduleGameProjection {
       ScheduleId = s.ScheduleId,
       FieldName = s.Field.FName,
       ...
   })
   .ToListAsync(ct);
   ```

3. **Update the repository interface** return type from `List<Entity>` to `List<ProjectionDto>`.

4. **Update service callers** to use the DTO properties instead of navigating entity relationships.

5. **Update the repository interface** in `TSIC.Contracts/Interfaces/`.

---

## Estimated Impact

| Priority | Methods | Avg Includes | Tables Avoided | Session Frequency |
|----------|---------|-------------|----------------|-------------------|
| P1 (Hot) | 4 | 2.3 | Identity, JobDisplayOptions, Widget, Category | Every login |
| P2 (Med) | 7 | 1.4 | Field, Agegroup, Sport, Identity | Feature page loads |
| P3 (Low) | 7 | 1.3 | Various | Occasional |
| P4 (Complex) | 1 | 6 | 6 tables, 3 levels | Admin detail view |

**Biggest single wins:**
- **C3 (GetByUserIdAsync):** `JobDisplayOptions` alone is a 20+ column table loaded on auth
- **C11 (GetByJobIdAsync):** `AspNetUsers` carries password hashes, security stamps — heavy and sensitive columns loaded just to display a name
- **C19 (GetRegistrationDetailAsync):** 6-table join; most impactful but highest effort

---

## Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Breaking change to service layer | Update callers at the same time; search for all `.Property` access on returned entities |
| Missing fields in projection | Trace consumer access thoroughly before creating DTO |
| EF Core query translation issues | Some expressions don't translate to SQL in `.Select()` — test each query |
| Collection navigations (C15) | `.Select()` with nested collections requires careful sub-query design |
| Conditional includes (C3) | May need multiple methods or a flexible projection approach |

---

## Decision Points — Resolved

| # | Question | Decision |
|---|----------|----------|
| 1 | DTO granularity | **Shared where shapes overlap.** Reuse existing response DTOs when flat and matching; create shared `*Projection` types otherwise. |
| 2 | Phase 0 — do it now? | **Yes.** Remove redundant `.Include()` from Category A immediately. |
| 3 | Naming convention | **`[Entity]Projection`** for query-layer types. Distinct from `*Dto` (API response shapes). |
| 4 | Conditional includes (C3) | **Split into separate methods.** e.g., `GetWithJobAsync`, `GetWithRoleAsync`. Each gets a tight projection. |
| 5 | Category B methods | **Yes — fix now.** Audit revealed 4 are pure read-only bugs, 2 can use `ExecuteUpdateAsync`, 4 need narrower includes. Phased into 1a/2/5. |
