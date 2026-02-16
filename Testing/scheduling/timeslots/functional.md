# Functional Tests: Scheduling — Manage Timeslots

> **Status**: Ready for walkthrough
> **Utility**: Timeslot dates + field configurations + cloning + capacity preview
> **Route**: `/:jobPath/admin/scheduling/timeslots`
> **Authorization**: AdminOnly (Director, SuperDirector, Superuser)

---

## Prerequisites

- At least **2 agegroups** with **2+ divisions** each
- At least **3 fields** assigned to the league-season (via Manage Fields)
- Pairings generated for at least one division (to verify capacity "games needed")
- No timeslot data for at least one agegroup (test from scratch)
- Existing timeslot data for at least one agegroup (test cloning)

---

## Section 1: Page Load & Agegroup Selection

### F1.1 — Page loads with agegroup navigator

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/:jobPath/admin/scheduling/timeslots` | Agegroup list on left; "Dropped Teams" and "WAITLIST*" excluded |

### F1.2 — Select agegroup loads tabs

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click an agegroup | Right panel: tabs for Dates, Fields, Capacity Preview |
| 2 | Dates tab active | Shows dates grid or empty state |

---

## Section 2: Dates Management

### F2.1 — Add a date

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "[+ Add Date]" | Input form appears |
| 2 | Select date Mar 1, 2026; Round = 1 | — |
| 3 | Save | Row: "Mar 1, 2026 | Rnd 1 | Sat" (DOW auto-calculated) |

### F2.2 — Edit a date

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Edit existing date, change round to 2 | — |
| 2 | Save | Round updates |

### F2.3 — Clone +1 Day

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Row: "Mar 1, Rnd 1, Sat" → click [+D] | New: "Mar 2, Rnd 2, Sun" |

### F2.4 — Clone +1 Week

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Row: "Mar 1, Rnd 1, Sat" → click [+W] | New: "Mar 8, Rnd 2, Sat" |

### F2.5 — Clone Same Date, Rnd+1

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Row: "Mar 1, Rnd 1" → click [+R] | New: "Mar 1, Rnd 2" (two rounds same day) |

### F2.6 — Delete a date

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Delete a date row | Row removed |

### F2.7 — Delete ALL dates

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | "Delete All Dates" → confirm | All dates for this agegroup removed; other agegroups unaffected |

---

## Section 3: Field Timeslots

### F3.1 — Fields tab loads

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Fields" tab | Grid: Field, DOW, Start, Interval, Max Games, Division |

### F3.2 — Add specific field timeslot

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Add: Field=Cedar Park A, Div=Gold, DOW=Sat, Start=08:00, Interval=60, Max=6 | — |
| 2 | Save | Row appears with correct values |

### F3.3 — Add with cartesian product

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Add: Field=All, Div=All, DOW=Sat, Start=08:00, Interval=60, Max=6 | — |
| 2 | Save | Rows = (# fields) × (# divisions) |

### F3.4 — Edit field timeslot

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Change Start from 08:00 to 09:00, Max from 6 to 5 → save | Values update |

### F3.5 — Delete field timeslot

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Delete a single field timeslot row | Row removed |

### F3.6 — Delete ALL field timeslots

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | "Delete All Fields" → confirm | All field timeslots for agegroup removed |

---

## Section 4: Cloning Operations

### F4.1 — Clone dates (agegroup → agegroup)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | U10 has dates; U12 has none | — |
| 2 | Clone Dates: U10 → U12 | U12 now has identical dates |

### F4.2 — Clone dates replaces existing

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | U12 already has dates | — |
| 2 | Clone U10 dates to U12 again | U12 old dates replaced with U10's |

### F4.3 — Clone fields (agegroup → agegroup)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | U10 has field timeslots; U12 has none | — |
| 2 | Clone Fields: U10 → U12 | U12 gets identical field timeslots |

### F4.4 — Clone fields blocked if target has data

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | U12 already has field timeslots | — |
| 2 | Try clone fields to U12 | Error message |

### F4.5 — Clone by field (within agegroup)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clone Field: Cedar Park A → Lakeline | Lakeline gets same DOW/time/capacity settings |

### F4.6 — Clone by division (within agegroup)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clone Division: Gold → Silver | Silver gets same field configs as Gold |

### F4.7 — Clone by DOW (within agegroup)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clone DOW: Saturday → Sunday | Sunday gets same configs |
| 2 | With optional start time override | Sunday rows use overridden start time |

### F4.8 — Clone field DOW (single row cycling)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Row: "Cedar Park A | Sat" → clone DOW | New: "Cedar Park A | Sun" |
| 2 | Verify cycling | Mon→Tue→...→Sun→Mon |

---

## Section 5: Capacity Preview

### F5.1 — Shows slot count

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | "Capacity Preview" tab | Table: Day, Fields, Game Slots, Games Needed |
| 2 | Game Slots | = SUM(MaxGamesPerField) for all fields on that DOW |

### F5.2 — Sufficient indicator

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Slots ≥ Needed for all days | Green: "Sufficient capacity" |

### F5.3 — Insufficient indicator

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Reduce MaxGamesPerField to create shortfall | Red indicator on shortfall days |

### F5.4 — Updates after changes

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Add a field timeslot | Capacity numbers update |

---

## Section 6: Edge Cases

### F6.1 — Fresh agegroup (no data)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select agegroup with no timeslot data | Empty states on all tabs |

### F6.2 — Division-specific dates

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Add date with specific division selected | Date shows division name; applies only to that division |

---

## Section 7: Visual

### F7.1 — Palette compatibility

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | All 8 palettes | Tabs, grids, capacity indicators render correctly |

---

**Total: 30 functional test scenarios**
