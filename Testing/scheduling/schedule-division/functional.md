# Functional Tests: Scheduling — Schedule by Division

> **Status**: Ready for walkthrough
> **Utility**: Auto-scheduling engine, manual game placement, move/swap, conflict detection, bracket enforcement
> **Route**: `/:jobPath/admin/scheduling/schedule-division`
> **Authorization**: AdminOnly (Director, SuperDirector, Superuser)

---

## Prerequisites

- Fields assigned, pairings generated, timeslots configured for at least **2 divisions**
- At least one division with **8 teams and 7 rounds** of round-robin pairings
- At least one division with **championship bracket pairings** (QF→F)
- Multiple fields configured (to test multi-column grid)
- Multiple game days configured (to test multi-row grid)
- Know the capacity: fields × MaxGamesPerField × game days

---

## Section 1: Page Load & Division Selection

### F1.1 — Page loads with navigator

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/:jobPath/admin/scheduling/schedule-division` | Agegroup tree on left |
| 2 | Filtering | "Dropped Teams", "WAITLIST*" excluded; "Unassigned" divisions excluded |
| 3 | Agegroup badges | Team counts with agegroup color; luminance-based contrast text |

### F1.2 — Select division loads context

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a division | Pairings panel, Teams panel, and Schedule Grid load |
| 2 | Pairings | Shows available (○) and scheduled (●) pairings |
| 3 | Teams | Division teams with rank, club, name |
| 4 | Grid | Date×Field grid with game cards or open slots |

---

## Section 2: Auto-Schedule

### F2.1 — Auto-schedule entire division

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select division with pairings but no scheduled games | Grid shows all OPEN slots |
| 2 | Click "Auto-Schedule" | Scheduling begins (progress indicator if implemented) |
| 3 | Observe grid | Games populate into slots — round by round, filling fields sequentially |
| 4 | Verify all RR pairings scheduled | All round-robin pairings show ● Scheduled |
| 5 | Count | Total games = total RR pairings (e.g., 28 for 8-team full RR) |

### F2.2 — Auto-schedule respects capacity

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Configure limited capacity (e.g., 2 fields × 3 max games = 6 slots per day) | — |
| 2 | Auto-schedule 28 games | Games fill available slots; no double-booking |
| 3 | If insufficient capacity | Some pairings remain unscheduled; toast/message reports how many couldn't be placed |

### F2.3 — Auto-schedule deletes existing games first

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Division already has some scheduled games | — |
| 2 | Click "Auto-Schedule" | Confirmation: "This will delete existing games" |
| 3 | Confirm | Old games deleted, fresh schedule generated |

### F2.4 — Auto-schedule uses round-specific dates

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Timeslot dates have specific round numbers (Rnd 1 = Mar 1, Rnd 2 = Mar 8) | — |
| 2 | Auto-schedule | Round 1 games placed on Mar 1; Round 2 games on Mar 8 |

---

## Section 3: Manual Game Placement

### F3.1 — Click pairing → click slot

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click an available pairing (○) in the pairings panel | Pairing becomes "selected" (highlighted) |
| 2 | Grid open slots highlight/become clickable | Visual indicator on open cells |
| 3 | Click an open slot in the grid | Game placed at that date/field |
| 4 | Pairing status | Changes to ● Scheduled |
| 5 | Grid cell | Shows game card with round, game#, team matchup |

### F3.2 — Auto-advance after placement

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Place a game via click-to-place | Game placed |
| 2 | Observe pairings panel | Next unscheduled pairing auto-selected |
| 3 | Observe grid scroll | Grid scrolls to next open slot forward in time |

### F3.3 — Cancel placement mode

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a pairing (enters placement mode) | — |
| 2 | Click the pairing again (or press Escape) | Placement mode cancelled; no game placed |

---

## Section 4: Rapid-Placement Modal

### F4.1 — Open rapid-placement modal

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click rapid-placement toggle in pairings panel header | Modal opens |
| 2 | First unscheduled pairing auto-selected | Pairing info displayed |
| 3 | Remaining count badge | Shows count of unscheduled pairings |

### F4.2 — Field typeahead

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Type partial field name | Filtered list of matching fields |
| 2 | Arrow keys + Enter to select | Field selected |

### F4.3 — Time typeahead

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | After selecting field | Available time slots shown (only open cells for that field) |
| 2 | First available auto-defaults | — |

### F4.4 — Place & auto-advance

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Field and time selected → click "Place & Next" | Game placed; auto-advances to next pairing |
| 2 | Same field stays selected | Field input retains value |
| 3 | Focus returns to field input | Ready for next placement |

### F4.5 — Last pairing shows "Place & Done"

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Only 1 unscheduled pairing remaining | Button label: "Place & Done" |
| 2 | Click | Game placed; modal closes |

---

## Section 5: Game Move & Swap

### F5.1 — Move game to empty slot

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a scheduled game card in the grid | Game selected (highlighted); enters move mode |
| 2 | Click an OPEN slot | Game moves to new date/field |
| 3 | Original slot | Now empty (OPEN) |
| 4 | New slot | Shows the game card |

### F5.2 — Swap two games

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a scheduled game (enters move mode) | — |
| 2 | Click another OCCUPIED slot | Games swap positions |
| 3 | Both slots | Each shows the other's original game |

### F5.3 — Cancel move mode

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a game to enter move mode | — |
| 2 | Click same game again (or Escape) | Move mode cancelled |

### F5.4 — Delete single game

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click delete button on a game card | Confirmation |
| 2 | Confirm | Game removed; slot becomes OPEN; pairing returns to ○ Available |

---

## Section 6: Conflict Detection

### F6.1 — Slot collision (backend)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Place two games at exact same time/field (if possible) | — |
| 2 | Observe | Red warning icon (`bi-layers-fill`) on affected game cards |
| 3 | Header badge | Breaking conflict count shown in danger badge |

### F6.2 — Team time clash (frontend)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Same team appears in two games on the same grid row (same time, different fields) | — |
| 2 | Observe | Red icon (`bi-people-fill`) on both game cards |
| 3 | Applies cross-division | Even if games are from different divisions sharing the same fields |

### F6.3 — Back-to-back warning

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Same team has games in consecutive timeslot rows on the same day | — |
| 2 | Observe | Amber icon (`bi-clock-history`) — non-breaking warning |
| 3 | Header badge | Warning count shown in amber badge |

### F6.4 — Time-clash prevention on placement

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Try to place a game where one of the teams already has a game at that time | — |
| 2 | Observe | Placement blocked with toast warning naming the conflicting team and game |

---

## Section 7: Bracket Enforcement

### F7.1 — Traditional mode: cross-division bracket blocked

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Agegroup with `bChampionsByDivision = false` | — |
| 2 | Division A already has bracket games on the grid | — |
| 3 | Switch to Division B, try to place a bracket game | Blocked with toast: "Championship bracket already placed by [Division A]" |

### F7.2 — Per-division mode: each division independent

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Agegroup with `bChampionsByDivision = true` | — |
| 2 | Division A has bracket games | — |
| 3 | Switch to Division B, place bracket game | Allowed — per-division brackets are independent |

---

## Section 8: Grid Features

### F8.1 — Other-division dimming

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Grid shows games from current division AND other divisions on shared fields | — |
| 2 | Current division games | Full opacity; delete button visible |
| 3 | Other division games | 35% opacity; delete button hidden |

### F8.2 — Agegroup color coding

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Games from different agegroups visible on shared fields | — |
| 2 | Each game card | Left border color matches agegroup color |

### F8.3 — Day boundary jumps

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Grid spans multiple days | — |
| 2 | Day jump buttons in header | e.g., "Sat Feb 1", "Sun Feb 2" |
| 3 | Click a day button | Grid scrolls to that day's first row |

### F8.4 — Smart-scroll on division select

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a division with games | Grid scrolls to first row with a current-division game |

---

## Section 9: Teams Panel

### F9.1 — Team rank editing

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Edit a team's DivRank in the teams panel | — |
| 2 | Save | Schedule recalculates: T1Name/T2Name update for all games referencing old/new rank |

### F9.2 — Delete all division games

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Delete All Games" | Typed confirmation required |
| 2 | Confirm | All games for this division deleted; grid shows all OPEN; pairings return to ○ |
| 3 | Cascade cleanup | BracketSeeds and DeviceGids also cleaned up |

---

## Section 10: Who Plays Who

### F10.1 — Matrix after scheduling

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | View Who Plays Who for a fully scheduled division | Matrix shows actual game counts between each team pair |
| 2 | Values match | Game counts match what's visible in the grid |

---

## Section 11: Visual & Responsive

### F11.1 — Palette compatibility

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | All 8 palettes | Grid cells, game cards, conflict icons, badges all render correctly |

### F11.2 — Horizontal scroll with frozen date column

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Grid with many field columns | Date column stays frozen on left while scrolling horizontally |
| 2 | Sticky headers | Field names stay visible while scrolling vertically |

---

## Test Execution Summary

| Section | Tests | Focus Area |
|---------|-------|------------|
| 1. Page Load | F1.1 – F1.2 | Navigator, division context |
| 2. Auto-Schedule | F2.1 – F2.4 | Core engine, capacity, round-specific dates |
| 3. Manual Placement | F3.1 – F3.3 | Click-to-place, auto-advance |
| 4. Rapid Placement | F4.1 – F4.5 | Modal, typeahead, place & advance |
| 5. Move & Swap | F5.1 – F5.4 | Move, swap, delete |
| 6. Conflict Detection | F6.1 – F6.4 | Slot collision, time clash, back-to-back |
| 7. Bracket Enforcement | F7.1 – F7.2 | Traditional vs per-division |
| 8. Grid Features | F8.1 – F8.4 | Dimming, color, day jumps, scroll |
| 9. Teams | F9.1 – F9.2 | Rank edit, delete all |
| 10. Who Plays Who | F10.1 | Post-schedule matrix |
| 11. Visual | F11.1 – F11.2 | Palettes, scrolling |

**Total: 30 functional test scenarios**
