# TSIC-Events Backend Tests

Backend test suite for endpoints serving the TSIC-Events mobile app.

## How to Run

**All TSIC-Events tests:**
```bash
dotnet test TSIC-Core-Angular/src/backend/TSIC.Tests/TSIC.Tests.csproj --filter "FullyQualifiedName~Mobile" --no-build --no-restore
```

**Individual categories (right-click → Run Code in VS Code):**
- `Devices/Device-Management-Tests.ps1` — 8 tests
- `EventBrowse/Event-Browse-Tests.ps1` — 6 tests
- `Schedule/Schedule-Tests.ps1` — 19 tests (games + standings + brackets)

## Test Summary

### Device Management (8 tests) — `Mobile/Shared/Devices/`

Shared by both TSIC-Events and TSIC-Teams.

| # | Test | What It Checks |
|---|------|---------------|
| 1 | Register new device | Creates Devices + DeviceJobs records |
| 2 | Register existing device | Idempotent — updates Modified, no duplicate |
| 3 | Register device for second job | Creates second DeviceJobs link |
| 4 | Toggle subscribe (new) | Creates DeviceTeams, returns subscribed |
| 5 | Toggle subscribe (existing) | Removes DeviceTeams, returns unsubscribed |
| 6 | Get subscribed teams | Returns correct team IDs filtered by job |
| 7 | Swap token — full migration | Old device deactivated, new device gets all links |
| 8 | Swap token — old not found | No-op, no errors |

### Event Browse (6 tests) — `Mobile/TSIC-Events/EventBrowse/`

| # | Test | What It Checks |
|---|------|---------------|
| 1 | Active public events | Returns only non-expired, non-suspended, public-access jobs |
| 2 | Expired job excluded | ExpiryUsers < now filtered out |
| 3 | Suspended job excluded | BSuspendPublic = true filtered out |
| 4 | Non-public job excluded | BScheduleAllowPublicAccess = false filtered out |
| 5 | Alerts newest first | Push notification sort order |
| 6 | Game clock config | GameClockParams mapping to DTO |

### Schedule Games (8 tests) — `Mobile/TSIC-Events/Schedule/`

| # | Test | What It Checks |
|---|------|---------------|
| 1 | Unfiltered returns all | Baseline — no filter returns every game |
| 2 | Filter by teamIds | T1Id/T2Id match filter |
| 3 | Filter by gameDays | Date filter |
| 4 | Filter unscoredOnly | Scored games excluded |
| 5 | DivName populated | Phase 1 DTO extension field |
| 6 | T1Record/T2Record | W-L-T record calculation from pool-play |
| 7 | AgDiv format | "Agegroup:Division" format |
| 8 | FAddress from field | Address parts concatenation |

### Standings (6 tests) — `Mobile/TSIC-Events/Schedule/`

| # | Test | What It Checks |
|---|------|---------------|
| 1 | W-L-T correct | Basic win/loss/tie counting |
| 2 | Soccer sort | Points DESC → Wins → GoalDiff |
| 3 | Lacrosse sort | Wins DESC → Losses → GoalDiff |
| 4 | GoalDiffMax9 capped | Clamped to ±9 |
| 5 | TiePoints | Number of ties (Phase 1 extension) |
| 6 | Unscored teams 0-0-0 | Teams with no scored games still appear |

### Brackets (5 tests) — `Mobile/TSIC-Events/Schedule/`

| # | Test | What It Checks |
|---|------|---------------|
| 1 | Grouped by agegroup | Separate bracket per agegroup |
| 2 | Champion from Finals | Winner of F-type game |
| 3 | CSS classes | winner/loser/pending per team |
| 4 | ParentGid tree links | Semi → Final parent reference |
| 5 | Round ordering | S before F |

## Architecture

- **Test framework**: xUnit + FluentAssertions + Moq
- **Database**: InMemory EF Core (isolated per test via `DbContextFactory.Create()`)
- **Data seeding**: `MobileDataBuilder` (fluent builder in `TSIC.Tests/Helpers/`)
- **Pattern**: Real repositories + InMemory DB, mocked external services

## Future: Auth Tests

Auth tests (`QuickLoginTests.cs`) require mocking `UserManager<ApplicationUser>` and will be added in a follow-up. The auth endpoints work correctly in manual testing; the test gap is the mock setup complexity.

## Future: TSIC-Teams Tests

Team-specific tests (attendance, chat, file upload, team auth) will be added under `Mobile/TSIC-Teams/` in a follow-up test suite.
