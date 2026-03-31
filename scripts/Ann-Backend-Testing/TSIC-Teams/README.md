# TSIC-Teams Backend Tests

Backend test suite for endpoints serving the TSIC-Teams mobile app.

## How to Run

**All TSIC-Teams tests:**
```bash
dotnet test TSIC-Core-Angular/src/backend/TSIC.Tests/TSIC.Tests.csproj --filter "FullyQualifiedName~TSIC_Teams" --no-build --no-restore
```

**Individual categories (right-click → Run Code in VS Code):**
- `Roster/Team-Roster-Tests.ps1` — 4 tests
- `Links/Team-Links-Tests.ps1` — 5 tests
- `Attendance/Team-Attendance-Tests.ps1` — 8 tests
- `Chat/Team-Chat-Tests.ps1` — 6 tests

**All mobile tests (Events + Teams + Shared):**
```bash
dotnet test TSIC-Core-Angular/src/backend/TSIC.Tests/TSIC.Tests.csproj --filter "FullyQualifiedName~Mobile" --no-build --no-restore
```

## Test Summary

### Team Roster (4 tests) — `Mobile/TSIC-Teams/Roster/`

| # | Test | What It Checks |
|---|------|---------------|
| 1 | Staff and players split | Registrations grouped by role (Staff vs Player) |
| 2 | Inactive excluded | BActive=false registrations filtered out |
| 3 | Parent contacts from Families | Mom/Dad name, email, cellphone populated |
| 4 | Uniform number and school | Registration fields mapped to DTO |

### Team Links (5 tests) — `Mobile/TSIC-Teams/Links/`

| # | Test | What It Checks |
|---|------|---------------|
| 1 | Both scopes returned | Team-scoped + job-scoped links in one list |
| 2 | Add creates record | TeamDocs row created with correct TeamId |
| 3 | AddAllTeams sets JobId | JobId set, TeamId null for job-scoped links |
| 4 | Delete removes record | TeamDocs row deleted |
| 5 | Delete nonexistent returns false | Graceful handling |

### Team Attendance (8 tests) — `Mobile/TSIC-Teams/Attendance/`

| # | Test | What It Checks |
|---|------|---------------|
| 1 | Create event, zero counts | New event starts with 0 present/0 absent |
| 2 | Get events with counts | Present/NotPresent tallied from RSVP records |
| 3 | Update RSVP creates record | New TeamAttendanceRecords row |
| 4 | Update RSVP toggles existing | Existing record updated, no duplicate |
| 5 | Delete event cascades | Event + all RSVP records removed |
| 6 | Player history ordered | Newest events first |
| 7 | Get event types | Returns seeded TeamAttendanceTypes |

### Team Chat (6 tests) — `Mobile/TSIC-Teams/Chat/`

| # | Test | What It Checks |
|---|------|---------------|
| 1 | Add message stores + returns | ChatMessages row created |
| 2 | Newest first with pagination | Skip/take ordering |
| 3 | Message count correct | Total count for pagination UI |
| 4 | Delete removes from DB | ChatMessages row deleted |
| 5 | Delete nonexistent returns false | Graceful handling |
| 6 | Scoped to team | Other team's messages excluded |

## Shared Tests

Device management tests (8 tests) are in `Mobile/Shared/Devices/` and covered by the TSIC-Events test suite. They apply to both apps.

## Architecture

- **Test framework**: xUnit + FluentAssertions + Moq
- **Database**: InMemory EF Core (isolated per test)
- **Data seeding**: `MobileDataBuilder` (shared with TSIC-Events)
- **Pattern**: Real repositories + InMemory DB, mocked external services (Firebase)
