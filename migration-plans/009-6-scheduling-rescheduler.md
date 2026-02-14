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

### ⚠️ CRITICAL PATTERNS (verified from codebase)

**Auth — NO leagueId claim exists.** Use standard `ResolveContext()`:
```csharp
private async Task<(Guid? jobId, string? userId, ActionResult? error)> ResolveContext()
{
    var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
    if (jobId == null)
        return (null, null, BadRequest(new { message = "Registration context required" }));

    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return (null, null, Unauthorized());

    return (jobId, userId, null);
}
```

**Filters — match `ScheduleFilterRequest` pattern** (same as View Schedule / Reg Search):
- `List<T>?` for multi-select (null = no filter, OR-union logic)
- `POST` body for filter criteria, `GET` for filter options
- Reuse `ScheduleFilterOptionsDto` (CADT tree + GameDays + Fields) from ViewScheduleDtos.cs

**Grid — reuse existing DTOs from ScheduleDivisionDtos.cs:**
- `ScheduleGridResponse` (Columns + Rows)
- `ScheduleGridRow` (GDate + Cells)
- `ScheduleGameDto` (game cell — already has AgDivLabel, Color, T1Id/T2Id for conflict detection)
- `ScheduleFieldColumn` (FieldId + FName)

**Move/Swap — identical technique to Schedule Division** (ScheduleDivisionService.MoveGameAsync):
- Reuse `MoveGameRequest` (Gid, TargetGDate, TargetFieldId)
- Same backend logic: GetGameById → GetGameAtSlot → move or swap → RescheduleCount++ → SaveChanges
- Frontend click-to-select, click-to-place pattern identical to schedule-division.component.ts

**Stored procedure — follow ReportingRepository pattern:**
- Repository creates `DbCommand` via `_context.Database.GetDbConnection()`
- `CommandType = CommandType.StoredProcedure`
- `SqlParameter` with explicit `SqlDbType`
- Output parameter for `@resultCode`

**Email — use existing `IEmailService.SendAsync()`** (Amazon SES):
- Build `EmailMessageDto` { ToAddresses, Subject, HtmlBody }
- Log to `EmailLogs` entity after send
- Registered as Singleton in Program.cs

---

### Phase 1: Backend — DTOs

**File:** `TSIC.Contracts/Dtos/Scheduling/ReschedulerDtos.cs`

Only Rescheduler-specific DTOs — grid/game/filter DTOs are reused from existing files.

```csharp
namespace TSIC.Contracts.Dtos.Scheduling;

// ── Grid request (extends ScheduleFilterRequest with additional timeslot) ──

/// <summary>
/// POST body for the rescheduler grid — same filter structure as View Schedule
/// plus optional additional timeslot injection.
/// </summary>
public record ReschedulerGridRequest
{
    // Same filter pattern as ScheduleFilterRequest (List<T>? = null means no filter)
    public List<string>? ClubNames { get; init; }
    public List<Guid>? AgegroupIds { get; init; }
    public List<Guid>? DivisionIds { get; init; }
    public List<Guid>? TeamIds { get; init; }
    public List<DateTime>? GameDays { get; init; }
    public List<Guid>? FieldIds { get; init; }

    /// <summary>
    /// Optional datetime to inject as an additional row in the grid.
    /// Allows admin to create a new timeslot that doesn't exist in the timeslot configuration.
    /// </summary>
    public DateTime? AdditionalTimeslot { get; init; }
}

// ── Weather adjustment ──

/// <summary>
/// Adjusts game times in batch via stored procedure [utility].[ScheduleAlterGSIPerGameDate].
/// "Before" = current schedule, "After" = desired schedule.
/// </summary>
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

/// <summary>
/// Preview: how many games would be affected by a weather adjustment.
/// </summary>
public record AffectedGameCountResponse
{
    public required int Count { get; init; }
}

// ── Email participants ──

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
    public required int FailedCount { get; init; }
    public required DateTime SentAt { get; init; }
}

/// <summary>
/// Preview: estimated recipient count before actually sending.
/// </summary>
public record EmailRecipientCountResponse
{
    public required int EstimatedCount { get; init; }
}

// ── Shared DTOs (NOT defined here — reuse from existing files) ──
// MoveGameRequest            → ScheduleDivisionDtos.cs
// ScheduleGridResponse       → ScheduleDivisionDtos.cs (Columns + Rows)
// ScheduleGridRow            → ScheduleDivisionDtos.cs (GDate + Cells)
// ScheduleGameDto            → ScheduleDivisionDtos.cs (game cell with Color, T1Id/T2Id)
// ScheduleFieldColumn        → ScheduleDivisionDtos.cs (FieldId + FName)
// ScheduleFilterOptionsDto   → ViewScheduleDtos.cs (CADT tree + GameDays + Fields)
```

### Phase 2: Backend — Repository

**Extend `IScheduleRepository`** with rescheduler-specific query methods:

```csharp
// New methods on IScheduleRepository:

/// <summary>
/// Cross-division grid — returns ALL games matching filters (not scoped to one division).
/// Reuses ScheduleGridResponse (same Columns/Rows/Cells shape as Schedule Division).
/// If additionalTimeslot is provided, injects an extra row at that datetime.
/// </summary>
Task<ScheduleGridResponse> GetReschedulerGridAsync(
    Guid jobId,
    ReschedulerGridRequest request,
    CancellationToken ct = default);

/// <summary>
/// Count games in a date/field range — used for weather adjustment preview.
/// </summary>
Task<int> GetAffectedGameCountAsync(
    Guid jobId,
    DateTime preFirstGame,
    List<Guid> fieldIds,
    CancellationToken ct = default);

/// <summary>
/// Collect and deduplicate email addresses for games in date/field range.
/// Sources: player, mom, dad, club rep, league reschedule addon.
/// Filters: removes nulls, empty, "not@given.com", invalid emails.
/// </summary>
Task<List<string>> GetEmailRecipientsAsync(
    Guid jobId,
    DateTime firstGame,
    DateTime lastGame,
    List<Guid> fieldIds,
    CancellationToken ct = default);

/// <summary>
/// Execute stored procedure [utility].[ScheduleAlterGSIPerGameDate].
/// Returns the int result code (1=success, 2-8=error).
/// </summary>
Task<int> ExecuteWeatherAdjustmentAsync(
    Guid jobId,
    AdjustWeatherRequest request,
    CancellationToken ct = default);
```

**Grid assembly logic** (ScheduleRepository implementation):
1. Query `Schedule` table with OR-union filters (same pattern as `GetFilteredGamesAsync` in 009-5)
2. Apply each non-null filter: ClubNames (via T1/T2 club join), AgegroupIds, DivisionIds, TeamIds (T1Id/T2Id), GameDays, FieldIds
3. Build distinct field columns from matched games (+ any fields from FieldIds filter)
4. Build distinct timeslot rows from matched game GDates
5. If `AdditionalTimeslot` provided and not already in rows, inject it (sorted)
6. For each row×column cell: find matching game or null (open slot)
7. Map to `ScheduleGameDto` — same projection as Schedule Division but cross-division (join Agegroup for Color)

**Stored procedure execution** (follows ReportingRepository pattern):
```csharp
public async Task<int> ExecuteWeatherAdjustmentAsync(
    Guid jobId, AdjustWeatherRequest request, CancellationToken ct = default)
{
    var connection = _context.Database.GetDbConnection();
    var cmd = connection.CreateCommand();

    cmd.CommandText = "[utility].[ScheduleAlterGSIPerGameDate]";
    cmd.CommandType = CommandType.StoredProcedure;

    cmd.Parameters.Add(new SqlParameter("@jobId", SqlDbType.UniqueIdentifier) { Value = jobId });
    cmd.Parameters.Add(new SqlParameter("@preFirstGame", SqlDbType.DateTime) { Value = request.PreFirstGame });
    cmd.Parameters.Add(new SqlParameter("@preGSI", SqlDbType.Int) { Value = request.PreGSI });
    cmd.Parameters.Add(new SqlParameter("@postFirstGame", SqlDbType.DateTime) { Value = request.PostFirstGame });
    cmd.Parameters.Add(new SqlParameter("@postGSI", SqlDbType.Int) { Value = request.PostGSI });
    cmd.Parameters.Add(new SqlParameter("@strFieldIds", SqlDbType.Text)
    {
        Value = string.Join(";", request.FieldIds)
    });
    cmd.Parameters.Add(new SqlParameter("@resultCode", SqlDbType.Int)
    {
        Direction = ParameterDirection.Output, Value = 0
    });

    if (connection.State != ConnectionState.Open)
        await connection.OpenAsync(ct);

    try
    {
        await cmd.ExecuteNonQueryAsync(ct);
        return Convert.ToInt32(cmd.Parameters["@resultCode"].Value);
    }
    finally
    {
        if (connection.State == ConnectionState.Open)
            await connection.CloseAsync();
    }
}
```

### Phase 3: Backend — Service

**Interface:** `TSIC.Contracts/Services/IReschedulerService.cs`
**Implementation:** `TSIC.API/Services/Scheduling/ReschedulerService.cs`

```csharp
public interface IReschedulerService
{
    // ── Grid ──
    Task<ScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default);
    Task<ScheduleGridResponse> GetReschedulerGridAsync(Guid jobId, ReschedulerGridRequest request, CancellationToken ct = default);

    // ── Move/Swap (identical to ScheduleDivisionService.MoveGameAsync) ──
    Task MoveGameAsync(string userId, MoveGameRequest request, CancellationToken ct = default);

    // ── Weather adjustment ──
    Task<AffectedGameCountResponse> GetAffectedGameCountAsync(Guid jobId, DateTime preFirstGame, List<Guid> fieldIds, CancellationToken ct = default);
    Task<AdjustWeatherResponse> AdjustForWeatherAsync(Guid jobId, AdjustWeatherRequest request, CancellationToken ct = default);

    // ── Email ──
    Task<EmailRecipientCountResponse> GetEmailRecipientCountAsync(Guid jobId, DateTime firstGame, DateTime lastGame, List<Guid> fieldIds, CancellationToken ct = default);
    Task<EmailParticipantsResponse> EmailParticipantsAsync(Guid jobId, string userId, EmailParticipantsRequest request, CancellationToken ct = default);
}
```

Constructor injects: `IScheduleRepository`, `IFieldRepository`, `IEmailService`, `ILogger<ReschedulerService>`

**MoveGameAsync** — duplicates ScheduleDivisionService logic exactly:
```
GetGameByIdAsync → GetGameAtSlotAsync → move or swap → RescheduleCount++ → SaveChanges
```

**AdjustForWeatherAsync** — calls `_scheduleRepo.ExecuteWeatherAdjustmentAsync()`, maps result code:
```csharp
var code = await _scheduleRepo.ExecuteWeatherAdjustmentAsync(jobId, request, ct);
return new AdjustWeatherResponse
{
    Success = code == 1,
    ResultCode = code,
    Message = code switch
    {
        1 => "Schedule adjusted successfully.",
        2 => "Cannot apply — adjustment would create overlapping games on one or more fields.",
        3 => "The 'before' interval doesn't match the actual game spacing. Verify the current first game time and interval.",
        4 => "The 'after' interval is invalid. Please enter a positive number of minutes.",
        5 => "All affected games must be within the same calendar year.",
        6 => "No games found for the selected date/time range and fields.",
        7 => "No changes — the before and after values are identical.",
        8 => "Some games in the range are not aligned to the specified interval. Manual adjustment required for off-interval games.",
        _ => $"Unexpected result code: {code}"
    }
};
```

**EmailParticipantsAsync** — collects recipients, sends via IEmailService, logs to EmailLogs:
```
1. GetEmailRecipientsAsync → deduplicated list
2. For each recipient: build EmailMessageDto, call IEmailService.SendAsync()
3. Log batch to EmailLogs entity (Count, JobId, SendFrom, SendTo, Subject, Msg, SenderUserId, SendTs)
4. Return RecipientCount + FailedCount + SentAt
```

### Phase 4: Backend — Controller

**File:** `TSIC.API/Controllers/ReschedulerController.cs`

```csharp
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/[controller]")]
public class ReschedulerController : ControllerBase
{
    // Constructor: IReschedulerService, IJobLookupService

    // ResolveContext() — standard pattern (jobId from regId claim, userId from NameIdentifier)

    GET    /api/rescheduler/filter-options        → GetFilterOptionsAsync(jobId)
    POST   /api/rescheduler/grid                  → GetReschedulerGridAsync(jobId, ReschedulerGridRequest)
    POST   /api/rescheduler/move-game             → MoveGameAsync(userId, MoveGameRequest)
    POST   /api/rescheduler/adjust-weather        → AdjustForWeatherAsync(jobId, AdjustWeatherRequest)
    GET    /api/rescheduler/affected-count         → GetAffectedGameCountAsync(jobId, [FromQuery] DateTime preFirstGame, [FromQuery] List<Guid> fieldIds)
    POST   /api/rescheduler/email-participants    → EmailParticipantsAsync(jobId, userId, EmailParticipantsRequest)
    GET    /api/rescheduler/recipient-count        → GetEmailRecipientCountAsync(jobId, [FromQuery] DateTime firstGame, [FromQuery] DateTime lastGame, [FromQuery] List<Guid> fieldIds)
}
```

**DI Registration** (Program.cs):
```csharp
builder.Services.AddScoped<IReschedulerService, ReschedulerService>();
```

### Phase 5: Frontend — Generate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

### Phase 6: Frontend — npm install Syncfusion RTE

```bash
npm install @syncfusion/ej2-angular-richtexteditor@31.2.4
```

Match existing Syncfusion version `31.2.x` in package.json. Requires adding RTE-specific styles to `_syncfusion.scss`.

### Phase 7: Frontend — Components

**Location:** `src/app/views/admin/scheduling/rescheduler/`

```
rescheduler.component.ts/.html/.scss    — Main container + grid + "add row" input
├── services/rescheduler.service.ts     — HTTP service
├── weather-modal.component.ts          — Weather adjustment form with affected-count preview
└── email-modal.component.ts            — Syncfusion Rich Text Editor email composition
```

**Reused from View Schedule (009-5):**
- `cadt-tree-filter.component.ts` — CADT hierarchical checkbox tree (Club→Agegroup→Division→Team)
- `ScheduleFilterOptionsDto` — filter options loaded via GET on init

**Main component signals** (follows schedule-division pattern exactly):
```typescript
// Filter state
readonly filterOptions = signal<ScheduleFilterOptionsDto | null>(null);
readonly activeFilters = signal<ReschedulerGridRequest>({});
readonly isLoading = signal(false);

// Grid state — reuses same DTOs as schedule-division
readonly gridResponse = signal<ScheduleGridResponse | null>(null);
readonly gridColumns = computed(() => this.gridResponse()?.columns ?? []);
readonly gridRows = computed(() => this.gridResponse()?.rows ?? []);

// Move mode — identical pattern to schedule-division.component.ts
readonly selectedGame = signal<{ game: ScheduleGameDto; row: ScheduleGridRow; colIndex: number } | null>(null);

// Additional timeslot — "add a row" input
readonly additionalTimeslot = signal<DateTime | null>(null);

// Modals
readonly showWeatherModal = signal(false);
readonly showEmailModal = signal(false);
```

**Grid interaction — identical to schedule-division:**
```typescript
onGridCellClick(row, colIndex):
  if (selectedGame()) → moveOrSwapGame(row, colIndex)
  else if (cell) → selectGameForMove(cell, row, colIndex)

moveOrSwapGame(targetRow, targetColIndex):
  svc.moveGame({ gid, targetGDate, targetFieldId }).subscribe → reload grid

selectGameForMove(game, row, colIndex):
  toggle selectedGame signal (same as schedule-division)
```

**"Add a Row" — additional timeslot:**
- Datetime input field above or below the grid
- User enters a date/time → sets `additionalTimeslot` signal
- On "Load Schedule" / "Refresh", the `ReschedulerGridRequest.AdditionalTimeslot` is sent to the backend
- Backend injects that row into the grid response (empty cells) — allows user to move games into it

### Phase 8: Frontend — Route

```typescript
{
  path: 'admin/scheduling/rescheduler',
  loadComponent: () => import('./views/admin/scheduling/rescheduler/rescheduler.component')
    .then(m => m.ReschedulerComponent),
  canActivate: [authGuard],
  data: { roles: ['SuperUser', 'Director', 'SuperDirector'] }
}
```

### Phase 9: Testing

- Verify ResolveContext uses `GetJobIdFromRegistrationAsync` (NOT leagueId claim)
- Verify filter options load CADT tree + GameDays + Fields (same shape as View Schedule)
- Verify grid request uses `List<T>?` filter pattern (null = no filter)
- Verify grid response reuses `ScheduleGridResponse`/`ScheduleGameDto` DTOs
- Verify cross-division grid shows all divisions with agegroup color coding
- Verify game move to empty slot: identical to ScheduleDivisionService.MoveGameAsync
- Verify game swap between two occupied slots: identical swap logic
- Verify RescheduleCount increments on moved games
- Verify "add a row" — additional timeslot injects new empty row into grid
- Verify weather adjustment: affected-count preview before execute
- Verify all 8 weather SP return codes produce correct human-readable messages
- Verify email recipient collection: players + parents + club reps + league addon
- Verify email deduplication and validation filters
- Verify email audit trail in EmailLogs
- Verify Syncfusion RTE renders in email modal with glassmorphic styling
