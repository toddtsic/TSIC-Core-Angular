# Functional Tests: Scheduling — View Schedule

> **Status**: Ready for walkthrough
> **Utility**: Schedule viewing (5 tabs), score editing, CADT filtering, dual-path auth (admin + public)
> **Routes**: `/:jobPath/admin/scheduling/view-schedule` (admin), `/:jobPath/schedule` (public)
> **Authorization**: Mixed — public endpoints + AdminOnly for score editing + Authorize for contacts

---

## Prerequisites

- Games already scheduled for multiple divisions (via Schedule Division)
- At least **2 agegroups** with games, each with **different agegroup Colors**
- At least some games with **scores entered** (for standings/records)
- At least one division with **bracket games** (for Brackets tab)
- At least **2 clubs** with teams (for CADT filtering)
- Team contacts populated (coach emails, parent contacts) for Contacts tab
- `Job.BScheduleAllowPublicAccess` set to `true` for public route testing
- Know whether `League.BHideContacts` is true/false

---

## Section 1: Page Load & CADT Filter

### F1.1 — Page loads with filter panel and tabs

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/:jobPath/admin/scheduling/view-schedule` | Page loads with CADT filter tree on left, tabs on right |
| 2 | Observe tabs | Games, Standings, Records, Brackets, Contacts |
| 3 | CADT tree | Club → Agegroup → Division → Team hierarchy; all collapsed by default |

### F1.2 — CADT tree with agegroup color dots

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Expand a club in the CADT tree | Agegroup nodes show color dots matching `Agegroup.Color` |

### F1.3 — CADT search box

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Type a club name in the search box | Tree filters to show only matching clubs |
| 2 | Clear search | Full tree reappears |

### F1.4 — CADT cascade check/uncheck

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Check an agegroup node | All divisions and teams underneath auto-checked |
| 2 | Uncheck one team | Agegroup shows indeterminate state; division shows indeterminate |
| 3 | Uncheck agegroup | All children unchecked |

### F1.5 — Filter applies to Games tab

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Check one specific team | — |
| 2 | Observe Games tab | Only games involving that team shown |
| 3 | Check an entire agegroup | All games in that agegroup shown |

### F1.6 — Game day filter

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select a specific game day | Only games on that day shown |
| 2 | Combined with CADT | Both filters applied (AND logic) |

### F1.7 — Unscored-only filter

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Toggle "Unscored only" | Only games without scores shown |

### F1.8 — Clear filters

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Clear" | All filters reset; full schedule loaded |

---

## Section 2: Games Tab

### F2.1 — Games table displays correctly

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Games tab active | Table: Date/Time, Field, Agegroup:Division, T1 vs T2, Score |
| 2 | Agegroup color stripe | Each row has colored left border matching agegroup color |

### F2.2 — Inline quick-score editing

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a score cell on an unscored game | Two number inputs appear (T1 score, T2 score) |
| 2 | Enter scores (e.g., 3 - 1) → press Enter | Scores saved; toast confirms |
| 3 | Press Escape instead | Edit cancelled; no changes |

### F2.3 — Full game edit modal

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click edit icon on a game row | Modal opens |
| 2 | Observe fields | T1/T2 name overrides, scores, annotations, status code |
| 3 | Edit score and annotation → Save | Values saved; game row updates |

### F2.4 — Team results drill-down

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a team name in the games table | Team Results modal opens |
| 2 | Observe | Game history for that team with W/L/T outcome badges |
| 3 | Record summary | Total W-L-T shown at top |

### F2.5 — Field info

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click a field name in the games table | Field info popup/alert with address and directions |

---

## Section 3: Standings Tab

### F3.1 — Standings display

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Standings" tab | Standings grouped by division |
| 2 | Columns | Team, GP, W, L, T, Pts, GF, GA, GD, PPG (varies by sport) |
| 3 | Sorting | Teams sorted by Pts DESC (soccer) or W DESC (lacrosse) |

### F3.2 — Standings calculation accuracy

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Team with 3 wins, 1 tie, 1 loss | Pts = (3×3)+(1×1) = 10 |
| 2 | GD | = GF - GA (clamped -9 to 9 per game) |
| 3 | PPG | = Pts / GP |

### F3.3 — Sport-specific columns

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Soccer job | Pts and PPG columns visible |
| 2 | Lacrosse job | Pts and PPG columns hidden |

### F3.4 — Only pool play games

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Division has both RR and bracket games | — |
| 2 | Standings | Only count round-robin (T1Type/T2Type = "T") games; brackets excluded |

---

## Section 4: Records Tab

### F4.1 — Full season records

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Records" tab | Records grouped by division |
| 2 | Unlike standings | Includes ALL game types (RR + bracket) |
| 3 | W-L-T | Accurate across all game types |

---

## Section 5: Brackets Tab

### F5.1 — CSS bracket diagram renders

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Brackets" tab | Bracket diagrams render for divisions with championship pairings |
| 2 | Round columns | QF → SF → F flowing left to right |
| 3 | Match cards | Show team names, scores; winner/loser styling |

### F5.2 — Zoom controls

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click [+] zoom | Bracket scales up |
| 2 | Click [-] zoom | Bracket scales down |
| 3 | Scroll wheel | Also zooms |
| 4 | Reset button | Returns to default scale |

### F5.3 — Drag-pan

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click and drag on the bracket | Bracket pans; grab cursor shown |

### F5.4 — Champion badge

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Finals game has a winner (both scores entered) | Champion badge with trophy icon on the winning team |

---

## Section 6: Contacts Tab

### F6.1 — Contacts display

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Contacts" tab | Hierarchical accordion: Agegroup → Division → Team |
| 2 | Expand a team | Phone numbers and email addresses shown with clickable links |

### F6.2 — Contacts hidden when configured

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `League.BHideContacts = true` | Contacts tab hidden or shows "Contacts are hidden" |

### F6.3 — Contacts require authentication

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Access public route `/:jobPath/schedule` | Contacts tab hidden (requires auth) |
| 2 | Access admin route while authenticated | Contacts tab visible |

---

## Section 7: Tab Caching & Refresh

### F7.1 — Tab data caches

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load Games tab | Data fetched |
| 2 | Switch to Standings, then back to Games | No re-fetch (cached data) |

### F7.2 — Filter change invalidates cache

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Change CADT filter | All tabs re-fetch on next activation |

---

## Section 8: Public Access

### F8.1 — Public route works without auth

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/:jobPath/schedule` (unauthenticated) | Schedule loads (Games, Standings, Records, Brackets) |
| 2 | No Contacts tab | Hidden for public |
| 3 | No score editing | Quick-score and edit modal not available |

### F8.2 — Public route blocked when disabled

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set `Job.BScheduleAllowPublicAccess = false` | — |
| 2 | Navigate to `/:jobPath/schedule` | Access denied or redirect |

---

## Section 9: Score Editing Authorization

### F9.1 — Admin can edit scores

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Logged in as admin on admin route | Quick-score and edit modal available |

### F9.2 — Non-admin cannot edit scores

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Logged in as non-admin (coach/parent) | Score cells not clickable; edit icons hidden |

---

## Section 10: Visual

### F10.1 — Palette compatibility

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | All 8 palettes | CADT tree, games table, standings, brackets, contacts accordion all render correctly |

---

## Test Execution Summary

| Section | Tests | Focus Area |
|---------|-------|------------|
| 1. CADT Filter | F1.1 – F1.8 | Tree, search, cascade, filters |
| 2. Games Tab | F2.1 – F2.5 | Display, score editing, drill-down |
| 3. Standings | F3.1 – F3.4 | Calculation, sport-specific, pool-only |
| 4. Records | F4.1 | Full season W-L-T |
| 5. Brackets | F5.1 – F5.4 | CSS diagram, zoom, pan, champion |
| 6. Contacts | F6.1 – F6.3 | Display, hide config, auth |
| 7. Caching | F7.1 – F7.2 | Tab cache, filter invalidation |
| 8. Public Access | F8.1 – F8.2 | Public route, toggle |
| 9. Score Auth | F9.1 – F9.2 | Admin vs non-admin |
| 10. Visual | F10.1 | Palettes |

**Total: 28 functional test scenarios**
