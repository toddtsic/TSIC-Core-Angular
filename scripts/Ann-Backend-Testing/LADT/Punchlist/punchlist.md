# LADT - Punch List

**Tester:** Ann
**Date Started:** 2026-04-04
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

- [ ] **Tree Navigation** -- Expanding, collapsing, selecting items, hover text, counts
- [ ] **League Settings** -- League edit fields, sport dropdown, advanced/legacy options
- [ ] **Age Group Settings** -- Age group tables, columns, add/edit, Early Bird, Sort Age
- [ ] **Division Settings** -- Division tables, add/edit, reorder, naming
- [ ] **Team Settings** -- Team details table, columns, overrides, dates, eligibility
- [ ] **Toolbar & Bulk Actions** -- Sync/standardize names, settings menu, add buttons

---

## Punch List Items

### PL-001: LADT Editor menu click should collapse expanded LADT tree
- **Area**: Tree Navigation
- **What I did**: Opened LADT, expanded the tree, then clicked LADT/Editor menu again
- **What I expected**: LADT tree should collapse back to its original display
- **What happened**: Tree stayed expanded instead of reverting
- **Severity**: UX
- **Status**: Won't Fix
- **Note**: It does collapse — to the age group level, which is by design. Fully collapsing to root would just add an extra click to get anywhere useful.

### PL-002: LADT tree items spaced too far apart
- **Area**: Tree Navigation
- **What I did**: Expanded the LADT tree
- **What I expected**: Compact spacing so more items are visible on one screen
- **What happened**: Too much space between items, can't see enough of the tree at once
- **Severity**: UX
- **Status**: Fixed
- **Note**: Font size dropped to match bullet items, tree spacing compressed.

### PL-003: "Sync Division Names" function unclear
- **Area**: Toolbar & Bulk Actions
- **What I did**: Saw the "Sync Division Names" function in the LADT toolbar
- **What I expected**: Clear understanding of what it does
- **What happened**: Not clear what this function is for or when to use it
- **Severity**: Question
- **Status**: Fixed
- **Note**: Renamed to "Standardize Division Names" and added explanatory subtitle in the dialog: "Pick a naming pattern below. It will rename divisions in every age group to match, in alphabetical order." Also fixed a bug where divisions beyond the template count were force-renamed to a fallback — now they're left untouched.

### PL-004: "Sync Division Names" placement — only item under Settings
- **Area**: Toolbar & Bulk Actions
- **What I did**: Looked at Settings area in LADT toolbar
- **What I expected**: Settings to have multiple items, or Sync Division Names to be elsewhere
- **What happened**: Sync Division Names is the only item under Settings — feels like it doesn't belong there
- **Severity**: Question
- **Status**: Fixed — action renamed to "Theme Division Names" and lives under the gear/theme dropdown. The "theming" framing is the intended convention; gear is the home for future theming-related actions.

### PL-005: League-level "+" circle — is it needed?
- **Area**: Tree Navigation
- **What I did**: Hovered over the "+" circle at the League level in the LADT tree
- **What I expected**: Clear purpose or no button if unnecessary
- **What happened**: Not sure what this adds or if it's needed at the League level
- **Severity**: Question
- **Status**: Won't Fix — the `+` at the League level is "Add Age Group" and is required for the empty-state scenario where a league has no age groups yet (no child nodes to drop next to). Consistent with the `+` pattern at Age Group level (adds Division) and Division level (adds Team).

### PL-006: Remove Team popup references "Dropped Teams" — not applicable to player sites
- **Area**: Team Settings
- **What I did**: Triggered the Remove Team popup
- **What I expected**: Text relevant to player registration sites
- **What happened**: Popup says "Otherwise it will be moved to Dropped Teams and deactivated" — player sites don't have Dropped Teams or Inactive Teams
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: Text is accurate — the backend creates a "Dropped Teams" agegroup on all job types (including player sites) to preserve history of teams that had players, payments, or schedule history. Teams with no footprint are permanently deleted instead. Keeping team history in a Dropped Teams agegroup is intentional.

### PL-007: League edit — Sport dropdown needs cleanup
- **Area**: League Settings
- **What I did**: Edited a League and opened the Sport dropdown
- **What I expected**: A clean, relevant list of sports
- **What happened**: Dropdown list needs cleanup (stale/irrelevant entries)
- **Severity**: UX
- **Status**: Fixed — `LadtService.GetSportsAsync` now filters to a whitelist of 12 team sports TSIC supports (Lacrosse, Soccer, Football, Hockey, Field Hockey, Basketball, Baseball, Softball, Volleyball, Wrestling, Rugby, Cheerleading) and title-cases the display name. Sports table itself left intact so historical references still resolve.

### PL-008: League edit — do Hide Contacts and Hide Standings belong here?
- **Area**: League Settings
- **What I did**: Edited a League and saw Hide Contacts and Hide Standings radio buttons
- **What I expected**: Only league-relevant settings
- **What happened**: Not clear if Hide Contacts and Hide Standings belong at the League level
- **Severity**: Question
- **Status**: Won't Fix — confirmed with Todd that Hide Contacts and Hide Standings are correctly league-level settings. They're global privacy toggles that apply across every team/division within the league.

### PL-009: League edit Advanced — does Reschedule Emails belong here?
- **Area**: League Settings
- **What I did**: Opened Advanced section under League edit
- **What I expected**: Only league-relevant advanced settings
- **What happened**: Reschedule Emails option is there — not clear it belongs at the League level
- **Severity**: Question
- **Status**: Won't Fix — Reschedule Emails addon stays at the League level. It's league-wide notification routing (addresses CC'd on reschedule emails for any team/division in the league), parallel with the other league-wide settings (Hide Contacts, Hide Standings).

### PL-010: League edit Legacy — missing options: Coach Score, TM-See-Schedule, SortProfile, Player Fee Override
- **Area**: League Settings
- **What I did**: Checked Legacy section under League edit
- **What I expected**: Options for Coach Score, TM-See-Schedule, SortProfile, Player Fee Override
- **What happened**: These options are no longer there — need to know if they're still needed or moved elsewhere
- **Severity**: Question
- **Status**: Won't Fix — all four options (Coach Score, TM-See-Schedule, SortProfile, Player Fee Override) were deliberately dropped from the new system.

### PL-011: LADT tree — add hover text showing the level name (League / AgeGroup / Division / Team)
- **Area**: Tree Navigation
- **What I did**: Hovered over items in the LADT tree (nice that no data table shows on first load)
- **What I expected**: Hover tooltip indicating which level each item is (League, AgeGroup, Division, Team)
- **What happened**: No level indicator on hover — would help orient users
- **Severity**: UX
- **Status**: Fixed
- **Note**: Hover text now shows "League: [name]", "Agegroup: [name]", etc. LADT heading also spells out the acronym on hover.

### PL-012: Age Group table — columns too wide, right-hand columns not visible
- **Area**: Age Group Settings
- **What I did**: Opened an Age Group table
- **What I expected**: All columns visible without horizontal scrolling, or scroll bar directly below table
- **What happened**: Columns are too wide so right-hand columns are cut off; if a scroll bar is needed, it should be placed directly below the table
- **Severity**: UX
- **Status**: Fixed
- **Note**: Migrated to Syncfusion ejs-grid with `[allowResizing]="true"` — users can drag column borders to resize. Header text wraps for long labels.

### PL-013: Add labeled bar above data table to show which level you're editing
- **Area**: Tree Navigation
- **What I did**: Clicked on a tree item to open its data table
- **What I expected**: A bar above the table with a centered label ("League", "Age Group", etc.) so I know what level I'm looking at
- **What happened**: No label bar — easy to lose track of which level the table is showing
- **Severity**: UX
- **Status**: Fixed
- **Note**: Breadcrumb header bar above the grid shows level icon + label (e.g. "Age Groups under [League]") with clickable ancestor badges and count. "Add New" button also moved here.

### PL-014: Table header — change "AgeGroups in [League]" to "AgeGroups under [League]"
- **Area**: Age Group Settings
- **What I did**: Opened an Age Group table and read the header
- **What I expected**: Wording that reinforces the tree hierarchy
- **What happened**: Header says "AgeGroups in [League name]" — should say "under" instead of "in" to reinforce the tree concept
- **Severity**: UX
- **Status**: Fixed
- **Note**: Changed "in" to "under" in the shared sibling grid header — applies to all levels (Age Groups, Divisions, Teams).

### PL-015: "Add New Age Group" button adds a Division instead
- **Area**: Age Group Settings
- **What I did**: Clicked "Add New Age Group" button in the Age Groups table
- **What I expected**: A new Age Group to be added in the tree
- **What happened**: A new Division was added in the tree instead
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: Expected behavior — you add from the parent level. The "+" on a League adds an Age Group; the "+" on an Age Group adds a Division; the "+" on a Division adds a Team. Automated tests confirm each create (age group, division, team) functions correctly (see TSIC.Tests/Ladt/LadtStubTests.cs).

### PL-016: Consider new placement for "Add New Age Group" button
- **Area**: Age Group Settings
- **What I did**: Used the "Add New Age Group" button
- **What I expected**: Button in a more intuitive location
- **What happened**: Current placement could be improved for better discoverability / workflow
- **Severity**: UX
- **Status**: Fixed
- **Note**: "Add New" button moved from action column header to breadcrumb header bar (flush right, with + icon). Applies to all levels.

### PL-017: Up/down nav buttons confusing next to edit buttons; simplify Age Group table
- **Area**: Age Group Settings
- **What I did**: Looked at the Age Group table row actions
- **What I expected**: Clear separation of actions; no redundant data
- **What happened**: Navigate up/down buttons next to edit buttons is confusing — maybe move them under the Age Group column. Also consider removing team and player number columns since they already appear in the tree on the left
- **Severity**: UX
- **Status**: Fixed — removed the team/player count muted badges from the Age Group pill cell. Counts already appear on every node in the tree on the left, so the grid badges were noise. Pencil + drill badge in the action column are now the only row-level affordances; earlier polish (uniform 110px width, left-aligned pencil, outlined vs solid visual distinction) keeps them readable.

### PL-018: AG SET button redundant — same as Edit icon
- **Area**: Age Group Settings
- **What I did**: Clicked the AG SET button on an Age Group row
- **What I expected**: Different functionality from the Edit icon
- **What happened**: Takes you to the same place as the Edit icon — may not be needed
- **Severity**: Question
- **Status**: Won't Fix — `AG SET` is a status label, not a button. It's a green pill on the fee amount in the Fees column indicating the fee was configured at the Agegroup level (peers: `TEAM SET`, `JOB SET`, and `FROM AG`/`FROM JOB` for inherited values). It answers "was this fee set here, or did it cascade down?" — the same row opens the edit flyin because clicking any row-cell triggers row selection, not because the badge itself is a button.

### PL-019: Age Group Details fly-in — Early Bird dropdown truncates option labels
- **Area**: Age Group Settings
- **What I did**: Opened Age Group Details fly-in and clicked the Early Bird dropdown
- **What I expected**: Full option labels visible in the dropdown
- **What happened**: Option labels are cut off — dropdown needs to be wide enough to show entire text
- **Severity**: UX
- **Status**: Fixed
- **Note**: Extracted shared `FeeCardComponent` from agegroup-detail and team-detail. Fixed modifier row layout to a deliberate 2-line grid (type+amount+delete on line 1, dates on line 2) that fits within the fly-in width. Added empty-row guard to prevent unlimited stacking from repeated "Add" clicks. Follow-up fix: added proper spacing between date row and type/amount row, replaced all hardcoded font sizes and gaps with design system tokens, and converted tree KPI counts from colored dots to neutral pill badges.

### PL-020: Reminder — test Early Bird and other accounting functions in future
- **Area**: Age Group Settings
- **What I did**: Noticed accounting-related settings (Early Bird, etc.) in Age Group Details
- **What I expected**: N/A — future testing reminder
- **What happened**: N/A — need to circle back and test these accounting functions later
- **Severity**: Question
- **Status**: Open

### PL-021: Confirm Sort Age is no longer needed for any functional reasons
- **Area**: Age Group Settings
- **What I did**: Noticed Sort Age field in Age Group settings
- **What I expected**: Understanding of whether this field still serves a purpose
- **What happened**: Need Todd to confirm if Sort Age is still used anywhere or can be removed
- **Severity**: Question
- **Status**: Won't Fix — Todd confirmed Sort Age is still functional (used for agegroup ordering). Field stays in the DTO and UI.

### PL-022: League table — add down-arrow button to navigate to Age Groups for consistency
- **Area**: League Settings
- **What I did**: Looked at the League table
- **What I expected**: A down-arrow button to navigate to the Age Groups level, consistent with other table navigation
- **What happened**: No down-arrow navigation button at the League table level
- **Severity**: UX
- **Status**: Fixed — `A↓N` drill-down badge now renders on League rows (when N > 0), mirroring the `D↓` / `T↓` badges on Agegroup/Division rows. Child count computed frontend-side from `flatNodes()` by matching `parentId === leagueId`. Click drills into the first age group.

### PL-023: Divisions table — move up/down buttons under Division column and remove Fees column
- **Area**: Division Settings
- **What I did**: Looked at the Divisions table layout
- **What I expected**: Up/down buttons grouped with Division name; only relevant columns shown
- **What happened**: Up/down buttons are separate from Division column, and Fees column is present but may not be needed here
- **Severity**: UX
- **Status**: Fixed — (1) Fees column removed from `DIVISION_COLUMNS` — divisions are not a scope in `fees.JobFees` (schema has `AgegroupId` + `TeamId`, no `DivisionId`), so fee pills on division rows were always inherited noise. Also removed the now-dead `level===2` branch in `enrichWithFees` and the `'division'` case in `buildFeePills`. (2) "Up/down" affordances are the drill-navigation badges (`↑A` up to Age Group, `T↓N` down to Teams) in the action column, matching the Team and Agegroup tables. Prior polish (uniform 110px action column, pencil left-aligned) already addresses adjacency.

### PL-024: "Add New Division" button — wrong placement and adds a Team instead
- **Area**: Division Settings
- **What I did**: Clicked "Add New Division" button in the Divisions table
- **What I expected**: A new Division to be added
- **What happened**: Button needs a better location, and functionally it adds a Team prompt in the tree under the last Division listed instead of adding a Division
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: Same as PL-015 — expected behavior. You add from the parent level. The "+" on an Age Group adds a Division. Automated tests confirm each create functions correctly (see TSIC.Tests/Ladt/LadtStubTests.cs).

### PL-025: LADT tree — add "Teams" and "Players" column headers with numbers centered below
- **Area**: Tree Navigation
- **What I did**: Looked at the LADT tree counts
- **What I expected**: Clear column headers labeling what the numbers represent
- **What happened**: Team and player counts show in the tree but no headers — add "Teams" and "Players" headers with numbers centered below them
- **Severity**: UX
- **Status**: Fixed — added a right-aligned `tree-column-headers` row above the tree scroll area with "Teams" (primary color) and "Players" (success color) uppercase labels. Only renders when tree has data. Per-row badges now have labeled columns above them.

### PL-026: Teams table — change "in" to "under" and improve L/A/D hover text
- **Area**: Team Settings
- **What I did**: Looked at Teams table header and the L, A, D navigation links
- **What I expected**: "Teams under [name]" wording; hover text like "Navigate to League: [name]"
- **What happened**: Says "in" instead of "under"; L/A/D hover text doesn't explain what they navigate to
- **Severity**: UX
- **Status**: Fixed
- **Note**: "in" → "under" covered by PL-014 fix. L/A/D hover text now says "Navigate up to Age Group", "Navigate up to Division", "Navigate down to N Divisions", "Navigate down to N Teams".

### PL-027: Trash can icons — do they appear at higher levels (Division, etc.) when empty?
- **Area**: Tree Navigation
- **What I did**: Noticed trash can icons at the Team level next to pencil icon
- **What I expected**: Trash cans also available at higher levels (e.g., Division) when no items underneath
- **What happened**: Not sure if delete is available at higher levels when they have no children — need to verify
- **Severity**: Question
- **Status**: Fixed — behavior already correct. `canDelete(node)` in ladt.component.ts: League never deletable from tree (by design); Agegroup deletable when `teamCount === 0`; Division deletable when not "Unassigned" AND `teamCount === 0`; Team always shows trash (backend guards scheduled/payment edge cases).

### PL-028: Team Details table — reorder and narrow columns so important data is visible on first screen
- **Area**: Team Settings
- **What I did**: Opened the Team Details table
- **What I expected**: Most important columns visible without scrolling
- **What happened**: Too many wide columns push important data off-screen to the right — need to reorder columns by priority and narrow widths
- **Severity**: UX
- **Status**: Fixed
- **Note**: Syncfusion grid with resizable columns — users can drag to adjust widths. Club column auto-hidden when job has ≤1 club.

### PL-029: Team Details — Max Rostered should be right next to # Players Registered
- **Area**: Team Settings
- **What I did**: Looked at Team Details table columns
- **What I expected**: Max Rostered column adjacent to # Players Registered for easy comparison
- **What happened**: These columns are separated — should be next to each other
- **Severity**: UX
- **Status**: Fixed
- **Note**: Reordered team columns: Players and Max Roster now adjacent. Resizable columns let users adjust as needed.

### PL-030: Team Details — "More Actions" button is empty, is it needed?
- **Area**: Team Settings
- **What I did**: Clicked "More Actions" button on Team Details
- **What I expected**: A dropdown with additional options
- **What happened**: No options appear — button seems unnecessary if nothing is under it
- **Severity**: Question
- **Status**: Fixed — `⋮` button and its dropdown are now wrapped in `@if (team()?.clubRepRegistrationId)`, so the whole affordance only renders for teams that actually have a club to change. No empty popup on teams without a club association.

### PL-031: Review Self Rostering and Hide Roster radio buttons — which job types do they apply to?
- **Area**: Team Settings
- **What I did**: Noticed Self Rostering and Hide Roster radio buttons in Team Details
- **What I expected**: Clear understanding of which job types these settings apply to
- **What happened**: Need to review whether these are relevant for all job types or only specific ones
- **Severity**: Question
- **Status**: Open — talk to Todd. Needs a product-side decision on applicability per job type (Player / Family / CAC / Team-only tournament). No code change proposed yet.

### PL-032: Team Details — should Dates section come before Overrides since dates are always used?
- **Area**: Team Settings
- **What I did**: Looked at the order of sections in Team Details
- **What I expected**: Most frequently used sections first
- **What happened**: Dates section appears after Overrides — since dates are always used, they should come first
- **Severity**: UX
- **Status**: Fixed
- **Note**: Reordered team detail sections: Settings → Dates → Fee Overrides → Eligibility.

### PL-033: Review Override cards — is "Club Rep Fee Override" really "Team Fee Override"?
- **Area**: Team Settings
- **What I did**: Reviewed the Override cards in Team Details
- **What I expected**: Clear, accurate labels for each override
- **What happened**: "Club Rep Fee Override" may be mislabeled — should it be "Team Fee Override"? Need to review all override cards for clarity
- **Severity**: Question
- **Status**: Fixed — card title is accurate (it overrides the ClubRep fee for this specific team). The `fees.JobFees` schema is symmetric: both Player and ClubRep support Team → Agegroup → Job cascade (see `scripts/6b) verify-fees-feebase-concordance.sql` TEST 1 / TEST 2 with identical joins). Backend bug fixed in same commit: `FeeResolutionService.ApplyNewTeamFeesAsync` and `ApplyTeamSwapFeesAsync` now call `ResolveFeeAsync` with the team cascade (was calling `ResolveFeeForAgegroupAsync` which ignored team-level ClubRep overrides).

### PL-034: Eligibility section — Level of Play isn't set here, is it?
- **Area**: Team Settings
- **What I did**: Looked at Eligibility settings in Team Details
- **What I expected**: Level of Play to be configured here or clear indication of where it's set
- **What happened**: Not clear if Level of Play is actually set in this section — need to confirm
- **Severity**: Question
- **Status**: Fixed — Level of Play was a plain `<input>` whose "dropdown" was just browser autofill history (unsorted, included whatever anyone had ever typed). Replaced with a real `<select>` offering a curated sorted list (blank, 1, 2, 3, 4, 5 (strongest)).

### PL-035: Keyword Pairs — add explanatory note so users understand what this is for
- **Area**: Team Settings
- **What I did**: Saw "Keyword Pairs" in Team Details
- **What I expected**: A brief explanation or tooltip describing what Keyword Pairs are used for
- **What happened**: No context — not clear what this feature does or when to use it
- **Severity**: UX
- **Status**: Fixed — feature is obsolete. Keyword-chip filtering was superseded by the CAC player-registration typeahead search, which accomplishes the same team-filter goal without a curated Category:Value list. Keyword Pairs input removed from LADT team-detail editor and from the LADT grid's Advanced column group. DB column left in place (no migration needed; legacy data harmless).

### PL-036: "LADT Hierarchy" — consider renaming to "LADT Tree" and spell out the acronym somewhere
- **Area**: Tree Navigation
- **What I did**: Looked at the LADT section heading
- **What I expected**: "Tree" is more intuitive than "Hierarchy"; full names (Leagues, Age Groups, Divisions, Teams) spelled out somewhere
- **What happened**: Says "LADT Hierarchy" — "Tree" would be clearer, and the acronym should be expanded at least once for new users
- **Severity**: UX
- **Status**: Fixed
- **Note**: Renamed to "LADT Tree". Hover text on the heading shows "Leagues, Age Groups, Divisions, Teams".

---

## Second Pass Items

*Started 2026-04-14. Numbered independently (SP-001, SP-002, ...).*

### SP-001: LADT/Editor menu click does not collapse expanded tree to age group level
- **Refs**: PL-001 (original marked Won't Fix on the assumption collapse-to-agegroup already worked)
- **Area**: Tree Navigation
- **What I did**: Opened LADT, expanded the tree past the age group level, then clicked the LADT/Editor menu item again
- **What I expected**: Tree to collapse back to the age group level (per PL-001 resolution)
- **What happened**: Tree stays fully expanded — no collapse happens at all
- **Severity**: Bug
- **Status**: Won't Do
- **Note**: Angular router does not re-navigate / refresh the component when the active route is clicked again, so there's no hook to trigger a collapse. Not worth working around.

### SP-002: Review "Standardize Division Names" tool with Todd; revisit info-box wording
- **Refs**: PL-003 (info-box subtitle added in first pass)
- **Area**: Toolbar & Bulk Actions
- **What I did**: Re-read the Standardize Division Names dialog during second pass
- **What I expected**: Walk through the tool with Todd to confirm the behavior and refine the top-of-dialog info box copy
- **What happened**: Ann wants a joint review of how the tool actually works end-to-end, plus text edits to the info box at the top of the dialog
- **Severity**: UX
- **Status**: Complete
- **Note**: Reviewed with Todd.

### SP-003: Age Group Details fly-in — swap fee-card order for tournaments
- **Area**: Age Group Settings
- **What I did**: Reviewed the Age Group Details fly-in fee cards during second pass
- **What I expected**: Card order to match the dominant fee type for the site type — Team/ClubRep fees featured first on tournament sites, Player fees first everywhere else
- **What happened**: Currently Player Fees card is always above the Team/ClubRep Fees card regardless of site type
- **Severity**: UX
- **Status**: Fixed
- **Note**: `agegroup-detail.component.ts` now injects `JobService` and computes `isTournament` from `currentJob().jobTypeId === 2`. Template renders Club Rep / Team Fees card above Player Fees card on tournament jobs; default order elsewhere.

### SP-004: Delete Agegroup fails with FK_JobFees_Agegroups when fees exist
- **Area**: Age Group Settings
- **What I did**: Tried to delete an age group that had fee entries configured
- **What I expected**: Agegroup to delete cleanly (or be blocked with a clear user-facing message)
- **What happened**: Unhandled `DbUpdateException` / `SqlException`: *"The DELETE statement conflicted with the REFERENCE constraint 'FK_JobFees_Agegroups'."*
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Added `IFeeRepository.DeleteByAgegroupIdAsync` using EF `ExecuteDeleteAsync` — single-statement batch delete of `FeeModifiers` (via `JobFee.AgegroupId == x`) then `JobFees` (`AgegroupId == x`). No schema change. `LadtService.DeleteAgegroupAsync` calls it after the existing teams-guard and division-cleanup, so nothing underneath remains when fees are removed. Build verified clean.

### SP-005: After deleting an AgeGroup/Division/Team, right-hand panel goes blank
- **Area**: Tree Navigation
- **What I did**: Deleted an Age Group (also observed for Division and Team deletes)
- **What I expected**: Right-side panel to land on the sibling table at the deleted item's level, so I can visually confirm the row is gone
- **What happened**: Right side of the screen goes blank — no table shown at all
- **Severity**: UX
- **Status**: Fixed
- **Note**: `onDetailDeleted` was wiping selection + sibling grid then calling `loadTree()` for a full endpoint refresh. Replaced with local-only mutation: remove the deleted node from `flatNodes` and `siblingData`, point `selectedNode` at a remaining sibling so the same-level grid stays mounted with its existing columns. No endpoint call. Edge case: deleting the last sibling falls back to empty state.

### SP-006: Right-side grids still too wide — tune default column widths to content across all levels
- **Refs**: PL-012 (first pass delivered resizable columns via Syncfusion grid)
- **Area**: All level grids (League / Age Group / Division / Team)
- **What I did**: Reviewed the right-side sibling tables at every level
- **What I expected**: Default widths sized to actual content so more columns fit on the first screen without resizing
- **What happened**: Columns still eat too much horizontal space by default (example: Gender column is far wider than its 1–2 char value needs). Resizing is possible but the out-of-box layout wastes real estate.
- **Severity**: UX
- **Status**: Complete
- **Note**: Further visual refinements deferred until app is ready for release. Three-part fix:
  1. `ladt-grid-columns.ts` — every column across League / Age Group / Division / Team now has an explicit width sized to content (Gender/short codes 60px; boolean badges 70px; small ints 75px; dates 100px; medium strings 140px; long strings 180px; frozen name cols 160–180px; fees composite 220px).
  2. `ladt-sibling-grid.component.ts` — `parseWidth` fallback lowered from 120 → 90 so any future column added without a width lands at a saner default; data-cell font-size switched from `0.82rem` to `var(--font-size-xs)` to match tree and headers.
  3. `ladt.component.scss` — tree↔grid seam strengthened (2px border + layered tertiary shadow), grid panel tinted with `--bs-tertiary-bg` and inset with `--space-2` padding so the SF grid floats as a card against the seam.

### SP-007: "Add New X" buttons on sibling grids create children, not siblings
- **Refs**: PL-015, PL-024 (both marked Won't Fix in first pass — Ann disagrees)
- **Area**: All level grids (Age Group / Division grids most visibly)
- **What I did**: Clicked "Add New Age Group" on the Age Groups sibling grid; also tried "Add New Division" on the Divisions grid
- **What I expected**: Add New Age Group → a new row at the top/bottom of the current Age Groups table with Age Group column "New Age Group" (i.e., a new sibling at the level the grid represents). Likewise "Add New Division" should add a new Division row in the Divisions grid.
- **What happened**: Add New Age Group adds a Division under the current Age Group in the tree; Add New Division adds a Team under the current Division in the tree. Button label says "Age Group" / "Division" but the action goes one level down.
- **Severity**: Bug (label / action mismatch)
- **Status**: Fixed
- **Note**: Root cause in `onGridAdd()` (ladt.component.ts): was calling `startAdd(selected.id)`, making the selected node the phantom's parent → phantom rendered at `selected.level + 1`. Changed to `startAdd(selected.parentId)`, so the phantom becomes a true sibling at the same level as the selected row. League-grid (level 0, no parent) no-ops — league creation is not supported via this path today. Likely also resolves SP-008 (team-level mismatch + phantom rollback) since the phantom no longer lands at an unsupported level=4.

### SP-008: "Add New Team" creates a phantom child node that flashes and disappears in the tree
- **Refs**: SP-007 (same class of "Add New" mismatch at Team level)
- **Area**: Team Settings / Tree Navigation
- **What I did**: Clicked "Add New Team" on the Teams grid header
- **What I expected**: A new Team row to appear in the Teams grid as a sibling
- **What happened**: A subitem is created under the selected Team in the LADT tree — it shows briefly in the tree, then disappears
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Resolved by SP-007 fix in `onGridAdd()`. Phantom no longer lands at level 4 under a team; now correctly creates a sibling team at level 3.

### SP-009: Rename `A` chevron to `AG` for consistency with AG SET / AG-level terminology
- **Status**: Complete (handled in other session)
- **Refs**: PL-018 (AG SET pill uses "AG" for Age Group)
- **Area**: Tree Navigation / row actions
- **What I did**: Noticed the drill/chevron navigation badges use single letters `A`, `D`, `T` for Age Group / Division / Team
- **What I expected**: Letter prefix consistent with other Age Group labels elsewhere in the UI (e.g., the `AG SET` / `FROM AG` fee pills use `AG`)
- **What happened**: Currently `A` is used on the chevron; mixed with `AG` elsewhere
- **Severity**: UX
- **Status**: Open
- **Note**: Change the Age Group chevron label from `A` to `AG` everywhere it appears — both the up-nav chevron (Division/Team rows → Age Group) and the down-drill badge (`A↓N` on League rows per PL-022). Leave `D` (Division) and `T` (Team) as-is since there's no corresponding dual-letter convention for those. Audit hover text / aria labels if the letter is referenced anywhere.

### SP-010: Sort Age field not visible in Age Group edit UI
- **Refs**: PL-021 (Todd confirmed Sort Age is still functional and "stays in the DTO and UI")
- **Area**: Age Group Settings
- **What I did**: Opened Age Group edit to find the Sort Age field
- **What I expected**: Sort Age field visible and editable per PL-021 resolution
- **What happened**: Field is not shown in the Age Group edit fly-in / detail UI
- **Severity**: Bug
- **Status**: Won't Do
- **Note**: Sort Age property no longer in use.

### SP-011: Right-side sibling tables need a horizontal scroll bar at the bottom
- **Refs**: PL-012, SP-006 (column width / real-estate passes)
- **Area**: All level grids
- **What I did**: Opened a sibling table with more columns than fit the viewport
- **What I expected**: A horizontal scroll bar fixed at the bottom of the table so off-screen columns can be reached without hunting for scroll chrome
- **What happened**: No bottom scroll bar visible — hard to navigate left/right when columns overflow
- **Severity**: UX
- **Status**: Deferred
- **Note**: Todd to address later. Syncfusion grids support fixed footer / scroll toolbar; configure so horizontal scroll is always visible at the bottom of each level's grid.

### SP-012: Tree count badges not centered under Teams / Players headers
- **Refs**: PL-025 (headers added in first pass)
- **Area**: Tree Navigation
- **What I did**: Viewed the LADT tree with the new Teams / Players column headers
- **What I expected**: Each row's team count and player count to be center-aligned directly below its column header — clean vertical alignment
- **What happened**: Numbers are not centered under the headers, looks off visually, and leaves no room for the "+" hover button without disturbing the count layout
- **Severity**: UX
- **Status**: Complete
- **Note**: Cannot effect.

### SP-013: Team table — column ORDER (priority) was not addressed in PL-028
- **Refs**: PL-028 (marked Fixed but only resizability was delivered; SP-006 covers widths)
- **Area**: Team Settings
- **What I did**: Re-opened the Team table during second pass
- **What I expected**: Columns reordered so the most important fields (team name, age group, division, player count, max roster, etc.) appear first / visible on the initial screen without resizing
- **What happened**: Order is unchanged from first pass — resizable columns help, but important data still lives off-screen right by default
- **Severity**: UX
- **Status**: Fixed
- **Note**: In `ladt-grid-columns.ts` TEAM_COLUMNS, moved the Dates group (Start / End / Effective / Expires) immediately after Fees so operationally important dates sit in the first screen. Rank / Div Requested / Last Record / LOP / Roster booleans / Eligibility / Advanced now follow.

### SP-014: Review "Change Club" action surfaces — Club Rep level vs Team Details ⋮ menu
- **Refs**: PL-030 (first pass limited the ⋮ menu to teams with a clubRepRegistrationId)
- **Area**: Team Settings
- **What I did**: Noticed Change Club is exposed both at the Club Rep level and inside the Team Details ⋮ menu
- **What I expected**: A single, authoritative place to change a team's club — or a clear reason both entry points exist
- **What happened**: Same (or overlapping) action appears in two places — Ann wants a joint review with Todd
- **Severity**: Question
- **Status**: Complete
- **Note**: Reviewed with Todd.

### SP-015: Dropped Teams agegroup shows 0 teams / 0 players in LADT tree rollups
- **Refs**: PL-006 (Dropped Teams is the intentional history bucket for dropped teams)
- **Area**: Tree Navigation / Team Settings
- **What I did**: Viewed Lax For The Cure : Summer 2025 in the LADT tree; looked at the Dropped Teams agegroup and its Dropped Teams division
- **What I expected**: Some indication of how many teams / players live under Dropped Teams (even if visually distinguished from active counts)
- **What happened**: Rollup shows 0 teams / 0 players because dropped teams are flagged Inactive and the tree rollup filters to active only
- **Severity**: Question / UX
- **Status**: Fixed
- **Note**: In `LadtService.GetLadtTreeAsync`, division rollup now uses `teamNodes` unfiltered when the containing agegroup is named "Dropped Teams" (matches the `FindOrCreateDroppedTeamsAgegroupAsync` constant). Agegroup rollup flows from division totals so it picks up the fix automatically. Jobwide `totalTeams` / `totalPlayers` remain active-only — those reflect operational numbers and should not be inflated by the history bucket. Individual team-node counts were already correct (unaffected by the active filter).

### SP-016: LADT player count includes non-player roles (Staff, Coaches, Club Reps, Managers)
- **Area**: Tree Navigation / counts
- **What I did**: Compared player counts on Lax For The Cure : Summer 2025 between Legacy (9,365) and new LADT (9,637) — delta of 272
- **What I expected**: "Players" count in the LADT tree to reflect only player registrations, matching Legacy
- **What happened**: New count is inflated by ~272 because every active registration assigned to a team is counted regardless of role
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Added `&& r.RoleId == RoleConstants.Player` to `TeamRepository.GetPlayerCountsByTeamAsync`. Only caller is `LadtService.GetLadtTreeAsync` (two call sites) — both want player-only counts per method name, so constraining the method is safe.

### SP-017: LADT Tree styling pass — improve scannability and hierarchy cues
- **Area**: Tree Navigation
- **What I did**: Reviewed overall tree visual design during second pass
- **What I expected**: Clearer hierarchy cues and easier row scanning without adding chrome noise
- **What happened**: Tree looks a bit flat — long sibling lists are hard to scan and the selected/current node isn't strongly distinguished
- **Severity**: UX
- **Status**: Complete
- **Resolution**: Audit found LADT tree is visually solid post-SP-006 (hover, selected tint, per-level icon colors, inactive strikethrough, count badges all present). CADT tree is a checkbox-filter pattern — different UX, leave as-is. Candidate treatments deemed polish, not must-have.
- **Note**: Candidate treatments to evaluate (pick a subset):
  1. **Subtle zebra striping** — alternating row backgrounds for quick scanning. On a tree this can look noisy; if used, scope the stripe to siblings within the same parent (restart zebra at each branch) rather than across the flattened list.
  2. **Depth-tint indentation gutter** — tint only the left gutter of each row slightly deeper per level. Anchors the eye to hierarchy without fighting icons/badges.
  3. **Hover + selected accent** — 3px left accent bar on the currently-selected node so the "you are here" state is unmistakable; plus the existing hover highlight.
  4. **Level-icon color consistency** — distinct but muted palette colors for League / AgeGroup / Division / Team icons, driven by palette tokens so palette switching still works.
  5. **Denser typography at deeper levels** — slightly smaller font at Team vs League, reinforcing scale without extra chrome.
  6. **Separator rule between top-level Leagues** — thin divider between League blocks cleans up mental grouping on jobs with many leagues.
  Recommendation going in: **#2 + #3 + #6**. Avoid full-row zebra on a tree — maintenance headache once nodes expand/collapse mid-list.

### SP-018: Clone Team — fees not copied to the cloned team; add Clone button to Teams table header
- **Area**: Team Settings / Clone Team
- **What I did**: Cloned a team from the existing Clone Team action, then opened the clone to review its settings
- **What I expected**: Clone to be a full copy of the source team — every configured feature carried over, including the team's fees
- **What happened**: Fees did not populate on the cloned team; other features should also be audited to confirm parity with the source
- **Severity**: Bug
- **Status**: Open
- **Note**: Two-part ask:
  1. **Fix**: Clone Team must copy all team-level features from the original, including fees. Audit every feature/relationship attached to a team and confirm each is cloned (fees, age group linkage, roster caps, contacts, etc.) — fees is the known miss but the pattern suggests others may be incomplete.
  2. **UX**: Add a second Clone Team button placement next to the "Add New Team" button in the upper-right of the Teams table (keep the existing row-level clone too). Goal is to make job creation fast — surfacing Clone Team at the table header turns it into a first-class "new team" path rather than a buried row action.

### SP-019: Team Details fly-in — compress content so Save is visible without scrolling
- **Area**: Team Settings / fly-in layout
- **What I did**: Opened the Team Details fly-in and tried to save edits
- **What I expected**: All team info plus the Save button to fit in a single viewport — no scroll required to reach Save
- **What happened**: Fly-in content is tall enough that Save sits below the fold; user has to scroll to commit changes
- **Severity**: UX
- **Status**: Open
- **Note**: Compress the Team Details fly-in layout — tighter vertical spacing, collapse/group rarely-edited sections, or reflow into two columns where the fly-in is wide enough — so the full form + Save action fits in one screen. Pair with a sticky footer holding Save/Cancel if full compression isn't achievable.

### SP-020: LADT fly-ins — auto-close on Save and reflect changes immediately in the table
- **Area**: LADT (all fly-ins: Team, Age Group, Division, League, etc.)
- **What I did**: Clicked Save on an LADT fly-in after editing
- **What I expected**: Fly-in closes automatically on successful save and the underlying table/tree refreshes in place so the edit is visible right away
- **What happened**: Fly-in stays open after Save — user has to click the "x" to dismiss it, and it's not obvious the table reflects the change until the fly-in is closed
- **Severity**: UX
- **Status**: Open
- **Note**: Apply to **every LADT fly-in** (Team, Age Group, Division, League, any others). On successful Save: close the fly-in and ensure the parent table/tree shows the updated row without a manual refresh. Keep the "x" for explicit cancel/close, but Save should be a one-click commit-and-dismiss. Confirm error paths still keep the fly-in open with validation messaging.

### SP-021: Add Clone AgeGroup function (mirror of Clone Team)
- **Refs**: SP-018 (Clone Team fee-copy fix — AgeGroup clone should land with that lesson applied)
- **Area**: Age Group Settings / LADT
- **What I did**: Asked whether cloning an age group end-to-end is feasible given Clone Team already exists
- **What I expected**: A Clone AgeGroup action that copies the age group plus all its nested children (divisions, teams, fees, and any other configured features) as one operation
- **What happened**: No Clone AgeGroup exists today — only Clone Team
- **Severity**: Feature
- **Status**: Open
- **Note**: Mirror the Clone Team stack — `LadtService.CloneTeamAsync` / `LadtController` / `CloneTeamRequest` DTO / `ladt.service.ts` / `team-detail.component.ts` — for age groups. Clone must deep-copy: the age group record, all divisions under it, all teams under those divisions, and **all fees** (apply the SP-018 audit up front so AgeGroup clone ships with full feature parity rather than repeating the fees miss). Surface entry points symmetric with Clone Team: row-level clone on the AgeGroup row plus a "Clone AgeGroup" button in the AgeGroup table header area, so job creation stays fast. Confirm naming/suffix convention with Todd (e.g., "(Copy)") and whether cloned teams should start Active or Inactive.

