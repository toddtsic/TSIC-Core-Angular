# Functional Tests: Scheduling — Manage Pairings

> **Status**: Ready for walkthrough
> **Utility**: Round-robin & bracket pairing generation, inline editing, Who Plays Who matrix
> **Route**: `/:jobPath/admin/scheduling/pairings`
> **Authorization**: AdminOnly (Director, SuperDirector, Superuser)

---

## Prerequisites

- At least **2 agegroups** with **2+ divisions** each, varying team counts (e.g., 6-team and 8-team divisions)
- Teams assigned to divisions with valid DivRanks
- No pairings yet for at least one division (test fresh generation)
- Existing pairings for at least one division (test display/editing)

---

## Section 1: Navigation & Division Selection

### F1.1 — Page loads with agegroup navigator

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/:jobPath/admin/scheduling/pairings` | Page loads with agegroup tree on left |
| 2 | Observe filtering | "Dropped Teams" and "WAITLIST*" agegroups excluded |
| 3 | Observe badges | Team count badges with agegroup color |

### F1.2 — Select division loads pairings

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a division | Right panel shows division name, team count, pairing grid |
| 2 | If pairings exist | Grid populated with game rows |
| 3 | If no pairings | Empty state with action buttons |

### F1.3 — Pairings grid columns

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Observe grid | Columns: G#, Rnd, T1, vs, T2, Type, Status (○ Open / ● Scheduled) |

---

## Section 2: Round-Robin Generation

### F2.1 — Add Block (1 round, 8 teams)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select division with 8 teams, no pairings | — |
| 2 | Click "Add Block" → select "1 round" → confirm | 4 pairings generated (8/2) |
| 3 | Verify | Game numbers sequential from 1; all Round 1; T1Type/T2Type = "T" |
| 4 | Verify matchups | Match Masterpairingtable for TCnt=8 |

### F2.2 — Add Block (7 rounds = full round-robin for 8 teams)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Add Block" → "7 rounds" | 28 pairings generated (4 × 7) |
| 2 | Verify rounds | Rounds 2 through 8 (offset from existing round 1) |
| 3 | Verify game numbers | Continue from previous max |

### F2.3 — Incremental add preserves offset

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Already have 28 pairings (games 1-28, rounds 1-7) | — |
| 2 | Add 1 more round | New pairings: Round 8, game numbers start at 29 |
| 3 | No collisions | All game numbers and rounds unique |

### F2.4 — Different team count

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select 6-team division | — |
| 2 | Add 1 round | 3 games generated; matchups match TCnt=6 template |

---

## Section 3: Single-Elimination Brackets

### F3.1 — Quarterfinals → Finals (8 teams)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Championship" → "Quarterfinals → Finals" | 7 pairings: 4 QF + 2 SF + 1 F |
| 2 | Verify types | QF = "Q", SF = "S", Finals = "F" |
| 3 | Verify game references | SF T1GnoRef/T2GnoRef point to QF game numbers |
| 4 | Verify calc types | T1CalcType = "W" (Winner) |

### F3.2 — Semifinals → Finals only

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Championship" → "Semifinals → Finals" | 3 pairings: 2 SF + 1 F |

### F3.3 — Finals only

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Championship" → "Finals only" | 1 pairing |

### F3.4 — RR vs Championship visual separation

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Division has both RR and bracket pairings | — |
| 2 | Observe grid | Separate sections: "Round-Robin" and "Championship" |

---

## Section 4: Add Single Pairing

### F4.1 — Add single pairing

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Add Single" | New row with next game number, editable fields |

---

## Section 5: Inline Editing

### F5.1 — Edit pairing T1/T2

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click to edit a pairing's T1 field | Becomes editable |
| 2 | Change value, save | Persists |

### F5.2 — Edit annotations

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Edit T1Annotation on a bracket game → "Winner of Pool A" | Saves and displays |

### F5.3 — Edit calc types

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Change T1CalcType from "W" to "L" on a bracket game | Updates |

---

## Section 6: Remove All Pairings

### F6.1 — Remove ALL with confirmation

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Remove ALL" | Deliberate confirmation dialog (not just browser confirm) |
| 2 | Confirm | All pairings for this team count removed; grid shows empty state |

### F6.2 — Only affects this team count

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Remove ALL from 8-team division | — |
| 2 | Switch to 6-team division | Its pairings still intact |

---

## Section 7: Delete Single Pairing

### F7.1 — Delete individual pairing

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click delete on one pairing row → confirm | Row removed; others unaffected |

---

## Section 8: Who Plays Who Matrix

### F8.1 — Matrix displays correctly

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Division with round-robin pairings | — |
| 2 | View Who Plays Who section | N×N matrix; cells = game count between teams; diagonal = "—" |

### F8.2 — Matrix is symmetric

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Cell [T1,T3] = cell [T3,T1] | Symmetric across diagonal |

### F8.3 — Zero-game highlighting

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Any team pair with 0 games | Cell highlighted yellow/warning |

### F8.4 — Matrix updates after changes

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Add another block | Matrix values update |

---

## Section 9: Availability Status

### F9.1 — Open vs Scheduled

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Unscheduled pairing | ○ Open |
| 2 | Pairing with corresponding Schedule record | ● Scheduled |

---

## Section 10: Visual

### F10.1 — Palettes & round coloring

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | All 8 palettes | Navigator, grid, matrix render correctly |
| 2 | Multiple rounds | Alternating row backgrounds per round |

---

**Total: 24 functional test scenarios**
