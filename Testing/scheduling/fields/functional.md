# Functional Tests: Scheduling — Manage Fields

> **Status**: Ready for walkthrough
> **Utility**: Global field library + league-season assignment (dual-panel swapper)
> **Route**: `/:jobPath/admin/scheduling/fields`
> **Authorization**: AdminOnly (Director, SuperDirector, Superuser)

---

## Prerequisites

- At least **5+ fields** in the global library (Fields table)
- At least **2-3 fields already assigned** to the current league-season
- At least **2-3 unassigned fields** available
- At least **1 system field** (name starts with `*`) to verify filtering
- At least **1 field referenced in Schedule** (to test delete blocking)
- Login as both **SuperUser** and **Director** to test scoping differences

---

## Section 1: Page Load & Panel Population

### F1.1 — Page loads with both panels populated

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/:jobPath/admin/scheduling/fields` | Page loads without errors |
| 2 | Observe left panel ("Available Fields") | Shows fields NOT assigned to current league-season |
| 3 | Observe right panel ("League-Season Fields") | Shows fields assigned to current league-season |
| 4 | Observe field counts | Each panel header shows count of fields |

### F1.2 — System fields excluded

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Look for any field starting with `*` in either panel | **Not visible** — system fields excluded from both panels |

### F1.3 — SuperUser sees all fields

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Login as **SuperUser** | — |
| 2 | Navigate to fields page | Available panel shows ALL non-system unassigned fields |

### F1.4 — Director sees only historically used fields

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Login as **Director** | — |
| 2 | Navigate to fields page | Available panel shows only fields historically used by this Director's jobs |
| 3 | Verify | Fields never used by any of this Director's jobs are NOT shown |

### F1.5 — Table columns present

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Observe both panel tables | Columns: checkbox, swap button, Field Name, City |

---

## Section 2: Selection, Filtering & Sorting

### F2.1 — Individual selection

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click checkbox on a field row in available panel | Row highlights, checkbox checked |
| 2 | Click again | Row deselects |
| 3 | Select 2 fields | "Assign Selected → (2)" button shows correct count |

### F2.2 — Select All / Deselect All

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "select all" checkbox in available panel header | All fields selected |
| 2 | Click again | All deselected |

### F2.3 — Text filter

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Type partial field name in available panel filter | Only matching fields shown (case-insensitive) |
| 2 | Clear filter | All fields reappear |

### F2.4 — Column sorting

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Field Name" header | Sorts ascending |
| 2 | Click again | Sorts descending |
| 3 | Click third time | Returns to default order |

---

## Section 3: Single-Row Transfer

### F3.1 — Assign single field (available → assigned)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click the `→` arrow button on a field in Available panel | Row shows spinner briefly |
| 2 | Observe available panel | Field gone; count decremented |
| 3 | Observe assigned panel | Field appears; count incremented |
| 4 | No confirmation modal | Transfer is immediate |

### F3.2 — Remove single field (assigned → available)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click the `←` arrow button on a field in Assigned panel | Spinner, then field moves back |
| 2 | Observe both panels | Counts updated correctly |

---

## Section 4: Batch Transfer

### F4.1 — Batch assign multiple fields

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select 3 fields via checkboxes in available panel | 3 checked |
| 2 | Click "Assign Selected → (3)" | All 3 move to assigned panel |
| 3 | Verify selections cleared | No checkboxes remain checked after transfer |

### F4.2 — Batch remove multiple fields

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select 2 fields in assigned panel | 2 checked |
| 2 | Click "← Remove Selected (2)" | Both move to available panel |

---

## Section 5: Field Detail Editor

### F5.1 — Click row opens detail form

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a field row (not checkbox or arrow) | Detail form appears below panels |
| 2 | Observe form fields | Name, Address, City, State, ZIP, Directions, Lat, Lng all populated |

### F5.2 — Edit field details

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Change the field name and address | — |
| 2 | Click "Save" | Success toast; name updates in the panel table |

### F5.3 — Create new field

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click `[+ New]` | Empty detail form appears |
| 2 | Enter field name, address, city, state, ZIP | — |
| 3 | Click "Save" | New field appears in **Available** panel (not auto-assigned) |

### F5.4 — Blank name rejected

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click `[+ New]`, leave name blank, click "Save" | Validation error; field not created |

### F5.5 — Delete unreferenced field

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a field NOT referenced in Schedule or Timeslots | — |
| 2 | Click "Delete" → confirm | Field deleted from panels and database |

### F5.6 — Delete blocked when referenced

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a field referenced in Schedule or Timeslots | — |
| 2 | Click "Delete" | Error: field cannot be deleted because it's referenced |

---

## Section 6: Edge Cases & Audit

### F6.1 — Empty available panel

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Assign ALL available fields | Available panel shows empty state icon + message |

### F6.2 — Audit trail

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create or edit a field | Check DB: `LebUserId` = current user, `Modified` = now |
| 2 | Assign a field | Check `FieldsLeagueSeason`: `LebUserId` and `Modified` populated |

---

## Section 7: Responsive & Visual

### F7.1 — Palette compatibility

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Switch through all 8 palettes | Panels, form controls all render correctly |

### F7.2 — Mobile responsive

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resize to < 768px | Panels stack vertically; detail form full width |

---

## Test Execution Summary

| Section | Tests | Focus Area |
|---------|-------|------------|
| 1. Page Load | F1.1 – F1.5 | Panels, scoping, system field exclusion |
| 2. Selection & Filter | F2.1 – F2.4 | Multi-select, filter, sort |
| 3. Single Transfer | F3.1 – F3.2 | Per-row assign/remove |
| 4. Batch Transfer | F4.1 – F4.2 | Multi-select assign/remove |
| 5. Detail Editor | F5.1 – F5.6 | Create, edit, delete |
| 6. Edge Cases | F6.1 – F6.2 | Empty panels, audit trail |
| 7. Responsive | F7.1 – F7.2 | Palettes, mobile |

**Total: 21 functional test scenarios**
