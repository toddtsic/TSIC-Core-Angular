# Migration Plan 009-5: Schedules/Index → View Schedule

## Context

The View Schedule page is the **consumer-facing** schedule viewer — the most broadly accessed page in the scheduling suite. While the preceding tools (009-1 through 009-4) are admin-only, View Schedule serves admins, coaches, parents, and optionally the general public. It provides five distinct views of the same schedule data:

1. **Team Schedules** — filterable game list
2. **Standings** — pool play standings by division
3. **Team Records** — full season W-L-T including playoffs
4. **Playoff Brackets** — visual bracket diagrams (pure CSS/HTML)
5. **Contacts** — team staff contact information

This is the **only scheduling tool with public access** — the `Job.BScheduleAllowPublicAccess` flag allows unauthenticated users to view schedules and standings (but not edit scores or access contacts).

**Legacy URL:** `/Schedules/Index` (Controller=Schedules, Action=Index)

---

## Implementation Status: COMPLETE ✅

All phases implemented and both backend + frontend compile successfully.

### Files Created/Modified

#### Backend — New Files
| File | Description |
|------|-------------|
| `TSIC.Contracts/Dtos/Scheduling/ViewScheduleDtos.cs` | All DTOs: ScheduleFilterRequest, ScheduleFilterOptionsDto, CADT tree nodes, ViewGameDto, StandingsDto, DivisionStandingsDto, StandingsByDivisionResponse, TeamResultDto, BracketMatchDto, DivisionBracketResponse, ContactDto, FieldDisplayDto, FieldSummaryDto, ScheduleCapabilitiesDto, EditScoreRequest, EditGameRequest |
| `TSIC.Contracts/Services/IViewScheduleService.cs` | Service interface (11 methods) |
| `TSIC.API/Services/Scheduling/ViewScheduleService.cs` | Service implementation with standings calculation, bracket grouping, team stats accumulation |
| `TSIC.API/Controllers/ViewScheduleController.cs` | REST endpoints with dual-path auth (authenticated + public via jobPath) |

#### Backend — Extended Files
| File | Changes |
|------|---------|
| `TSIC.Contracts/Repositories/IScheduleRepository.cs` | +8 new methods: GetFilteredGamesAsync, GetScheduleFilterOptionsAsync, GetTeamGamesAsync, GetBracketGamesAsync, GetContactsAsync, GetFieldDisplayAsync, GetSportNameAsync, GetScheduleFlagsAsync |
| `TSIC.Infrastructure/Repositories/ScheduleRepository.cs` | +8 method implementations with CADT tree construction, OR-union filter logic, contacts via navigation properties |
| `TSIC.API/Program.cs` | Added `IViewScheduleService` → `ViewScheduleService` registration |

#### Frontend — New Files
| File | Description |
|------|-------------|
| `view-schedule/view-schedule.component.ts` | Main container: tabs, filter panel (CADT tree + game day + unscored-only), data loading orchestration, modal management |
| `view-schedule/services/view-schedule.service.ts` | HTTP service for all 11 endpoints with optional jobPath for public access |
| `view-schedule/components/cadt-tree-filter.component.ts` | CADT (Club→Agegroup→Division→Team) hierarchical checkbox tree with search box, color dots, cascade check/uncheck, indeterminate state |
| `view-schedule/components/games-tab.component.ts` | Games table with inline quick-score editing, agegroup color stripe, clickable teams/fields |
| `view-schedule/components/standings-tab.component.ts` | Pool play standings grouped by division, sport-specific column visibility (lacrosse hides Pts/PPG) |
| `view-schedule/components/records-tab.component.ts` | Full season records grouped by division (all game types, not just pool play) |
| `view-schedule/components/brackets-tab.component.ts` | Pure CSS bracket diagrams with zoom (+/-/reset/scroll wheel), drag-pan, round columns with fanning gap, champion badge |
| `view-schedule/components/contacts-tab.component.ts` | Hierarchical accordion (Agegroup→Division→Team) with phone/email links |
| `view-schedule/components/team-results-modal.component.ts` | Team game history drill-down modal with W/L/T outcome badges and record summary |
| `view-schedule/components/edit-game-modal.component.ts` | Full game edit modal: T1/T2 name overrides, scores, annotations, status code |

#### Frontend — Extended Files
| File | Changes |
|------|---------|
| `app.routes.ts` | +3 routes: `admin/scheduling/view-schedule`, `scheduling/schedules` (legacy), `schedule` (public) |
| `client-menu.component.ts` | Added `scheduling/schedules` to legacy route map |

---

## Key Design Decisions

### 1. CADT Filter Tree (NOT separate dropdowns)
- **Club → Agegroup → Division → Team** hierarchy
- Search box at top for filtering clubs (critical for 50+ club tournaments)
- Color dots on agegroup nodes using `Agegroup.Color` property
- All nodes collapsed by default
- Emits `{ clubNames[], agegroupIds[], divisionIds[], teamIds[] }` for OR-union filter

### 2. POST Body Filter Pattern (from Registration Search)
- Single `ScheduleFilterRequest` object sent as POST body
- OR-union logic: game matches if ANY selected club/agegroup/division/team matches
- Game day and unscored-only filters applied via WHERE clause

### 3. Pure CSS Brackets (NOT Syncfusion Diagram)
- CSS Grid columns per round with exponentially increasing gap for bracket fanning
- Match cards with team names, scores, winner/loser CSS classes
- Zoom via `transform: scale()` with +/-/scroll wheel controls
- Drag-pan via pointer events with grab cursor
- Champion badge with trophy icon on division header

### 4. Dual Score Editing
- **Inline quick-score**: Click score cell → two number inputs → Enter saves, Escape cancels
- **Full edit modal**: Click edit icon → modal with T1/T2 name overrides, scores, annotations, status

### 5. Dual-Path Authentication
- **Authenticated**: regId claim → jobId resolution (standard pattern)
- **Public**: jobPath query param → jobId resolution when `Job.BScheduleAllowPublicAccess` is true
- Controller has `[AllowAnonymous]` on data endpoints, `[Authorize]` on contacts, `[Authorize(AdminOnly)]` on score editing

### 6. Standings Calculation
- Points = (W × 3) + (T × 1)
- GoalDiffMax9 = clamp(GF - GA, -9, 9)
- PPG = Points / GP
- Soccer sort: Pts DESC, W DESC, GD DESC, GF DESC
- Lacrosse sort: W DESC, L ASC, GD DESC

---

## Deferred Items

| Item | Reason |
|------|--------|
| Firebase push notifications | No Firebase service in new backend; defer to dedicated session |
| SignalR live updates | No SignalR hub exists yet; defer to dedicated session |
| Score entry side effects (bracket auto-advancement) | `ScheduleRecord_RecalcValues()` partially built in 009-4; will integrate when tested |
| CSV/Excel export | Can be added as enhancement; core view functionality complete |
| Field info modal | Currently uses `alert()`; can upgrade to modal later |

---

## Testing Checklist

- [ ] Navigate to `/{jobPath}/admin/scheduling/view-schedule`
- [ ] Filter options load with CADT tree (clubs, agegroups with color dots, divisions, teams)
- [ ] CADT search box filters clubs correctly for 50+ clubs
- [ ] Games tab shows filtered schedule with agegroup color stripe
- [ ] Standings tab shows pool-play standings grouped by division
- [ ] Records tab shows full-season records (all game types)
- [ ] Brackets tab renders CSS bracket diagrams with zoom/pan
- [ ] Contacts tab shows staff contacts in hierarchical accordion
- [ ] Quick-score inline editing works (click score → edit → Enter saves)
- [ ] Full game edit modal works (team overrides, annotations, status)
- [ ] Team results drill-down modal shows game history with W/L/T
- [ ] Field info popup shows address and directions
- [ ] Tab data caches correctly (switching tabs doesn't re-fetch unless filters change)
- [ ] Filter clear button resets all filters and refreshes
- [ ] Public route: `/{jobPath}/schedule` shows schedule without auth (when enabled)
- [ ] Contacts hidden when `League.BHideContacts` is true
- [ ] Score editing only available for admin users
