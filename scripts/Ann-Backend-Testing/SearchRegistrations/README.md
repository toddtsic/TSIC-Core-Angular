# Registration Search Tests

These automated tests validate the **Search/Registrations** admin grid — the query that powers the registration search panel with its multi-select filters, text search, and financial aggregates.

**No database or server needs to be running** — tests use an in-memory database.

## How to Run

In VS Code, navigate to `scripts/Ann-Backend-Testing/SearchRegistrations/` in the file explorer.

1. **Right-click** `Registration-Search-Tests.ps1`
2. Select **"Run Code"**
3. Results appear in the output panel

---

## What Each Test Verifies

Every test seeds test data, runs a search with specific filters, and checks that:
1. **The correct registrations are returned** — only matching records, not extras
2. **Aggregates are accurate** — TotalFees, TotalPaid, TotalOwed

---

## Registration Search Tests (16 tests)

| Test | Filter | What It Checks |
|------|--------|---------------|
| **No filters → all registrations** | *(none)* | Returns every registration for the job |
| **No filters → excludes other jobs** | *(none)* | Only returns registrations from the queried job |
| **Name (single term)** | Name | "Smith" matches FirstName OR LastName containing "Smith" |
| **Name (first + last)** | Name | "Alice Smith" matches first AND last name |
| **Email (partial)** | Email | "example.com" matches email containing that string |
| **Active status True** | ActiveStatuses | Only active (BActive=true) registrations |
| **Active status False** | ActiveStatuses | Only inactive (BActive=false) registrations |
| **Pay status PAID IN FULL** | PayStatuses | OwedTotal = 0 |
| **Pay status UNDER PAID** | PayStatuses | OwedTotal > 0 |
| **Pay status multi-select** | PayStatuses | PAID IN FULL + UNDER PAID uses OR logic |
| **Role filter** | RoleIds | Only registrations with matching RoleId |
| **Club name** | ClubNames | Only registrations with matching ClubName |
| **Position** | Positions | Only registrations with matching Position |
| **Date range** | RegDateFrom/To | Only registrations within the date window |
| **Team ID (LADT)** | TeamIds | Only registrations assigned to that team |
| **Agegroup ID (LADT)** | AgegroupIds | Only registrations in that agegroup |
| **Aggregates** | *(none)* | TotalFees, TotalPaid, TotalOwed summed correctly |
| **Sort order** | *(none)* | Results ordered by LastName, then FirstName |
| **School name** | SchoolName | Partial match on school name |
| **Combined filters** | ActiveStatuses + PayStatuses | AND logic between different filter types |
| **No matches** | Name | Empty result with zero aggregates |

---

## Key Search Principles

### Filter Logic
- **Different filter types** use AND logic (active + pay status = both must match)
- **Multi-select within a filter** uses OR logic (PAID IN FULL + UNDER PAID = either matches)
- **Text filters** use case-insensitive partial matching (Contains)

### Aggregates
Computed across ALL matching records before any paging:
- `TotalFees` = sum of FeeTotal
- `TotalPaid` = sum of PaidTotal
- `TotalOwed` = sum of OwedTotal

---

**Need help?** Ask Claude Code: *"explain the registration search test results"*
