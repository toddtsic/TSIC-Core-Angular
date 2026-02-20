# Auto-Build Entire Schedule — Implementation Plan

> **Status**: DRAFT
> **Date**: 2026-02-20
> **Feature**: Pattern-replay auto-scheduling from prior year's proven schedule
> **Persistent copy**: Save to `docs/Plans/AUTO-BUILD-SCHEDULE.md` at implementation start

---

## Context

Tournament schedulers spend weeks manually placing games, balancing competing constraints: parents (compressed), vendors (spread out), college recruiters (elite games staggered vertically), field conditions (turf/grass, age restrictions), and parking capacity. Next year, they repeat the process despite ~80% identical structural inputs.

**The insight**: Last year's schedule was battle-tested. If fields, agegroups, and timeslot parameters are similar, replay that proven pattern onto this year's data. The scheduler keeps steps 1-4 (pool assignment, fields, pairings, timeslots) — only step 5 (game placement) is automated.

**UX vision**: A conversational "AI agent" panel — not a rigid wizard. The system analyzes, reports findings conversationally, asks intelligent questions about mismatches, validates parking, and builds on approval.

---

## Pattern Extraction Strategy

The pattern extracted from last year's schedule is:

```
For each game: (AgegroupName, DivName, Round, GameNumber)
                    → (FieldName, DayOfWeek, TimeOfDay, DayOrdinal)
```

- **FieldName**: from `Schedule.FName` — fields are shared entities, names persist across years
- **DayOfWeek**: from `Schedule.GDate.DayOfWeek` — abstracts away calendar dates
- **TimeOfDay**: from `Schedule.GDate.TimeOfDay` — preserves exact time placement
- **DayOrdinal**: sorted position of the date (0=first Saturday, 1=first Sunday, etc.) — handles multi-day events

To replay: match DayOrdinal to this year's sorted dates, match FieldName, combine date + TimeOfDay for the game's GDate.

---

## Division Matching Algorithm

1. Get source divisions: `(AgegroupName, DivName, TeamCount, GameCount)` from prior year
2. Get current divisions: `(AgegroupId, AgegroupName, DivId, DivName, TeamCount)` from this year
3. Normalize source AgegroupName with year-increment logic (reuse `IncrementYearsInName` from `JobCloneService`)
4. Match by `(normalized AgegroupName, DivName)`:

| Match Type | Condition | Action |
|---|---|---|
| **ExactMatch** | Same agegroup + div + team count | Direct pattern replay |
| **SizeMismatch** | Same agegroup + div, different team count | Ask user: replay/auto-schedule/skip |
| **NewDivision** | Exists this year only | Fallback to existing `AutoScheduleDivAsync` |
| **RemovedDivision** | Existed last year only | Ignored (shown as gray in analysis) |

---

## Agent Conversation Phases

### Phase 1: Source Selection
- System auto-detects prior year jobs (same `CustomerId`, ordered by Year DESC)
- Pre-selects most recent with games
- User confirms or picks a different source

### Phase 2: Analysis (backend `AnalyzeAsync`)
- Extract pattern from source schedule
- Match divisions
- Check field availability
- Compute feasibility score

### Phase 3: Feasibility Report
- Green/yellow/red division breakdown
- Confidence percentage
- Field mismatch warnings

### Phase 4: Questions (for yellow/red items)
- Per-division interactive cards for size mismatches
- Field remapping if names changed
- Options: "Use current pairings" / "Auto-schedule from scratch" / "Skip"

### Phase 5: Preview
- Parking validation (teams/cars on-site per complex)
- Conflict check summary
- Bracket game caveat messaging

### Phase 6: Approval
- Final summary: "Ready to build X games across Y divisions"
- Checkboxes: include bracket games, skip already-scheduled divisions

### Phase 7: Execution
- Division-by-division progress
- Real-time counter

### Phase 8: Results
- Per-division breakdown (placed / failed / skipped)
- Undo button
- Links to View Schedule and Schedule Division for manual tweaks

---

## Backend Architecture

### New Files

| # | File | Purpose |
|---|------|---------|
| 1 | `TSIC.Contracts/Dtos/Scheduling/AutoBuildDtos.cs` | All DTOs (pattern, matching, feasibility, request, result, parking) |
| 2 | `TSIC.Contracts/Repositories/IAutoBuildRepository.cs` | Repository interface for pattern extraction, division matching, field mapping |
| 3 | `TSIC.Contracts/Services/IAutoBuildScheduleService.cs` | Service interface (GetSourceJobs, Analyze, Build, Undo, ParkingPreview) |
| 4 | `TSIC.Infrastructure/Repositories/AutoBuildRepository.cs` | Repository implementation |
| 5 | `TSIC.API/Services/Scheduling/AutoBuildScheduleService.cs` | Core orchestration: pattern extraction → matching → generation |
| 6 | `TSIC.API/Controllers/AutoBuildController.cs` | REST endpoints (AdminOnly) |

### Modified Files

| File | Change |
|------|--------|
| `TSIC.API/Program.cs` | DI registration for `IAutoBuildRepository` + `IAutoBuildScheduleService` |

### Controller Endpoints

```
GET  /api/auto-build/source-jobs          → List<AutoBuildSourceJobDto>
POST /api/auto-build/analyze              → AutoBuildAnalysisResponse
POST /api/auto-build/execute              → AutoBuildResult
POST /api/auto-build/undo                 → { gamesDeleted: int }
GET  /api/auto-build/parking-preview      → AutoBuildParkingResponse?
```

### Key DTOs

```csharp
// Pattern extraction
public record GamePlacementPattern {
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required int Rnd { get; init; }
    public required int GameNumber { get; init; }
    public required string FieldName { get; init; }
    public required Guid FieldId { get; init; }
    public required DayOfWeek DayOfWeek { get; init; }
    public required TimeSpan TimeOfDay { get; init; }
    public required int DayOrdinal { get; init; }
    public required string T1Type { get; init; }
    public required string T2Type { get; init; }
}

// Division matching
public record DivisionMatch {
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public Guid? CurrentDivId { get; init; }
    public Guid? CurrentAgegroupId { get; init; }
    public required int SourceTeamCount { get; init; }
    public int? CurrentTeamCount { get; init; }
    public required DivisionMatchType MatchType { get; init; }
    public required int SourceGameCount { get; init; }
}

public enum DivisionMatchType { ExactMatch, SizeMismatch, NewDivision, RemovedDivision }

// Feasibility
public record AutoBuildFeasibility {
    public required int TotalDivisions { get; init; }
    public required int ExactMatches { get; init; }
    public required int SizeMismatches { get; init; }
    public required int NewDivisions { get; init; }
    public required int RemovedDivisions { get; init; }
    public required string ConfidenceLevel { get; init; }  // "green" | "yellow" | "red"
    public required int ConfidencePercent { get; init; }
    public required List<string> FieldMismatches { get; init; }
    public required List<string> Warnings { get; init; }
}

// Build request (after user answers questions)
public record AutoBuildRequest {
    public required Guid SourceJobId { get; init; }
    public List<Guid>? SkipDivisionIds { get; init; }
    public List<SizeMismatchResolution>? MismatchResolutions { get; init; }
    public bool IncludeBracketGames { get; init; }
    public bool SkipAlreadyScheduled { get; init; }
}

// Build result
public record AutoBuildResult {
    public required int TotalDivisions { get; init; }
    public required int DivisionsScheduled { get; init; }
    public required int DivisionsSkipped { get; init; }
    public required int TotalGamesPlaced { get; init; }
    public required int GamesFailedToPlace { get; init; }
    public required List<AutoBuildDivisionResult> DivisionResults { get; init; }
}

// Parking
public record ParkingPreviewDto {
    public required string ComplexName { get; init; }
    public required DateTime PeakTime { get; init; }
    public required int PeakTeamsOnSite { get; init; }
    public required int EstimatedCars { get; init; }
    public required string Severity { get; init; }  // "ok" | "warn" | "critical"
}
```

### Core Build Algorithm (in AutoBuildScheduleService.BuildAsync)

```
1. Load pattern placements for source job (grouped by AgegroupName+DivName)
2. Load current year's timeslot dates (sorted ascending → DayOrdinal map)
3. Load current year's field map (FName → FieldId)
4. Initialize occupied slots via GetOccupiedSlotsAsync
5. For each division (sorted by agegroup, then divname):
   a. Skip if in SkipDivisionIds
   b. Skip if already has games AND SkipAlreadyScheduled=true
   c. If ExactMatch or SizeMismatch with "use-current-pairings":
      - Get current pairings for this division
      - Get pattern placements for this (AgegroupName, DivName)
      - Map DayOrdinal → current year date
      - Map FieldName → current year FieldId
      - Match pairings to placements by (Rnd, GameNumber)
      - For each: check occupied, place if open, fallback if occupied
      - Bulk sync team assignments
   d. If SizeMismatch with "auto-schedule": delegate to AutoScheduleDivAsync
   e. If NewDivision: delegate to AutoScheduleDivAsync
6. Return AutoBuildResult with per-division breakdown
```

### Reusable Patterns from Existing Code

| What | Where | How to Reuse |
|------|-------|-------------|
| `FindNextAvailableTimeslot()` | `ScheduleDivisionService:374` | Extract to shared helper or call via service for fallback |
| `IncrementYearsInName()` | `JobCloneService:654` | Extract to shared utility class |
| `GetOccupiedSlotsAsync()` | `IScheduleRepository` | Use directly — same occupied slot tracking |
| `SynchronizeScheduleTeamAssignmentsForDivisionAsync()` | `IScheduleRepository` | Call after each division's games are placed |
| `AddGame()` + `SaveChangesAsync()` | `IScheduleRepository` | Use for game creation |
| `DeleteDivisionGamesAsync()` | `IScheduleRepository` | Use for undo and pre-build cleanup |
| Field complex extraction | Parking sproc | `FName.Substring(0, FName.IndexOf('-'))` |

---

## Frontend Architecture

### New Files

```
src/app/views/admin/scheduling/auto-build/
├── auto-build.component.ts/.html/.scss    — Main container + conversation state machine
├── services/
│   └── auto-build.service.ts              — HTTP service
└── components/
    ├── agent-message.component.ts          — Message bubble (icon, text, actions)
    ├── source-picker.component.ts          — Phase 1: prior year job selector
    ├── division-match-card.component.ts    — Phase 3: green/yellow/red match cards
    ├── question-card.component.ts          — Phase 4: interactive mismatch resolution
    ├── parking-preview.component.ts        — Phase 5: parking table
    ├── build-progress.component.ts         — Phase 7: division-by-division progress
    └── build-results.component.ts          — Phase 8: summary with undo
```

### Modified Files

| File | Change |
|------|--------|
| `app.routes.ts` | Add `auto-build` route as child of scheduling shell |
| `scheduling-dashboard.component.html` | Add "Auto-Build Schedule" tool link |

### State Machine

```typescript
type AutoBuildPhase =
  | 'idle' | 'source-selection' | 'analyzing' | 'feasibility'
  | 'questions' | 'preview' | 'approval' | 'building' | 'results' | 'error';

// All state in signals:
phase = signal<AutoBuildPhase>('idle');
messages = signal<AgentMessage[]>([]);
sourceJobId = signal<string | null>(null);
analysis = signal<AutoBuildAnalysisResponse | null>(null);
userAnswers = signal<Map<string, string>>(new Map());
buildResult = signal<AutoBuildResult | null>(null);
```

### Agent Message Types

```typescript
type AgentMessageType = 'thinking' | 'info' | 'success' | 'warning' | 'question' | 'error' | 'result';

interface AgentMessage {
  id: string;
  type: AgentMessageType;
  content: string;
  timestamp: Date;
  component?: string;       // embedded component key for interactive cards
  componentData?: unknown;
  actions?: AgentAction[];   // clickable buttons within the message
}
```

### Conversational UX

- Left-aligned message bubbles with agent avatar (CPU/magic icon)
- Thinking animation: three-dot pulse with staggered opacity
- Messages appear with subtle slide-up entrance animation
- Auto-scroll to latest message
- Interactive cards inline within the message flow
- Sticky bottom bar for current phase's primary action
- Full-page route under scheduling shell (not a modal)
- All styling via CSS variables (palette-responsive)

### Route

```typescript
// In app.routes.ts, under scheduling shell children:
{ path: 'auto-build', loadComponent: () =>
    import('./auto-build/auto-build.component').then(m => m.AutoBuildComponent) }
```

Dashboard button in Post-Scheduling Tools:
```html
<a routerLink="auto-build" class="tool-link">
    <i class="bi bi-magic"></i>
    <span>Auto-Build Schedule</span>
</a>
```

---

## Phased Delivery

### Phase 1: Backend Pattern Extraction + Matching
- DTOs, repository interface/impl, service interface
- Pattern extraction query (Schedule → GamePlacementPattern)
- Division matching algorithm with year-increment normalization
- Source job discovery (CustomerId-based)
- DI registration

### Phase 2: Backend Schedule Generation
- `AutoBuildScheduleService.BuildAsync()` — core replay algorithm
- Controller with all endpoints
- Fallback integration with existing `AutoScheduleDivAsync`
- Undo capability
- Transaction wrapping

### Phase 3: Frontend Agent UI
- Run `2-Regenerate-API-Models.ps1` for frontend DTOs
- Auto-build component + all sub-components
- HTTP service
- Conversational state machine
- Route + dashboard button integration
- Styling (design system compliant)

### Phase 4: Parking Validation
- Parking calculation (C# implementation of the sproc logic)
- `parking-preview.component.ts`
- Graceful skip if data unavailable

### Phase 5: Polish & Edge Cases
- Partially-scheduled event handling
- Field fuzzy matching
- Bracket game messaging
- Error recovery + rollback
- Accessibility (keyboard nav, ARIA)
- Performance for large schedules

---

## Edge Cases

| Scenario | Handling |
|----------|----------|
| No prior year job exists | Info message, link to manual scheduling |
| Source job has zero games | Error: "No games to use as template" |
| Fields renamed between years | Match by FieldId first, then FName prefix, warn if unmatched |
| Team count changed | SizeMismatch: user picks strategy per division |
| New division this year | Fallback to existing `AutoScheduleDivAsync` |
| Already-scheduled divisions | Detect + offer skip or replace |
| Pattern slot occupied | Log conflict, fallback to `FindNextAvailableTimeslot` |
| No timeslots/pairings configured | Error: "Complete steps 3-4 first" |
| Bracket games | Replay positions, message that team assignments resolve after RR standings |
| Parking proc unavailable | Gracefully skip, show "not available" info |
| Build fails mid-way | Transaction rollback, return error |

---

## Verification

1. **Backend unit tests**: Division matching with year-incremented names, various team count combos
2. **Integration test**: Create source job with schedule → clone job → run auto-build → verify game placement matches pattern
3. **Frontend smoke test**: Navigate to `/:jobPath/admin/scheduling/auto-build`, verify agent conversation flow renders
4. **Build verification**: `dotnet build && dotnet test`
5. **Frontend build**: `ng build` (zero errors)
6. **Manual test**: Use existing dev data — find a job with a completed schedule, clone it, run auto-build against the clone
