# `.Include()` → `.Select()` Projection Refactor Plan

## Executive Summary

The codebase had **~65 `.Include()` calls across 14 repositories**. Many returned full entity graphs to service layers that only consumed a subset of fields. This document categorizes every instance and tracks which were converted to `.Select()` projections.

**Key principle:** `.Include()` generates `SELECT *` across all joined tables. `.Select()` projection generates a targeted `SELECT col1, col2, ...` — less data over the wire, less memory, faster queries.

---

## Completion Status (2026-02-16)

| Phase | Status | Methods Refactored | Notes |
|-------|--------|-------------------|-------|
| Phase 0 (Cat A cleanup) | **DONE** | 7 methods | Removed redundant `.Include()` from methods already using `.Select()` |
| Phase 1a (B-Sub1 bugs) | **DONE** | 2 methods | Added `AsNoTracking()`, removed unnecessary `.Include()` |
| Phase 1b (C1-C4 hot paths) | **DONE** | 3 methods (C1, C2, C4) | Widget + Menu projections. C3 already had conditional includes — left as-is |
| Phase 2 (B-Sub2) | **DONE** | 1 method | `AdministratorRepository.GetByIdAsync` split into `FindAsync` + display projection |
| Phase 3 (C9-C11) | **DONE** | 3 methods | League + Admin list projections |
| Phase 4 (C12-C18) | **DONE** | 6 of 7 methods | C15 skipped (collection nav, narrow tables, low ROI) |
| Phase 5 (B-Sub3) | **DEFERRED** | 0 | Tracked mutations — infrequent ops (payment, pool transfer), splitting read/write adds complexity with minimal gain |
| Phase 6 (C19) | **DEFERRED** | 0 | Single-entity detail view with reflection-based 50+ field extraction — projection infeasible |
| C5-C8 (Schedule) | **DEFERRED** | 0 | Denormalized Schedule entity — wide table but `.Include()` only adds FName/AgegroupName |

**Total `.Include()` calls eliminated: ~28 across 22 methods**
**Highest-value wins:** Eliminated `AspNetUsers` password hash loading (C11, C16, C18), `JobDisplayOptions` wide table (C1-C2), identity table joins for single-field lookups (C4, C9-C10, C14, C17)

---

## Current State

| Metric | Count |
|--------|-------|
| Repository files with `.Include()` | 14 |
| Total `.Include()` occurrences (original) | ~65 |
| `.Include()` removed via projection | ~28 |
| Already using `.Select()` projection | 7 methods (good examples) |
| Intentionally tracked (need entity for updates) | 10 methods — **leave as-is** |
| Deferred (low ROI / high complexity) | ~9 methods |

---

## Category A: Already Projecting — `.Include()` Removed ✅ DONE

These methods already followed the preferred pattern — `.Include()` + `.Select()`. The redundant `.Include()` calls were removed (Phase 0):

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

#### C5–C8. `ScheduleRepository` methods — DEFERRED (low ROI)
- **Methods:** `GetFilteredGamesAsync`, `GetTeamGamesAsync`, `GetBracketGamesAsync`, `GetReschedulerGridAsync`
- **Includes:** `Field` and/or `Agegroup` (1-2 joins each)
- **Rationale for deferral:** Schedule entity is denormalized (wide table), `.Include()` only adds FName/AgegroupName. The Schedule table itself dominates the SELECT width. Converting to projection yields minimal savings.

#### C9. `LeagueRepository.GetLeaguesByJobIdAsync` ✅ DONE
- **Refactored to:** `.Select()` projection with `SportName = l.Sport.SportName`
- Eliminated full `Sports` entity load for a single field

#### C10. `LeagueRepository.GetByIdWithSportAsync` ✅ DONE
- **Refactored to:** `.Select()` projection with `SportName` flattened
- Same pattern as C9

#### C11. `AdministratorRepository.GetByJobIdAsync` ✅ DONE
- **Refactored to:** `List<AdministratorListItemDto>` projection
- **High value:** Eliminated `AspNetUsers` password hash + security stamp loading, full `AspNetRoles` loading — now selects only `FirstName`, `LastName`, `Email`, `Role.Name`

---

### Priority 3 — Lower Frequency / Smaller Payloads

#### C12. `TimeslotRepository.GetDatesAsync` ✅ DONE
- **Refactored to:** `List<TimeslotDateDto>` projection with `DivName = d.Div.DivName`
- Callers (`TimeslotService`, `ScheduleDivisionService`) updated to use DTO properties

#### C13. `TimeslotRepository.GetFieldTimeslotsAsync` ✅ DONE
- **Refactored to:** `List<TimeslotFieldDto>` projection with `FieldName`, `DivName` flattened
- `ScheduleDivisionService.FindNextAvailableTimeslot` parameter types updated entity → DTO

#### C14. `FieldRepository.GetLeagueSeasonFieldsAsync` ✅ DONE
- **Refactored to:** `List<LeagueSeasonFieldDto>` projection (reused existing DTO)
- `FieldManagementService` simplified — no more manual `.Select()` mapping

#### C15. `PairingsRepository.GetAgegroupsWithDivisionsAsync` — SKIPPED
- Collection navigation + narrow tables → low ROI, medium complexity

#### C16. `FamilyRepository.GetFamilyRegistrationsForJobAsync` ✅ DONE
- **Refactored to:** `GetFamilyPlayerEmailsForJobAsync` returning `List<string>` (emails only)
- **High value:** Eliminated full `AspNetUsers` loading (password hashes!) for email-only lookup
- Both overloads (jobId and jobPath) converted

#### C17. `ClubRepRepository.GetClubsForUserAsync` ✅ DONE
- **Refactored to:** Internal `.Select()` projection (`ClubId`, `ClubName`)
- No interface change needed — already returned `List<ClubWithUsageInfo>`

#### C18. `ProfileMetadataRepository.GetRegistrationWithJobAsync` ✅ DONE
- **Refactored to:** `GetJobDataForRegistrationAsync` returning `RegistrationJobProjection`
- Projects only 5 Job fields needed across all 8 callers
- All 8 callers in `ProfileMetadataMigrationService` updated

---

### Priority 4 — High Risk / High Effort — DEFERRED

#### C19. `RegistrationRepository.GetRegistrationDetailAsync` — DEFERRED
- **Includes:** `User`, `Role`, `AssignedTeam`, `Job`, `FamilyUser`, `RegistrationAccounting.ThenInclude(PaymentMethod)` — **7 navigation loads, 8 tables**
- **Rationale for deferral:** Single-entity load (`FirstOrDefaultAsync`) — absolute query overhead is negligible. Method uses **reflection to extract 50+ dynamic profile properties** from the entity, making projection infeasible. Already uses `AsNoTracking()`.

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

## Implementation Strategy (Completed)

### Execution Log

1. **Phase 0 (Quick Wins):** ✅ Removed redundant `.Include()` from 7 Category A methods.
2. **Phase 1a (Bugs — B-Sub1):** ✅ Fixed 2 read-only methods (added `AsNoTracking()`, removed unnecessary includes).
3. **Phase 1b (Hot Paths — C1–C4):** ✅ Refactored Widget (C1, C2) and Menu (C4) to projections. C3 left as-is (conditional includes already optimized).
4. **Phase 2 (B-Sub2):** ✅ Split `AdministratorRepository.GetByIdAsync` into `FindAsync` + `GetAdminProjectionByIdAsync`.
5. **Phase 3 (C9–C11):** ✅ League + Admin list projections. C5–C8 Schedule methods deferred (denormalized entity, low ROI).
6. **Phase 4 (C12–C18):** ✅ 6 of 7 refactored. C15 skipped (collection navigation, narrow tables).
7. **Phase 5 (B-Sub3):** ⏭️ Deferred — tracked mutations used in infrequent operations (payment, pool transfer). Splitting read/write queries adds complexity with minimal performance gain.
8. **Phase 6 (C19):** ⏭️ Deferred — single-entity load with reflection-based 50+ field extraction. Projection is infeasible.

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
