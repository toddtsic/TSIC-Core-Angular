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
- **Status**: Open
- **Note**: Ann agrees collapsing all the way to root is wrong; the correct behavior is collapse-to-agegroup as originally stated in PL-001. Needs investigation into why the menu click isn't triggering that collapse.

### SP-002: Review "Standardize Division Names" tool with Todd; revisit info-box wording
- **Refs**: PL-003 (info-box subtitle added in first pass)
- **Area**: Toolbar & Bulk Actions
- **What I did**: Re-read the Standardize Division Names dialog during second pass
- **What I expected**: Walk through the tool with Todd to confirm the behavior and refine the top-of-dialog info box copy
- **What happened**: Ann wants a joint review of how the tool actually works end-to-end, plus text edits to the info box at the top of the dialog
- **Severity**: UX
- **Status**: Open
- **Note**: Action item — schedule walkthrough with Todd, then propose revised info-box copy.

### SP-003: Age Group Details fly-in — swap fee-card order for tournaments
- **Area**: Age Group Settings
- **What I did**: Reviewed the Age Group Details fly-in fee cards during second pass
- **What I expected**: Card order to match the dominant fee type for the site type — Team/ClubRep fees featured first on tournament sites, Player fees first everywhere else
- **What happened**: Currently Player Fees card is always above the Team/ClubRep Fees card regardless of site type
- **Severity**: UX
- **Status**: Open
- **Note**: On tournament sites, put Club Rep / Team Fees card above the Player Fees card. On all other site types (league, player, family, CAC), keep Player Fees card on top. Goal: reduce data-entry errors when setting up a tourney by surfacing the relevant fee type first.

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
- **Status**: Open
- **Note**: On successful delete, navigate/select to the parent's children-grid at the same level (e.g., deleting an Age Group shows the League's Age Groups grid; deleting a Division shows the Age Group's Divisions grid; deleting a Team shows the Division's Teams grid). Gives immediate visual confirmation the item was removed.

### SP-006: Right-side grids still too wide — tune default column widths to content across all levels
- **Refs**: PL-012 (first pass delivered resizable columns via Syncfusion grid)
- **Area**: All level grids (League / Age Group / Division / Team)
- **What I did**: Reviewed the right-side sibling tables at every level
- **What I expected**: Default widths sized to actual content so more columns fit on the first screen without resizing
- **What happened**: Columns still eat too much horizontal space by default (example: Gender column is far wider than its 1–2 char value needs). Resizing is possible but the out-of-box layout wastes real estate.
- **Severity**: UX
- **Status**: Open
- **Note**: Pass on column defaults at all four levels. Size narrow columns (Gender, counts, short codes) to content; reserve flex growth for name/label columns. Goal: maximize visible columns on first paint before any user resize.

### SP-007: "Add New X" buttons on sibling grids create children, not siblings
- **Refs**: PL-015, PL-024 (both marked Won't Fix in first pass — Ann disagrees)
- **Area**: All level grids (Age Group / Division grids most visibly)
- **What I did**: Clicked "Add New Age Group" on the Age Groups sibling grid; also tried "Add New Division" on the Divisions grid
- **What I expected**: Add New Age Group → a new row at the top/bottom of the current Age Groups table with Age Group column "New Age Group" (i.e., a new sibling at the level the grid represents). Likewise "Add New Division" should add a new Division row in the Divisions grid.
- **What happened**: Add New Age Group adds a Division under the current Age Group in the tree; Add New Division adds a Team under the current Division in the tree. Button label says "Age Group" / "Division" but the action goes one level down.
- **Severity**: Bug (label / action mismatch)
- **Status**: Open
- **Note**: Ann's mental model: the grid shows X at a given level; an "Add New X" button on that grid should create another X (sibling). Todd's prior reasoning (PL-015/PL-024) was "you add from the parent level" — needs revisiting given the button text explicitly names the grid's level. Either rewire the action to create a sibling, or rename the button to match what it actually does.

### SP-008: "Add New Team" creates a phantom child node that flashes and disappears in the tree
- **Refs**: SP-007 (same class of "Add New" mismatch at Team level)
- **Area**: Team Settings / Tree Navigation
- **What I did**: Clicked "Add New Team" on the Teams grid header
- **What I expected**: A new Team row to appear in the Teams grid as a sibling
- **What happened**: A subitem is created under the selected Team in the LADT tree — it shows briefly in the tree, then disappears
- **Severity**: Bug
- **Status**: Open
- **Note**: Two problems: (1) same level-mismatch pattern as SP-007 — action fires one level below the grid; (2) the phantom node appearing then disappearing suggests the create call succeeds optimistically then gets rolled back / filtered out because Teams have no valid child type. Needs investigation alongside SP-007 fix.

### SP-009: Rename `A` chevron to `AG` for consistency with AG SET / AG-level terminology
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
- **Status**: Open
- **Note**: Check whether the field was dropped from the template, hidden behind a flag, or needs to be added back to the Age Group detail component.

### SP-011: Right-side sibling tables need a horizontal scroll bar at the bottom
- **Refs**: PL-012, SP-006 (column width / real-estate passes)
- **Area**: All level grids
- **What I did**: Opened a sibling table with more columns than fit the viewport
- **What I expected**: A horizontal scroll bar fixed at the bottom of the table so off-screen columns can be reached without hunting for scroll chrome
- **What happened**: No bottom scroll bar visible — hard to navigate left/right when columns overflow
- **Severity**: UX
- **Status**: Open
- **Note**: Syncfusion grids support fixed footer / scroll toolbar; configure so horizontal scroll is always visible at the bottom of each level's grid. Complements SP-006 (better default widths) — even with tuned widths, users with many columns still need easy horizontal nav.

### SP-012: Tree count badges not centered under Teams / Players headers
- **Refs**: PL-025 (headers added in first pass)
- **Area**: Tree Navigation
- **What I did**: Viewed the LADT tree with the new Teams / Players column headers
- **What I expected**: Each row's team count and player count to be center-aligned directly below its column header — clean vertical alignment
- **What happened**: Numbers are not centered under the headers, looks off visually, and leaves no room for the "+" hover button without disturbing the count layout
- **Severity**: UX
- **Status**: Open
- **Note**: Center-align count values under each header. Side benefit: gives the `+` hover affordance room to appear without shifting the counts.

### SP-013: Team table — column ORDER (priority) was not addressed in PL-028
- **Refs**: PL-028 (marked Fixed but only resizability was delivered; SP-006 covers widths)
- **Area**: Team Settings
- **What I did**: Re-opened the Team table during second pass
- **What I expected**: Columns reordered so the most important fields (team name, age group, division, player count, max roster, etc.) appear first / visible on the initial screen without resizing
- **What happened**: Order is unchanged from first pass — resizable columns help, but important data still lives off-screen right by default
- **Severity**: UX
- **Status**: Open
- **Note**: Do a priority pass on Team column order. Pair with SP-006 (default widths) so a fresh-load Team table fits the most important columns without any user adjustment.

### SP-014: Review "Change Club" action surfaces — Club Rep level vs Team Details ⋮ menu
- **Refs**: PL-030 (first pass limited the ⋮ menu to teams with a clubRepRegistrationId)
- **Area**: Team Settings
- **What I did**: Noticed Change Club is exposed both at the Club Rep level and inside the Team Details ⋮ menu
- **What I expected**: A single, authoritative place to change a team's club — or a clear reason both entry points exist
- **What happened**: Same (or overlapping) action appears in two places — Ann wants a joint review with Todd
- **Severity**: Question
- **Status**: Open
- **Note**: Schedule walkthrough with Todd: confirm whether the Team-level ⋮ Change Club action duplicates the Club-Rep-level flow, whether they hit the same backend, and whether one should be removed for clarity.

### SP-015: Dropped Teams agegroup shows 0 teams / 0 players in LADT tree rollups
- **Refs**: PL-006 (Dropped Teams is the intentional history bucket for dropped teams)
- **Area**: Tree Navigation / Team Settings
- **What I did**: Viewed Lax For The Cure : Summer 2025 in the LADT tree; looked at the Dropped Teams agegroup and its Dropped Teams division
- **What I expected**: Some indication of how many teams / players live under Dropped Teams (even if visually distinguished from active counts)
- **What happened**: Rollup shows 0 teams / 0 players because dropped teams are flagged Inactive and the tree rollup filters to active only
- **Severity**: Question / UX
- **Status**: Open
- **Note**: Root cause confirmed in `LadtService.GetLadtTreeAsync` — line 98 bumps `totalTeams` / `totalPlayers` only when `team.Active == true`, and line 121 computes division rollups from `teamNodes.Where(t => t.Active)`. Individual dropped-team nodes still render (with the "Inactive" badge per `ladt.component.html` lines 138–139), but the parent counts never include them. Discuss with Todd whether to add a separate "Dropped: N" count on the Dropped Teams node, keep as-is, or change rollup semantics.

### SP-016: LADT player count includes non-player roles (Staff, Coaches, Club Reps, Managers)
- **Area**: Tree Navigation / counts
- **What I did**: Compared player counts on Lax For The Cure : Summer 2025 between Legacy (9,365) and new LADT (9,637) — delta of 272
- **What I expected**: "Players" count in the LADT tree to reflect only player registrations, matching Legacy
- **What happened**: New count is inflated by ~272 because every active registration assigned to a team is counted regardless of role
- **Severity**: Bug
- **Status**: Open
- **Note**: Root cause in `TeamRepository.GetPlayerCountsByTeamAsync` (TeamRepository.cs:443-448) — `.Where(r => r.JobId == jobId && r.BActive == true && r.AssignedTeamId != null)` has no role filter, so Coaches / Managers / ClubReps / Staff with `AssignedTeamId` are all summed into the "Players" total. Fix: add a role filter to restrict to player role(s) only (confirm with Todd which RoleId value(s) qualify — likely the player role constant used elsewhere). Audit any other places that use this method to make sure the new filter matches their intent.

### SP-017: LADT Tree styling pass — improve scannability and hierarchy cues
- **Area**: Tree Navigation
- **What I did**: Reviewed overall tree visual design during second pass
- **What I expected**: Clearer hierarchy cues and easier row scanning without adding chrome noise
- **What happened**: Tree looks a bit flat — long sibling lists are hard to scan and the selected/current node isn't strongly distinguished
- **Severity**: UX
- **Status**: Open
- **Note**: Candidate treatments to evaluate (pick a subset):
  1. **Subtle zebra striping** — alternating row backgrounds for quick scanning. On a tree this can look noisy; if used, scope the stripe to siblings within the same parent (restart zebra at each branch) rather than across the flattened list.
  2. **Depth-tint indentation gutter** — tint only the left gutter of each row slightly deeper per level. Anchors the eye to hierarchy without fighting icons/badges.
  3. **Hover + selected accent** — 3px left accent bar on the currently-selected node so the "you are here" state is unmistakable; plus the existing hover highlight.
  4. **Level-icon color consistency** — distinct but muted palette colors for League / AgeGroup / Division / Team icons, driven by palette tokens so palette switching still works.
  5. **Denser typography at deeper levels** — slightly smaller font at Team vs League, reinforcing scale without extra chrome.
  6. **Separator rule between top-level Leagues** — thin divider between League blocks cleans up mental grouping on jobs with many leagues.
  Recommendation going in: **#2 + #3 + #6**. Avoid full-row zebra on a tree — maintenance headache once nodes expand/collapse mid-list.

