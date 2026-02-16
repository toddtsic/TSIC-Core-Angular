# Functional Tests: Scheduling — Rescheduler

> **Status**: Ready for walkthrough
> **Utility**: Cross-division game move/swap, weather adjustment, bulk email to participants
> **Route**: `/:jobPath/admin/scheduling/rescheduler`
> **Authorization**: AdminOnly (Director, SuperDirector, Superuser) — NO public access

---

## Prerequisites

- Games already scheduled across **multiple divisions** (via Schedule Division)
- At least **3 fields** with games
- At least **2 game days** with games
- Teams with contacts populated (player emails, parent emails, club rep emails)
- `League.RescheduleEmailsToAddon` set (if testing addon recipients)
- Know current game times and intervals for weather adjustment testing

---

## Section 1: Page Load & Filtering

### F1.1 — Page loads with filter panel

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/:jobPath/admin/scheduling/rescheduler` | Filter panel with CADT tree + game day + field dropdowns |
| 2 | Filter options load | Clubs, agegroups, divisions, teams, game days, fields all populated |

### F1.2 — Apply filters and load grid

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a game day and 2 fields | — |
| 2 | Click "Load Schedule" | Cross-division grid loads: Date/Time rows × Field columns |
| 3 | All divisions visible | Games from ALL divisions shown (not just one) |
| 4 | Agegroup color coding | Each game card has agegroup color for visual distinction |

### F1.3 — Active filter chips

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Observe filter chips above grid | Active filters shown as dismissible chips |
| 2 | Click ✕ on a chip | Filter removed; grid refreshes |

### F1.4 — Clear all filters

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Clear" | All filters reset |

---

## Section 2: Cross-Division Grid

### F2.1 — Grid shows all divisions

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Observe grid | Games from different divisions visible in same grid |
| 2 | Each game card | Shows agegroup:division label (e.g., "U10:Gold") |
| 3 | Color coding | Different agegroup colors distinguish divisions visually |

### F2.2 — Open slots visible

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Grid cells without games | Show "OPEN SLOT" or empty with placement indicator |

---

## Section 3: Game Move & Swap

### F3.1 — Move game to empty slot

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a game card in the grid | Game selected; enters move mode |
| 2 | Click an OPEN slot | Game moves to new position |
| 3 | Original slot | Now empty |
| 4 | Toast | Success message |
| 5 | RescheduleCount | Incremented on moved game |

### F3.2 — Swap two games

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a game (enters move mode) | — |
| 2 | Click another occupied slot | Games swap positions |
| 3 | Both slots updated | Each shows the other's original game |

### F3.3 — Cancel move mode

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a game to select | — |
| 2 | Click same game again | Move mode cancelled |

### F3.4 — Delete game from grid

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click delete [✕] on a game card | Confirmation |
| 2 | Confirm | Game removed; slot becomes OPEN |

---

## Section 4: Additional Timeslot

### F4.1 — Add a custom timeslot row

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Enter a date/time in the "Additional Timeslot" input | — |
| 2 | Load/refresh schedule | New empty row appears in the grid at that date/time |
| 3 | All cells in that row | OPEN (no games — this is a new time not in timeslot config) |

### F4.2 — Move game into additional timeslot

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a game, click the additional timeslot's open cell | Game moves to the injected row |
| 2 | Verify | Game now scheduled at the custom date/time |

---

## Section 5: Weather Adjustment

### F5.1 — Open weather modal with preview

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Adjust for Weather" | Modal opens |
| 2 | Observe affected games count | Shows how many games will be shifted |
| 3 | Observe fields | "Before" first game time and interval pre-populated from current schedule |

### F5.2 — Configure weather adjustment

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set "After" first game to 2 hours later (e.g., 8:00 → 10:00) | — |
| 2 | Set "After" interval to 50 min (from 60) | — |
| 3 | Select which fields to affect (checkboxes) | — |
| 4 | Affected count updates | Reflects only selected fields |

### F5.3 — Execute weather adjustment (success)

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Apply Adjustment" | Executes stored procedure |
| 2 | Success | Toast: "Schedule adjusted successfully." |
| 3 | Grid refreshes | All affected games now at new times |
| 4 | Unaffected fields | Games on unchecked fields unchanged |

### F5.4 — Weather adjustment error codes

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set "Before" and "After" to same values → Apply | Error: "No changes — the before and after values are identical." |
| 2 | Set invalid interval (0 or negative) | Error: "The 'after' interval is invalid." |
| 3 | Select date range with no games | Error: "No games found for the selected date/time range and fields." |

### F5.5 — Human-readable error messages

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | For each error scenario | Error message is descriptive (not a magic number code) |

---

## Section 6: Email Participants

### F6.1 — Open email modal with recipient preview

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Email Participants" | Modal opens |
| 2 | Observe | Affected game range, fields, estimated recipient count |
| 3 | Recipient count | Deduplicated across players, parents, club reps, league addon |

### F6.2 — Compose email

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Enter subject line | — |
| 2 | Compose body in rich text editor | Bold, italic, lists, links all work |

### F6.3 — Preview email

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Preview" | Rendered email shown (not raw HTML) |

### F6.4 — Send email

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Send to All" | Email sends (non-blocking with progress) |
| 2 | Success | Toast: "142 emails sent" (or similar with actual count) |
| 3 | Failed count | If any bounced/invalid, shows failed count |

### F6.5 — Email audit trail

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | After sending | Check `EmailLogs` table: sender, timestamp, batch ID, subject, recipient count |

### F6.6 — Email recipient filtering

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify | "not@given.com" placeholder excluded |
| 2 | Verify | Empty/null emails excluded |
| 3 | Verify | Duplicate emails deduplicated (parent of 2 players → 1 email) |

---

## Section 7: Edge Cases

### F7.1 — No games match filters

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply filters that match zero games | Grid empty; "Adjust for Weather" and "Email" buttons disabled or show 0 affected |

### F7.2 — Single-game grid

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Filter to show exactly 1 game | Grid shows 1 cell occupied; move/swap still works |

---

## Section 8: Visual

### F8.1 — Palette compatibility

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | All 8 palettes | Grid, modals, rich text editor, filter chips all render correctly |

---

## Test Execution Summary

| Section | Tests | Focus Area |
|---------|-------|------------|
| 1. Filtering | F1.1 – F1.4 | CADT, game day, field filters |
| 2. Cross-Division Grid | F2.1 – F2.2 | All-division view, color coding |
| 3. Move & Swap | F3.1 – F3.4 | Move, swap, cancel, delete |
| 4. Additional Timeslot | F4.1 – F4.2 | Custom row injection |
| 5. Weather | F5.1 – F5.5 | Preview, execute, error handling |
| 6. Email | F6.1 – F6.6 | Compose, preview, send, audit |
| 7. Edge Cases | F7.1 – F7.2 | Empty grid, single game |
| 8. Visual | F8.1 | Palettes |

**Total: 26 functional test scenarios**
