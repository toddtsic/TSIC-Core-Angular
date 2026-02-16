# Testing Plan: LADT Editor

## Context

The LADT Editor is **one of the most heavily used and critical** administrative tools in TSIC. It manages the 4-level hierarchy of Leagues → Age Groups → Divisions → Teams with 25+ API endpoints, conditional business logic (drop vs delete), fee cascading, club reassignment, and batch operations. A bug here can mean lost payment history, orphaned teams, or corrupted hierarchy data.

This testing plan focuses on **tests that protect data integrity and user journeys** — not ceremonial coverage. Every test listed here guards against a specific, realistic failure mode.

---

## Philosophy: What NOT to Test

- ❌ That Angular renders templates (framework's job)
- ❌ That `signal.set(x)` makes `signal()` return `x` (framework's job)
- ❌ That auto-generated API models have correct properties (generator's job)
- ❌ That Bootstrap icons render (CSS's job)
- ❌ That `ngModel` binds a text field (framework's job)
- ❌ Detail form field-by-field rendering (if a label is wrong, you'll see it immediately)
- ❌ Sibling grid column definitions (static config, not logic)
- ❌ CSS/responsive layout correctness (visual, not logical)

**Guiding principle**: If a test would pass even with a serious data bug, it's not worth writing.

---

## Layer 1: Backend Integration Tests (HIGHEST VALUE — Start Here)

### Why This Layer Matters Most

The backend enforces data integrity. If `DropTeam` silently hard-deletes a team with $2,000 in payment history, no amount of frontend testing catches that. The repository pattern makes these tests clean: seed data → call endpoint → assert database state.

### Setup

- Use `WebApplicationFactory<Program>` with a test database (or in-memory SQLite)
- Call real HTTP endpoints through `HttpClient`
- Assert on **database state**, not just HTTP status codes
- Seed realistic data per test (teams with/without payments, agegroups with/without teams)

### Test Cases

#### 1.1 Team Drop — Smart Delete Logic (CRITICAL)

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 1 | Drop team WITH payment history | Team + RegistrationAccounting records | `POST /api/ladt/teams/{id}/drop` | Team still exists, `isActive=false`, moved to "Dropped Teams" division, payment records intact |
| 2 | Drop team WITHOUT history | Team with zero registrations | `POST /api/ladt/teams/{id}/drop` | Team hard-deleted from DB, no orphan records |
| 3 | Drop team WITH player registrations but no payments | Team + Registration records (no accounting) | `POST /api/ladt/teams/{id}/drop` | Verify correct behavior (drop vs delete — confirm which rule applies) |
| 4 | Drop already-dropped team | Inactive team in "Dropped Teams" | `POST /api/ladt/teams/{id}/drop` | Idempotent or rejected gracefully (not duplicated) |

```csharp
[Fact]
public async Task DropTeam_WithPaymentHistory_MovesToDroppedTeams()
{
    // Arrange: seed team with payment record
    // Act: POST /api/ladt/teams/{id}/drop
    // Assert: team exists, isActive=false, parentDiv="Dropped Teams"
    // Assert: RegistrationAccounting records intact
}
```

#### 1.2 Delete Guards — Referential Integrity

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 5 | Delete agegroup WITH teams | Agegroup → Division → 3 Teams | `DELETE /api/ladt/agegroups/{id}` | 400/409 rejected, agegroup still exists |
| 6 | Delete agegroup WITHOUT teams | Empty agegroup (divisions only, no teams) | `DELETE /api/ladt/agegroups/{id}` | 200, agegroup removed, child divisions cascade-deleted |
| 7 | Delete "UNASSIGNED" division | Division with name "UNASSIGNED" | `DELETE /api/ladt/divisions/{id}` | Rejected (system-protected entity) |
| 8 | Delete regular division WITH teams | Division → 2 Teams | `DELETE /api/ladt/divisions/{id}` | 400/409 rejected |
| 9 | Delete regular division WITHOUT teams | Empty division | `DELETE /api/ladt/divisions/{id}` | 200, division removed |

#### 1.3 Fee Cascading — Financial Accuracy

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 10 | Push agegroup fees to players | Agegroup (fee=$100) → Div → Team → 5 Registrations (fee=$80 each) | `POST /api/ladt/batch/update-fees/{agId}` | All 5 registrations updated to $100, return count=5 |
| 11 | Push fees — no registrations | Agegroup with no downstream registrations | `POST /api/ladt/batch/update-fees/{agId}` | Return count=0, no errors |
| 12 | Push fees — mixed states | Some registrations already at correct fee | `POST /api/ladt/batch/update-fees/{agId}` | Only stale records updated, count reflects actual changes |

#### 1.4 Club Team Reassignment

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 13 | Move single team to new club | Team owned by Club A | `POST /api/ladt/teams/{id}/change-club` (single) | Team.clubId = Club B, Club A's other teams unaffected |
| 14 | Move ALL teams from club | 3 teams owned by Club A | `POST /api/ladt/teams/{id}/change-club` (batch) | All 3 teams now under Club B |
| 15 | Move team to same club | Team already in target club | `POST /api/ladt/teams/{id}/change-club` | No-op or graceful handling |

#### 1.5 Batch Operations — Idempotency

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 16 | Add WAITLIST agegroups — first run | 3 leagues, none with WAITLIST | `POST /api/ladt/batch/waitlist-agegroups` | 6 new agegroups created (WAITLIST + WAITLIST2 per league), count=6 |
| 17 | Add WAITLIST agegroups — second run (idempotent) | 3 leagues, all already have WAITLIST | `POST /api/ladt/batch/waitlist-agegroups` | count=0, no duplicates |
| 18 | Add WAITLIST — partial state | 1 of 3 leagues already has WAITLIST | `POST /api/ladt/batch/waitlist-agegroups` | Only missing leagues get WAITLIST, count=4 |

#### 1.6 Create Stub — Hierarchy Integrity

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 19 | Create stub agegroup | League exists | `POST /api/ladt/agegroups/stub/{leagueId}` | New agegroup created with defaults, auto-created "UNASSIGNED" division, returns new ID |
| 20 | Create stub division | Agegroup exists | `POST /api/ladt/divisions/stub/{agId}` | New division created, returns new ID |
| 21 | Create stub team | Division exists | `POST /api/ladt/teams/stub/{divId}` | New team created with defaults (inactive, zero fees), returns new ID |
| 22 | Create stub under nonexistent parent | Invalid parent ID | `POST /api/ladt/teams/stub/{badId}` | 404, no orphan records created |

#### 1.7 Season/Year Immutability (CRITICAL — Data Integrity)

Season and Year are Job-level properties that MUST be consistent across all LADT entities. They cannot change once set, and must always match the Job's values.

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 23 | Stub agegroup gets Season from Job | Job with Season="Spring" | `POST /api/ladt/agegroups/stub/{leagueId}` | New agegroup has `Season="Spring"` (not null) |
| 24 | Created agegroup gets Season from Job | Job with Season="Fall" | `POST /api/ladt/agegroups` (full create) | Agegroup `Season="Fall"` regardless of what request sends |
| 25 | Update agegroup cannot overwrite Season | Agegroup with Season="Spring" | `PUT /api/ladt/agegroups/{id}` with any payload | Agegroup `Season` unchanged (still "Spring") |
| 26 | Stub team gets Season AND Year from Job | Job with Season="Fall", Year="2026" | `POST /api/ladt/teams/stub/{divId}` | New team has `Season="Fall"`, `Year="2026"` (not null) |
| 27 | Created team gets Season/Year from Job | Job with Season="Spring", Year="2025" | `POST /api/ladt/teams` (full create) | Team `Season="Spring"`, `Year="2025"` regardless of request |
| 28 | Update team cannot overwrite Season/Year | Team with Season="Fall", Year="2026" | `PUT /api/ladt/teams/{id}` with any payload | Team `Season`/`Year` unchanged |
| 29 | Cloned team inherits Season/Year from source | Source team with Season="Fall", Year="2026" | `POST /api/ladt/teams/{id}/clone` | Clone has identical `Season`/`Year` as source |
| 30 | Season/Year NOT in UpdateAgegroupRequest schema | N/A | Inspect OpenAPI spec | `UpdateAgegroupRequest` schema has no `season` property |
| 31 | Season/Year NOT in UpdateTeamRequest schema | N/A | Inspect OpenAPI spec | `UpdateTeamRequest` schema has no `season`/`year` properties |
| 32 | Season/Year NOT in CreateAgegroupRequest schema | N/A | Inspect OpenAPI spec | `CreateAgegroupRequest` schema has no `season` property |
| 33 | Season/Year NOT in CreateTeamRequest schema | N/A | Inspect OpenAPI spec | `CreateTeamRequest` schema has no `season`/`year` properties |

```csharp
[Fact]
public async Task CreateStubAgegroup_SetsSeasonFromJob()
{
    // Arrange: Job with Season="Spring 2026"
    // Act: POST /api/ladt/agegroups/stub/{leagueId}
    // Assert: new agegroup has Season="Spring 2026", not null
}

[Fact]
public async Task UpdateAgegroup_DoesNotOverwriteSeason()
{
    // Arrange: Agegroup with Season="Spring 2026"
    // Act: PUT /api/ladt/agegroups/{id} with all fields
    // Assert: agegroup Season still "Spring 2026" (unchanged)
}

[Fact]
public async Task CreateStubTeam_SetsSeasonAndYearFromJob()
{
    // Arrange: Job with Season="Fall", Year="2026"
    // Act: POST /api/ladt/teams/stub/{divId}
    // Assert: new team has Season="Fall", Year="2026"
}
```

#### 1.8 Schedule Denormalization Sync (CRITICAL — Data Integrity)

The `Schedule` entity has denormalized name fields (`AgegroupName`, `DivName`, `Div2Name`, `T1Name`, `T2Name`) that must stay in sync when LADT entities are renamed. If these drift, the public schedule displays stale/wrong names.

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 34 | Rename agegroup → schedules updated | Agegroup "U12" with 5 Schedule records (AgegroupId matches) | `PUT /api/ladt/agegroups/{id}` with `AgegroupName="Under 12"` | All 5 Schedule records have `AgegroupName="Under 12"` |
| 35 | Rename agegroup — no schedules exist | Agegroup "U14" with zero Schedule records | `PUT /api/ladt/agegroups/{id}` with `AgegroupName="Under 14"` | No error, agegroup name updated, zero schedule rows touched |
| 36 | Rename division → DivName updated | Division "Pool A" with 3 Schedule records (DivId matches) | `PUT /api/ladt/divisions/{id}` with `DivName="Pool Alpha"` | All 3 Schedule records have `DivName="Pool Alpha"` |
| 37 | Rename division → Div2Name updated | Division "Pool B" used as secondary division (Div2Id matches on 2 records) | `PUT /api/ladt/divisions/{id}` with `DivName="Pool Beta"` | 2 Schedule records have `Div2Name="Pool Beta"`, `DivName` unchanged on those records |
| 38 | Rename division — both DivId and Div2Id match | Schedule record where DivId AND Div2Id both point to same division | `PUT /api/ladt/divisions/{id}` with `DivName="Combined"` | Both `DivName` and `Div2Name` updated to `"Combined"` on that record |
| 39 | Rename division — no schedules exist | Division with zero Schedule records | `PUT /api/ladt/divisions/{id}` with `DivName="New Name"` | No error, division name updated |
| 40 | Rename team → T1Name/T2Name updated | Team "Hawks" appears as T1Id on 3 games and T2Id on 2 games | `PUT /api/ladt/teams/{id}` with `TeamName="Falcons"` | T1Name updated on 3 records, T2Name on 2 records (with correct club name prefix if applicable) |
| 41 | Rename team — club name prefix preserved | Team with club rep (ClubName="Metro"), Job has BShowTeamNameOnlyInSchedules=false | `PUT /api/ladt/teams/{id}` with `TeamName="Elite"` | Schedule names show `"Metro:Elite"` (not just `"Elite"`) |
| 42 | Rename team — show-team-name-only mode | Team with club rep, Job has BShowTeamNameOnlyInSchedules=true | `PUT /api/ladt/teams/{id}` with `TeamName="Thunder"` | Schedule names show `"Thunder"` (no club prefix) |
| 43 | Rename team — no schedules exist | Team with zero Schedule records | `PUT /api/ladt/teams/{id}` with `TeamName="New Name"` | No error, team name updated |
| 44 | Update team WITHOUT changing name | Team "Hawks" | `PUT /api/ladt/teams/{id}` without `TeamName` field (null) | Schedule names NOT touched (sync skipped — only fires when `TeamName != null`) |
| 45 | Rename agegroup — only same-job schedules affected | Agegroup shared ID, but schedules exist under different JobId | `PUT /api/ladt/agegroups/{id}` | Only schedules with matching `JobId` updated |

```csharp
[Fact]
public async Task RenameAgegroup_UpdatesScheduleAgegroupName()
{
    // Arrange: seed agegroup "U12" with 5 Schedule records where AgegroupId matches
    // Act: PUT /api/ladt/agegroups/{id} with AgegroupName="Under 12"
    // Assert: all 5 Schedule records have AgegroupName="Under 12"
}

[Fact]
public async Task RenameDivision_UpdatesBothDivNameAndDiv2Name()
{
    // Arrange: seed division "Pool A" with 3 Schedule records (DivId) + 2 records (Div2Id)
    // Act: PUT /api/ladt/divisions/{id} with DivName="Pool Alpha"
    // Assert: 3 records have DivName="Pool Alpha", 2 records have Div2Name="Pool Alpha"
}

[Fact]
public async Task RenameTeam_RecomposesWithClubNamePrefix()
{
    // Arrange: team with club rep (ClubName="Metro"), BShowTeamNameOnlyInSchedules=false
    // Act: PUT /api/ladt/teams/{id} with TeamName="Elite"
    // Assert: Schedule T1Name/T2Name = "Metro:Elite"
}
```

#### 1.9 Sport Dropdown Lookup

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 46 | Get sports returns all sports | 3 Sports in DB | `GET /api/ladt/sports` | Returns 3 items with SportId and SportName, alphabetically sorted |
| 47 | Update league with valid SportId | League + valid Sport | `PUT /api/ladt/leagues/{id}` with SportId | League's SportId updated, response includes SportName |

**Estimated effort**: ~47 tests, 4-5 days

---

## Layer 2: Frontend State Machine Tests (MEDIUM VALUE)

### Why This Layer Matters

The LADT editor's signal-based state management has subtle async chains: add a stub → reload tree → expand ancestors → select new node. If any step fails silently, the user sees a stale tree or loses their place. These tests verify the **orchestration logic**, not the HTTP calls.

### Setup

- Angular `TestBed` with `LadtService` mocked (return canned `of(...)` observables)
- `fakeAsync` + `tick()` for async signal propagation
- Assert on signal values, not DOM

### Test Cases

#### 2.1 Tree State After Mutations

| # | Test | Mock Setup | Action | Assert |
|---|------|-----------|--------|--------|
| 1 | Add stub → tree reloads → new node selected | `addStubTeam` returns `'new-42'`, `getTree` returns tree containing `'new-42'` | `component.addStub('div-5')` | `selectedNode().id === 'new-42'`, all ancestor IDs in `expandedIds` |
| 2 | Delete node → selection cleared | `deleteAgegroup` returns success, `getTree` returns tree without deleted node | `component.onDeleteConfirmed()` | `selectedNode() === null`, `siblingData().length === 0` |
| 3 | Drop team → tree reloads → appropriate message | `dropTeam` returns `{ wasDropped: true }` | `component.dropTeam(node)` | Tree reloaded, success message shown |

#### 2.2 Visibility & Expansion

| # | Test | Setup | Action | Assert |
|---|------|-------|--------|--------|
| 4 | Collapse parent hides all descendants | Tree with L→AG→Div→Team all expanded | Collapse league node | `visibleNodes()` excludes AG, Div, Team |
| 5 | Expand node shows only direct children (not grandchildren) | All collapsed | Expand league node | Only agegroup children visible, their children still hidden |
| 6 | Expand ancestors of deep node | Fully collapsed tree, target node at team level | `expandAncestors(teamNode)` | League, agegroup, division all in `expandedIds` |

#### 2.3 Guard Logic

| # | Test | Setup | Action | Assert |
|---|------|-------|--------|--------|
| 7 | `canDelete` returns false for leagues | League node selected | `canDelete(leagueNode)` | `false` |
| 8 | `canDelete` returns false for UNASSIGNED division | Division node with name "UNASSIGNED" | `canDelete(divNode)` | `false` |
| 9 | `canDelete` returns false for agegroup with teams | Agegroup node with `teamCount > 0` | `canDelete(agNode)` | `false` |
| 10 | `canDelete` returns true for empty agegroup | Agegroup node with `teamCount === 0` | `canDelete(agNode)` | `true` |

#### 2.4 Delete Confirmation Flow

| # | Test | Setup | Action | Assert |
|---|------|-------|--------|--------|
| 11 | Confirm dialog shows correct message per level | Team node selected | `confirmDelete(teamNode)` | `showDeleteConfirm() === true`, message mentions "drop" |
| 12 | Dismiss confirmation → no delete | Dialog open | `cancelDelete()` | `showDeleteConfirm() === false`, no service call made |

**Estimated effort**: ~12 tests, 1-2 days

---

## Layer 3: E2E Smoke Tests (HIGH VALUE — Do After Layer 1)

### Why This Layer Matters

These are your "sleep well at night" tests. They verify that the full stack works together for the 5 most critical user journeys. If these pass, the tool works.

### Setup

- Playwright
- Test database with known seed data (reset between tests)
- Run against the real Angular app + real .NET backend

### Test Cases

| # | Journey | Steps | Key Assertions |
|---|---------|-------|----------------|
| 1 | **Create full hierarchy** | Navigate to LADT → Add league stub → expand → add agegroup → expand → verify auto-created UNASSIGNED division → add team | Tree shows all 4 levels, each node clickable, detail form loads |
| 2 | **Drop a team** | Select team with registrations → click Drop → confirm dialog appears → confirm → tree updates | Team removed from active tree (or moved to Dropped), confirmation message displayed |
| 3 | **Clone a team** | Select team → Clone → enter name → optionally check "add to club library" → submit | New team appears as sibling in tree, name matches input |
| 4 | **Move team to different club** | Select team → Change Club → warning dialog → select target club → confirm | Team's club name updated in tree, sibling grid reflects change |
| 5 | **Fee cascade** | Select agegroup → edit fee field → save → click "Push fees to players" → confirm | Count message displayed, navigate to affected team to verify |

| 6 | **Edit league Sport dropdown** | Select league → verify Sport dropdown loads options → change Sport → save → grid and detail both reflect new Sport name | Sport dropdown populated, save succeeds, SportName column updated |
| 7 | **Edit agegroup Color** | Select agegroup → verify Color dropdown shows color swatches → change Color → save → grid shows color dot next to name | Color dropdown works, save succeeds, agegroup name column has color dot |

### Smoke Test Variants (run in CI)

| # | Scenario | Purpose |
|---|----------|---------|
| 8 | Load tree with 50+ teams | Performance sanity check — page loads within 3 seconds |
| 9 | Mobile viewport (375px) | Drawer opens on tree click, detail panel full width |
| 10 | Season/Year not editable in UI | Select agegroup → verify no Season field. Select team → verify no Season/Year fields. | Immutable fields not exposed |

**Estimated effort**: 7-10 tests, 2-3 days (including Playwright setup)

---

## Implementation Priority

```
Phase 1 (Week 1):  Backend integration tests 1.1–1.2     → 9 tests  (drop/delete guards)
Phase 2 (Week 1):  Backend integration tests 1.3–1.6     → 13 tests (fees, clubs, batch, stubs)
Phase 3 (Week 1):  Backend integration tests 1.7–1.8     → 25 tests (season/year immutability, schedule sync)
Phase 4 (Week 1):  Backend integration tests 1.9          → 2 tests  (sport lookup)
Phase 5 (Week 2):  E2E smoke tests 3.1–3.7               → 7 tests  (critical user journeys incl. Sport/Color)
Phase 6 (Week 2):  Frontend state tests 2.1–2.4           → 12 tests (signal orchestration)
Phase 7 (Week 3):  E2E edge cases 3.8–3.10                → 3 tests  (performance, mobile, field absence)
```

**Total: ~69 tests across 3 layers**

---

## Infrastructure Decisions (To Be Made Before Implementation)

| Decision | Options | Recommendation |
|----------|---------|----------------|
| Backend test DB | In-memory SQLite vs SQL Server LocalDB | SQLite for speed; SQL Server if testing SQL-specific features (e.g., triggers) |
| Test data seeding | Per-test setup vs shared fixtures | Per-test for isolation (each test seeds its own data) |
| E2E framework | Playwright vs Cypress | Playwright (better cross-browser, native `async/await`, lighter) |
| E2E test data | Shared test database vs API-seeded | API-seeded per test (call backend to create test data, then exercise UI) |
| CI integration | Run on every PR vs nightly | Backend tests on every PR; E2E nightly (slower) |

---

## Files That Will Be Created

| File | Layer | Purpose | Est. LOC |
|------|-------|---------|----------|
| `Tests/Integration/LadtDropTeamTests.cs` | Backend | Drop vs delete logic | 150 |
| `Tests/Integration/LadtDeleteGuardTests.cs` | Backend | Referential integrity guards | 120 |
| `Tests/Integration/LadtFeeCascadeTests.cs` | Backend | Fee push accuracy | 100 |
| `Tests/Integration/LadtClubReassignTests.cs` | Backend | Club team ownership transfer | 80 |
| `Tests/Integration/LadtBatchOperationTests.cs` | Backend | WAITLIST idempotency | 80 |
| `Tests/Integration/LadtStubCreationTests.cs` | Backend | Hierarchy creation integrity | 80 |
| `Tests/Integration/LadtSeasonYearTests.cs` | Backend | Season/Year immutability across all paths | 180 |
| `Tests/Integration/LadtScheduleSyncTests.cs` | Backend | Schedule denormalization sync on LADT rename | 200 |
| `Tests/Integration/LadtSportLookupTests.cs` | Backend | Sports endpoint + league sport update | 50 |
| `ladt-editor.component.spec.ts` | Frontend | Signal state machine tests | 200 |
| `e2e/ladt-editor.spec.ts` | E2E | Critical user journeys (incl. Sport/Color) | 320 |

**Total estimated**: ~1,560 LOC of test code

---

## Success Metrics

- ✅ All 47 backend tests pass against a fresh database
- ✅ All 12 frontend state tests pass with mocked services
- ✅ All 10 E2E smoke tests pass end-to-end
- ✅ Zero false positives (no tests that break on irrelevant changes)
- ✅ Backend tests run in < 30 seconds (fast feedback loop)
- ✅ E2E tests run in < 2 minutes

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Test database schema drift | Tests fail after migrations | Run migrations in test setup; share schema with production |
| Flaky E2E tests (timing) | False failures erode trust | Use Playwright's auto-wait; avoid `sleep()`; retry once on CI |
| Over-mocking hides real bugs | False confidence | Backend tests use real endpoints; E2E tests use real stack |
| Test maintenance burden | Tests become stale | Only test business logic, not UI details; delete tests that don't catch real bugs |
| SQLite vs SQL Server differences | Tests pass locally, fail in prod | If triggers/stored procs are used, use SQL Server LocalDB instead |

---

## Amendments Log

| Date | Amendment | Rationale |
|------|-----------|-----------|
| 2026-02-09 | Initial testing plan created | Establish testing strategy for mission-critical LADT editor |
| 2026-02-10 | Added Season/Year immutability tests (1.7, 11 tests) | Season/Year are Job-level invariants — create/stub must set from Job, update must never overwrite. Critical data integrity concern since these fields were removed from UI edit forms but backend previously accepted them in request DTOs. |
| 2026-02-10 | Added Sport lookup tests (1.8, 2 tests) | New `GET /api/ladt/sports` endpoint for league Sport dropdown. Verify lookup returns sorted list and league update persists SportId. |
| 2026-02-10 | Added E2E journeys for Sport dropdown and Color dropdown (3.6-3.7) | New UI widgets (Sport `<select>`, Color `<select>` with swatches, color dot in grid) need end-to-end validation. |
| 2026-02-10 | Added E2E smoke test for immutable field absence (3.10) | Verify Season/Year fields are NOT rendered in agegroup/team detail forms — guards against accidental re-introduction. |
| 2026-02-11 | Added Schedule Denormalization Sync tests (1.8, 12 tests) | New `SynchronizeScheduleAgegroupNameAsync` and `SynchronizeScheduleDivisionNameAsync` methods + wiring of existing team name sync into `UpdateTeamAsync`. Tests verify agegroup rename → `AgegroupName` updated, division rename → `DivName`/`Div2Name` updated, team rename → `T1Name`/`T2Name` recomposed with club name prefix, job-scoping, no-op when no schedules exist, and skip-when-name-unchanged guard. Renumbered Sport Lookup to section 1.9 (tests 46-47). |
