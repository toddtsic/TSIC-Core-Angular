# Search Teams - Punch List

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

- [ ] **Search Filters** -- League, age group, division, team name, and other filter options
- [ ] **Search Results** -- Columns, sorting, pagination, data accuracy
- [ ] **Team Details** -- Clicking into a team from search results
- [ ] **Roster View** -- Viewing players on a team from search results
- [ ] **Export** -- Exporting search results to CSV or other formats

---

## Punch List Items

### PL-003: Filters icon needs a label — hard to discover
- **Area**: Search Filters
- **What I did**: Opened Search Teams and looked for how to apply filters
- **What I expected**: Obvious entry point to the filter panel
- **What happened**: Filters are behind an icon with no label — didn't know where to find them at first. Add text like "View Filters" next to (or as a tooltip on) the icon so the affordance is discoverable.
- **Severity**: UX
- **Status**: Fixed — Todd handled this elsewhere (filter toggle now carries a "Set Filters" text label).

### PL-002: Team Details flyin — Level of Play field isn't tall enough
- **Area**: Team Details
- **What I did**: Opened the Team Details flyin from Search Teams results
- **What I expected**: The Level of Play field to be tall enough to show its value clearly
- **What happened**: LOP field is too short — value gets clipped or feels cramped. Increase field height.
- **Severity**: UX
- **Status**: Fixed — added `min-height: 2.5rem` to `.form-select` in team-detail-panel.component.scss (appearance:none select had no intrinsic height). Scoped, UI-only.

### PL-001: Search Results Table — "Active" column header is cut off
- **Area**: Search Results
- **What I did**: Ran a Search Teams query and looked at the results table
- **What I expected**: The "Active" column header to be fully visible
- **What happened**: "Active" header is cut off (truncated). Column needs more width or the header needs to wrap/render in full.
- **Severity**: UX
- **Status**: Fixed — widened the `active` column width 75 → 95 in search-teams.component.html so the full header shows. UI-only.

