# Migration Plan 009-5: Schedules/Index → View Schedule

## Context

The View Schedule page is the **consumer-facing** schedule viewer — the most broadly accessed page in the scheduling suite. While the preceding tools (009-1 through 009-4) are admin-only, View Schedule serves admins, coaches, parents, and optionally the general public. It provides five distinct views of the same schedule data:

1. **Team Schedules** — filterable game list
2. **Standings** — pool play standings by division
3. **Team Records** — full season W-L-T including playoffs
4. **Playoff Brackets** — visual bracket diagrams (Syncfusion Diagram)
5. **Contacts** — team staff contact information

This is the **only scheduling tool with public access** — the `Job.BScheduleAllowPublicAccess` flag allows unauthenticated users to view schedules and standings (but not edit scores or access contacts).

**Legacy URL:** `/Schedules/Index` (Controller=Schedules, Action=Index)

**Legacy Controller:** `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Scheduling/SchedulesController.cs`
**Legacy View:** `reference/TSIC-Unify-2024/TSIC-Unify/Views/Schedules/Index.cshtml`

---

## 1. Legacy Strengths (Preserve These!)

- **Five-view design** — schedules, standings, records, brackets, contacts all accessible from one page
- **Hierarchical filter system** — filter by Club → Team, Agegroup → Division → Pool; filters persist across views
- **Public access mode** — unauthenticated users can view schedules when enabled per-job
- **Bracket visualization** — Syncfusion Diagram renders elimination brackets with color-coded winners/losers
- **Score entry** — admins can enter scores directly from the schedule view; bracket games auto-advance
- **Push notifications** — Firebase notifications sent to subscribed mobile devices when scores entered
- **SignalR live updates** — when enabled, scores update in real-time across connected clients
- **Team results drill-down** — click any team to see their full game history with opponent records
- **Standings calculation** — W-L-T, Goals For/Against, Goal Difference (capped at 9), Points, Points Per Game — with sport-specific sorting (soccer vs. lacrosse)

## 2. Legacy Pain Points (Fix These!)

- **Five separate AJAX calls** — each view is a separate round-trip; no caching or pre-fetching
- **Standings tree grid** — Syncfusion TreeGrid for a simple flat table; overly complex
- **No Excel export for standings** — only export toolbar button exists, doesn't always work
- **Score entry in modal** — separate modal for editing a game's score; should be inline
- **Bracket rendering hardcoded** — bracket node sizes, positions, and CSS are inline in the view
- **Contact list loads slowly** — queries registrations, family users, club reps in separate queries
- **Direct SqlDbContext** — controller accesses database directly

## 3. Modern Vision

**Recommended UI: Tabbed Multi-View with Persistent Filters**

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Schedule                                              [⚙ Filters]  [🔗]   │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─ Active Filters ────────────────────────────────────────────────────────┐│
│  │ Agegroup: U10 ✕ │ Division: Gold ✕ │ Day: Saturday ✕ │   [Clear All]  ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                                                              │
│  [Games]  [Standings]  [Records]  [Brackets]  [Contacts]                    │
│  ────────────────────────────────────────────────────────                    │
│                                                                              │
│  ── Games Tab ──────────────────────────────────────────────────────────── │
│                                                                              │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │ Date       │ Time  │ Field      │ Div   │ Home      │ Away     │Score │ │
│  ├────────────────────────────────────────────────────────────────────────┤ │
│  │ Sat 3/1    │ 8:00  │ Cedar Pk A │ Gold  │ Storm     │ Lonestar │ 2-1 │ │
│  │ Sat 3/1    │ 9:00  │ Lakeline   │ Gold  │ Texans    │ Thunder  │ —   │ │
│  │ Sat 3/1    │ 10:00 │ Cedar Pk A │ Gold  │ Eagles    │ United   │ —   │ │
│  │ Sun 3/2    │ 9:00  │ Round Rock │ Gold  │ Storm     │ Texans   │ —   │ │
│  │ ...                                                                    │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│  Page 1 of 5  ◀ ▶         [Export CSV]                                     │
│                                                                              │
│  ── Standings Tab (pool play only) ──────────────────────────────────────  │
│                                                                              │
│  U10 Gold                                                                   │
│  ┌──────────────────────────────────────────────────────────────────┐       │
│  │ #│ Team     │ GP │ W │ L │ T │ Pts│ GF│ GA│ GD │ PPG  │        │       │
│  ├──────────────────────────────────────────────────────────────────┤       │
│  │ 1│ Storm    │  3 │ 3 │ 0 │ 0 │  9 │ 8 │ 2 │ +6 │ 3.00 │ [📊]  │       │
│  │ 2│ Lonestar │  3 │ 2 │ 1 │ 0 │  6 │ 5 │ 3 │ +2 │ 2.00 │ [📊]  │       │
│  │ 3│ Texans   │  3 │ 1 │ 1 │ 1 │  4 │ 4 │ 4 │  0 │ 1.33 │ [📊]  │       │
│  │ ...                                                              │       │
│  └──────────────────────────────────────────────────────────────────┘       │
│  [📊] = Click for team's full game results                                 │
│                                     [Export Excel]                          │
│                                                                              │
│  ── Brackets Tab ────────────────────────────────────────────────────────  │
│                                                                              │
│  U10 Gold                                                                   │
│  ┌─────────────────────────────────────────────────┐                       │
│  │        QF              SF              F         │                       │
│  │  ┌──────────┐   ┌──────────┐                    │                       │
│  │  │Storm   3 │──▶│          │                    │                       │
│  │  │Texans  1 │   │Storm   2 │──▶┌──────────┐    │                       │
│  │  └──────────┘   │Lonestar 1│   │          │    │                       │
│  │  ┌──────────┐──▶│          │   │Storm   🏆│    │                       │
│  │  │Lonestar 2│   └──────────┘   │Eagles    │    │                       │
│  │  │Thunder  0│                ──▶│          │    │                       │
│  │  └──────────┘   ┌──────────┐   └──────────┘    │                       │
│  │  ┌──────────┐──▶│Eagles  3 │                    │                       │
│  │  │Eagles  4 │   │United  0 │──▶                 │                       │
│  │  │United  2 │   │          │                    │                       │
│  │  └──────────┘   └──────────┘                    │                       │
│  │  ┌──────────┐──▶                                │                       │
│  │  │FC Dallas 1│                                   │                       │
│  │  │Dynamo   3│                                   │                       │
│  │  └──────────┘                                   │                       │
│  └─────────────────────────────────────────────────┘                       │
│                                                                              │
│  ── Contacts Tab ────────────────────────────────────────────────────────  │
│  (staff contacts organized by Agegroup > Division > Club > Team)            │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Key improvements:**
- ✅ **Persistent filters** — applied once, maintained across tab switches and page refreshes
- ✅ **Inline score entry** — click score cell to edit (no modal needed for simple scores)
- ✅ **Export capabilities** — CSV export for games, Excel export for standings
- ✅ **Bracket keeps Syncfusion Diagram** — already licensed, proven rendering
- ✅ **Optimized contact loading** — single query with joins instead of multiple round-trips
- ✅ **Lazy tab loading** — only fetch data for the active tab, cache results for revisits

**Design alignment:** Glassmorphic cards, Syncfusion Grid for games/standings/contacts, Syncfusion Diagram for brackets. CSS variable colors, 8px grid spacing.

---

## 4. Security

- **Default:** `[Authorize]` — authenticated users can view all tabs
- **Public mode:** When `Job.BScheduleAllowPublicAccess == true`, games/standings/brackets/fields are `[AllowAnonymous]`
- **Score editing:** Requires `AdminOnly` policy OR `Scorer` role
- **Contacts:** Controlled by `League.BHideContacts` flag — hidden from non-admin users when enabled
- **Public route:** Needs a separate Angular route without `authGuard` for public access

---

## 5. Business Rules

### Standings Calculation

```
For each team in a division (pool play games only — T1Type = "T", T2Type = "T"):

  Games Played (GP) = count of scored games
  Wins (W) = games where team's score > opponent's score
  Losses (L) = games where team's score < opponent's score
  Ties (T) = games where scores are equal
  Goals For (GF) = sum of team's scores
  Goals Against (GA) = sum of opponent's scores
  Goal Difference (GD) = GF - GA, capped at ±9 (GoalDiffMax9)
  Points = (W × 3) + (T × 1) + (L × 0)
  Points Per Game (PPG) = Points / GP

Sort order (soccer): Points DESC, then W DESC, then GD DESC, then GF DESC
Sort order (lacrosse): W DESC, then L ASC, then GD DESC
```

### Bracket Types and Rendering

| Key | Name | Games | Feeds Into |
|-----|------|-------|------------|
| Z | Round of 64 | 32 | Y |
| Y | Round of 32 | 16 | X |
| X | Round of 16 | 8 | Q |
| Q | Quarterfinals | 4 | S |
| S | Semifinals | 2 | F |
| F | Finals | 1 | Champion |

Bracket rendering uses **Syncfusion Diagram** with `HierarchicalTree` layout, `RightToLeft` orientation. Winners shown in green, losers in red.

### Score Entry Side Effects

When a score is entered/updated:
1. Game status updated (GStatusCode)
2. `ScheduleRecord_RecalcValues()` called — same pipeline as 009-4:
   - UpdateGameIds
   - AutoadvanceSingleEliminationBracketGameWinner
   - PopulateBracketSeeds
3. Firebase push notification sent to subscribed mobile devices
4. SignalR broadcast to connected clients (if `Job.BSignalRschedule` enabled)

---

## 6. Implementation Steps

### Phase 1: Backend — DTOs

**File:** `TSIC.Contracts/Dtos/Scheduling/ViewScheduleDtos.cs`

```csharp
public record GameDto
{
    public required int Gid { get; init; }
    public required DateTime GDate { get; init; }
    public required string FName { get; init; }
    public required Guid FieldId { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public required string AgDiv { get; init; }      // "U10:Gold"
    public required string T1Name { get; init; }
    public required string T2Name { get; init; }
    public Guid? T1Id { get; init; }
    public Guid? T2Id { get; init; }
    public int? T1Score { get; init; }
    public int? T2Score { get; init; }
    public required string T1Type { get; init; }
    public required string T2Type { get; init; }
    public string? T1Ann { get; init; }
    public string? T2Ann { get; init; }
    public int? Rnd { get; init; }
    public int? GStatusCode { get; init; }
    public string? T1Record { get; init; }
    public string? T2Record { get; init; }
}

public record StandingsDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required int Games { get; init; }
    public required int Wins { get; init; }
    public required int Losses { get; init; }
    public required int Ties { get; init; }
    public required int GoalsFor { get; init; }
    public required int GoalsAgainst { get; init; }
    public required int GoalDiffMax9 { get; init; }
    public required int Points { get; init; }
    public required decimal PointsPerGame { get; init; }
    public int? RankOrder { get; init; }
}

public record StandingsByDivisionResponse
{
    public required List<DivisionStandingsDto> Divisions { get; init; }
}

public record DivisionStandingsDto
{
    public required Guid DivId { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required List<StandingsDto> Teams { get; init; }
}

public record TeamResultDto
{
    public required int Gid { get; init; }
    public required DateTime GDate { get; init; }
    public required string Location { get; init; }
    public required string OpponentName { get; init; }
    public Guid? OpponentTeamId { get; init; }
    public int? TeamScore { get; init; }
    public int? OpponentScore { get; init; }
    public string? Outcome { get; init; }   // "won", "lost", "tie"
    public required string GameType { get; init; }  // "Regular", "Playoff"
    public string? TeamRecord { get; init; }
}

public record BracketMatchDto
{
    public required int Gid { get; init; }
    public required string T1Name { get; init; }
    public required string T2Name { get; init; }
    public int? T1Score { get; init; }
    public int? T2Score { get; init; }
    public required string T1Css { get; init; }     // "winner", "loser", "pending"
    public required string T2Css { get; init; }
    public string? LocationTime { get; init; }
    public required string RoundType { get; init; }  // Q, S, F, X, etc.
}

public record DivisionBracketResponse
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public string? Champion { get; init; }
    public required List<BracketMatchDto> Matches { get; init; }
}

public record ContactDto
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string ClubName { get; init; }
    public required string TeamName { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Cellphone { get; init; }
    public string? Email { get; init; }
}

public record FieldDisplayDto
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Zip { get; init; }
    public string? Directions { get; init; }
}

public record ScheduleFilterOptionsDto
{
    public required List<ClubOptionDto> Clubs { get; init; }
    public required List<AgegroupWithDivisionsDto> Agegroups { get; init; }
    public required List<DateTime> GameDays { get; init; }
    public required List<FieldSummaryDto> Fields { get; init; }
}

public record ClubOptionDto
{
    public required string ClubName { get; init; }
    public required List<TeamOptionDto> Teams { get; init; }
}

public record TeamOptionDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
}

public record EditScoreRequest
{
    public required int Gid { get; init; }
    public required int T1Score { get; init; }
    public required int T2Score { get; init; }
    public int? GStatusCode { get; init; }
}

public record ScheduleUserPreferences
{
    public List<string>? ClubPreferences { get; init; }
    public List<Guid>? TeamPreferences { get; init; }
    public List<DateTime>? GameDayPreferences { get; init; }
    public List<Guid>? LocationPreferences { get; init; }
    public List<Guid>? AgegroupPreferences { get; init; }
    public List<Guid>? DivPreferences { get; init; }
    public bool? UnscoredOnly { get; init; }
}
```

### Phase 2: Backend — Repository

**Extend `IScheduleRepository`** with view/query methods:

```
New Methods:
- GetFilteredScheduleAsync(Guid jobId, ScheduleUserPreferences prefs) → List<Schedule>
- GetStandingsAsync(Guid jobId, ScheduleUserPreferences prefs) → List<StandingsDto>
- GetTeamResultsAsync(Guid teamId) → List<TeamResultDto>
- GetBracketsAsync(Guid jobId, Guid? agegroupId) → List<DivisionBracketResponse>
- GetContactsAsync(Guid jobId, ScheduleUserPreferences prefs) → List<ContactDto>
- GetFieldsForScheduleAsync(Guid jobId) → List<FieldDisplayDto>
- GetFilterOptionsAsync(Guid jobId) → ScheduleFilterOptionsDto
- UpdateScoreAsync(int gid, int t1Score, int t2Score, int? statusCode) → void
```

### Phase 3: Backend — Service

**Interface:** `TSIC.Contracts/Services/IViewScheduleService.cs`
**Implementation:** `TSIC.API/Services/Scheduling/ViewScheduleService.cs`

```
Methods:
- GetScheduleAsync(ScheduleUserPreferences prefs) → List<GameDto>
- GetStandingsAsync(ScheduleUserPreferences prefs) → StandingsByDivisionResponse
- GetTeamRecordsAsync(ScheduleUserPreferences prefs) → StandingsByDivisionResponse
- GetTeamResultsAsync(Guid teamId) → List<TeamResultDto>
- GetBracketsAsync(ScheduleUserPreferences prefs) → List<DivisionBracketResponse>
- GetContactsAsync(ScheduleUserPreferences prefs) → List<ContactDto>
- GetFieldsAsync() → List<FieldDisplayDto>
- GetFilterOptionsAsync() → ScheduleFilterOptionsDto
- EditScoreAsync(EditScoreRequest request) → void
```

### Phase 4: Backend — Controller

**File:** `TSIC.API/Controllers/ViewScheduleController.cs`

```
[ApiController]
[Route("api/[controller]")]

// Public-accessible (when Job.BScheduleAllowPublicAccess is true)
[AllowAnonymous or conditional]
POST   /api/view-schedule/games              → GetScheduleAsync(prefs)
POST   /api/view-schedule/standings          → GetStandingsAsync(prefs)
POST   /api/view-schedule/team-records       → GetTeamRecordsAsync(prefs)
POST   /api/view-schedule/brackets           → GetBracketsAsync(prefs)
GET    /api/view-schedule/filter-options     → GetFilterOptionsAsync()
GET    /api/view-schedule/team-results/{id}  → GetTeamResultsAsync(teamId)
GET    /api/view-schedule/fields             → GetFieldsAsync()

// Admin or Scorer only
[Authorize(Policy = "AdminOnly")]
POST   /api/view-schedule/edit-score         → EditScoreAsync(request)

// Authenticated only (contacts may be hidden by league setting)
[Authorize]
POST   /api/view-schedule/contacts           → GetContactsAsync(prefs)
```

### Phase 5: Frontend — Generate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

### Phase 6: Frontend — Components

**Location:** `src/app/views/admin/scheduling/view-schedule/`

```
view-schedule.component.ts           — Main container with tabs
├── schedule-filters.component.ts     — Reusable filter panel (shared with 009-6 Rescheduler)
├── games-tab.component.ts            — Syncfusion Grid with games
├── standings-tab.component.ts        — Standings grouped by division
├── records-tab.component.ts          — Full season records
├── brackets-tab.component.ts         — Syncfusion Diagram brackets
├── contacts-tab.component.ts         — Staff contact list
└── team-results-modal.component.ts   — Drill-down team game history
```

Key signals:
- `activeTab` — signal<'games' | 'standings' | 'records' | 'brackets' | 'contacts'>
- `filters` — signal<ScheduleUserPreferences>
- `filterOptions` — signal<ScheduleFilterOptionsDto>
- `games` — signal<GameDto[]>
- `standings` — signal<StandingsByDivisionResponse | null>
- `brackets` — signal<DivisionBracketResponse[]>
- `contacts` — signal<ContactDto[]>
- `isPublicMode` — signal<boolean>
- `canScore` — signal<boolean>

### Phase 7: Frontend — Routes

```typescript
// Authenticated admin/coach view
{
  path: 'admin/scheduling/view-schedule',
  loadComponent: () => import('./views/admin/scheduling/view-schedule/view-schedule.component')
    .then(m => m.ViewScheduleComponent),
  canActivate: [authGuard],
  data: { roles: ['SuperUser', 'Director', 'SuperDirector', 'Scorer'] }
},
// Public view (when BScheduleAllowPublicAccess is enabled)
{
  path: 'schedule/:jobId',
  loadComponent: () => import('./views/admin/scheduling/view-schedule/view-schedule.component')
    .then(m => m.ViewScheduleComponent),
  data: { publicMode: true }
}
```

### Phase 8: Testing

- Verify public access mode shows games/standings/brackets but hides contacts and score editing
- Verify filter persistence across tab switches
- Verify standings calculation matches legacy (Points = 3W + 1T, GD capped at ±9)
- Verify sport-specific sorting (soccer vs. lacrosse)
- Verify bracket rendering shows correct advancement (winner in green, loser in red)
- Verify score entry triggers bracket auto-advancement
- Verify Firebase push notification on score change
- Verify SignalR live updates (when enabled)
- Verify CSV/Excel export produces correct data
- Verify team results drill-down shows all games with W-L-T
- Verify contacts hidden when `League.BHideContacts` is true
