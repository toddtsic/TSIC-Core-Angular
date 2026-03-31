# Team Search Tests

These automated tests validate the **Search/Teams** admin grid — the query that powers the team search panel with its multi-select filters, waitlist detection, and club rep info projection.

**No database or server needs to be running** — tests use an in-memory database.

## How to Run

In VS Code, navigate to `scripts/Ann-Backend-Testing/SearchTeams/` in the file explorer.

1. **Right-click** `Team-Search-Tests.ps1`
2. Select **"Run Code"**
3. Results appear in the output panel

---

## What Each Test Verifies

Every test seeds test data, runs a search with specific filters, and checks that:
1. **The correct teams are returned** — only matching records, not extras
2. **Team data is projected correctly** — club rep name, financials, agegroup info

---

## Team Search Tests (16 tests)

| Test | Filter | What It Checks |
|------|--------|---------------|
| **No filters → all teams** | *(none)* | Returns every team for the job |
| **No filters → excludes other jobs** | *(none)* | Only returns teams from the queried job |
| **Active status True** | ActiveStatuses | Only active teams |
| **Active status False** | ActiveStatuses | Only inactive teams |
| **Club name** | ClubNames | Only teams whose club rep has matching ClubName |
| **Pay status PAID IN FULL** | PayStatuses | Teams with OwedTotal = 0 |
| **Pay status UNDER PAID** | PayStatuses | Teams with OwedTotal > 0 |
| **Level of play** | LevelOfPlays | Only teams with matching LevelOfPlay (e.g., "AA") |
| **Agegroup** | AgegroupIds | Only teams in that agegroup |
| **LADT tree (TeamId)** | TeamIds | Returns specific team by ID |
| **LADT tree (LeagueId)** | LeagueIds | Returns all teams in that league |
| **Waitlist WAITLISTED** | WaitlistScheduledStatus | Only teams in agegroups containing "WAITLIST" |
| **Waitlist NOT_WAITLISTED** | WaitlistScheduledStatus | Excludes waitlisted teams |
| **Sort order** | *(none)* | Results ordered by ClubName, AgegroupName, TeamName |
| **Club rep info** | *(none)* | ClubRepName, ClubRepEmail projected from user join |
| **Financial totals** | *(none)* | PaidTotal and OwedTotal projected correctly |
| **Combined filters** | ActiveStatuses + PayStatuses | AND logic between filter types |
| **No matches** | ActiveStatuses | Empty list when nothing matches |
| **CADT tree** | CadtTeamIds | Returns only specified team IDs |

---

## Key Search Principles

### Filter Logic
- **Different filter types** use AND logic (active + club name = both must match)
- **Multi-select within a filter** uses OR logic
- **Club name** is resolved through the club rep registration join (not a direct team field)

### Waitlist Detection
- Teams are "waitlisted" if their agegroup name contains "WAITLIST"
- This is a convention: `WAITLIST - U14 Boys` indicates a waitlisted agegroup

### Sort Order
Results are sorted: ClubName → AgegroupName → DivName → TeamName

---

**Need help?** Ask Claude Code: *"explain the team search test results"*
