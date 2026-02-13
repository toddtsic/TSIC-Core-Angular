# Migration Plan 009-6: Rescheduler/Index → Rescheduler

## Context

The Rescheduler is the **final tool** in the scheduling pipeline — **step 6**, strictly admin-only. After the schedule is generated (009-4) and published for viewing (009-5), real-world issues inevitably arise: weather delays, field closures, team conflicts, referee no-shows. The Rescheduler gives administrators the power to:

1. **Move/swap games** across a cross-division visual grid (unlike 009-4 which operates on a single division)
2. **Bulk adjust for weather delays** — shift an entire game day's start time and intervals via stored procedure
3. **Email all affected participants** — compose and send rich-text notifications to players, parents, club reps, and league contacts

This is the only scheduling tool with **bulk email capability** and **weather adjustment**. It shares the `Schedule` table and filter infrastructure with View Schedule (009-5) but has a completely different authorization model and user workflow.

**Legacy URL:** `/Rescheduler/Index` (Controller=Rescheduler, Action=Index)

**Legacy Controller:** `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Scheduling/ReschedulerController.cs`
**Legacy View:** `reference/TSIC-Unify-2024/TSIC-Unify/Views/Rescheduler/Index.cshtml`

---

## 1. Legacy Strengths (Preserve These!)

- **Cross-division view** — shows all divisions in one grid (unlike 009-4 which is single-division), with agegroup color coding for visual distinction
- **Weather delay adjustment** — stored procedure `[utility].[ScheduleAlterGSIPerGameDate]` bulk-updates game times and intervals for an entire day with comprehensive validation (8 error codes)
- **Per-field selectability** — weather adjustment can target specific fields, leaving others untouched
- **Bulk email with rich text** — CKEditor composition with variable substitution for personalized messages
- **Smart email recipients** — automatically collects player emails, parent (mom/dad) emails, club rep emails, and league-wide addon recipients
- **Email audit trail** — all sent emails logged to `EmailLogs` table with sender, timestamp, and batch ID
- **Additional timeslot injection** — manually add a game time that doesn't exist in the timeslot configuration
- **Game move/swap** — identical click-to-select, click-to-place pattern as 009-4
- **Multi-criteria filtering** — filter by club, team, game day, field, agegroup, division

## 2. Legacy Pain Points (Fix These!)

- **CKEditor 4 dependency** — end-of-life; replace with Syncfusion Rich Text Editor (already licensed)
- **Email sends are synchronous** — blocks UI during bulk email send; large recipient lists (100+) cause timeout risk
- **No email preview** — admin can't preview the composed email before sending
- **No delivery status** — fire-and-forget; admin gets confirmation count but no bounce tracking
- **Weather adjustment error codes are magic numbers** — returned as int (1–8) with no human-readable messages from the server
- **No confirmation of affected game count** — weather adjustment doesn't preview how many games will be shifted before executing
- **Direct SqlDbContext** — controller accesses database directly

## 3. Modern Vision

**Recommended UI: Filterable Cross-Division Grid + Action Modals**

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Rescheduler                                                                 │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─ Filters ───────────────────────────────────────────────────────────────┐│
│  │ Clubs: [☑ ▼]  Teams: [☑ ▼]  Days: [☑ ▼]  Fields: [☑ ▼]             ││
│  │ Agegroups: [☑ ▼]  Divisions: [☑ ▼]                                   ││
│  │                                                                          ││
│  │ Additional Timeslot: [datetime-local    ]                                ││
│  │                                                                          ││
│  │ [Load Schedule]  [Clear]                                                 ││
│  └──────────────────────────────────────────────────────────────────────────┘│
│                                                                              │
│  ┌─ Active Filters ─────────────────────────────────────────────┐           │
│  │ Day: Sat 3/1 ✕ │ Fields: Cedar Pk ✕, Lakeline ✕ │ [Clear]  │           │
│  └──────────────────────────────────────────────────────────────┘           │
│                                                                              │
│  [Adjust for Weather]  [Email Participants]                                 │
│                                                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │ Date/Time       │ Cedar Park A    │ Lakeline        │ Round Rock    │   │
│  ├──────────────────────────────────────────────────────────────────────┤   │
│  │ Sat 3/1 8:00    │ U10:Gold R1     │ U10:Silver R1   │ U12:Gold R1  │   │
│  │                  │ Storm v Lonestar│ Eagles v Thunder│ FC v Dynamo  │   │
│  │                  │ [↔] [✕]        │ [↔] [✕]        │ [↔] [✕]     │   │
│  │ Sat 3/1 9:00    │ U10:Gold R1     │ OPEN SLOT       │ U12:Gold R1  │   │
│  │                  │ Texans v United │ [📝 place]      │ Hawks v Sting│   │
│  │ Sat 3/1 10:00   │ OPEN SLOT       │ OPEN SLOT       │ OPEN SLOT    │   │
│  │                  │ [📝 place]      │ [📝 place]      │ [📝 place]   │   │
│  │ ...                                                                  │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│  [↔] = Select for move/swap    [✕] = Delete game                           │
│                                                                              │
│  ── Move Mode ──                                                            │
│  Selected: Game #127 U10:Gold Storm v Lonestar (Sat 8:00 Cedar Pk)         │
│  Click a destination slot to move (empty) or swap (occupied)                │
│  [Cancel Move]                                                              │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘

═══════════════════════════════════════════════════════════════════════════════

Weather Adjustment Modal:

┌──────────────────────────────────────────────┐
│  Adjust for Weather Delay                    │
├──────────────────────────────────────────────┤
│                                              │
│  Affected games: 18                          │
│                                              │
│  Current Schedule:                           │
│  First Game: [Sat 3/1 8:00 AM  ]            │
│  Interval:   [60    ] minutes                │
│                                              │
│  New Schedule:                               │
│  First Game: [Sat 3/1 10:00 AM ]            │
│  Interval:   [50    ] minutes                │
│                                              │
│  Fields: [☑ Cedar Park A]                   │
│          [☑ Lakeline    ]                   │
│          [☐ Round Rock  ]                   │
│                                              │
│              [Cancel]  [Apply Adjustment]    │
└──────────────────────────────────────────────┘

═══════════════════════════════════════════════════════════════════════════════

Email Participants Modal:

┌──────────────────────────────────────────────────────┐
│  Email Participants                                  │
├──────────────────────────────────────────────────────┤
│                                                      │
│  Affected Games: Sat 3/1, 8:00 AM – 2:00 PM         │
│  Fields: Cedar Park A, Lakeline                      │
│  Est. Recipients: ~142 emails                        │
│                                                      │
│  Subject: [Weather delay - updated game times    ]   │
│                                                      │
│  Body:                                               │
│  ┌──────────────────────────────────────────────┐   │
│  │ [B] [I] [U] [Link] [List]                    │   │
│  │                                              │   │
│  │ Dear families,                               │   │
│  │                                              │   │
│  │ Due to weather conditions, all games on      │   │
│  │ Saturday March 1st have been pushed back     │   │
│  │ 2 hours. Please check the updated schedule.  │   │
│  │                                              │   │
│  └──────────────────────────────────────────────┘   │
│                                                      │
│  [Preview]           [Cancel]  [Send to All]         │
└──────────────────────────────────────────────────────┘
```

**Key improvements:**
- ✅ **Affected game count** — weather modal shows how many games will be shifted before executing
- ✅ **Estimated recipient count** — email modal shows count before send to prevent surprise bulk emails
- ✅ **Email preview** — see composed email rendered before sending
- ✅ **Async email send** — non-blocking with progress indication
- ✅ **Weather adjustment error messages** — human-readable instead of magic numbers
- ✅ **Rich text editor** — Syncfusion Rich Text Editor replaces CKEditor 4
- ✅ **Shared filter component** — same `schedule-filters.component.ts` as View Schedule (009-5)

**Design alignment:** Glassmorphic cards, CSS variable colors, 8px grid spacing. Same grid cell rendering as 009-4's schedule grid but cross-division.

---

## 4. Security

- **Authorization:** `[Authorize(Policy = "AdminOnly")]` on **all** endpoints — no public access
- **Email sending:** Logged to `EmailLogs` with sender's UserId and batch ID
- **Weather adjustment:** Validated server-side with 8 error code checks before execution
- **Move/swap:** Updates `RescheduleCount` and `Modified` audit fields on affected games

---

## 5. Business Rules

### Weather Adjustment Stored Procedure

```sql
EXEC [utility].[ScheduleAlterGSIPerGameDate]
  @jobId, @preFirstGame, @preGSI, @postFirstGame, @postGSI, @fieldIds

Return codes:
  1 = Success
  2 = Would create overlapping games
  3 = Invalid BEFORE GSI (doesn't match actual game intervals)
  4 = Invalid AFTER GSI
  5 = Date range must be within same calendar year
  6 = No games found in specified range
  7 = Parameters unchanged (before == after)
  8 = Off-interval games exist in range (games not aligned to GSI)
```

Human-readable messages for each code:

| Code | Message |
|------|---------|
| 1 | "Schedule adjusted successfully." |
| 2 | "Cannot apply — adjustment would create overlapping games on one or more fields." |
| 3 | "The 'before' interval doesn't match the actual game spacing. Verify the current first game time and interval." |
| 4 | "The 'after' interval is invalid. Please enter a positive number of minutes." |
| 5 | "All affected games must be within the same calendar year." |
| 6 | "No games found for the selected date/time range and fields." |
| 7 | "No changes — the before and after values are identical." |
| 8 | "Some games in the range are not aligned to the specified interval. Manual adjustment required for off-interval games." |

### Email Recipient Collection

```
For games in specified date/field range:
  1. Player emails — from Registration.User.Email
  2. Mom emails — from FamilyUser (mother) linked to Registration
  3. Dad emails — from FamilyUser (father) linked to Registration
  4. Club rep emails — from Team.ClubrepRegistration.User.Email
  5. League addon — from League.RescheduleEmailsToAddon (semicolon-delimited)

Filter: Remove nulls, empty strings, and "not@given.com" placeholder
Validate: Each email must pass EmailAddressAttribute validation
Deduplicate: Same email appearing multiple times (e.g., parent of two players) → send once
```

### Game Move/Swap Algorithm

Same as 009-4 `MoveGame`:
```
1. GET record A (game to move)
2. GET record B at target date/field

If B is null (empty slot): Move A to target
If B exists (occupied): Swap A ↔ B

3. Send email notifications to affected team coaches (if configured)
4. Increment RescheduleCount on moved game(s)
5. Update Modified timestamp and LebUserId
```

---

## 6. Implementation Steps

### Phase 1: Backend — DTOs

**File:** `TSIC.Contracts/Dtos/Scheduling/ReschedulerDtos.cs`

```csharp
public record ReschedulerGridResponse
{
    public required List<string> ColNames { get; init; }
    public required List<Guid?> ColFieldIds { get; init; }
    public required List<ReschedulerGridRow> Rows { get; init; }
}

public record ReschedulerGridRow
{
    public required DateTime GDate { get; init; }
    public required List<ReschedulerCellDto?> Cells { get; init; }
}

public record ReschedulerCellDto
{
    public required int Gid { get; init; }
    public required string AgDivLabel { get; init; }
    public required int Rnd { get; init; }
    public required string T1Label { get; init; }
    public required string T2Label { get; init; }
    public string? Color { get; init; }
}

public record AdjustWeatherRequest
{
    public required DateTime PreFirstGame { get; init; }
    public required int PreGSI { get; init; }
    public required DateTime PostFirstGame { get; init; }
    public required int PostGSI { get; init; }
    public required List<Guid> FieldIds { get; init; }
}

public record AdjustWeatherResponse
{
    public required bool Success { get; init; }
    public required int ResultCode { get; init; }
    public required string Message { get; init; }
}

public record EmailParticipantsRequest
{
    public required DateTime FirstGame { get; init; }
    public required DateTime LastGame { get; init; }
    public required string EmailSubject { get; init; }
    public required string EmailBody { get; init; }
    public required List<Guid> FieldIds { get; init; }
}

public record EmailParticipantsResponse
{
    public required int RecipientCount { get; init; }
    public required DateTime SentAt { get; init; }
}

// MoveGameRequest is shared with 009-4 (ScheduleDivisionDtos.cs)
// ScheduleUserPreferences is shared with 009-5 (ViewScheduleDtos.cs)
```

### Phase 2: Backend — Repository

**Extend `IScheduleRepository`** (or create `IReschedulerRepository` if preferred for separation):

```
New Methods:
- GetReschedulerGridAsync(Guid jobId, ScheduleUserPreferences prefs, DateTime? additionalTimeslot) → ReschedulerGridResponse
- GetAffectedGameCountAsync(DateTime preFirstGame, List<Guid> fieldIds) → int
- GetEmailRecipientsAsync(DateTime firstGame, DateTime lastGame, List<Guid> fieldIds) → List<string>
```

### Phase 3: Backend — Service

**Interface:** `TSIC.Contracts/Services/IReschedulerService.cs`
**Implementation:** `TSIC.API/Services/Scheduling/ReschedulerService.cs`

```
Methods:
- GetReschedulerGridAsync(ScheduleUserPreferences prefs, DateTime? additionalTimeslot) → ReschedulerGridResponse
- MoveGameAsync(MoveGameRequest request) → void
- AdjustForWeatherAsync(AdjustWeatherRequest request) → AdjustWeatherResponse
- GetAffectedGameCountAsync(DateTime preFirstGame, List<Guid> fieldIds) → int
- EmailParticipantsAsync(EmailParticipantsRequest request) → EmailParticipantsResponse
```

The `AdjustForWeatherAsync` method calls the stored procedure and maps the int return code to a human-readable message using the table in Section 5.

### Phase 4: Backend — Controller

**File:** `TSIC.API/Controllers/ReschedulerController.cs`

```
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/[controller]")]

POST   /api/rescheduler/grid                    → GetReschedulerGridAsync(prefs)
POST   /api/rescheduler/move-game               → MoveGameAsync(request)
POST   /api/rescheduler/adjust-weather           → AdjustForWeatherAsync(request)
GET    /api/rescheduler/affected-count?...       → GetAffectedGameCountAsync(...)
POST   /api/rescheduler/email-participants       → EmailParticipantsAsync(request)
```

### Phase 5: Frontend — Generate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

### Phase 6: Frontend — Components

**Location:** `src/app/views/admin/scheduling/rescheduler/`

```
rescheduler.component.ts              — Main container
├── schedule-filters.component.ts      — Shared with View Schedule (009-5)
├── rescheduler-grid.component.ts      — Dynamic date×field grid with move/swap
├── weather-modal.component.ts         — Weather adjustment form with preview
└── email-modal.component.ts           — Syncfusion Rich Text Editor email composition
```

Key signals:
- `filters` — signal<ScheduleUserPreferences>
- `gridData` — signal<ReschedulerGridResponse | null>
- `selectedGame` — signal<ReschedulerCellDto | null> (for move mode)
- `isMoveMode` — signal<boolean>
- `isLoading` — signal<boolean>

### Phase 7: Frontend — Route

```typescript
{
  path: 'admin/scheduling/rescheduler',
  loadComponent: () => import('./views/admin/scheduling/rescheduler/rescheduler.component')
    .then(m => m.ReschedulerComponent),
  canActivate: [authGuard],
  data: { roles: ['SuperUser', 'Director', 'SuperDirector'] }
}
```

### Phase 8: Testing

- Verify game move to empty slot updates GDate, FieldId, FName
- Verify game swap between two occupied slots swaps all fields correctly
- Verify weather adjustment: 8:00 AM / 60min → 10:00 AM / 50min updates all games correctly
- Verify all 8 weather adjustment return codes produce correct human-readable messages
- Verify affected game count preview before weather adjustment
- Verify email recipient collection: players + parents + club reps + league addon
- Verify email validation filters invalid addresses and deduplicates
- Verify email audit trail in EmailLogs with sender, timestamp, batch ID
- Verify additional timeslot injection adds new row to grid
- Verify RescheduleCount increments on moved games
- Verify cross-division grid shows agegroup colors correctly
- Verify filter component is shared instance with View Schedule
