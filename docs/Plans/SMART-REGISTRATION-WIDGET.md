# Smart Registration Widget — Feature Plan

> **Status**: DRAFT
> **Created**: 2026-02-24
> **Surfaces**: Public job page widget + Role selection "What's New" section

---

## Problem Statement

### 1. Manual Bulletin Drudgery
Today, announcing "player registration is open" or "the store is live" requires
manually creating a bulletin for each job. The information already exists as
boolean flags on the Jobs entity — but the public page doesn't surface it.

### 2. The Login Paradox
New users try to **log in first** — but they don't have an account yet. They
need to **register** first. The public page doesn't make this clear because
registration availability is buried behind authentication.

### 3. Returning-User Blindness
When a returning user logs in and reaches role selection, they see their
existing registrations — but have **no idea** what else is new or available
for jobs they were previously involved with.

### 4. Competitor Intelligence Risk
We must NEVER create a public page that lists all open registrations across
all jobs — that's a competitive intelligence gift. Registration awareness
must be **per-job only** on public pages.

---

## Two Surfaces

### Surface A — Public Job Page Widget ("Job Pulse")

**Who sees it**: Anyone visiting `/{jobPath}` (unauthenticated or authenticated)
**Where it lives**: Public widget workspace, rendered by `widget-dashboard`
**WidgetType**: `content`
**ComponentKey**: `job-pulse`
**displayStyle**: `pulse`

This widget reads **existing** boolean flags from the Jobs entity and presents
context-aware cards showing what's available for THIS job right now.

### Surface B — Role Selection "What's New" Section

**Who sees it**: Authenticated users on the role selection page (after phase-1 login)
**Where it lives**: `role-selection.component.ts` (inline section, NOT a widget)
**Why not a widget**: Role selection is pre-jobPath — no widget dashboard context

This section shows what's new/available across jobs the user has **prior
registrations** for. Cross-job awareness lives HERE only.

---

## Surface A: Job Pulse Widget — Detailed Design

### Data Source: Jobs Entity Flags (Already in DB)

| Flag | Column | Type | Meaning |
|------|--------|------|---------|
| Player registration open | `BRegistrationAllowPlayer` | bool? | Players can self-register |
| Team registration open | `BRegistrationAllowTeam` | bool? | Teams can register |
| Store enabled | `BEnableStore` | bool? | Merch store is live |
| Schedule published | `BScheduleAllowPublicAccess` | bool? | Public can view schedule |
| Player profile metadata | `PlayerProfileMetadataJson` | string? | Non-null = player reg is planned |
| Adult profile metadata | `AdultProfileMetadataJson` | string? | Non-null = adult reg is planned |
| Public suspended | `BSuspendPublic` | bool | Entire public access suspended |
| Registration expiry | `ExpiryUsers` | DateTime | When registration window closes |

### What the Widget Displays

Each card appears **only when its condition is true**. No cards = widget hidden.

#### Card: "Player Registration is Open"
- **Condition**: `BRegistrationAllowPlayer == true`
- **CTA**: "Register Now" → links to `/{jobPath}/family-account`
- **Icon**: `bi-person-plus`

#### Card: "Team Registration is Open"
- **Condition**: `BRegistrationAllowTeam == true`
- **CTA**: "Register Your Team" → links to `/{jobPath}/register-team`
- **Icon**: `bi-shield-plus`

#### Card: "Shop the Merch Store"
- **Condition**: `BEnableStore == true` AND store has active items
- **CTA**: "Browse Store" → links to `/{jobPath}/store`
- **Icon**: `bi-bag`

#### Card: "Schedules Are Live"
- **Condition**: `BScheduleAllowPublicAccess == true`
- **CTA**: "View Schedule" → links to `/{jobPath}/scheduling/view-schedule`
- **Icon**: `bi-calendar-check`

#### Card: "Player Registration Coming Soon"
- **Condition**: `PlayerProfileMetadataJson != null` AND `BRegistrationAllowPlayer != true`
- **CTA**: None (informational only)
- **Icon**: `bi-clock`
- **Note**: ProfileMetadata being configured = intent signal that reg will open

#### Card: "Adult Registration Coming Soon"
- **Condition**: `AdultProfileMetadataJson != null` AND adult reg not yet open
- **CTA**: None (informational only)
- **Icon**: `bi-clock`

### Backend Changes

#### 1. New DTO: `JobPulseDto`

**File**: `TSIC.Contracts/Dtos/JobPulseDtos.cs`

```csharp
public record JobPulseDto
{
    public required bool PlayerRegistrationOpen { get; init; }
    public required bool TeamRegistrationOpen { get; init; }
    public required bool StoreEnabled { get; init; }
    public required bool StoreHasActiveItems { get; init; }
    public required bool SchedulePublished { get; init; }
    public required bool PlayerRegistrationPlanned { get; init; }
    public required bool AdultRegistrationPlanned { get; init; }
    public required bool PublicSuspended { get; init; }
    public DateTime? RegistrationExpiry { get; init; }
}
```

**Why a dedicated DTO?** The existing `JobMetadataResponse` is already bloated
with branding/waiver fields. JobPulse is a clean, purpose-built summary. It also
avoids exposing raw boolean column names to the frontend.

#### 2. New Repository Method: `IJobRepository.GetJobPulseAsync`

```csharp
Task<JobPulseDto?> GetJobPulseAsync(string jobPath, CancellationToken ct = default);
```

Implementation queries `Jobs.Jobs` + checks `stores.StoreItems` for active items:

```csharp
public async Task<JobPulseDto?> GetJobPulseAsync(string jobPath, CancellationToken ct = default)
{
    return await (
        from j in _context.Jobs
        where j.JobPath == jobPath
        select new JobPulseDto
        {
            PlayerRegistrationOpen = j.BRegistrationAllowPlayer == true,
            TeamRegistrationOpen = j.BRegistrationAllowTeam == true,
            StoreEnabled = j.BEnableStore == true,
            StoreHasActiveItems = j.BEnableStore == true
                && _context.Stores.Any(s => s.JobId == j.JobId
                    && _context.StoreItems.Any(si => si.StoreId == s.StoreId && si.Active)),
            SchedulePublished = j.BScheduleAllowPublicAccess == true,
            PlayerRegistrationPlanned = j.PlayerProfileMetadataJson != null
                && j.BRegistrationAllowPlayer != true,
            AdultRegistrationPlanned = j.AdultProfileMetadataJson != null,
            PublicSuspended = j.BSuspendPublic,
            RegistrationExpiry = j.ExpiryUsers
        }
    ).AsNoTracking().FirstOrDefaultAsync(ct);
}
```

#### 3. New Public Endpoint

**Controller**: `JobsController` (existing)

```
GET /api/jobs/{jobPath}/pulse
```

Returns `JobPulseDto`. No authentication required (public endpoint).

#### 4. Widget Database Registration

```sql
INSERT INTO widgets.Widget (Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)
VALUES ('Job Pulse', 'content', 'job-pulse', 1, 'Smart registration availability cards', '{"displayStyle":"pulse"}');
```

Plus `WidgetDefault` rows for Anonymous role × all JobTypes (same pattern as
bulletins/event-contact — 7 rows for JobTypeId 0–6).

### Frontend Changes

#### 1. New Widget Component

**Path**: `src/app/widgets/registration/job-pulse-widget/`

Files:
- `job-pulse-widget.component.ts`
- `job-pulse-widget.component.html`
- `job-pulse-widget.component.scss`

**Pattern**: Self-sufficient (injects services, no inputs). Follows
`event-contact-widget` pattern for public/auth dual resolution.

```typescript
@Component({
  selector: 'app-job-pulse-widget',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // ...
})
export class JobPulseWidgetComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly pulse = signal<JobPulseDto | null>(null);
  readonly loading = signal(true);

  // Computed: does the widget have anything to show?
  readonly hasContent = computed(() => {
    const p = this.pulse();
    if (!p) return false;
    return p.playerRegistrationOpen || p.teamRegistrationOpen
        || p.storeEnabled || p.schedulePublished
        || p.playerRegistrationPlanned || p.adultRegistrationPlanned;
  });

  ngOnInit(): void {
    const jobPath = this.resolveJobPath();
    this.http.get<JobPulseDto>(`${environment.apiUrl}/jobs/${jobPath}/pulse`)
      .subscribe({
        next: d => { this.pulse.set(d); this.loading.set(false); },
        error: () => this.loading.set(false),
      });
  }
}
```

#### 2. Widget Registry Entry

```typescript
// widget-registry.ts
'job-pulse': {
  component: JobPulseWidgetComponent,
  label: 'Job Pulse',
  icon: 'bi-activity',
  widgetType: 'content',
  workspace: 'public',
  displayStyle: 'pulse',
},
```

#### 3. Visual Design

Cards use the standard design system:
- `var(--bs-primary)` accent for open/active items
- `var(--neutral-400)` for "coming soon" items
- `var(--space-4)` card padding, `var(--radius-md)` corners
- Icons from Bootstrap Icons
- Responsive: 1 column mobile, 2 columns tablet, 3+ columns desktop
- Each card is a subtle elevated surface with icon + text + optional CTA button

---

## Surface B: Role Selection "What's New" — Detailed Design

### Concept

After phase-1 login, the user sees their available registrations grouped by
role. **Below** the existing registration list, a new "What's New" section
appears showing availability updates for jobs they've been registered with
before.

### Data Source

The backend already knows which jobs the user has registrations for (that's
what populates the role selection list). We need a **new endpoint** that:

1. Finds all **distinct jobIds** the user has ever registered for
2. For each job, runs the same pulse logic (player reg open, store, schedule, etc.)
3. Filters to only jobs where **something new is available** that the user
   doesn't already have a registration for that specific role type
4. Returns a compact summary

### Backend Changes

#### 1. New DTO: `JobWhatsNewDto`

**File**: `TSIC.Contracts/Dtos/JobPulseDtos.cs` (same file)

```csharp
public record JobWhatsNewDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
    public string? JobLogoPath { get; init; }
    public required List<string> AvailableActions { get; init; }
    // e.g. ["Player registration is open", "Store is live", "Schedules published"]
}
```

#### 2. New Endpoint

```
GET /api/auth/whats-new
```

Requires phase-1 JWT (username claim). Returns `List<JobWhatsNewDto>`.

**Logic**:
1. Get all distinct `JobId`s from user's registrations
2. For each job, query pulse flags
3. Compare against user's existing registrations for that job
4. If player reg is open but user has no player registration → include it
5. If store is open → always include (store doesn't require registration)
6. If schedule published → always include
7. Filter out jobs where nothing new is available

#### 3. Service: `IWhatsNewService`

Lives in `TSIC.API/Services/` — orchestrates the cross-job query.

### Frontend Changes

#### 1. Role Selection Component Enhancement

Add a new section below the existing registration cards:

```html
<!-- Existing role cards above -->

@if (whatsNew().length > 0) {
  <section class="whats-new-section">
    <h5>What's New in Your Jobs</h5>
    @for (job of whatsNew(); track job.jobId) {
      <div class="whats-new-card">
        <img [src]="job.jobLogoPath" [alt]="job.jobName" />
        <div>
          <h6>{{ job.jobName }}</h6>
          @for (action of job.availableActions; track action) {
            <span class="badge">{{ action }}</span>
          }
        </div>
      </div>
    }
  </section>
}
```

#### 2. Auth Service Extension

New method: `loadWhatsNew()` — called alongside `loadAvailableRegistrations()`.

---

## Implementation Phases

### Phase 1: Job Pulse Widget (Public Page) — HIGH PRIORITY
**Solves**: Manual bulletin drudgery + login paradox

1. Create `JobPulseDto` in Contracts
2. Add `GetJobPulseAsync` to `IJobRepository` / `JobRepository`
3. Add `GET /api/jobs/{jobPath}/pulse` endpoint
4. Regenerate frontend API models
5. Build `JobPulseWidgetComponent`
6. Register in `WIDGET_MANIFEST`
7. Add widget DB rows (Widget + WidgetDefault for Anonymous × all JobTypes)
8. Update `0-Restore-DevConfig-DEV.ps1` to capture new widget rows

### Phase 2: Expand JobMetadataResponse — QUICK WIN
**Solves**: Frontend already loads job metadata; expose missing flags

Add to `JobMetadataResponse`:
- `BRegistrationAllowPlayer` (currently missing!)
- `BScheduleAllowPublicAccess`
- `BEnableStore`

This is a 5-minute change that unblocks other UI improvements.

### Phase 3: Role Selection "What's New" — MEDIUM PRIORITY
**Solves**: Returning-user blindness

1. Create `JobWhatsNewDto`
2. Create `IWhatsNewService` / `WhatsNewService`
3. Add `GET /api/auth/whats-new` endpoint
4. Add `loadWhatsNew()` to auth service
5. Enhance `role-selection.component.ts` with "What's New" section

### Phase 4: Polish & Edge Cases
- Handle `BSuspendPublic` (hide entire widget when suspended)
- Handle `ExpiryUsers` (show "Registration closes [date]" when close to expiry)
- Handle authenticated vs. unauthenticated CTA differences
  - Authenticated: deep-link to registration form
  - Unauthenticated: link to login first, then redirect to form
- Store card: include item count badge ("12 items available")
- Animated entrance for cards (respect `prefers-reduced-motion`)

---

## Files to Create / Modify

### New Files
| File | Purpose |
|------|---------|
| `TSIC.Contracts/Dtos/JobPulseDtos.cs` | `JobPulseDto` + `JobWhatsNewDto` |
| `widgets/registration/job-pulse-widget/job-pulse-widget.component.ts` | Widget component |
| `widgets/registration/job-pulse-widget/job-pulse-widget.component.html` | Widget template |
| `widgets/registration/job-pulse-widget/job-pulse-widget.component.scss` | Widget styles |
| `TSIC.API/Services/WhatsNew/IWhatsNewService.cs` | What's New interface |
| `TSIC.API/Services/WhatsNew/WhatsNewService.cs` | What's New implementation |

### Modified Files
| File | Change |
|------|--------|
| `TSIC.Contracts/Repositories/IJobRepository.cs` | Add `GetJobPulseAsync` |
| `TSIC.Infrastructure/Repositories/JobRepository.cs` | Implement `GetJobPulseAsync` |
| `TSIC.API/Controllers/JobsController.cs` | Add `/pulse` endpoint |
| `TSIC.Contracts/Dtos/JobMetadataResponse.cs` | Add missing boolean flags |
| `widgets/widget-registry.ts` | Add `job-pulse` entry |
| `views/auth/role-selection/role-selection.component.ts` | Add What's New section |
| `views/auth/role-selection/role-selection.component.html` | Render What's New cards |
| `infrastructure/services/auth.service.ts` | Add `loadWhatsNew()` |
| `TSIC.API/Program.cs` | Register `IWhatsNewService` |

### DB Changes
| Script | What |
|--------|------|
| Widget INSERT | 1 Widget row + 7 WidgetDefault rows for Anonymous role |
| Re-run `0-Restore-DevConfig-DEV.ps1` | Capture updated widget config |

---

## Architectural Decisions

### Why a dedicated `/pulse` endpoint (not extend `/jobs/{jobPath}`)?

1. **Separation of concerns** — Job metadata is about identity/branding/configuration;
   pulse is about real-time availability status
2. **Cache profiles differ** — Metadata changes rarely (admin edits); pulse changes
   when booleans flip (could be several times per season)
3. **Payload size** — JobMetadataResponse is already heavy with waiver HTML; pulse is
   a few booleans
4. **Frontend can call in parallel** — Widget loads pulse independently of banner/bulletins

### Why "coming soon" via ProfileMetadataJson presence?

There's no explicit "registration planned" flag. But when an admin configures
custom profile fields (PlayerProfileMetadataJson), it's a strong signal that
registration for that role type will open. This is a zero-config approach —
admins don't have to flip a "show coming soon" toggle.

### Why NOT a cross-job public listing?

Competitor intelligence risk. A public `/api/registrations/open` endpoint
would expose every client's registration status. Per-job-only on public pages;
cross-job only behind authentication on the role selection page.

### Why role-selection inline vs. a separate page?

The role selection page is the one place where the user is authenticated but
hasn't yet selected a job context. It's the natural "lobby" where cross-job
awareness belongs. A separate page would add friction.

---

## Open Questions

1. **Should "coming soon" cards link to anything?** Currently informational only.
   Could link to a "notify me" signup (future feature).

2. **Should the pulse widget replace bulletins?** No — they serve different purposes.
   Pulse is automated availability status; bulletins are manual announcements with
   rich text. They should coexist.

3. **Should authenticated users see the pulse widget too?** Yes — it's useful for
   everyone. But authenticated users might see different CTAs (e.g., "Register
   Another Player" vs. "Register Now").

4. **What about adult registration?** `AdultProfileMetadataJson` signals intent,
   but there's no `BRegistrationAllowAdult` flag yet. Phase 4 could add this.

---

## Success Metrics

- Reduction in "how do I register?" support calls
- Reduction in manual bulletin creation for registration announcements
- Increase in registration conversion from public page visits
- Returning-user engagement with "What's New" section
