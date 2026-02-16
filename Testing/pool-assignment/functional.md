# Functional Tests: Pool Assignment

> **Status**: In progress — paused at F7.1 (pending shared component refactoring)
> **Utility**: Team transfer between divisions/agegroups with fee recalculation, schedule-aware symmetrical swaps, club rep financial sync
> **Route**: `/:jobPath/teampoolassignment/index`
> **Authorization**: AdminOnly (Director, SuperDirector, Superuser)

---

## Prerequisites

Before running these tests, ensure:
- A job exists with at least **2 leagues**, each with **2+ agegroups**, each with **2+ divisions** (including at least one "Dropped Teams" agegroup)
- At least **5-10 teams** spread across divisions, some with club reps assigned
- At least **2-3 teams with scheduled games** (appear in Schedule table)
- At least **2-3 teams with NO scheduled games**
- At least one agegroup with **different fee structure** (different TeamFee/RosterFee) than another
- A club rep with **multiple teams across different divisions** (to test club rep financial sync)
- Know the current fee values for teams you'll be moving (screenshot or note them before starting)

---

## Section 1: Page Load & Division Selection

### F1.1 — Page loads with empty panels

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/:jobPath/admin/pool-assignment` | Page loads without errors |
| 2 | Observe both panels | Source and Target panels visible, both showing division dropdown with no selection |
| 3 | Observe buttons | "Move Selected" buttons on both panels are disabled |
| 4 | Observe capacity bars | No capacity bars visible (no division selected) |

### F1.2 — Division dropdown is grouped by agegroup

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click the Source division dropdown | Dropdown opens |
| 2 | Observe grouping | Divisions are grouped under agegroup headers (optgroup labels) |
| 3 | Observe team counts | Each division option shows its team count (e.g., "Pool A (6)") |
| 4 | Observe "Dropped Teams" | Any "Dropped Teams" division is visually distinct (red/italic or warning icon) |
| 5 | Observe ordering | Groups are alphabetical by agegroup name; divisions alphabetical within each group |

### F1.3 — Selecting a source division loads teams

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a division with teams from the Source dropdown | Teams load in the source panel table |
| 2 | Observe table columns | Columns visible: checkbox, #, swap button, Team Name, Club, Club Rep, LOP, Reg Date, Comments, DivRank |
| 3 | Observe capacity bar | Capacity bar appears showing current/max (e.g., "6/8 teams") with appropriate color |
| 4 | Observe team data | Team names, club names, club rep names all populated correctly |
| 5 | Observe inactive teams | Any inactive teams show with muted/tertiary text color |

### F1.4 — Selecting a target division loads teams

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a different division from the Target dropdown | Teams load in the target panel table |
| 2 | Verify same columns and data quality as source | All columns populated, capacity bar shown |
| 3 | Verify source panel unchanged | Source panel still shows its teams, selection intact |

### F1.5 — Cannot select same division for both panels

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Note which division is selected as Source | — |
| 2 | Try to select the same division in Target dropdown | Either: option is disabled/hidden, OR selection is rejected with a message |

### F1.6 — Switching divisions clears previous state

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select some teams in source (check a few boxes) | Checkboxes are checked |
| 2 | Change the Source dropdown to a different division | New teams load, all checkboxes unchecked, previous selections cleared |
| 3 | Verify filter text also cleared | Filter input (if it was populated) is reset |

---

## Section 2: Team Selection & Filtering

### F2.1 — Individual team selection

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click checkbox on a team row in the source panel | Row becomes highlighted (selected state), checkbox checked |
| 2 | Click the same checkbox again | Row deselects, checkbox unchecked |
| 3 | Select 2-3 teams | All selected rows highlighted; "Move Selected → (N)" button shows correct count |

### F2.2 — Select All / Deselect All

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click the "select all" checkbox in the source table header | All teams in the source panel are selected |
| 2 | Verify count | "Move Selected →" button shows total team count |
| 3 | Click "select all" again | All teams deselected |

### F2.3 — Filter teams by name

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Type a partial team name in the source filter box | Table filters to show only matching teams (case-insensitive) |
| 2 | Verify non-matching teams are hidden | Only teams whose name, club, or club rep contains the filter text are shown |
| 3 | Clear the filter | All teams reappear |

### F2.4 — Filter preserves selections

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select 3 teams in source panel | 3 teams checked |
| 2 | Type a filter that hides 1 of the selected teams | 2 selected teams visible, 1 hidden by filter |
| 3 | Clear the filter | All 3 teams still selected (selection preserved through filtering) |

### F2.5 — Column sorting

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Team Name" column header | Teams sort alphabetically ascending; sort icon shows up-arrow |
| 2 | Click same header again | Sort reverses to descending; icon shows down-arrow |
| 3 | Click a third time | Sort clears (returns to default order) |
| 4 | Click "DivRank" header | Teams sort by rank numerically |

---

## Section 3: Same-Agegroup Transfer (No Fee Changes)

### F3.1 — Move single team within same agegroup (quick-move button)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select Source: a division in agegroup "U12 Boys" | Teams load |
| 2 | Select Target: a different division in the **same** agegroup "U12 Boys" | Teams load |
| 3 | Find a team with **no scheduled games** | — |
| 4 | Click the arrow/swap button on that team's row | Transfer executes (possibly with brief confirmation) |
| 5 | Observe source panel | Team is gone from source list; team count decremented |
| 6 | Observe target panel | Team appears in target list; team count incremented |
| 7 | Observe toast | Success toast: "1 team moved" (or similar) |
| 8 | Verify: **no fee changes** mentioned | No fee preview was shown; toast does not mention fee recalculation |

### F3.2 — Bulk move within same agegroup

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Source and Target in same agegroup (as above) | — |
| 2 | Select 2-3 unscheduled teams via checkboxes | Teams checked |
| 3 | Click "Move Selected → (N)" | Confirmation panel appears |
| 4 | Observe confirmation | Shows "Same agegroup — no fee changes" (or similar); NO fee preview table |
| 5 | Click "Confirm Transfer" | Transfer executes |
| 6 | Observe both panels | Teams moved from source to target; counts updated |
| 7 | Observe DivRank | Moved teams have new ranks in target; source ranks renumbered (no gaps) |

### F3.3 — Verify DivRank renumbering after move

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Note DivRanks in source division before the move (e.g., 1, 2, 3, 4, 5) | — |
| 2 | Move team with rank 3 to target | — |
| 3 | Observe source DivRanks | Ranks are now 1, 2, 3, 4 (renumbered, no gap at position 3) |
| 4 | Observe target DivRanks | Moved team has the next available rank (appended at end) |

---

## Section 4: Cross-Agegroup Transfer (Fee Recalculation)

### F4.1 — Fee preview shown for cross-agegroup move

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select Source: a division in agegroup with TeamFee = $500 | Note team fees |
| 2 | Select Target: a division in a **different** agegroup with TeamFee = $650 | — |
| 3 | Select 1-2 unscheduled teams | Teams checked |
| 4 | Click "Move Selected →" | **Fee preview panel appears** (not immediate transfer) |
| 5 | Observe fee preview table | Each team shows: Team Name, Current Fee, New Fee, Delta |
| 6 | Verify fee values | Current fees match the source agegroup's fee; New fees match target agegroup's fee; Delta is correct |
| 7 | Verify delta coloring | Positive delta (fee increase) shown in red/warning; negative delta in green |

### F4.2 — Club rep impact shown in fee preview

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (Continuing from F4.1 — preview panel visible) | — |
| 2 | Observe "Club Rep Impact" section | Shows affected club rep(s) with: Club Name, Current Total → New Total, Delta |
| 3 | Verify accuracy | Club rep total reflects the sum change of all their teams being moved |

### F4.3 — Confirm cross-agegroup transfer

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (Continuing from F4.1 — preview panel visible) | — |
| 2 | Click "Confirm Transfer" | Transfer executes with spinner |
| 3 | Observe both panels | Teams moved; source/target counts updated |
| 4 | Observe toast | Success message mentions: teams moved count + fees recalculated count |
| 5 | **Verify team fees** | Click on a moved team or check its data — FeeBase/FeeTotal now match the target agegroup's fee structure |
| 6 | **Verify club rep financials** | Navigate to the club rep's registration (via Search Registrations or LADT) — FeeTotal reflects the updated sum of all their active teams |

### F4.4 — Cancel fee preview

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Initiate a cross-agegroup move (fee preview appears) | Preview panel visible |
| 2 | Click "Cancel" | Preview panel disappears |
| 3 | Verify no changes | Teams remain in original positions; no fees changed; no toast |

---

## Section 5: Dropped Teams Division

### F5.1 — Deactivation warning when target is Dropped Teams

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select Target: "Dropped Teams" division (should be visually distinct in dropdown) | — |
| 2 | Select 1 active team from source | — |
| 3 | Click "Move Selected →" | Confirmation/preview panel appears |
| 4 | Observe warning | Explicit deactivation warning: "Teams will be deactivated" or similar |
| 5 | Observe fee preview | If cross-agegroup, fees shown recalculated (likely to $0) |

### F5.2 — Confirm move to Dropped Teams

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (Continuing from F5.1) Click "Confirm Transfer" | Transfer executes |
| 2 | Observe target panel | Team appears in Dropped Teams division |
| 3 | **Verify Active status** | Team is now **inactive** (Active = false), shown with muted text |
| 4 | **Verify fees** | Team's fees recalculated per Dropped agegroup's fee structure (typically $0) |
| 5 | **Verify club rep** | Club rep's totals decreased by the moved team's former fees |

### F5.3 — Move team OUT of Dropped Teams

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select Source: "Dropped Teams" division | Inactive teams shown |
| 2 | Select Target: a normal active division | — |
| 3 | Select an inactive team, move it | Transfer executes |
| 4 | **Verify Active status** | Confirm whether team is re-activated or remains inactive (document actual behavior) |
| 5 | **Verify fees** | Fees recalculated to match target agegroup's fee structure |

---

## Section 6: Schedule-Aware Symmetrical Swap

### F6.1 — Scheduled team detection in preview

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select Source division containing a team WITH scheduled games | — |
| 2 | Select Target division | — |
| 3 | Select the scheduled team | — |
| 4 | Click "Move Selected →" | Preview panel appears |
| 5 | Observe warning | Banner: "Symmetrical swap required — some teams have scheduled games" (or similar) |
| 6 | Observe "Confirm" button | **Disabled** — cannot confirm without selecting target teams |

### F6.2 — Symmetrical swap — select target teams

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (Continuing from F6.1 — preview shows symmetrical swap required) | — |
| 2 | Select equal number of teams from the **Target panel** | Target team(s) checked |
| 3 | Click "Update Preview" (or observe automatic update) | Preview updates to show both directions of the swap |
| 4 | Observe preview | Shows: Source teams → Target div, Target teams → Source div |
| 5 | Observe fee impact | If cross-agegroup: fees shown for BOTH directions |
| 6 | Observe "Confirm" button | Now **enabled** |

### F6.3 — Symmetrical swap — unequal selection blocked

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select 2 scheduled source teams | — |
| 2 | Select only 1 target team | — |
| 3 | Observe | Warning: "Select equal numbers of teams from each panel" (or similar) |
| 4 | Observe "Confirm" button | **Disabled** |

### F6.4 — Symmetrical swap execution

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (Continuing from F6.2 — equal selections, preview showing both directions) | — |
| 2 | Click "Confirm Swap" | Transfer executes |
| 3 | Observe source panel | Source teams gone, target teams now appear here |
| 4 | Observe target panel | Target teams gone, source teams now appear here |
| 5 | Observe toast | Success message mentions teams swapped in both directions |
| 6 | **Verify schedule names** | Navigate to View Schedule — games involving swapped teams show updated team names and division/agegroup context |

### F6.5 — Unscheduled teams move freely (no swap required)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select Source division with teams that have **no scheduled games** | — |
| 2 | Select Target division | — |
| 3 | Select unscheduled team(s) | — |
| 4 | Click "Move Selected →" | Confirmation appears WITHOUT symmetrical swap requirement |
| 5 | Confirm | One-directional move succeeds normally |

### F6.6 — Mixed scheduled/unscheduled selection

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a mix of scheduled and unscheduled teams from source | — |
| 2 | Click "Move Selected →" | Preview shows symmetrical swap required (stricter rule applies to entire batch) |
| 3 | Verify | Must select target teams to proceed, even though some source teams are unscheduled |

---

## Section 7: Inline Editing

### F7.1 — Edit DivRank

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click on a team's DivRank value in the table | Inline dropdown appears with rank options (1 through N) |
| 2 | Select a different rank | Rank updates immediately (auto-save) |
| 3 | Observe other teams | Team previously at the selected rank swaps to the edited team's old rank |
| 4 | Verify no gaps | All ranks are contiguous (1, 2, 3... N) |

### F7.2 — Toggle team Active status

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Find an active team in the panel | Active status shown (toggle/switch in active state) |
| 2 | Toggle Active off | Team becomes inactive; row styling changes to muted/tertiary |
| 3 | Toggle Active back on | Team becomes active again; row styling returns to normal |
| 4 | Verify persistence | Refresh page or re-select division — Active status persists |

---

## Section 8: Capacity Bar Behavior

### F8.1 — Capacity bar reflects team count

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a division with 6 teams, MaxTeams = 8 | Capacity bar shows "6/8" |
| 2 | Move 1 team to this division | Capacity bar updates to "7/8" |
| 3 | Move 1 team away from this division | Capacity bar updates back to "6/8" |

### F8.2 — Capacity bar color thresholds

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Division at < 75% capacity | Bar is green |
| 2 | Division at 75-90% capacity | Bar is yellow/warning |
| 3 | Division at > 90% capacity | Bar is red/danger |
| 4 | Division exceeds MaxTeams | Bar extends beyond 100% or shows over-capacity indicator |

---

## Section 9: Edge Cases & Error Handling

### F9.1 — Empty division

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a division with 0 teams | Panel shows empty state (no table rows, possibly a message) |
| 2 | Verify "Move Selected" button | Disabled (nothing to move) |
| 3 | Move a team INTO this empty division | Team appears; capacity bar shows 1/N |

### F9.2 — Division at or exceeding MaxTeams

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a target division already at MaxTeams | Capacity bar shows full |
| 2 | Move a team into it | Transfer is **allowed** (MaxTeams is a soft limit, not enforced) |
| 3 | Capacity bar | Shows over-capacity (e.g., "9/8") |

### F9.3 — Transfer with no target selected

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a source division and check some teams | Teams selected |
| 2 | Do NOT select a target division | — |
| 3 | Observe "Move Selected →" button | **Disabled** (no target to move to) |

### F9.4 — Network error during transfer

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Simulate a network error (disconnect, or backend down) | — |
| 2 | Attempt a transfer | Error toast appears with meaningful message |
| 3 | Verify no partial state | Teams remain in original positions (atomic transaction — all or nothing) |

---

## Section 10: Responsive & Visual

### F10.1 — Palette compatibility

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Switch through all 8 palettes | — |
| 2 | For each palette, verify: | Panel borders, capacity bars, selected row highlighting, fee preview colors, toast colors all render correctly using CSS variables |
| 3 | Verify no hardcoded colors | No color breaks across palettes |

### F10.2 — Mobile responsive layout

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resize browser to < 768px width | Panels stack vertically (source on top, target below) |
| 2 | Each panel | Independently scrollable |
| 3 | Dropdowns and buttons | Still functional and reachable |

---

## Section 11: Data Integrity Verification (Post-Transfer Checks)

These are the "did it actually work in the database" checks. Perform after completing Sections 3-6.

### F11.1 — Team entity after same-agegroup move

| Check | Expected |
|-------|----------|
| Team.DivId | Updated to target division ID |
| Team.AgegroupId | **Unchanged** (same agegroup) |
| Team.FeeBase, FeeTotal, OwedTotal | **Unchanged** (no fee recalculation) |
| Team.DivRank | New rank in target division (no gaps) |
| Team.Modified | Updated to current timestamp |

### F11.2 — Team entity after cross-agegroup move

| Check | Expected |
|-------|----------|
| Team.DivId | Updated to target division ID |
| Team.AgegroupId | Updated to target agegroup ID |
| Team.FeeBase | Recalculated using target agegroup's fee structure |
| Team.FeeTotal | FeeBase + FeeProcessing - FeeDiscount |
| Team.OwedTotal | FeeTotal - PaidTotal |
| Team.DivRank | New rank in target division |

### F11.3 — Club rep financials after move

| Check | Expected |
|-------|----------|
| ClubRep.FeeBase | SUM of all their active teams' FeeBase |
| ClubRep.FeeTotal | SUM of all their active teams' FeeTotal |
| ClubRep.OwedTotal | ClubRep.FeeTotal - ClubRep.PaidTotal |

### F11.4 — Schedule records after symmetrical swap

| Check | Expected |
|-------|----------|
| Schedule.T1Name / T2Name | Updated to reflect moved team's new display name |
| Schedule.AgegroupId | Updated if team moved to different agegroup |
| Schedule.AgegroupName | Updated if agegroup changed |
| Schedule.DivId / DivName | Updated to reflect team's new division |
| Game structure | T1Id/T2Id still point to correct team entities (unchanged) |

---

## Test Execution Summary

| Section | Tests | Focus Area |
|---------|-------|------------|
| 1. Page Load & Division Selection | F1.1 – F1.6 | UI loads correctly, dropdowns work |
| 2. Selection & Filtering | F2.1 – F2.5 | Multi-select, filter, sort |
| 3. Same-Agegroup Transfer | F3.1 – F3.3 | Basic moves, no fee changes, rank mgmt |
| 4. Cross-Agegroup Transfer | F4.1 – F4.4 | Fee preview, club rep impact, confirmation |
| 5. Dropped Teams | F5.1 – F5.3 | Deactivation, fee zeroing, reactivation |
| 6. Symmetrical Swap | F6.1 – F6.6 | Schedule detection, swap enforcement, execution |
| 7. Inline Editing | F7.1 – F7.2 | DivRank, Active toggle |
| 8. Capacity Bar | F8.1 – F8.2 | Count accuracy, color thresholds |
| 9. Edge Cases | F9.1 – F9.4 | Empty div, over-capacity, no target, errors |
| 10. Responsive & Visual | F10.1 – F10.2 | Palettes, mobile layout |
| 11. Data Integrity | F11.1 – F11.4 | Database-level verification |

**Total: 30 functional test scenarios across 11 sections**

---

## Walkthrough Results (2026-02-15)

### Test Results

| Test | Result | Notes |
|------|--------|-------|
| F1.1 | **Pass** | |
| F1.2 | **Pass*** | *Dropdown works but lacks agegroup-colored badges |
| F1.3 | **Pass** | |
| F1.4 | **Pass** | |
| F1.5 | **Pass** | |
| F1.6 | **Pass** | |
| F2.1 | **Pass** | |
| F2.2 | **Pass** | |
| F2.3 | **Pass** | |
| F2.4 | **Pass** | |
| F2.5 | **Pass** | |
| F3.1 | **Pass** | |
| F3.2 | **Pass** | |
| F3.3 | **Pass** | |
| F4.1 | **Pass** | |
| F4.2 | **Pass** | |
| F4.3 | **Pass** | |
| F4.4 | **Pass** | |
| F5.1–F5.3 | **Skipped** | Needs Dropped Teams data setup |
| F6.1 | **Pass** | |
| F6.2 | **Pass** | |
| F6.3 | **Skipped** | Needs specific data setup |
| F6.4 | **Pass** | |
| F6.5 | **Pass** | |
| F6.6 | **Skipped** | Needs mixed scheduled/unscheduled data |
| F7.1–F11.4 | **Not tested** | Paused — resume after shared component refactoring |

### Fixes Applied During Testing

1. **Agegroup context in dropdowns** — Option text now shows `Agegroup: Division` format (e.g., "U10 Boys: Reef (4/36)")
2. **Panel header labels** — Changed from "Source Division" / "Target Division" to "Source Agegroup:Division" / "Target Agegroup:Division"
3. **Placeholder text** — Changed from "Select a division..." to "Select an agegroup:division..."

### Bugs Found

1. **Transfer preview doesn't auto-hide on deselect** — When unchecking all source teams after preview is shown, the Transfer Preview and Club Rep Impact panels remain visible. Should auto-dismiss (same as clicking Cancel).

### Enhancements Identified

1. **Custom dropdown with agegroup-colored badges** — Replace native `<select>` with custom dropdown matching schedule-division navigator style (agegroup color badges with contrast text)
2. **Visually distinct Dropped Teams / WAITLIST agegroups** — Not currently styled differently in dropdown
3. **Filter input clear button** — Add (✕) button on the right side of the filter input
4. **Inline team name editing** — Make team name editable inline; on save, call single-point-of-truth method to update Schedule denormalized fields (T1Name/T2Name, AgegroupName, DivName)
