# Search Registrations - Punch List

**Tester:** Ann
**Date Started:** 2026-04-06
**Status:** In Progress

---

## How to Read Severity

| Label | Meaning |
|-------|---------|
| Bug | Something is broken or produces wrong results |
| UX | It works but is confusing, ugly, or hard to use |
| Question | Not sure if this is right -- need to ask Todd |

## How to Read Status

| Label | Meaning |
|-------|---------|
| Open | Not yet looked at |
| Fixed | Todd/Claude fixed it |
| Won't Fix | Intentional behavior, not changing |

---

## Test Areas

Use these as a guide for what to walk through. You don't have to go in order.

- [ ] **Search Filters** -- Name, team, age group, status, and other filter options
- [ ] **Search Results** -- Columns, sorting, pagination, data accuracy
- [ ] **Registration Details** -- Clicking into a registration from search results
- [ ] **Bulk Actions** -- Selecting multiple registrations and applying actions
- [ ] **Export** -- Exporting search results to CSV or other formats

---

## Punch List Items

### PL-001: Replace the magnifying-glass icon with a larger "Search Filters" button
- **Area**: Search Filters
- **What I did**: Tried to open the search filter popup on the Search Registrations screen
- **What I expected**: An obvious, labeled button so it's immediately clear where to open the filters
- **What happened**: Only a small magnifying-glass icon is shown — too easy to miss and not self-describing. Replace it with a larger button labeled **"Search Filters"** so the entry point is unmistakable.
- **Severity**: UX
- **Status**: Fixed

### PL-002: Keep filter access reachable when scrolled down a long results list (don't force a scroll back to the top)
- **Refs**: PL-001 (more visible "Search Filters" button)
- **Area**: Search Filters / Search Results
- **What I did**: Scrolled down through a long list of search results and wanted to view or add filters
- **What I expected**: To be able to open and adjust filters from wherever I am in the list
- **What happened**: The filter entry point is only at the top of the screen, so on a long list I have to scroll all the way back up just to view or add a filter. Make filter access reachable while scrolled down — e.g., a sticky/floating "Search Filters" button or a pinned filter bar — so the user doesn't have to return to the top.
- **Severity**: UX
- **Status**: Fixed

