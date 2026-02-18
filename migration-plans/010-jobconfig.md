# 010 — Job Configuration Editor

> **Status**: Design Spec — Ready for Review
> **Date**: 2026-02-18
> **Keyword**: `CWCC Implement JOB-CONFIG-EDITOR`
> **Legacy reference**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Admin/JobController.cs`
> **Legacy view**: `reference/TSIC-Unify-2024/TSIC-Unify/Views/Job/Admin.cshtml`

---

## 1. Problem Statement

The legacy Job/Admin page is a monolithic 68 KB controller serving two separate Razor views
(SuperUser and Director) with 200+ properties spread across 10 tabs. It:

- Violates repository pattern (raw `_context.Jobs` access everywhere)
- Duplicates validation logic between two views
- Has no DTO layer — ViewModels map 1:1 to entity columns
- Offers no audit trail for configuration changes
- Cannot be extended for mobile or API consumers

The new system **combines both role views into a single Angular component** with
role-based field visibility, clean API endpoints, and repository-backed persistence.

---

## 2. Design Decisions

### 2.1 Unified Component, Role-Based Visibility

Instead of two separate views, a single `JobConfigComponent` loads the full
configuration and applies **field-level visibility** based on the user's role claim:

| Visibility | Rule |
|---|---|
| `superOnly` | Visible when `authService.isSuperuser() === true` |
| `adminAll` | Visible to SuperUser, SuperDirector, and Director |

The backend enforces this too — the DTO returned by the API omits `superOnly`
fields for non-SuperUser callers, and the update endpoint rejects writes to
fields the caller cannot see.

### 2.2 Tab Consolidation (10 → 8)

| # | Tab | Legacy tabs merged | Notes |
|---|---|---|---|
| 1 | **General** | General | Job identity, type, sport, season, expiry |
| 2 | **Payment & Billing** | Payment + ARB | All financial config in one place |
| 3 | **Communications** | Communications | Email settings, CC/BCC lists |
| 4 | **Player Registration** | Player | Forms, waivers, discounts, insurance |
| 5 | **Teams & Club Reps** | ClubReps/Teams | Club rep permissions, team reg settings |
| 6 | **Coaches & Staff** | Coaches/Staff | Adult waivers, roster visibility |
| 7 | **Scheduling** | Scheduling | Game clock, event dates, public access |
| 8 | **Mobile & Store** | Mobile + Merch | Feature flags, store config |

**Rationale**: ARB is inherently a payment concept; Mobile and Merch are both
"feature toggle" categories with few fields each. Combining them reduces tab
clutter without losing discoverability. Every tab fits on one row at 1280px+.

### 2.3 API Shape — Single Load, Per-Tab Save

A single `GET /api/job-config` returns all 8 categories in one response object.
The entire Jobs row is ~5-8 KB — one round-trip is always cheaper than lazy-loading
each tab on click. Saves remain **per-category** via `PUT /api/job-config/{category}`,
preserving partial-save semantics and targeted validation.

### 2.4 Rich Text Fields

Waiver, refund policy, code of conduct, and confirmation fields are rich HTML
(~15 fields across 4 tabs). The legacy system used CKEditor.

**Replacement: `ngx-quill`** (Quill 2.0 + Angular wrapper)
- ~40 KB gzipped (vs CKEditor's 200-400 KB)
- 283K weekly npm downloads, Angular 21 confirmed (v29/30)
- Drop-in `<quill-editor formControlName="...">` with full `ControlValueAccessor`
- Built-in configurable toolbar, HTML output, multi-instance proven

Implementation: a shared `<tsic-rich-text>` wrapper component that lazy-loads
`ngx-quill` on first render. Phase 2-4 use `<textarea>` placeholders; Phase 5
swaps in the wrapper with zero tab component changes.

### 2.5 PaymentMethodsAllowedCode — Not a Bitfield

Despite the name, this is a simple enum (not a bitmask):

| Code | Meaning | UI Behavior |
|---|---|---|
| `1` | Credit Card Only | No payment choice shown to registrant |
| `2` | Credit Card or Check | Registrant picks CC vs Check at checkout |
| `3` | Check Only | No payment choice shown to registrant |

When code = `2`, the registration wizard presents a dropdown allowing the user
to choose between Credit Card (internally treated as `1`) and Check/Cash
(internally treated as `3`).

A constants file will be created at
`TSIC.Contracts/Constants/PaymentMethodConstants.cs`:

```csharp
namespace TSIC.Contracts.Constants;

public static class PaymentMethodConstants
{
    public const int CreditCardOnly = 1;
    public const int CreditCardOrCheck = 2;
    public const int CheckOnly = 3;
}
```

Frontend equivalent in `src/app/core/constants/payment-methods.ts`:

```typescript
export const PAYMENT_METHODS = {
    CreditCardOnly: 1,
    CreditCardOrCheck: 2,
    CheckOnly: 3,
} as const;

export const PAYMENT_METHOD_LABELS: Record<number, string> = {
    1: 'Credit Card Only',
    2: 'Credit Card or Check',
    3: 'Check Only',
};
```

The field renders as a **radio group** (not multi-checkbox).

### 2.6 GameClockParams — Logical 1:1, Physical 1:Many

The EF entity declares `ICollection<GameClockParams>` but in practice there is
exactly **one row per job** (or zero for jobs that haven't configured scheduling).
The legacy system uses `SingleOrDefaultAsync()` / `FirstOrDefaultAsync()`.

**Strategy**: Upsert pattern. On save, check if a record exists:
- **Exists** → update in place
- **Null** → insert new record with `JobId`

The repository exposes `GetGameClockParamsAsync(jobId)` returning a single
`GameClockParams?`, not a list.

### 2.7 Routing — Admin Parent Route Relaxation (Option B)

The current `admin` parent route gates on `requireSuperUser: true`, blocking
Directors entirely. We adopt **Option B**:

1. Change the `admin` parent route from `requireSuperUser: true` to
   `requireAdmin: true` (new guard data flag: SuperUser + SuperDirector + Director)
2. Add `requireSuperUser: true` explicitly on each existing SuperUser-only child
   (widget-editor, profile-migration, etc.)
3. `job-config` lives under `admin` with no additional guard override

This is a net-zero security change — existing routes get the same restriction
they had before, just expressed explicitly instead of inherited. Future admin
features that need Director access simply omit the `requireSuperUser` data flag.

**Auth guard change** (`auth.guard.ts`):

```typescript
const requireAdmin = route.data['requireAdmin'] === true;

if (requireAdmin && !authService.isAdmin()) {
    toastService.show('Access denied. Admin privileges required.', 'danger');
    return false;
}
```

Where `isAdmin()` checks for SuperUser, SuperDirector, or Director role.

### 2.8 Concurrency — Single Owner, No Locking

Job configuration is typically managed by a single individual per event.
Last-write-wins is acceptable. No optimistic concurrency needed.

---

## 3. Database Schema

### 3.1 Existing Tables (No Migrations Required)

The `jobs.Jobs` table already contains all 200+ columns. No schema changes needed
for the core job configuration — we are exposing existing columns through a clean API.

**Related tables read/written by this feature:**

| Table | Relationship | Usage |
|---|---|---|
| `jobs.Jobs` | Primary | All job configuration fields |
| `jobs.GameClockParams` | 1:1 logical (FK JobId) | Scheduling tab — clock timings, UTC offset; upsert pattern |
| `jobs.JobAdminCharges` | 1:many (FK JobId) | Payment tab — monthly admin fees |
| `jobs.JobAdminChargeTypes` | Lookup | Charge type reference data |
| `jobs.JobDisplayOptions` | 1:1 (FK JobId) | General tab — banner/logo settings |
| `jobs.JobAgeRanges` | 1:many (FK JobId) | Teams tab — age range restrictions |
| `dbo.JobTypes` | Lookup (FK JobTypeId) | General tab — job type dropdown |
| `dbo.Sports` | Lookup (FK SportId) | General tab — sport dropdown |
| `dbo.Customers` | Lookup (FK CustomerId) | General tab — customer dropdown |
| `dbo.BillingTypes` | Lookup (FK BillingTypeId) | Payment tab — billing type dropdown |

### 3.2 Future: Audit Log Table (Phase 6)

```sql
CREATE TABLE jobs.JobConfigAuditLog (
    Id            INT IDENTITY PRIMARY KEY,
    JobId         UNIQUEIDENTIFIER NOT NULL REFERENCES jobs.Jobs(JobId),
    Category      VARCHAR(50)      NOT NULL,   -- 'general', 'payment', etc.
    FieldName     VARCHAR(100)     NOT NULL,
    OldValue      NVARCHAR(MAX)    NULL,
    NewValue      NVARCHAR(MAX)    NULL,
    ChangedBy     NVARCHAR(450)    NOT NULL,   -- UserId
    ChangedAt     DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
);
```

Deferred to Phase 6 — not required for MVP.

---

## 4. Backend Architecture

### 4.1 Layer Overview

```
JobConfigController          (API — thin, delegates to service)
    ↓
IJobConfigService            (Application — validation, role filtering, mapping)
    ↓
IJobConfigRepository         (Infrastructure — data access via EF Core)
    ↓
SqlDbContext                  (never touched outside repository)
```

### 4.2 Authorization Strategy

The controller uses `[Authorize(Policy = "AdminOnly")]` — this allows
SuperUser, SuperDirector, and Director. The **service layer** inspects the
caller's role claim to decide which fields to expose and which writes to accept.

```
Controller level:  [Authorize(Policy = "AdminOnly")]
Service level:     isSuperUser = claims.HasRole("SuperUser")
                   → filters DTO fields based on role
                   → rejects writes to super-only fields for non-super callers
```

This is a cleaner pattern than two separate controllers with duplicated logic.

### 4.3 DTOs (`TSIC.Contracts/Dtos/JobConfig/`)

One request + response DTO per tab category. All use `required` + `init` pattern.

```csharp
namespace TSIC.Contracts.Dtos.JobConfig;

// ── General ──────────────────────────────────────────────

public record JobConfigGeneralDto
{
    // Admin-visible
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public required string? JobName { get; init; }
    public required string? JobDescription { get; init; }
    public required string? JobTagline { get; init; }
    public required string? Season { get; init; }
    public required string? Year { get; init; }
    public required DateTime ExpiryUsers { get; init; }

    // SuperUser-only (null for non-super callers)
    public string? JobNameQbp { get; init; }
    public DateTime? ExpiryAdmin { get; init; }
    public int? JobTypeId { get; init; }
    public Guid? SportId { get; init; }
    public Guid? CustomerId { get; init; }
    public int? BillingTypeId { get; init; }
    public bool? BSuspendPublic { get; init; }
    public string? JobCode { get; init; }
}

public record UpdateJobConfigGeneralRequest
{
    public required string? JobName { get; init; }
    public required string? JobDescription { get; init; }
    public required string? JobTagline { get; init; }
    public required string? Season { get; init; }
    public required string? Year { get; init; }
    public required DateTime ExpiryUsers { get; init; }

    // SuperUser-only (ignored for non-super callers)
    public string? JobNameQbp { get; init; }
    public DateTime? ExpiryAdmin { get; init; }
    public int? JobTypeId { get; init; }
    public Guid? SportId { get; init; }
    public Guid? CustomerId { get; init; }
    public int? BillingTypeId { get; init; }
    public bool? BSuspendPublic { get; init; }
    public string? JobCode { get; init; }
}

// ── Payment & Billing ────────────────────────────────────

public record JobConfigPaymentDto
{
    // Admin-visible
    public required int PaymentMethodsAllowedCode { get; init; }
    public required bool BAddProcessingFees { get; init; }
    public required decimal? ProcessingFeePercent { get; init; }
    public required bool? BApplyProcessingFeesToTeamDeposit { get; init; }
    public required decimal? PerPlayerCharge { get; init; }
    public required decimal? PerTeamCharge { get; init; }
    public required decimal? PerMonthCharge { get; init; }
    public required string? PayTo { get; init; }
    public required string? MailTo { get; init; }
    public required string? MailinPaymentWarning { get; init; }
    public required string? Balancedueaspercent { get; init; }
    public required bool? BTeamsFullPaymentRequired { get; init; }
    public required bool? BAllowRefundsInPriorMonths { get; init; }
    public required bool? BAllowCreditAll { get; init; }

    // SuperUser-only — ARB settings
    public bool? AdnArb { get; init; }
    public int? AdnArbBillingOccurrences { get; init; }
    public int? AdnArbIntervalLength { get; init; }
    public DateTime? AdnArbStartDate { get; init; }
    public decimal? AdnArbMinimumTotalCharge { get; init; }

    // SuperUser-only — admin charges summary
    public List<JobAdminChargeDto>? AdminCharges { get; init; }
}

public record JobAdminChargeDto
{
    public required int Id { get; init; }
    public required int ChargeTypeId { get; init; }
    public required string? ChargeTypeName { get; init; }
    public required decimal ChargeAmount { get; init; }
    public required string? Comment { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
}

// ── Communications ───────────────────────────────────────

public record JobConfigCommunicationsDto
{
    public required string? DisplayName { get; init; }
    public required string? RegFormFrom { get; init; }
    public required string? RegFormCcs { get; init; }
    public required string? RegFormBccs { get; init; }
    public required string? Rescheduleemaillist { get; init; }
    public required string? Alwayscopyemaillist { get; init; }
    public required bool? BDisallowCcplayerConfirmations { get; init; }
}

// ── Player Registration ──────────────────────────────────

public record JobConfigPlayerDto
{
    public required bool? BRegistrationAllowPlayer { get; init; }
    public required string RegformNamePlayer { get; init; }
    public required string? CoreRegformPlayer { get; init; }
    public required string? PlayerRegConfirmationEmail { get; init; }
    public required string? PlayerRegConfirmationOnScreen { get; init; }
    public required string? PlayerRegRefundPolicy { get; init; }
    public required string? PlayerRegReleaseOfLiability { get; init; }
    public required string? PlayerRegCodeOfConduct { get; init; }
    public required string? PlayerRegCovid19Waiver { get; init; }
    public required int? PlayerRegMultiPlayerDiscountMin { get; init; }
    public required int? PlayerRegMultiPlayerDiscountPercent { get; init; }

    // SuperUser-only
    public bool? BOfferPlayerRegsaverInsurance { get; init; }
    public string? MomLabel { get; init; }
    public string? DadLabel { get; init; }
    public string? PlayerProfileMetadataJson { get; init; }
}

// ── Teams & Club Reps ────────────────────────────────────

public record JobConfigTeamsDto
{
    public required bool? BRegistrationAllowTeam { get; init; }
    public required string RegformNameTeam { get; init; }
    public required string RegformNameClubRep { get; init; }
    public required bool? BClubRepAllowEdit { get; init; }
    public required bool? BClubRepAllowDelete { get; init; }
    public required bool? BClubRepAllowAdd { get; init; }
    public required bool? BRestrictPlayerTeamsToAgerange { get; init; }
    public required bool? BTeamPushDirectors { get; init; }
    public required bool BUseWaitlists { get; init; }
    public required bool BShowTeamNameOnlyInSchedules { get; init; }

    // SuperUser-only
    public bool? BOfferTeamRegsaverInsurance { get; init; }
}

// ── Coaches & Staff ──────────────────────────────────────

public record JobConfigCoachesDto
{
    public required string RegformNameCoach { get; init; }
    public required string? AdultRegConfirmationEmail { get; init; }
    public required string? AdultRegConfirmationOnScreen { get; init; }
    public required string? AdultRegRefundPolicy { get; init; }
    public required string? AdultRegReleaseOfLiability { get; init; }
    public required string? AdultRegCodeOfConduct { get; init; }
    public required string? RefereeRegConfirmationEmail { get; init; }
    public required string? RefereeRegConfirmationOnScreen { get; init; }
    public required string? RecruiterRegConfirmationEmail { get; init; }
    public required string? RecruiterRegConfirmationOnScreen { get; init; }
    public required bool BAllowRosterViewAdult { get; init; }
    public required bool BAllowRosterViewPlayer { get; init; }
}

// ── Scheduling ───────────────────────────────────────────

public record JobConfigSchedulingDto
{
    public required DateTime? EventStartDate { get; init; }
    public required DateTime? EventEndDate { get; init; }
    public required bool? BScheduleAllowPublicAccess { get; init; }
    public required GameClockParamsDto? GameClock { get; init; }
}

public record GameClockParamsDto
{
    public required int Id { get; init; }
    public required decimal HalfMinutes { get; init; }
    public required decimal HalfTimeMinutes { get; init; }
    public required decimal TransitionMinutes { get; init; }
    public required decimal PlayoffMinutes { get; init; }
    public decimal? PlayoffHalfMinutes { get; init; }
    public decimal? PlayoffHalfTimeMinutes { get; init; }
    public decimal? QuarterMinutes { get; init; }
    public decimal? QuarterTimeMinutes { get; init; }
    public int? UtcOffsetHours { get; init; }
}

// ── Mobile & Store ───────────────────────────────────────

public record JobConfigMobileStoreDto
{
    // Mobile — admin-visible
    public required bool? BEnableTsicteams { get; init; }
    public required bool? BEnableMobileRsvp { get; init; }
    public required bool? BEnableMobileTeamChat { get; init; }
    public required bool BAllowMobileLogin { get; init; }
    public required bool? BAllowMobileRegn { get; init; }
    public required int? MobileScoreHoursPastGameEligible { get; init; }

    // SuperUser-only — Mobile
    public string? MobileJobName { get; init; }
    public string? JobCode { get; init; }

    // SuperUser-only — Store
    public bool? BEnableStore { get; init; }
    public bool? BenableStp { get; init; }
    public string? StoreContactEmail { get; init; }
    public string? StoreRefundPolicy { get; init; }
    public string? StorePickupDetails { get; init; }
    public decimal? StoreSalesTax { get; init; }
    public decimal? StoreTsicrate { get; init; }
}

// ── Reference data (dropdowns) ───────────────────────────

public record JobTypeRefDto
{
    public required int JobTypeId { get; init; }
    public required string Name { get; init; }
}

public record SportRefDto
{
    public required Guid SportId { get; init; }
    public required string Name { get; init; }
}

public record CustomerRefDto
{
    public required Guid CustomerId { get; init; }
    public required string Name { get; init; }
}

public record BillingTypeRefDto
{
    public required int BillingTypeId { get; init; }
    public required string Name { get; init; }
}
```

### 4.4 Repository Interface (`IJobConfigRepository`)

```csharp
namespace TSIC.Contracts.Repositories;

public interface IJobConfigRepository
{
    // ── Read ─────────────────────────────────────────────
    Task<Jobs?> GetJobByPathAsync(string jobPath, CancellationToken ct = default);
    Task<Jobs?> GetJobByIdAsync(Guid jobId, CancellationToken ct = default);
    Task<GameClockParams?> GetGameClockParamsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<JobAdminCharges>> GetAdminChargesAsync(Guid jobId, CancellationToken ct = default);
    Task<JobDisplayOptions?> GetDisplayOptionsAsync(Guid jobId, CancellationToken ct = default);

    // ── Reference data ───────────────────────────────────
    Task<List<JobTypeRefDto>> GetJobTypesAsync(CancellationToken ct = default);
    Task<List<SportRefDto>> GetSportsAsync(CancellationToken ct = default);
    Task<List<CustomerRefDto>> GetCustomersAsync(CancellationToken ct = default);
    Task<List<BillingTypeRefDto>> GetBillingTypesAsync(CancellationToken ct = default);
    Task<List<JobAdminChargeTypes>> GetChargeTypesAsync(CancellationToken ct = default);

    // ── Write ────────────────────────────────────────────
    // Entity is loaded tracked → mutated in service → SaveChanges commits
    Task<Jobs?> GetJobTrackedAsync(Guid jobId, CancellationToken ct = default);
    void AddGameClockParams(GameClockParams gcp);
    void RemoveGameClockParams(GameClockParams gcp);
    void AddAdminCharge(JobAdminCharges charge);
    void RemoveAdminCharge(JobAdminCharges charge);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

### 4.5 Service Interface (`IJobConfigService`)

```csharp
namespace TSIC.Contracts.Services;

public interface IJobConfigService
{
    // Single load — returns ALL categories, role-filtered
    Task<JobConfigFullDto> GetFullConfigAsync(Guid jobId, bool isSuperUser, CancellationToken ct);

    // Per-category writes — validates and applies per caller role
    Task UpdateGeneralAsync(Guid jobId, UpdateJobConfigGeneralRequest req, bool isSuperUser, CancellationToken ct);
    Task UpdatePaymentAsync(Guid jobId, UpdateJobConfigPaymentRequest req, bool isSuperUser, CancellationToken ct);
    Task UpdateCommunicationsAsync(Guid jobId, UpdateJobConfigCommunicationsRequest req, CancellationToken ct);
    Task UpdatePlayerAsync(Guid jobId, UpdateJobConfigPlayerRequest req, bool isSuperUser, CancellationToken ct);
    Task UpdateTeamsAsync(Guid jobId, UpdateJobConfigTeamsRequest req, bool isSuperUser, CancellationToken ct);
    Task UpdateCoachesAsync(Guid jobId, UpdateJobConfigCoachesRequest req, CancellationToken ct);
    Task UpdateSchedulingAsync(Guid jobId, UpdateJobConfigSchedulingRequest req, CancellationToken ct);
    Task UpdateMobileStoreAsync(Guid jobId, UpdateJobConfigMobileStoreRequest req, bool isSuperUser, CancellationToken ct);

    // Reference data
    Task<JobConfigReferenceDataDto> GetReferenceDataAsync(CancellationToken ct);

    // Admin charges CRUD (SuperUser only)
    Task<JobAdminChargeDto> AddAdminChargeAsync(Guid jobId, CreateAdminChargeRequest req, CancellationToken ct);
    Task DeleteAdminChargeAsync(Guid jobId, int chargeId, CancellationToken ct);
}
```

The `JobConfigFullDto` is a wrapper containing all 8 category DTOs:

```csharp
public record JobConfigFullDto
{
    public required JobConfigGeneralDto General { get; init; }
    public required JobConfigPaymentDto Payment { get; init; }
    public required JobConfigCommunicationsDto Communications { get; init; }
    public required JobConfigPlayerDto Player { get; init; }
    public required JobConfigTeamsDto Teams { get; init; }
    public required JobConfigCoachesDto Coaches { get; init; }
    public required JobConfigSchedulingDto Scheduling { get; init; }
    public required JobConfigMobileStoreDto MobileStore { get; init; }
}
```

### 4.6 Controller (`JobConfigController`)

```csharp
[ApiController]
[Route("api/job-config")]
[Authorize(Policy = "AdminOnly")]
public class JobConfigController : ControllerBase
{
    private readonly IJobConfigService _service;

    // Helper — extracts SuperUser flag from claims
    private bool IsSuperUser => User.IsInRole(RoleConstants.Names.SuperuserName);

    // Helper — extracts JobId from token claims
    private Guid JobId => /* from JWT claim */;

    // ── Single load (all categories) ─────────────────
    [HttpGet]
    public async Task<ActionResult<JobConfigFullDto>> GetFullConfig(CancellationToken ct)
        => Ok(await _service.GetFullConfigAsync(JobId, IsSuperUser, ct));

    // ── Reference data ───────────────────────────────
    [HttpGet("reference-data")]
    public async Task<ActionResult<JobConfigReferenceDataDto>> GetReferenceData(CancellationToken ct)
        => Ok(await _service.GetReferenceDataAsync(ct));

    // ── Per-category PUT ─────────────────────────────
    [HttpPut("general")]
    public async Task<IActionResult> UpdateGeneral(UpdateJobConfigGeneralRequest req, CancellationToken ct)
    {
        await _service.UpdateGeneralAsync(JobId, req, IsSuperUser, ct);
        return NoContent();
    }

    // ... one PUT per category (8 total)

    // ── Admin charges (SuperUser-only) ───────────────
    [HttpPost("admin-charges")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<JobAdminChargeDto>> AddCharge(CreateAdminChargeRequest req, CancellationToken ct)
        => Ok(await _service.AddAdminChargeAsync(JobId, req, ct));

    [HttpDelete("admin-charges/{chargeId:int}")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> DeleteCharge(int chargeId, CancellationToken ct)
    {
        await _service.DeleteAdminChargeAsync(JobId, chargeId, ct);
        return NoContent();
    }
}
```

**Endpoint summary: 12 endpoints**

| Verb | Route | Policy | Purpose |
|---|---|---|---|
| GET | ` ` (root) | AdminOnly | Load all 8 categories in one response |
| GET | `reference-data` | AdminOnly | Dropdowns for General tab |
| PUT | `general` | AdminOnly | Save General tab |
| PUT | `payment` | AdminOnly | Save Payment tab |
| PUT | `communications` | AdminOnly | Save Comms tab |
| PUT | `player` | AdminOnly | Save Player tab |
| PUT | `teams` | AdminOnly | Save Teams tab |
| PUT | `coaches` | AdminOnly | Save Coaches tab |
| PUT | `scheduling` | AdminOnly | Save Scheduling tab |
| PUT | `mobile-store` | AdminOnly | Save Mobile/Store tab |
| POST | `admin-charges` | SuperUserOnly | Add admin charge |
| DELETE | `admin-charges/{id}` | SuperUserOnly | Delete admin charge |

### 4.7 Role-Based Field Filtering (Service Layer)

The service applies a **strip pattern** for non-SuperUser callers:

```csharp
public async Task<JobConfigGeneralDto> GetGeneralAsync(
    Guid jobId, bool isSuperUser, CancellationToken ct)
{
    var job = await _repo.GetJobByIdAsync(jobId, ct)
        ?? throw new NotFoundException("Job not found");

    return new JobConfigGeneralDto
    {
        // Always visible
        JobId = job.JobId,
        JobPath = job.JobPath,
        JobName = job.JobName,
        JobDescription = job.JobDescription,
        // ...

        // SuperUser-only — null for directors
        ExpiryAdmin = isSuperUser ? job.ExpiryAdmin : null,
        JobTypeId = isSuperUser ? job.JobTypeId : null,
        SportId = isSuperUser ? job.SportId : null,
        CustomerId = isSuperUser ? job.CustomerId : null,
        BillingTypeId = isSuperUser ? job.BillingTypeId : null,
        BSuspendPublic = isSuperUser ? job.BSuspendPublic : null,
        JobCode = isSuperUser ? job.JobCode : null,
        JobNameQbp = isSuperUser ? job.JobNameQbp : null,
    };
}
```

For writes, super-only fields in the request body are **silently ignored**
for non-SuperUser callers (not rejected — this keeps the client simple):

```csharp
public async Task UpdateGeneralAsync(
    Guid jobId, UpdateJobConfigGeneralRequest req, bool isSuperUser, CancellationToken ct)
{
    var job = await _repo.GetJobTrackedAsync(jobId, ct)
        ?? throw new NotFoundException("Job not found");

    // Admin-editable fields — always applied
    job.JobName = req.JobName;
    job.JobDescription = req.JobDescription;
    // ...

    // SuperUser-only — skipped for directors
    if (isSuperUser)
    {
        if (req.ExpiryAdmin.HasValue) job.ExpiryAdmin = req.ExpiryAdmin.Value;
        if (req.JobTypeId.HasValue) job.JobTypeId = req.JobTypeId.Value;
        // ...
    }

    job.Modified = DateTime.UtcNow;
    await _repo.SaveChangesAsync(ct);
}
```

---

## 5. Frontend Architecture

### 5.1 File Structure

```
src/app/views/admin/job-config/
├── job-config.component.ts          // Shell — tabs, loading, save orchestration
├── job-config.component.html        // Tab navigation + @switch for active tab
├── job-config.component.scss        // Glassmorphic card, tab styling
├── job-config.service.ts            // HTTP calls, signal state per category
└── tabs/
    ├── general-tab.component.ts
    ├── payment-tab.component.ts
    ├── communications-tab.component.ts
    ├── player-tab.component.ts
    ├── teams-tab.component.ts
    ├── coaches-tab.component.ts
    ├── scheduling-tab.component.ts
    └── mobile-store-tab.component.ts
```

### 5.2 State Management

The `JobConfigService` (injectable, non-singleton — provided in component) holds:

```typescript
@Injectable()
export class JobConfigService {
    private readonly http = inject(JobConfigApiService); // auto-generated
    private readonly auth = inject(AuthService);

    // ── Full config (loaded once) ────────────────
    readonly config = signal<JobConfigFullDto | null>(null);
    readonly referenceData = signal<JobConfigReferenceData | null>(null);

    // ── Convenience accessors ────────────────────
    readonly general = computed(() => this.config()?.general ?? null);
    readonly payment = computed(() => this.config()?.payment ?? null);
    readonly communications = computed(() => this.config()?.communications ?? null);
    readonly player = computed(() => this.config()?.player ?? null);
    readonly teams = computed(() => this.config()?.teams ?? null);
    readonly coaches = computed(() => this.config()?.coaches ?? null);
    readonly scheduling = computed(() => this.config()?.scheduling ?? null);
    readonly mobileStore = computed(() => this.config()?.mobileStore ?? null);

    // ── UI state ─────────────────────────────────
    readonly activeTab = signal<TabKey>('general');
    readonly isLoading = signal(false);
    readonly isSaving = signal(false);
    readonly isSuperUser = computed(() => this.auth.isSuperuser());

    // ── Dirty tracking per tab ───────────────────
    readonly dirtyTabs = signal<Set<TabKey>>(new Set());

    // Single load — fetches all categories in one GET
    loadConfig(): void { ... }

    // Per-tab save — PUTs only the active category, then refreshes config
    saveTab(tab: TabKey, formValue: unknown): void { ... }
}
```

### 5.3 Tab Component Pattern

Each tab is a **standalone component** receiving its DTO via `input()` and
emitting saves via `output()`:

```typescript
@Component({
    selector: 'tsic-general-tab',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [ReactiveFormsModule, /* ... */],
})
export class GeneralTabComponent {
    readonly data = input.required<JobConfigGeneralDto>();
    readonly isSuperUser = input.required<boolean>();
    readonly referenceData = input.required<JobConfigReferenceData>();
    readonly save = output<UpdateJobConfigGeneralRequest>();

    // Reactive form for this tab's fields
    private readonly fb = inject(FormBuilder);
    readonly form = this.fb.group({ /* ... */ });

    // Populate form when data changes
    constructor() {
        effect(() => {
            const d = this.data();
            if (d) this.form.patchValue(d);
        });
    }

    onSave(): void {
        if (this.form.valid) {
            this.save.emit(this.form.getRawValue() as UpdateJobConfigGeneralRequest);
        }
    }
}
```

### 5.4 Shell Component (Tab Host)

```html
<!-- job-config.component.html -->
<div class="job-config-shell">
    <div class="tab-header">
        @for (tab of tabs; track tab.key) {
            <button
                class="tab-btn"
                [class.active]="activeTab() === tab.key"
                [class.dirty]="dirtyTabs().has(tab.key)"
                (click)="switchTab(tab.key)">
                {{ tab.label }}
                @if (dirtyTabs().has(tab.key)) {
                    <span class="dirty-dot"></span>
                }
            </button>
        }
    </div>

    <div class="tab-body">
        @if (isLoading()) {
            <div class="loading-spinner">Loading...</div>
        } @else {
            @switch (activeTab()) {
                @case ('general') {
                    <tsic-general-tab
                        [data]="general()!"
                        [isSuperUser]="isSuperUser()"
                        [referenceData]="referenceData()!"
                        (save)="onSave('general', $event)" />
                }
                @case ('payment') {
                    <tsic-payment-tab
                        [data]="payment()!"
                        [isSuperUser]="isSuperUser()"
                        (save)="onSave('payment', $event)" />
                }
                <!-- ... remaining tabs ... -->
            }
        }
    </div>
</div>
```

### 5.5 Routing

Per Section 2.7, the `admin` parent route is relaxed to `requireAdmin: true`.
Each existing SuperUser-only child gets explicit `requireSuperUser: true`.
Job config lives under `admin` with no additional guard:

```typescript
// In app.routes.ts — admin parent (CHANGED)
{
    path: 'admin',
    canActivate: [authGuard],
    data: { requireAdmin: true },  // Was: requireSuperUser: true
    children: [
        {
            path: 'profile-migration',
            data: { requireSuperUser: true },  // Explicit — was inherited
            loadComponent: () => import('./views/admin/profile-migration/...').then(m => m.ProfileMigrationComponent)
        },
        {
            path: 'widget-editor',
            data: { requireSuperUser: true },  // Explicit — was inherited
            loadComponent: () => import('./views/admin/widget-editor/...').then(m => m.WidgetEditorComponent)
        },
        {
            path: 'job-config',
            // No requireSuperUser — Directors + SuperDirectors + SuperUsers
            loadComponent: () => import('./views/admin/job-config/job-config.component')
                .then(m => m.JobConfigComponent)
        }
    ]
}
```

**Breadcrumb mappings**:
```typescript
ROUTE_TITLE_MAP['admin/job-config'] = 'Job Configuration';
ROUTE_WORKSPACE_MAP['admin/job-config'] = 'job-config';
```

### 5.6 Styling Approach

- **Glassmorphic shell card** with `backdrop-filter: blur(12px)` (matches widget editor)
- **Tab bar**: horizontal pill-style buttons using `var(--bs-primary)` active state
- **Form layout**: 2-column responsive grid (`grid-template-columns: 1fr 1fr`)
  at `≥768px`, single column below
- **SuperUser-only fields**: wrapped in `@if (isSuperUser())` — not rendered at all
  for directors (not just hidden)
- **Dirty indicator**: small colored dot on tab button when unsaved changes exist
- **Save bar**: sticky bottom bar (same pattern as widget editor) with
  "Save [Tab Name]" button + "Discard Changes" link
- **All spacing**: `var(--space-N)` tokens
- **All colors**: CSS variables from design system
- **Rich text fields**: full-width, min-height `var(--space-20)`

---

## 6. Implementation Phases

### Phase 1 — Backend Foundation (Backend only)

**Goal**: Repository + service + controller + DTOs, fully testable.

| Task | Files |
|---|---|
| Create DTO file | `Contracts/Dtos/JobConfig/JobConfigDtos.cs` |
| Create payment method constants | `Contracts/Constants/PaymentMethodConstants.cs` |
| Create repository interface | `Contracts/Repositories/IJobConfigRepository.cs` |
| Create service interface | `Contracts/Services/IJobConfigService.cs` |
| Implement repository | `Infrastructure/Repositories/JobConfigRepository.cs` |
| Implement service | `API/Services/JobConfig/JobConfigService.cs` |
| Create controller | `API/Controllers/JobConfigController.cs` |
| Register DI | `Program.cs` |
| Relax admin route + add `requireAdmin` guard | `auth.guard.ts`, `app.routes.ts` |
| Add explicit `requireSuperUser` to existing children | `app.routes.ts` (widget-editor, profile-migration, etc.) |

**Deliverable**: All 12 endpoints callable via Swagger. Role-based filtering
verified by toggling JWT role claim.

### Phase 2 — Frontend Shell + General Tab

**Goal**: Working tabbed UI with one functional tab.

| Task | Files |
|---|---|
| Regenerate API models | `scripts/2-Regenerate-API-Models.ps1` |
| Create component shell | `views/admin/job-config/job-config.component.*` |
| Create service | `views/admin/job-config/job-config.service.ts` |
| Implement General tab | `views/admin/job-config/tabs/general-tab.component.ts` |
| Add route | `app.routes.ts` |
| Add breadcrumb mapping | `breadcrumb.service.ts` |

**Deliverable**: Navigate to `/:jobPath/admin/job-config`, see tab bar,
load General tab, edit fields, save. SuperUser-only fields hidden for directors.

### Phase 3 — Remaining Tabs (Payment, Comms, Player)

**Goal**: High-value tabs that directors use most.

| Task | Files |
|---|---|
| Payment & Billing tab | `tabs/payment-tab.component.ts` |
| Communications tab | `tabs/communications-tab.component.ts` |
| Player Registration tab | `tabs/player-tab.component.ts` |

**Deliverable**: 4 of 8 tabs fully functional. Admin charges CRUD on Payment tab
(SuperUser only).

### Phase 4 — Remaining Tabs (Teams, Coaches, Scheduling, Mobile/Store)

**Goal**: Complete tab coverage.

| Task | Files |
|---|---|
| Teams & Club Reps tab | `tabs/teams-tab.component.ts` |
| Coaches & Staff tab | `tabs/coaches-tab.component.ts` |
| Scheduling tab | `tabs/scheduling-tab.component.ts` |
| Mobile & Store tab | `tabs/mobile-store-tab.component.ts` |

**Deliverable**: All 8 tabs functional. Game clock params fully editable.
Feature parity with legacy.

### Phase 5 — Polish & Rich Text

**Goal**: Production-ready UX.

| Task | Detail |
|---|---|
| Install `ngx-quill` | `npm install ngx-quill quill` — Angular 21 compatible (v29/30) |
| Create `<tsic-rich-text>` wrapper | Shared component in `shared-ui/rich-text/` — lazy-loads Quill, wraps `ControlValueAccessor` |
| Swap `<textarea>` → `<tsic-rich-text>` | Replace in Player, Coaches, Mobile/Store tabs (~15 fields) |
| Create frontend payment constants | `src/app/core/constants/payment-methods.ts` |
| Unsaved changes guard | `canDeactivate` guard warns on navigation with dirty tabs |
| Responsive design | Test and polish at 1024px, 768px, 480px breakpoints |
| Toast notifications | Success/error feedback on save |
| Keyboard navigation | Tab through fields, Enter to save |
| Palette testing | Verify all 8 palettes render correctly |

### Phase 6 — Audit Trail (Future, Optional)

**Goal**: Track who changed what and when.

| Task | Detail |
|---|---|
| Create audit table | SQL migration for `jobs.JobConfigAuditLog` (see Section 3.2) |
| Service interceptor | Log field-level diffs on every update |
| Audit viewer UI | Read-only timeline of changes (SuperUser only) |

Not required for production — single-owner pattern makes concurrent edit
conflicts unlikely. Implement only if operational need arises.

---

## 7. Field Visibility Matrix

**Legend**: `A` = All admins, `S` = SuperUser only

### General Tab

| Field | Visibility | Type |
|---|---|---|
| JobName | A | text |
| JobDescription | A | textarea |
| JobTagline | A | textarea |
| Season | A | text |
| Year | A | text |
| ExpiryUsers | A | datetime |
| ExpiryAdmin | S | datetime |
| JobTypeId | S | dropdown |
| SportId | S | dropdown |
| CustomerId | S | dropdown |
| BillingTypeId | S | dropdown |
| BSuspendPublic | S | toggle |
| JobCode | S | text |
| JobNameQbp | S | text |

### Payment & Billing Tab

| Field | Visibility | Type |
|---|---|---|
| PaymentMethodsAllowedCode | A | radio group (enum: 1=CC, 2=CC+Check, 3=Check) |
| BAddProcessingFees | A | toggle |
| ProcessingFeePercent | A | number |
| BApplyProcessingFeesToTeamDeposit | A | toggle |
| PerPlayerCharge | A | currency |
| PerTeamCharge | A | currency |
| PerMonthCharge | A | currency |
| PayTo | A | text |
| MailTo | A | text |
| MailinPaymentWarning | A | textarea |
| Balancedueaspercent | A | text |
| BTeamsFullPaymentRequired | A | toggle |
| BAllowRefundsInPriorMonths | A | toggle |
| BAllowCreditAll | A | toggle |
| AdnArb (enable ARB) | S | toggle |
| AdnArbBillingOccurrences | S | number |
| AdnArbIntervalLength | S | number |
| AdnArbStartDate | S | date |
| AdnArbMinimumTotalCharge | S | currency |
| Admin Charges (CRUD table) | S | inline table |

### Communications Tab

| Field | Visibility | Type |
|---|---|---|
| DisplayName | A | text |
| RegFormFrom | A | email |
| RegFormCcs | A | text (comma-sep) |
| RegFormBccs | A | text (comma-sep) |
| Rescheduleemaillist | A | text (comma-sep) |
| Alwayscopyemaillist | A | text (comma-sep) |
| BDisallowCcplayerConfirmations | A | toggle |

### Player Registration Tab

| Field | Visibility | Type |
|---|---|---|
| BRegistrationAllowPlayer | A | toggle |
| RegformNamePlayer | A | text |
| CoreRegformPlayer | A | text |
| PlayerRegConfirmationEmail | A | rich text |
| PlayerRegConfirmationOnScreen | A | rich text |
| PlayerRegRefundPolicy | A | rich text |
| PlayerRegReleaseOfLiability | A | rich text |
| PlayerRegCodeOfConduct | A | rich text |
| PlayerRegCovid19Waiver | A | rich text |
| PlayerRegMultiPlayerDiscountMin | A | number |
| PlayerRegMultiPlayerDiscountPercent | A | number |
| BOfferPlayerRegsaverInsurance | S | toggle |
| MomLabel | S | text |
| DadLabel | S | text |
| PlayerProfileMetadataJson | S | code/textarea |

### Teams & Club Reps Tab

| Field | Visibility | Type |
|---|---|---|
| BRegistrationAllowTeam | A | toggle |
| RegformNameTeam | A | text |
| RegformNameClubRep | A | text |
| BClubRepAllowEdit | A | toggle |
| BClubRepAllowDelete | A | toggle |
| BClubRepAllowAdd | A | toggle |
| BRestrictPlayerTeamsToAgerange | A | toggle |
| BTeamPushDirectors | A | toggle |
| BUseWaitlists | A | toggle |
| BShowTeamNameOnlyInSchedules | A | toggle |
| BOfferTeamRegsaverInsurance | S | toggle |

### Coaches & Staff Tab

| Field | Visibility | Type |
|---|---|---|
| RegformNameCoach | A | text |
| AdultRegConfirmationEmail | A | rich text |
| AdultRegConfirmationOnScreen | A | rich text |
| AdultRegRefundPolicy | A | rich text |
| AdultRegReleaseOfLiability | A | rich text |
| AdultRegCodeOfConduct | A | rich text |
| RefereeRegConfirmationEmail | A | rich text |
| RefereeRegConfirmationOnScreen | A | rich text |
| RecruiterRegConfirmationEmail | A | rich text |
| RecruiterRegConfirmationOnScreen | A | rich text |
| BAllowRosterViewAdult | A | toggle |
| BAllowRosterViewPlayer | A | toggle |

### Scheduling Tab

| Field | Visibility | Type |
|---|---|---|
| EventStartDate | A | date |
| EventEndDate | A | date |
| BScheduleAllowPublicAccess | A | toggle |
| GameClock.HalfMinutes | A | number |
| GameClock.HalfTimeMinutes | A | number |
| GameClock.TransitionMinutes | A | number |
| GameClock.PlayoffMinutes | A | number |
| GameClock.PlayoffHalfMinutes | A | number |
| GameClock.PlayoffHalfTimeMinutes | A | number |
| GameClock.QuarterMinutes | A | number |
| GameClock.QuarterTimeMinutes | A | number |
| GameClock.UtcOffsetHours | A | number |

### Mobile & Store Tab

| Field | Visibility | Type |
|---|---|---|
| BEnableTsicteams | A | toggle |
| BEnableMobileRsvp | A | toggle |
| BEnableMobileTeamChat | A | toggle |
| BAllowMobileLogin | A | toggle |
| BAllowMobileRegn | A | toggle |
| MobileScoreHoursPastGameEligible | A | number |
| MobileJobName | S | text |
| JobCode | S | text |
| BEnableStore | S | toggle |
| BenableStp | S | toggle |
| StoreContactEmail | S | email |
| StoreRefundPolicy | S | rich text |
| StorePickupDetails | S | rich text |
| StoreSalesTax | S | decimal |
| StoreTsicrate | S | decimal |

---

## 8. Constraints & Conventions

| Rule | Detail |
|---|---|
| Repository pattern | All data access via `IJobConfigRepository` — zero DbContext in service/controller |
| Sequential awaits | No `Task.WhenAll` — DbContext is not thread-safe |
| DTO pattern | `required` + `init` properties, object initializer syntax |
| No positional records | Use `{ get; init; }` style for all DTOs |
| Auto-generated models | Run `2-Regenerate-API-Models.ps1` after backend DTO changes |
| CSS variables only | No hardcoded colors, spacing, or shadows |
| 8px spacing grid | `var(--space-1)` through `var(--space-20)` |
| Signals for state | `signal<T>()` for component + service state |
| Observables for HTTP | `subscribe()` to HTTP calls, update signals in callback |
| OnPush detection | All components use `ChangeDetectionStrategy.OnPush` |
| `@if` / `@for` | Modern control flow syntax |
| Standalone components | No NgModules |
| Relative routerLinks | Never absolute — preserve `:jobPath` prefix |
| Breadcrumb updates | Add to `ROUTE_TITLE_MAP` and `ROUTE_WORKSPACE_MAP` |
| WCAG AA | 4.5:1 contrast minimum, keyboard navigable |

---

## 9. Risk & Mitigation

| Risk | Impact | Mitigation |
|---|---|---|
| 200+ fields is a large surface | High complexity | Category-scoped DTOs reduce blast radius per save |
| Rich text editor dependency | Bundle size | `ngx-quill` at ~40 KB gzipped; lazy-load via shared wrapper; `<textarea>` for MVP |
| Director sees stale SuperUser fields | Data integrity | Backend strips fields — frontend never receives them |
| Admin route relaxation exposes new routes | Security | Each existing child gets explicit `requireSuperUser: true`; net-zero change |
| GameClockParams may not exist for a job | Null handling | Upsert pattern — insert if null, update if exists; UI shows empty form with defaults |

---

## 10. Success Criteria

- [ ] All 8 tabs load and save without error
- [ ] SuperUser sees all fields; Director sees restricted subset
- [ ] Director cannot write to SuperUser-only fields (backend enforced)
- [ ] Game clock params create/update/read works
- [ ] Admin charges CRUD works (SuperUser only)
- [ ] Dirty tab indicators show unsaved changes
- [ ] Navigation guard prevents losing unsaved work
- [ ] All 8 color palettes render correctly
- [ ] Responsive layout works at 1024px, 768px, 480px
- [ ] No hardcoded colors, spacing, or shadows in SCSS
- [ ] Zero `SqlDbContext` references outside repository
- [ ] All DTOs use `required` + `init` pattern
