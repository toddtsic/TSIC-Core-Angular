# Migration Plan: Search/Index → Registration Search & Management

## Context

The legacy `Search/Index` page is the **most-used, most-important interface** in the entire application. It is where administrators spend the majority of their time. It serves as the central hub for finding registrations by flexible criteria and then acting on them — viewing/editing details, managing accounting records, processing credit card refunds, and sending batch emails. The organization's reputation often hinges on the quality and responsiveness of this single view.

The Registration entity is massive (40+ player profile columns, 9 financial fields, team assignment, insurance, waivers, payment subscriptions) and the form fields shown per-registrant are dynamic — controlled by `Job.PlayerProfileMetadataJson` which varies by job/role. The accounting subsystem tracks every payment (credit card, check, cash) via `RegistrationAccounting` with full Authorize.Net transaction IDs for refund capability.

**Legacy URL**: `/Search/Index` (Controller=Search, Action=Index)

---

## 1. Legacy Strengths (Preserve These!)

- **Flexible multi-criteria search** — filter by any combination of: name, email, team, agegroup, division, club, role, active status, payment status, registration date range, and more
- **Grid columns vetted over years** — the visible columns represent the data admins actually need day-to-day
- **Per-registrant accounting view** — click a row to see all payment history (every RegistrationAccounting record)
- **Per-registrant detail view/edit** — full registration form with all profile questions, driven by the job's metadata
- **Batch email with substitution variables** — select found registrations, compose email with tokens like `!PERSON`, `!AMTOWED`, `!JOBNAME`, etc.
- **Quick financial overview** — fee totals, paid, owed visible at a glance per row
- **Role-based filtering** — quickly narrow to Players, Staff, Directors, ClubReps, etc.

## 2. Legacy Pain Points (Fix These!)

- **jqGrid dependency** — dated look, heavy jQuery, poor mobile experience, limited export options
- **No server-side paging** — loads all matching registrations into memory, then pages client-side; slow for jobs with 2,000+ registrations
- **No saved filter presets** — admins re-enter the same filter criteria every session
- **Accounting view in separate page** — navigating to accounting loses search context; back button doesn't restore filters
- **No inline refund capability** — credit card refunds require going to Authorize.Net dashboard separately
- **Registration detail edit in separate page** — loses search context, form is static HTML (not metadata-driven)
- **Batch email is rudimentary** — plain text only, no preview, no delivery status feedback
- **No export** — no Excel/CSV export of search results
- **No column visibility toggle** — all columns always shown, many irrelevant for specific workflows
- **Anti-forgery token plumbing** — boilerplate in every AJAX call
- **No financial summary row** — no totals for fees/paid/owed across all found registrations

## 3. Modern Vision

**Recommended UI: Syncfusion Grid with Filter Panel + Slide-Over Detail Panels**

This interface deserves Syncfusion's full grid capability. The data volume (hundreds to thousands of rows), the need for sorting/filtering/paging/export, and the financial column formatting all play to Syncfusion's strengths. Your license is already configured, the Bootstrap 5 theme is integrated, and `_syncfusion.scss` already provides glassmorphic grid styling with palette-responsive CSS variables.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Registration Search                                               [⟳]     │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─ Filter Panel (glassmorphic card) ────────────────────────────────────┐  │
│  │                                                                        │  │
│  │  ── Compact Bar (always visible) ──                                    │  │
│  │  Name: [          ]  Email: [          ]                               │  │
│  │  Role: [☑ multiselect ▼]  Status: [☑ Active ▼]  Pay: [☑ multi ▼]    │  │
│  │        Player (347)          Active (892)          PAID (650)          │  │
│  │        Coach (45)            Inactive (53)         UNDER (195)        │  │
│  │        Staff (12)                                  OVER (3)           │  │
│  │                                                                        │  │
│  │  [Search]  [Clear]  [▼ More Filters]       [Email Selected] [Export]  │  │
│  │                                                                        │  │
│  │  ── More Filters (expandable, slide-down animation) ──                 │  │
│  │  Text: Phone [      ]  School [      ]  From [    ]  To [    ]        │  │
│  │  Org:  Team [☑▼]  Agegroup [☑▼]  Division [☑▼]  Club [☑▼]           │  │
│  │  Demo: Gender [☑▼]  Position [☑▼]  GradYr [☑▼]  Grade [☑▼]  Age[☑▼]│  │
│  │  Bill: ARB Subscription [☑▼]  Mobile Registrations [☑▼]              │  │
│  │                                                                        │  │
│  └────────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
│  ┌─ Filter Chips Strip ──────────────────────────────────────────────────┐  │
│  │ Role: Player ✕ │ Status: Active ✕ │ Pay: UNDER PAID ✕ │ [Clear All] │  │
│  └────────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
│  Found: 347 registrations                          Page 1 of 18  ◀ ▶       │
│  ┌──────────────────────────────────────────────────────────────────────────┐│
│  │☐│ #  │ Last     │ First   │ Email          │ Team      │ Role   │ Fees  ││
│  │  │    │          │         │                │           │        │ /Owed ││
│  ├──────────────────────────────────────────────────────────────────────────┤│
│  │☐│ 1  │ Smith    │ John    │ j@email.com    │ Storm U14 │ Player │$500   ││
│  │  │    │          │         │                │           │        │$0     ││
│  │☐│ 2  │ Johnson  │ Emily   │ em@email.com   │ Thunder   │ Player │$500   ││
│  │  │    │          │         │                │           │        │$250   ││
│  │☐│ 3  │ Williams │ Mike    │ mw@email.com   │ —         │ Coach  │$150   ││
│  │  │    │          │         │                │           │        │$150   ││
│  │...                                                                       │
│  ├──────────────────────────────────────────────────────────────────────────┤│
│  │                               Totals: Fees $85,200  Paid $71,000        ││
│  │                                       Owed $14,200                      ││
│  └──────────────────────────────────────────────────────────────────────────┘│
│                                                                              │
│                         ┌──────────────────────────────────────┐             │
│                         │  Pager: ◀ 1 2 3 ... 18 ▶  20/page  │             │
│                         └──────────────────────────────────────┘             │
└──────────────────────────────────────────────────────────────────────────────┘

═══════════════════════════════════════════════════════════════════════════════

When a row is clicked → Slide-over detail panel (right side, 560px):

┌──────────────────────────────────────────────────────────┐
│  John Smith                                        [✕]   │
│  Player • Storm U14 • Active  [Change Job] [Delete]      │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  [Details]  [Accounting]  [Email]                        │
│  ─────────────────────────────────────────                │
│                                                          │
│  ── Details Tab ──                                       │
│                                                          │
│  ┌─ Contact Info (editable) ─────────────────────────┐   │
│  │  Email: [j@email.com  ]  Cellphone: [555-1234  ]  │   │
│  │  Uniform #: [42       ]                            │   │
│  │  ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─  │   │
│  │  Mom                        Dad                    │   │
│  │  Email: [mom@email  ]       Email: [dad@email  ]   │   │
│  │  Cell:  [555-5678   ]       Cell:  [555-9012   ]   │   │
│  │                                           [Save]   │   │
│  └────────────────────────────────────────────────────┘   │
│                                                          │
│  REGISTRATION                                            │
│  Reg #      1234         Registered  Feb 1, 2026         │
│                                                          │
│  DEMOGRAPHICS                                            │
│  Date of Birth  03/15/2012    Gender  Male               │
│  Address  123 Main St, Anytown, CA 90210                 │
│                                                          │
│  FAMILY                                                  │
│  Mom  Jane Smith          Dad  Bob Smith                 │
│                                                          │
│  PLAYER PROFILE  (only populated fields shown)           │
│  Grad Year  2028          Position  Attack               │
│  Jersey Size  L           Height  5'10"                  │
│                                                          │
│  ── Accounting Tab ──  (unchanged)                       │
│  ── Email Tab ──  (unchanged)                            │
│                                                          │
└──────────────────────────────────────────────────────────┘

═══════════════════════════════════════════════════════════════════════════════

Refund Modal (triggered from Accounting tab → "Credit/Refund CC Payment"):

┌──────────────────────────────────────────────┐
│  Credit Card Refund                          │
├──────────────────────────────────────────────┤
│                                              │
│  Original Transaction:                       │
│  Date:      2/1/2026                         │
│  Amount:    $250.00                          │
│  Card:      ••4242                           │
│  Trans ID:  12345678                         │
│                                              │
│  Refund Amount: [$250.00    ]                │
│  (Max: $250.00 — full or partial)            │
│                                              │
│  Reason: [                          ]        │
│                                              │
│  ⚠ This will credit the cardholder's        │
│    account via Authorize.Net                 │
│                                              │
│  [Cancel]              [Process Refund]      │
│                                              │
└──────────────────────────────────────────────┘

═══════════════════════════════════════════════════════════════════════════════

Batch Email Modal (triggered from filter bar → "Email Selected"):

┌──────────────────────────────────────────────────────────┐
│  Batch Email — 347 Recipients                            │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  From: [job-configured from address       ]              │
│  Subject: [                               ]              │
│                                                          │
│  Body:                                                   │
│  ┌────────────────────────────────────────────┐          │
│  │ Dear !PERSON,                              │          │
│  │                                             │          │
│  │ !JOBNAME registration update:               │          │
│  │ Your balance is !AMTOWED.                   │          │
│  │                                             │          │
│  │ !F-ACCOUNTING                               │          │
│  │                                             │          │
│  │ Thank you,                                  │          │
│  │ !CUSTOMERNAME                               │          │
│  └────────────────────────────────────────────┘          │
│                                                          │
│  Available Tokens:                                       │
│  !PERSON  !EMAIL  !JOBNAME  !AMTFEES  !AMTPAID          │
│  !AMTOWED  !SEASON  !SPORT  !CUSTOMERNAME                │
│  !F-ACCOUNTING  !F-PLAYERS  !J-CONTACTBLOCK              │
│                                                          │
│  [Preview (first 3)]  [Cancel]  [Send to 347 recipients] │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

### Why Syncfusion Grid

| Requirement | Syncfusion Capability |
|---|---|
| **Client-side paging** | Built-in grid paging with all results loaded (capped at 5,000) |
| **Multi-column sorting** | Click headers, multi-sort via Ctrl+click |
| **Column filtering** | Filter bar below headers OR filter menu per column |
| **Column visibility** | Built-in column chooser (hamburger menu) |
| **Excel/CSV export** | `ExcelExportService` already in use in teams-step |
| **Aggregate row** | Footer aggregates for Fees/Paid/Owed totals |
| **Row selection** | Checkbox column with select-all |
| **Responsive** | Adaptive column hiding, horizontal scroll |
| **Theming** | Already styled via `_syncfusion.scss` with CSS variables |
| **Tight density** | `.tight-table` class already defined for compact rows |

### Why Slide-Over Panel (Not Separate Page)

The #1 legacy pain point is losing search context when drilling into a registration's details or accounting. A slide-over panel (right-side drawer, 480px) keeps the grid visible underneath, preserving mental context. Admin can:
1. Click a row → panel slides in with details
2. Edit details, view accounting, or send email
3. Close panel → grid is exactly where they left it (same page, same filters, same scroll position)

No navigation, no back button, no re-entering filters.

## 4. User Value

- **10x faster workflows**: Client-side paging with server-side filtering handles large datasets (up to 5,000 results) with instant page/sort responsiveness
- **Zero context loss**: Slide-over panel keeps search results visible while editing/viewing details
- **Inline refunds**: Process partial or full credit card refunds without leaving the app
- **Batch email with preview**: Compose template, preview rendered output for first N recipients, then send
- **Export to Excel**: One-click export of current search results (filtered, sorted)
- **Financial dashboard**: Aggregate footer row shows total fees/paid/owed across all found registrations
- **Dynamic detail forms**: Registration detail form auto-assembles from `PlayerProfileMetadataJson` — every job gets the right fields

## 5. Design Alignment

- **Syncfusion Grid** with `tight-table` density class and existing `_syncfusion.scss` theming
- Bootstrap 5 + CSS variables (all 8 palettes)
- Signal-based state, OnPush change detection
- Toast notifications via existing `ToastService`
- `ConfirmDialogComponent` for destructive actions (refunds, batch email)
- WCAG AA compliant (keyboard-navigable grid, ARIA labels on slide-over, focus trap in modals)

## 6. UI Standards Created / Employed

### CREATED (new patterns this module introduces)

- **Slide-Over Detail Panel** — right-side drawer (480px) with tabs (Details, Accounting, Email). Keeps grid visible underneath. Includes close button and escape-key dismissal. This pattern can be reused across other admin tools that need row-level detail without navigation. **Desktop/tablet only** (hidden below 768px — mobile uses Quick Lookup mode instead).

- **Mobile Quick Lookup Mode** — below 768px, the entire desktop UI (filter panel, Syncfusion grid, slide-over panel) is replaced with a purpose-built mobile experience: a single search input, card-based results (name, team, role, owes badge), and tap-to-expand detail cards. Designed for the one thing an admin does on their phone: "which team is this kid on?" / "does this person owe money?" No refunds, no batch email, no Excel export — those are desk work. This pattern establishes the standard for mobile-first admin views: don't degrade the desktop experience to chase mobile parity; build a separate, intentionally limited mobile mode instead.

- **Client-Side Paged Syncfusion Grid with Server-Side Filtering** — backend returns all matching results (capped at 5,000) in a single response; Syncfusion grid handles paging and sorting locally for instant responsiveness. Aggregates (TotalFees/TotalPaid/TotalOwed) computed server-side across the full result set. This pattern works well for datasets under 5,000 rows — for larger datasets, a server-side `DataManager` adapter pattern should be considered.

- **Multi-Select Filter Panel with Count Badges** — Syncfusion `ejs-multiselect` with `mode="CheckBox"` and custom `itemTemplate` for registration count badges. Compact bar shows most-used filters always visible, expandable section reveals additional categories. Filter chips strip shows active selections with one-click removal. Parallel `Task.WhenAll` GroupBy queries compute counts for all 14 filter categories efficiently. This pattern replaces the legacy accordion-checkbox approach and can be reused for any admin search interface needing faceted filtering.

- **Inline Refund Workflow** — modal triggered from accounting tab, pre-populated with transaction details, supports partial/full refund amount, processes via `ADN_Refund()`, creates negative accounting record, updates registration financials.

- **Dynamic Metadata-Driven Read-Only Display** — registration detail panel parses `PlayerProfileMetadataJson` at runtime to determine field labels and types, but renders populated fields as compact read-only label:value pairs instead of editable form inputs. Only fields with actual values are displayed — empty fields are hidden entirely. Checkbox fields are rendered as "Yes"/"No". This pattern — metadata-driven display that adapts to the job's profile type without code changes — is reusable for any future metadata-driven info view.

- **Batch Email Composer with Token Reference** — modal with template editor, clickable token insertion, preview rendering (substitutes tokens for first N recipients to show admin what the email will look like), and batch send with progress/result feedback.

- **LADT Hierarchical Checkbox Tree Filter** — standalone component rendering League > Agegroup > Division > Team as an indented tree with tri-state checkboxes (check/uncheck cascade), expand/collapse, pill count badges (teams/players), and special node styling. Replaces flat multi-select dropdowns when the data has a parent-child hierarchy that provides essential context for selection. Tree data from existing `/api/ladt/tree` endpoint; checked IDs emitted to parent for classification by level. Reusable for any hierarchical filter scenario (e.g., location trees, category taxonomies).

- **Change Job Modal** — in-panel modal (using `position: absolute` to work within the CSS-transformed slide-over) with a dropdown of other jobs under the same customer, loaded on-demand when the button is clicked. Includes automatic team name matching in the target job. This pattern — fetching options on modal open rather than pre-loading — prevents stale data and keeps the initial detail load lightweight. The `position: absolute` approach (instead of `position: fixed`) is required because the detail panel's `transform: translateX()` creates a new containing block that makes `position: fixed` relative to the panel rather than the viewport.

- **Delete Registration with Server-Side Guard Rails** — frontend shows a simple confirmation modal, but the server enforces all business rules (role-based authorization, pre-condition checks). Error messages are returned as human-readable strings that the frontend displays directly in a toast. This pattern — thin frontend confirmation + thick server validation with descriptive error messages — is preferred over duplicating business logic in the frontend for destructive operations. The server returns 409/Conflict for pre-condition failures and 200/OK for success.

- **Dirty-State "Update Results" Button** — search button that pulses yellow (`btn-warning` + `pulse-glow` animation) when filters have changed since last search. Detects changes by comparing `JSON.stringify(sanitizeRequest(current))` against last-searched snapshot. Requires `[changeOnBlur]="false"` on Syncfusion multi-selects for immediate activation. Baseline set after filter options load so changes are detected even before first search. Button text switches between "Search" and "Update Results" contextually.

### EMPLOYED (existing patterns reused)

- Syncfusion grid with `GridAllModule` (from team-registration-wizard teams-step)
- `_syncfusion.scss` glassmorphic theme overrides (from existing global styles)
- `tight-table` density class (from existing global styles)
- Signal-based state management (from all admin tools)
- CSS variable design system tokens (all colors, spacing, borders)
- `@if` / `@for` template syntax
- OnPush change detection
- `inject()` dependency injection
- Repository pattern (RegistrationRepository, RegistrationAccountingRepository)
- `ConfirmDialogComponent` for destructive confirmations
- `ToastService` for success/error feedback
- `TextSubstitutionService` for email token rendering (existing, comprehensive)
- `EmailService.SendBatchAsync()` for bulk email delivery (existing)
- `IAdnApiService.ADN_Refund()` for credit card refunds (existing, never called — first use)

---

## 7. Security Requirements

**CRITICAL**: All endpoints must derive `jobId` from JWT claims, NOT from route parameters.

- **Route**: `/:jobPath/admin/search` (jobPath for routing only)
- **API Endpoints**: Must use `ClaimsPrincipalExtensions.GetJobIdFromRegistrationAsync()` to derive `jobId` from the authenticated user's `regId` claim
- **NO route parameters containing sensitive IDs**: All `[Authorize]` endpoints extract job context from JWT token
- **Policy**: `[Authorize(Policy = "AdminOnly")]` — Directors, SuperDirectors, and Superusers can search registrations
- **Refund authorization**: Refunds require `AdminOnly` policy; the API must verify the accounting record belongs to the user's job before processing
- **Refund amount validation**: Server must enforce `refundAmount <= originalPayAmount` and `refundAmount > 0`
- **Batch email**: Server must verify all recipient registrations belong to the user's job
- **Registration editing**: Server must verify the registration belongs to the user's job before persisting changes
- **ADN credentials**: Fetched server-side from `Customer` entity by `jobId` — never exposed to frontend
- **Change Job authorization**: AdminOnly policy (Director/SuperDirector/Superuser). Server verifies target job belongs to the same customer as the source job — cannot move registrations across customers
- **Delete registration authorization**: Fine-grained role-based checks beyond AdminOnly policy:

| Registration Role | Allowed Caller Roles |
|---|---|
| Player | Director, SuperDirector, Superuser |
| Staff | Director, SuperDirector, Superuser |
| Unassigned Adult | **Superuser only** |
| Other roles | Deletion not allowed |

- **Delete pre-conditions** (server-enforced, all must pass):
  1. No `RegistrationAccounting` records for this registration
  2. No `StoreCartBatchSkus` records linked via `DirectToRegId`
  3. No `RegsaverPolicyId` on the Registration entity (no active insurance policy)
- **Device cleanup before delete**: `DeviceRegistrationIds` linked to the registration are removed before deletion to prevent orphaned device records

---

## 8. Database Entities (Existing — No Schema Changes)

### Key Entities Involved:

**Registrations** (primary search target):
- `RegistrationId` (Guid, PK), `RegistrationAi` (int, auto-increment display ID)
- `JobId` (Guid, FK) — scopes all queries to current job
- `UserId` (string, FK → AspNetUsers) — the player/registrant
- `FamilyUserId` (string, FK → Families) — the family account
- `RoleId` (string, FK → AspNetRoles) — Player, Coach, Staff, etc.
- `AssignedTeamId` (Guid?, FK → Teams) — team assignment
- `BActive` (bool) — active/inactive status
- 9 financial fields: `FeeBase`, `FeeDiscount`, `FeeDiscountMp`, `FeeDonation`, `FeeLatefee`, `FeeProcessing`, `FeeTotal`, `OwedTotal`, `PaidTotal`
- 40+ player profile columns (mapped from `PlayerProfileMetadataJson`)
- `RegistrationTs` (DateTime) — registration timestamp
- `Modified` (DateTime) — last modified

**AspNetUsers** (registrant identity — joined for search/display):
- `FirstName`, `LastName`, `Email`, `PhoneNumber`, `Cellphone`
- `Address1`, `City`, `State`, `Zip`
- `Birthdate`, `Gender`

**Teams** (for display and filtering):
- `TeamId`, `TeamName`, `AgegroupId`, `DivId`

**Agegroups** (for filtering):
- `AgegroupId`, `AgegroupName`

**Divisions** (for filtering):
- `DivId`, `DivName`

**AspNetRoles** (for role filtering):
- `Id`, `Name` — Player, Coach, Director, ClubRep, Staff, etc.

**RegistrationAccounting** (payment history per registration):
- `AId` (int, PK), `RegistrationId` (Guid, FK)
- `Dueamt`, `Payamt` (decimal?) — fee and payment amounts
- `Paymeth` (string) — payment method description
- `PaymentMethodId` (Guid, FK → AccountingPaymentMethods)
- `AdnTransactionId`, `AdnCc4`, `AdnCcexpDate` — Authorize.Net details (needed for refund)
- `AdnInvoiceNo` — invoice number
- `Comment` (string?) — admin notes
- `Createdate` (DateTime?) — payment date
- `Active` (bool?)

**AccountingPaymentMethods** (reference data):
- `PaymentMethodId` (Guid, PK)
- `PaymentMethod` (string) — "Credit Card Payment", "Check", "Cash", etc.
- Known CC GUID: `30ECA575-A268-E111-9D56-F04DA202060D`

---

## 9. Implementation Steps

### Phase 1: Backend — Search DTOs

**Status**: [x] Complete

**File**:
- `TSIC.Contracts/Dtos/RegistrationSearch/RegistrationSearchDtos.cs`

**DTOs** (as implemented):
```csharp
// ── Search request — ALL filters are multi-select arrays (legacy parity) ──
public record RegistrationSearchRequest
{
    // Text filters
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? SchoolName { get; init; }

    // Multi-select filters
    public List<string>? RoleIds { get; init; }
    public List<Guid>? TeamIds { get; init; }
    public List<Guid>? AgegroupIds { get; init; }
    public List<Guid>? DivisionIds { get; init; }
    public List<string>? ClubNames { get; init; }
    public List<string>? Genders { get; init; }
    public List<string>? Positions { get; init; }
    public List<string>? GradYears { get; init; }
    public List<string>? Grades { get; init; }
    public List<int>? AgeRangeIds { get; init; }

    // Status filters (multi-select)
    public List<string>? ActiveStatuses { get; init; }       // "True"/"False"
    public List<string>? PayStatuses { get; init; }          // "PAID IN FULL"/"UNDER PAID"/"OVER PAID"
    public List<string>? ArbSubscriptionStatuses { get; init; }  // "PAYING BY SUBSCRIPTION"/"NOT PAYING BY SUBSCRIPTION"
    public List<string>? MobileRegistrationRoles { get; init; }  // Role names of mobile-registered

    // Date range
    public DateTime? RegDateFrom { get; init; }
    public DateTime? RegDateTo { get; init; }

    // Club roster threshold search
    public int? RosterThreshold { get; init; }
    public string? RosterThresholdClub { get; init; }
    // NOTE: No Skip/Take/SortField/SortDirection — grid handles paging/sorting client-side
}

// ── Search result row ──
public record RegistrationSearchResultDto
{
    public required Guid RegistrationId { get; init; }
    public required int RegistrationAi { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
    public DateTime? Dob { get; init; }
    public required string RoleName { get; init; }
    public required bool Active { get; init; }
    public string? Position { get; init; }
    public string? TeamName { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivisionName { get; init; }
    public string? ClubName { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required DateTime RegistrationTs { get; init; }
    public DateTime? Modified { get; init; }
}

// ── Response wrapper with aggregates ──
public record RegistrationSearchResponse
{
    public required List<RegistrationSearchResultDto> Result { get; init; }
    public required int Count { get; init; }
    public required decimal TotalFees { get; init; }
    public required decimal TotalPaid { get; init; }
    public required decimal TotalOwed { get; init; }
}

// ── Filter options with counts for all 14 categories ──
public record RegistrationFilterOptionsDto
{
    // Organization
    public required List<FilterOption> Roles { get; init; }
    public required List<FilterOption> Teams { get; init; }
    public required List<FilterOption> Agegroups { get; init; }
    public required List<FilterOption> Divisions { get; init; }
    public required List<FilterOption> Clubs { get; init; }
    // Status
    public required List<FilterOption> ActiveStatuses { get; init; }
    public required List<FilterOption> PayStatuses { get; init; }
    // Demographics
    public required List<FilterOption> Genders { get; init; }
    public required List<FilterOption> Positions { get; init; }
    public required List<FilterOption> GradYears { get; init; }
    public required List<FilterOption> Grades { get; init; }
    public required List<FilterOption> AgeRanges { get; init; }
    // Billing & Mobile
    public required List<FilterOption> ArbSubscriptionStatuses { get; init; }
    public required List<FilterOption> MobileRegistrations { get; init; }
}

public record FilterOption
{
    public required string Value { get; init; }
    public required string Text { get; init; }
    public int Count { get; init; }              // Registration count for this option
    public bool DefaultChecked { get; init; }    // Pre-selected in UI (Active=true)
}
```

### Phase 2: Backend — Accounting & Refund DTOs

**Status**: [x] Complete

**File to create**:
- `TSIC.Contracts/Dtos/RegistrationSearch/AccountingDtos.cs`

**DTOs**:
```csharp
// ── Accounting record for display ──
public record AccountingRecordDto
{
    public required int AId { get; init; }
    public required DateTime? Date { get; init; }
    public required string PaymentMethod { get; init; }
    public required decimal? DueAmount { get; init; }
    public required decimal? PaidAmount { get; init; }
    public string? Comment { get; init; }
    public string? CheckNo { get; init; }
    public string? PromoCode { get; init; }
    public bool? Active { get; init; }

    // CC details (for refund eligibility)
    public string? AdnTransactionId { get; init; }
    public string? AdnCc4 { get; init; }            // Last 4 digits
    public string? AdnCcExpDate { get; init; }
    public string? AdnInvoiceNo { get; init; }
    public bool CanRefund { get; init; }             // true if CC payment with transaction ID
}

// ── Create accounting record request ──
public record CreateAccountingRecordRequest
{
    public required Guid RegistrationId { get; init; }
    public required Guid PaymentMethodId { get; init; }
    public decimal? DueAmount { get; init; }
    public decimal? PaidAmount { get; init; }
    public string? Comment { get; init; }
    public string? CheckNo { get; init; }
    public string? PromoCode { get; init; }
}

// ── Refund request ──
public record RefundRequest
{
    public required int AccountingRecordId { get; init; }   // AId of the original payment
    public required decimal RefundAmount { get; init; }     // Partial or full (up to original Payamt)
    public string? Reason { get; init; }
}

// ── Refund response ──
public record RefundResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? TransactionId { get; init; }     // ADN refund transaction ID
    public decimal? RefundedAmount { get; init; }
}

// ── Payment methods list (for create accounting dropdown) ──
public record PaymentMethodOptionDto
{
    public required Guid PaymentMethodId { get; init; }
    public required string PaymentMethod { get; init; }
}
```

### Phase 3: Backend — Registration Detail DTOs

**Status**: [x] Complete

**File to create**:
- `TSIC.Contracts/Dtos/RegistrationSearch/RegistrationDetailDtos.cs`

**DTOs**:
```csharp
// ── Full registration detail (for slide-over panel) ──
public record RegistrationDetailDto
{
    // Identity
    public required Guid RegistrationId { get; init; }
    public required int RegistrationAi { get; init; }

    // Person (from AspNetUsers)
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }

    // Context
    public required string RoleName { get; init; }
    public required bool Active { get; init; }
    public string? TeamName { get; init; }

    // Financials (summary)
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }

    // Dynamic profile fields (from PlayerProfileMetadataJson)
    // Key = metadata field name (camelCase), Value = current value as string
    public required Dictionary<string, string?> ProfileValues { get; init; }

    // Metadata schema (from Job.PlayerProfileMetadataJson — for form rendering)
    public required string? ProfileMetadataJson { get; init; }

    // Accounting records
    public required List<AccountingRecordDto> AccountingRecords { get; init; }
}

// ── Update registration profile request ──
public record UpdateRegistrationProfileRequest
{
    public required Guid RegistrationId { get; init; }
    // Key = dbColumn name, Value = new value as string
    public required Dictionary<string, string?> ProfileValues { get; init; }
}

// ── Batch email request ──
public record BatchEmailRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required string Subject { get; init; }
    public required string BodyTemplate { get; init; }   // Contains substitution tokens
}

// ── Batch email response ──
public record BatchEmailResponse
{
    public required int TotalRecipients { get; init; }
    public required int Sent { get; init; }
    public required int Failed { get; init; }
    public required List<string> FailedAddresses { get; init; }
}

// ── Email preview request (renders tokens for N recipients) ──
public record EmailPreviewRequest
{
    public required List<Guid> RegistrationIds { get; init; }   // First N to preview
    public required string Subject { get; init; }
    public required string BodyTemplate { get; init; }
}

// ── Email preview response ──
public record EmailPreviewResponse
{
    public required List<RenderedEmailPreview> Previews { get; init; }
}

public record RenderedEmailPreview
{
    public required string RecipientName { get; init; }
    public required string RecipientEmail { get; init; }
    public required string RenderedSubject { get; init; }
    public required string RenderedBody { get; init; }
}

// ── Family contact (for detail panel editing) ──
public record FamilyContactDto { /* all nullable string props from Families entity */ }

// ── User demographics (for detail panel editing) ──
public record UserDemographicsDto { /* dateOfBirth, gender, address fields from AspNetUsers */ }

// ── Update family contact request ──
public record UpdateFamilyContactRequest
{
    public required Guid RegistrationId { get; init; }
    public required FamilyContactDto FamilyContact { get; init; }
}

// ── Update demographics request ──
public record UpdateUserDemographicsRequest
{
    public required Guid RegistrationId { get; init; }
    public required UserDemographicsDto Demographics { get; init; }
}

// ── Change Job DTOs ──
public record JobOptionDto
{
    public required string JobId { get; init; }
    public required string JobName { get; init; }
    public string? SeasonName { get; init; }
}

public record ChangeJobRequest
{
    public required string NewJobId { get; init; }
}

public record ChangeJobResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? NewJobName { get; init; }
}

// ── Delete Registration DTOs ──
public record DeleteRegistrationResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
}
```

### Phase 4: Backend — Repository Extensions

**Status**: [x] Complete

**Files modified**:
- `TSIC.Contracts/Repositories/IRegistrationRepository.cs` (added methods)
- `TSIC.Infrastructure/Repositories/RegistrationRepository.cs` (implemented)
- `TSIC.Contracts/Repositories/IRegistrationAccountingRepository.cs` (added methods)
- `TSIC.Infrastructure/Repositories/RegistrationAccountingRepository.cs` (implemented)

**IRegistrationRepository methods** (as implemented):

```
SearchAsync(Guid jobId, RegistrationSearchRequest request, CancellationToken ct) → RegistrationSearchResponse
    -- Base query: .Where(r => r.JobId == jobId && r.UserId != null).AsNoTracking()
    -- ALL filters use multi-value .Contains() for multi-select support:
    --   ActiveStatuses: converts "True"/"False" strings to booleans, uses boolValues.Contains(r.BActive.Value)
    --   PayStatuses: OR logic — "PAID IN FULL" → OwedTotal == 0, "UNDER PAID" → OwedTotal > 0, "OVER PAID" → OwedTotal < 0
    --   RoleIds: request.RoleIds.Contains(r.RoleId)
    --   TeamIds/AgegroupIds/DivisionIds: OR logic across levels — tree selections
    --     at different levels are unioned so a registration matches if assigned at
    --     ANY checked level (team, division, or agegroup). Frontend always emits
    --     IDs at all descendant levels for full coverage.
    --   ClubNames: request.ClubNames.Contains(r.ClubName)
    --   Genders: request.Genders.Contains(r.User.Gender)
    --   Positions: request.Positions.Contains(r.Position)
    --   GradYears: request.GradYears.Contains(r.GradYear)
    --   Grades: request.Grades.Contains(r.SchoolGrade)
    --   AgeRangeIds: cross-join with JobAgeRanges, DOB between RangeLeft/RangeRight
    --   ArbSubscriptionStatuses: "PAYING" → active/suspended AdnSubscriptionStatus, "NOT PAYING" → null/other
    --   MobileRegistrationRoles: r.ModifiedMobile != null && roles.Contains(r.RoleId)
    --   Name: split on space → first/last contains (or single term matches either)
    --   Email: contains match on User.Email
    --   Phone: contains match on User.Cellphone
    --   SchoolName: contains match on r.SchoolName
    --   RegDateFrom/RegDateTo: bracket on RegistrationTs
    --   RosterThreshold: subquery finds club rep registrations whose teams have
    --     roster counts <= threshold. Optional RosterThresholdClub narrows to one club.
    -- Joins: Registrations → AspNetUsers, Teams, AspNetRoles, Agegroups, Divisions
    -- Returns ALL matching results (no Skip/Take — client-side paging, capped at 5000)
    -- Computes aggregates (TotalFees, TotalPaid, TotalOwed) across full result set

GetFilterOptionsAsync(Guid jobId, CancellationToken ct) → RegistrationFilterOptionsDto
    -- Parallel execution via Task.WhenAll for all 14 filter categories
    -- Dynamic GroupBy categories (10): Roles, Teams, Agegroups, Divisions, Clubs,
    --   Positions, Genders, GradYears, Grades, MobileRegistrations
    -- Fixed-value count categories (3): ActiveStatuses (GroupBy BActive),
    --   PayStatuses (3 separate CountAsync: OwedTotal ==0/>0/<0),
    --   ArbSubscriptionStatuses (2 separate CountAsync: active+suspended / other)
    -- Computed category (1): AgeRanges (cross-join with JobAgeRanges, DOB in range)
    -- ActiveStatuses "Active" option gets DefaultChecked = true
    -- All counts scoped to jobId + UserId != null

GetRegistrationDetailAsync(Guid registrationId, Guid jobId) → RegistrationDetailDto
    -- Full registration with user data, profile values, accounting records
    -- Joins: Registrations → AspNetUsers, Teams, Roles
    --        Registrations → RegistrationAccounting (with PaymentMethod)
    -- Reads Job.PlayerProfileMetadataJson for form schema
    -- Builds ProfileValues dictionary from entity columns using metadata field→dbColumn mapping
    -- Validates registrationId belongs to jobId
    -- AsNoTracking (except profile values which use reflection)

UpdateRegistrationProfileAsync(Guid jobId, string userId, UpdateRegistrationProfileRequest request) → void
    -- Loads registration (tracked) for update
    -- Validates registration belongs to job
    -- For each profileValue entry:
    --   Maps key (dbColumn) to Registrations entity property via reflection
    --   Sets property value with appropriate type conversion
    -- Updates Modified timestamp + LebUserId
    -- SaveChangesAsync

FindMatchingRegistrationTeamAsync(Guid registrationId, Guid newJobId, CancellationToken ct) → Guid?
    -- For Change Job: finds a team in the target job with the same name as the source registration's team
    -- Matches on TeamName equality (case-insensitive)
    -- Returns matching TeamId or null if no match found
    -- AsNoTracking

HasAccountingRecordsAsync(Guid registrationId, CancellationToken ct) → bool
    -- For Delete Registration pre-condition check
    -- Returns true if any RegistrationAccounting records exist for this registration
    -- AsNoTracking

HasStoreCartBatchRecordsAsync(Guid registrationId, CancellationToken ct) → bool
    -- For Delete Registration pre-condition check
    -- Returns true if any StoreCartBatchSkus records have DirectToRegId matching this registration
    -- AsNoTracking

GetRegistrationRoleNameAsync(Guid registrationId, CancellationToken ct) → string?
    -- For Delete Registration role-based authorization
    -- Navigates Registration → Role (AspNetRoles) to get role name
    -- Returns null if registration not found or role not set
    -- AsNoTracking
```

**IJobRepository methods** (added for Change Job):
```
GetOtherJobsForCustomerAsync(Guid currentJobId, CancellationToken ct) → List<JobOptionDto>
    -- Returns all other jobs belonging to the same customer as the current job
    -- Excludes the current job from results
    -- Orders by job name
    -- Used for Change Job dropdown options
    -- AsNoTracking
```

**IRegistrationAccountingRepository methods**:
```
GetByRegistrationIdAsync(Guid registrationId) → List<AccountingRecordDto>
    -- Returns all accounting records for a registration
    -- Joins: RegistrationAccounting → AccountingPaymentMethods
    -- Sets CanRefund = true where PaymentMethod contains "Credit Card" AND AdnTransactionId is not null
    -- Ordered by Createdate desc
    -- AsNoTracking

GetByIdAsync(int aId) → RegistrationAccounting?
    -- Returns single accounting record (tracked, for refund operations)
    -- Includes Registration navigation for financial recalculation

GetPaymentMethodOptionsAsync() → List<PaymentMethodOptionDto>
    -- Returns all payment methods for dropdown
    -- AsNoTracking
```

### Phase 5: Backend — Registration Search Service

**Status**: [x] Complete

**Files to create**:
- `TSIC.Contracts/Services/IRegistrationSearchService.cs`
- `TSIC.API/Services/Admin/RegistrationSearchService.cs`

**Dependencies**:
- `IRegistrationRepository`
- `IRegistrationAccountingRepository`
- `IAdnApiService`
- `IJobRepository`
- `IEmailService`
- `ITextSubstitutionService`
- `IProfileMetadataService` (for parsing PlayerProfileMetadataJson)
- `IRegistrationRecordFeeCalculatorService` (for financial recalculation after refund)
- `IDeviceRepository` (for device cleanup before registration deletion)

**Methods**:

```
SearchAsync(Guid jobId, RegistrationSearchRequest request) → RegistrationSearchResponse
    -- Delegates to repository
    -- Validates page size (max 100)

GetFilterOptionsAsync(Guid jobId) → RegistrationFilterOptionsDto
    -- Delegates to repository

GetRegistrationDetailAsync(Guid registrationId, Guid jobId) → RegistrationDetailDto
    -- Delegates to repository
    -- Enriches with parsed metadata for form rendering

UpdateRegistrationProfileAsync(Guid jobId, string userId, UpdateRegistrationProfileRequest req) → void
    -- Delegates to repository
    -- Validates field names against job's metadata schema (prevent arbitrary column writes)

CreateAccountingRecordAsync(Guid jobId, string userId, CreateAccountingRecordRequest req) → AccountingRecordDto
    -- Validates registration belongs to job
    -- Creates RegistrationAccounting entity
    -- Updates registration financial totals (PaidTotal, OwedTotal)
    -- SaveChangesAsync
    -- Returns created record

ProcessRefundAsync(Guid jobId, string userId, RefundRequest req) → RefundResponse
    -- Loads original accounting record by AId
    -- Validates:
    --   1. Record exists and belongs to a registration in this job
    --   2. Record is a CC payment (has AdnTransactionId)
    --   3. RefundAmount > 0 AND RefundAmount <= original Payamt
    -- Loads job's ADN credentials from Customer entity
    -- Calls ADN_Refund(new AdnRefundRequest {
    --     TransactionId = original.AdnTransactionId,
    --     Amount = request.RefundAmount,
    --     CardNumberLast4 = original.AdnCc4,
    --     Expiry = original.AdnCcexpDate,
    --     InvoiceNumber = original.AdnInvoiceNo,
    --     ... env/credentials from job's customer ...
    -- })
    -- If successful:
    --   1. Creates new RegistrationAccounting record (negative Payamt = -RefundAmount)
    --      Paymeth = "Credit Card Refund", Comment = request.Reason
    --      AdnTransactionId = refund transaction ID
    --   2. Updates registration.PaidTotal -= RefundAmount
    --   3. Updates registration.OwedTotal = registration.FeeTotal - registration.PaidTotal
    --   4. SaveChangesAsync
    -- Returns RefundResponse with success/failure and transaction ID

SendBatchEmailAsync(Guid jobId, string userId, BatchEmailRequest req) → BatchEmailResponse
    -- Validates all registration IDs belong to job
    -- For each registration:
    --   1. Loads registrant email from AspNetUsers
    --   2. Renders template using TextSubstitutionService.SubstituteAsync()
    --   3. Builds EmailMessageDto
    -- Calls EmailService.SendBatchAsync()
    -- Returns result with sent/failed counts

PreviewEmailAsync(Guid jobId, EmailPreviewRequest req) → EmailPreviewResponse
    -- Same as batch email but only renders first N (req.RegistrationIds count)
    -- Does NOT send — returns rendered HTML for preview display

GetChangeJobOptionsAsync(Guid jobId, CancellationToken ct) → List<JobOptionDto>
    -- Delegates to IJobRepository.GetOtherJobsForCustomerAsync()
    -- Returns all other jobs under the same customer for the Change Job dropdown

ChangeJobAsync(Guid jobId, string userId, Guid registrationId, ChangeJobRequest req, CancellationToken ct) → ChangeJobResponse
    -- Loads registration, validates belongs to current job
    -- Validates target job exists and belongs to same customer
    -- Attempts team matching: calls FindMatchingRegistrationTeamAsync() to find a team
    --   in the target job with the same name as the source team
    -- Updates reg.JobId to new job
    -- If team match found: updates reg.AssignedTeamId; otherwise clears assignment
    -- Logs the transfer (source job, target job, team match result)
    -- Returns success response with new job name

DeleteRegistrationAsync(Guid jobId, string userId, string callerRole, Guid registrationId, CancellationToken ct) → DeleteRegistrationResponse
    -- Loads registration, validates belongs to current job
    -- **Role-based authorization**:
    --   "Unassigned Adult" role → only Superuser can delete
    --   "Player" or "Staff" role → Director, SuperDirector, Superuser can delete
    --   Other roles → deletion not allowed
    -- **Pre-condition checks** (all must pass):
    --   1. No RegistrationAccounting records (HasAccountingRecordsAsync)
    --   2. No StoreCartBatchSkus records via DirectToRegId (HasStoreCartBatchRecordsAsync)
    --   3. No RegsaverPolicyId on registration entity (no active insurance)
    -- Returns 409/Conflict with human-readable reason if any check fails
    -- **Device cleanup**: removes DeviceRegistrationIds linked to this registration
    --   via IDeviceRepository.GetDeviceRegistrationIdsByRegistrationAsync() + RemoveDeviceRegistrationIds()
    -- **Delete**: _registrationRepo.Remove(reg) + SaveChangesAsync
    -- Logs deletion (regId, role, jobId, userId)
    -- Returns success response
```

### Phase 6: Backend — Controller

**Status**: [x] Complete

**File to create**:
- `TSIC.API/Controllers/RegistrationSearchController.cs`

**Route**: `api/registration-search`

**Endpoints**:
- `POST api/registration-search/search` → `RegistrationSearchResponse`
  - Body: `RegistrationSearchRequest`
  - POST because filter criteria can be complex (body > query string)

- `GET api/registration-search/filter-options` → `RegistrationFilterOptionsDto`
  - Returns dropdown options for filter panel (roles, teams, agegroups, divisions, clubs)

- `GET api/registration-search/{registrationId:guid}` → `RegistrationDetailDto`
  - Full registration detail with profile values, metadata schema, accounting records

- `PUT api/registration-search/{registrationId:guid}/profile` → `void`
  - Body: `UpdateRegistrationProfileRequest`
  - Updates dynamic profile fields

- `POST api/registration-search/{registrationId:guid}/accounting` → `AccountingRecordDto`
  - Body: `CreateAccountingRecordRequest`
  - Creates new accounting record

- `POST api/registration-search/refund` → `RefundResponse`
  - Body: `RefundRequest`
  - Processes credit card refund via Authorize.Net

- `GET api/registration-search/payment-methods` → `List<PaymentMethodOptionDto>`
  - Returns payment method options for create-accounting dropdown

- `POST api/registration-search/batch-email` → `BatchEmailResponse`
  - Body: `BatchEmailRequest`
  - Sends batch email with token substitution

- `POST api/registration-search/email-preview` → `EmailPreviewResponse`
  - Body: `EmailPreviewRequest`
  - Renders email template for preview (no send)

- `PUT api/registration-search/{registrationId:guid}/family` → `void`
  - Body: `UpdateFamilyContactRequest`
  - Updates family/guardian contact information

- `PUT api/registration-search/{registrationId:guid}/demographics` → `void`
  - Body: `UpdateUserDemographicsRequest`
  - Updates user demographic fields (DOB, address, etc.)

- `GET api/registration-search/change-job-options` → `List<JobOptionDto>`
  - Returns other jobs under the same customer for the Change Job dropdown

- `POST api/registration-search/{registrationId:guid}/change-job` → `ChangeJobResponse`
  - Body: `ChangeJobRequest`
  - Moves registration to a different job under the same customer
  - Attempts automatic team matching by name in the target job

- `DELETE api/registration-search/{registrationId:guid}` → `DeleteRegistrationResponse`
  - Permanently deletes a registration after role-based authorization and pre-condition checks
  - Returns 409/Conflict if pre-conditions fail (accounting records, store records, insurance)
  - Extracts `callerRole` from JWT `ClaimTypes.Role` for Unassigned Adult authorization

**Authorization**: All endpoints `[Authorize(Policy = "AdminOnly")]`, derive `jobId` from JWT via `GetJobIdFromRegistrationAsync()`. Delete endpoint performs additional fine-grained role check: Unassigned Adult registrations require Superuser role.

### Phase 7: Backend — DI Registration

**Status**: [x] Complete

**File to modify**:
- `TSIC.API/Program.cs`

**Add registration**:
```csharp
builder.Services.AddScoped<IRegistrationSearchService, RegistrationSearchService>();
```

### Phase 8: Frontend — Service

**Status**: [x] Complete

**File to create**:
- `src/app/views/admin/registration-search/services/registration-search.service.ts`

**Methods** (all return Observables — import DTOs from `@core/api`):
- `search(request: RegistrationSearchRequest): Observable<RegistrationSearchResponse>`
- `getFilterOptions(): Observable<RegistrationFilterOptionsDto>`
- `getRegistrationDetail(registrationId: string): Observable<RegistrationDetailDto>`
- `updateProfile(registrationId: string, request: UpdateRegistrationProfileRequest): Observable<void>`
- `createAccountingRecord(registrationId: string, request: CreateAccountingRecordRequest): Observable<AccountingRecordDto>`
- `processRefund(request: RefundRequest): Observable<RefundResponse>`
- `getPaymentMethods(): Observable<PaymentMethodOptionDto[]>`
- `sendBatchEmail(request: BatchEmailRequest): Observable<BatchEmailResponse>`
- `previewEmail(request: EmailPreviewRequest): Observable<EmailPreviewResponse>`
- `getLadtTree(): Observable<LadtTreeRootDto>` (calls `/api/ladt/tree`)
- `updateFamilyContact(registrationId: string, request: UpdateFamilyContactRequest): Observable<void>`
- `updateDemographics(registrationId: string, request: UpdateUserDemographicsRequest): Observable<void>`
- `getChangeJobOptions(): Observable<JobOptionDto[]>`
- `changeJob(registrationId: string, request: ChangeJobRequest): Observable<ChangeJobResponse>`
- `deleteRegistration(registrationId: string): Observable<DeleteRegistrationResponse>`

### Phase 8b: Frontend — LADT Tree Filter Component

**Status**: [x] Complete

**File created**:
- `src/app/views/admin/registration-search/components/ladt-tree-filter.component.ts`

**Standalone component** with inline template and styles. Replaces the flat Team/Agegroup/Division multi-select dropdowns with a hierarchical checkbox tree showing League > Agegroup > Division > Team with count badges at every level.

**Inputs**: `treeData: LadtTreeNodeDto[]`, `checkedIds: Set<string>`
**Outputs**: `checkedIdsChange: EventEmitter<Set<string>>`

**Features**:
- **Tree flattening** with sorting: regular agegroups alpha-first, specials (Dropped Teams, WAITLIST*) at bottom; divisions with "Unassigned" first
- **Tri-state checkboxes**: check parent → check all descendants, uncheck parent → uncheck all descendants, partial children → parent shows indeterminate
- **Expand/collapse**: league level expanded by default, expand-all/collapse-all toolbar buttons
- **Count badges**: pill badges per node — blue (`--bs-info`) for teams, primary (`--bs-primary`) for players
- **Totals row**: aggregated team/player counts across all leagues
- **Special node styling**: italic + reduced opacity for Dropped Teams and WAITLIST agegroups

**Data flow**: Parent loads tree via `getLadtTree()` which calls `/api/ladt/tree`. Tree component manages expansion/check state internally and emits `Set<string>` of checked node IDs. Parent's `onLadtCheckedChange()` handler classifies IDs by level into `teamIds`/`agegroupIds`/`divisionIds` arrays — always emitting IDs at ALL descendant levels (not just highest checked ancestor) so the backend OR filter catches registrations assigned at any level.

### Phase 9: Frontend — Registration Search Component (Main Grid)

**Status**: [x] Complete

**Files created**:
- `src/app/views/admin/registration-search/registration-search.component.ts`
- `src/app/views/admin/registration-search/registration-search.component.html`
- `src/app/views/admin/registration-search/registration-search.component.scss`

**Component imports** (as implemented):
```typescript
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import { MultiSelectModule, CheckBoxSelectionService } from '@syncfusion/ej2-angular-dropdowns';
// schemas: [CUSTOM_ELEMENTS_SCHEMA]
// providers: [CheckBoxSelectionService]
```

**Component state** (signals, as implemented):
```typescript
// Filter options & search
filterOptions = signal<RegistrationFilterOptionsDto | null>(null);
searchRequest = signal<RegistrationSearchRequest>({
  name: '', email: '', phone: '', schoolName: '',
  roleIds: [], teamIds: [], agegroupIds: [], divisionIds: [],
  clubNames: [], genders: [], positions: [], gradYears: [],
  grades: [], ageRangeIds: [],
  activeStatuses: ['True'],  // Default: Active pre-checked
  payStatuses: [], arbSubscriptionStatuses: [],
  mobileRegistrationRoles: [],
  regDateFrom: undefined, regDateTo: undefined,
  rosterThreshold: undefined, rosterThresholdClub: undefined
});

// LADT tree state
ladtTree = signal<LadtTreeNodeDto[]>([]);
ladtCheckedIds = signal<Set<string>>(new Set());
ladtNodeMap = computed(/* flat Map<id, {name, level}> for chip label lookups */);
searchResults = signal<RegistrationSearchResponse | null>(null);
isSearching = signal(false);

// Expandable "More Filters"
moreFiltersExpanded = signal(false);

// Syncfusion MultiSelect fields config
msFields = { value: 'value', text: 'text' };

// Filter chips — computed from active filter selections
activeFilterChips = computed<FilterChip[]>(() => { ... });

// Grid state
selectedRegistrations = signal<Set<string>>(new Set());

// Slide-over panel
selectedDetail = signal<RegistrationDetailDto | null>(null);
isPanelOpen = signal(false);

// Modals
showBatchEmailModal = signal(false);
showRefundModal = signal(false);
refundTarget = signal<AccountingRecordDto | null>(null);

// Mobile detection
isMobile = signal(false);
```

**Syncfusion Grid configuration**:
- `allowPaging: true`, `pageSettings: { pageSize: 20 }` (client-side paging)
- `allowSorting: true` (client-side sorting)
- `allowExcelExport: true`
- Columns: Row #, Last, First, Email, Phone, Team, Role, Active, Position, Fees, Paid, Owed, Reg Date
- Checkbox selection column
- Aggregate footer row: Sum of FeeTotal, PaidTotal, OwedTotal (from server response)
- `queryCellInfo` event: Color-code OwedTotal (green if $0, red if > $0), Row # calculation
- Detail link on last name → opens slide-over panel
- CSS class: `tight-table` for compact density

**Filter panel — Compact Bar + Expandable "More Filters"** (legacy parity):

*Compact Bar (always visible):*
| Filter | Type | Notes |
|---|---|---|
| Name | text input | |
| Email | text input | |
| Role | `ejs-multiselect` mode=CheckBox | Count badges via itemTemplate |
| Status | `ejs-multiselect` mode=CheckBox | Active pre-selected via default |
| Pay Status | `ejs-multiselect` mode=CheckBox | Count badges |

*Action Row:*
- Search button, Clear button, "More Filters" toggle (animated arrow), Email Selected, Export

*Expandable "More Filters" (toggled via button):*
- **Text Filters**: Phone, School Name, Date From, Date To
- **Organization**: LADT checkbox tree (League/Agegroup/Division/Team hierarchy with count badges) + Club dropdown (see Phase 9b)
- **Club Roster Search**: Roster threshold dropdown (0–25) + optional Club dropdown
- **Demographics**: Gender, Position, Grad Year, Grade, Age Range (5-column grid, all `ejs-multiselect` with counts)
- **Billing & Mobile**: ARB Subscription, Mobile Registrations (both `ejs-multiselect` with counts)

*Filter Chips Strip (between filter panel and grid):*
- Shows active filter selections as removable colored chips: `Role: Player x | Status: Active x | Clear All`
- Visible when filters are active AND search results are loaded
- Chip removal triggers immediate re-search

**Count Badge Template** (Syncfusion MultiSelect itemTemplate):
```html
<ng-template #countBadgeTemplate let-data>
  <span class="filter-item">
    <span class="filter-item-text">{{ data.text }}</span>
    <span class="filter-item-count">{{ data.count }}</span>
  </span>
</ng-template>
```

**Key update helpers**:
- `updateMultiSelect(field, values)` — generic multi-select onChange handler
- `removeFilterChip(chip)` — removes specific filter value and re-searches
- `sanitizeRequest()` — converts empty arrays to undefined before API call

**Key behaviors**:
- On component init: load filter options (no auto-search — admin clicks "Search" button)
- Filter changes do NOT auto-search — admin clicks "Search" button (intentional; prevents excessive API calls during filter setup)
- Grid sorting and paging are client-side (all results returned from server, capped at 5,000)
- Last name is a clickable detail link → opens slide-over panel with registration detail
- Checkbox selection tracks IDs for batch operations
- Excel export uses current search results
- "More Filters" toggle expands/collapses additional filter categories with slide-down animation
- Filter chip removal triggers immediate re-search

### Phase 10: Frontend — Slide-Over Detail Panel Component

**Status**: [x] Complete

**Files to create**:
- `src/app/views/admin/registration-search/components/registration-detail-panel.component.ts`
- `src/app/views/admin/registration-search/components/registration-detail-panel.component.html`
- `src/app/views/admin/registration-search/components/registration-detail-panel.component.scss`

**Inputs**: `detail: RegistrationDetailDto`, `isOpen: boolean`
**Outputs**: `closed: EventEmitter<void>`, `saved: EventEmitter<void>`, `refundRequested: EventEmitter<AccountingRecordDto>`

**Tabs**:
1. **Details** — Read-first layout with contact edit zone
   - **Contact Edit Zone** (editable, top of tab): Email, Cellphone (all roles); Uniform # and family contact email/cellphone (player roles). Single "Save" button fires applicable endpoints in parallel via `forkJoin` (updateDemographics + updateFamilyContact + updateProfile).
   - **Read-Only Info** (below contact zone): Registration info, Demographics (DOB, gender, address), Family names, Player Profile metadata fields (or Non-Player fields). Uses compact label:value pairs in 2-column grid. **Only fields with values are rendered** — empty/null fields hidden. Metadata still parsed from `ProfileMetadataJson` for field labels/types, but displayed as plain text instead of form inputs. Waiver fields and always-include fields (UniformNo) excluded from read-only display (waivers skipped entirely, UniformNo in editable zone).

2. **Accounting** — Payment history table + actions
   - Bootstrap table of `AccountingRecords`
   - Columns: `#`, `Date`, `Method`, `Due$`, `Paid$`, `Comment`
   - CC rows show last-4 digits and "Refund" button (if `CanRefund`)
   - Footer: totals for Due$ and Paid$
   - Financial summary: Fees / Paid / Owed
   - "+ Add Payment Record" button → inline form or small modal
   - "Refund" button → emits `refundRequested` with the record

3. **Email** — Single-recipient email composer
   - Subject + body textarea
   - Token reference chips (clickable to insert)
   - Preview button → calls `previewEmail()` for this one registration
   - Send button → calls `sendBatchEmail()` with single registration ID

**Header actions** (in panel header, beside registration name/role):
- **Change Job** button → opens modal with dropdown of other jobs under the same customer. On submit, moves the registration to the selected job. Auto-matches team by name in the target job if possible.
- **Delete** button (danger outline) → opens confirmation modal with registrant name and role. Server enforces role-based authorization (Unassigned Adult → Superuser only) and pre-condition checks (no accounting, no store records, no insurance). On success, closes panel and refreshes grid.

**Change Job Modal**:
```
┌──────────────────────────────────────────────┐
│  Change Job                                  │
├──────────────────────────────────────────────┤
│                                              │
│  Select destination job:                     │
│  [▼ 2026 Fall Season — U14 Boys           ] │
│                                              │
│  [Cancel]              [Change Job]          │
│                                              │
└──────────────────────────────────────────────┘
```

**Delete Confirmation Modal**:
```
┌──────────────────────────────────────────────┐
│  Delete Registration                         │
├──────────────────────────────────────────────┤
│                                              │
│  Are you sure you want to permanently delete │
│  this registration for John Smith (Player)?  │
│                                              │
│  This action cannot be undone.               │
│                                              │
│  [Cancel]              [Delete Registration] │
│                                              │
└──────────────────────────────────────────────┘
```

Both modals use `position: absolute` within the detail panel (not `position: fixed`) because the panel's `transform: translateX()` creates a new stacking context that makes `position: fixed` relative to the panel rather than the viewport.

**Slide-over styling**:
```scss
.detail-panel {
  position: fixed;
  top: 0;
  right: 0;
  width: 480px;
  height: 100vh;
  background: var(--bs-body-bg);
  border-left: 1px solid var(--bs-border-color);
  box-shadow: var(--shadow-xl);
  z-index: 1050;
  transform: translateX(100%);
  transition: transform 0.3s ease;
  overflow-y: auto;

  &.open {
    transform: translateX(0);
  }
}

// Backdrop
.detail-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.3);
  z-index: 1049;
}

// Mobile: panel hidden entirely (Quick Lookup mode takes over)
@media (max-width: 767.98px) {
  .detail-panel,
  .detail-backdrop {
    display: none;
  }
}
```

### Phase 11: Frontend — Refund Modal Component

**Status**: [x] Complete

**Files to create**:
- `src/app/views/admin/registration-search/components/refund-modal.component.ts`
- `src/app/views/admin/registration-search/components/refund-modal.component.html`
- `src/app/views/admin/registration-search/components/refund-modal.component.scss`

**Inputs**: `accountingRecord: AccountingRecordDto`, `isOpen: boolean`
**Outputs**: `closed: EventEmitter<void>`, `refunded: EventEmitter<RefundResponse>`

**Features**:
- Shows original transaction details (date, amount, card last-4, transaction ID)
- Refund amount input (defaults to full amount, max = original Payamt)
- Reason text input
- Warning message about crediting cardholder
- Confirm button → calls `processRefund()`, shows loading state
- On success → emits `refunded`, shows toast, closes modal
- On failure → shows error message in modal

### Phase 12: Frontend — Batch Email Modal Component

**Status**: [x] Complete

**Files to create**:
- `src/app/views/admin/registration-search/components/batch-email-modal.component.ts`
- `src/app/views/admin/registration-search/components/batch-email-modal.component.html`
- `src/app/views/admin/registration-search/components/batch-email-modal.component.scss`

**Inputs**: `registrationIds: Guid[]`, `recipientCount: number`, `isOpen: boolean`
**Outputs**: `closed: EventEmitter<void>`, `sent: EventEmitter<BatchEmailResponse>`

**Features**:
- From address (read-only, from job configuration)
- Subject input
- Body textarea (rich enough for HTML templates with tokens)
- Token reference section — clickable chips that insert token at cursor position
- Available tokens: `!PERSON`, `!EMAIL`, `!JOBNAME`, `!AMTFEES`, `!AMTPAID`, `!AMTOWED`, `!SEASON`, `!SPORT`, `!CUSTOMERNAME`, `!F-ACCOUNTING`, `!F-PLAYERS`, `!J-CONTACTBLOCK`
- "Preview" button → renders template for first 3 recipients, shows in expandable preview section
- "Send" button → calls `sendBatchEmail()`, shows progress, then result (sent/failed counts)
- Failed addresses shown in collapsible error section

### Phase 13: Frontend — Mobile Quick Lookup Component

**Status**: [x] Complete

**Files to create**:
- `src/app/views/admin/registration-search/components/mobile-quick-lookup.component.ts`
- `src/app/views/admin/registration-search/components/mobile-quick-lookup.component.html`
- `src/app/views/admin/registration-search/components/mobile-quick-lookup.component.scss`

**Design philosophy**: This is a completely separate component, not a responsive adaptation of the desktop grid. It is shown exclusively below 768px via `@if` in the parent template, while the desktop layout (filter panel + Syncfusion grid + slide-over) is hidden. No compromises in either direction.

**Layout**:
```
┌─────────────────────────────────┐
│  Registration Lookup       [⟳] │
├─────────────────────────────────┤
│                                 │
│  🔍 [Search by name...      ]  │
│                                 │
│  Role: [▼ All]  Status: [▼ All]│
│                                 │
│  ── 12 results ──               │
│                                 │
│  ┌─────────────────────────────┐│
│  │ John Smith            ▼    ││
│  │ Player • Storm U14         ││
│  │ Owes: $250              🔴 ││
│  └─────────────────────────────┘│
│  ┌─────────────────────────────┐│
│  │ Emily Johnson          ▼   ││
│  │ Player • Thunder U12       ││
│  │ Paid up                 🟢 ││
│  └─────────────────────────────┘│
│  ┌─────────────────────────────┐│
│  │ Mike Williams          ▲   ││
│  │ Coach • —                  ││
│  │ Owes: $150              🔴 ││
│  │─────────────────────────────││
│  │ Email: mw@email.com        ││
│  │ Phone: (555) 123-4567      ││
│  │ Club:  ABC Athletics       ││
│  │ Fees:  $150                ││
│  │ Paid:  $0                  ││
│  │ Owed:  $150                ││
│  │                             ││
│  │ [📞 Call]  [✉ Email]       ││
│  └─────────────────────────────┘│
│  ...                            │
│                                 │
│  [Load more...]                 │
└─────────────────────────────────┘
```

**Component state** (signals):
```typescript
// Search
searchText = signal('');
roleFilter = signal<string | null>(null);
activeFilter = signal<boolean | null>(true);
results = signal<RegistrationSearchResultDto[]>([]);
totalCount = signal(0);
isSearching = signal(false);

// Expansion
expandedId = signal<string | null>(null);  // One card expanded at a time
expandedDetail = signal<RegistrationDetailDto | null>(null);

// Pagination
currentPage = signal(0);
readonly pageSize = 20;
```

**Features**:
- **Single search input** — searches name (first + last, contains match). Debounced 400ms to avoid excessive API calls while typing
- **Minimal filters** — just Role and Active status dropdowns, inline beside the search bar
- **Card-based results** — each result is a tappable card showing: name (bold), role + team, owes badge (green circle if paid up, red with amount if owes)
- **Tap to expand** — tapping a card expands it in-place (accordion style, one at a time) to show: email, phone, club, fee breakdown. Expansion fetches `RegistrationDetailDto` for that registration
- **Action buttons in expanded card** — "Call" (tel: link) and "Email" (mailto: link) — the actions an admin actually does on their phone
- **Load more** — infinite-scroll style pagination via "Load more" button (appends next page to results)
- **No refunds, no batch email, no Excel export, no profile editing** — these are desktop operations
- **Reuses same API endpoints** — calls `search()` with simplified parameters (name + role + active only) and `getRegistrationDetail()` for expanded card

**Styling**:
```scss
.mobile-lookup {
  padding: var(--space-3);
  max-width: 100vw;
}

.lookup-card {
  background: var(--bs-card-bg);
  border: 1px solid var(--bs-border-color);
  border-radius: var(--radius-md);
  padding: var(--space-3);
  margin-bottom: var(--space-2);
  transition: all 0.2s ease;

  &.expanded {
    border-color: var(--bs-primary);
    box-shadow: var(--shadow-md);
  }
}

.owes-badge {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  font-weight: 600;
  font-size: 0.875rem;

  &.paid-up { color: var(--bs-success); }
  &.owes { color: var(--bs-danger); }
}

.card-actions {
  display: flex;
  gap: var(--space-2);
  margin-top: var(--space-3);
  padding-top: var(--space-2);
  border-top: 1px solid var(--bs-border-color);

  a {
    flex: 1;
    text-align: center;
    padding: var(--space-2);
    border-radius: var(--radius-sm);
    background: var(--bs-secondary-bg);
    color: var(--bs-body-color);
    text-decoration: none;
    font-weight: 600;
  }
}
```

**Parent component integration** (in `registration-search.component.html`):
```html
<!-- Desktop: full grid experience (768px+) -->
@if (!isMobile()) {
  <!-- Filter panel + Syncfusion grid + slide-over panel -->
}

<!-- Mobile: quick lookup (<768px) -->
@if (isMobile()) {
  <app-mobile-quick-lookup />
}
```

**Detection**: `isMobile` signal derived from `BreakpointObserver` (Angular CDK) or `window.matchMedia('(max-width: 767.98px)')` with resize listener.

### Phase 14: Frontend — Routing

**Status**: [x] Complete

**File to modify**:
- `src/app/app.routes.ts`

**Add routes**:
```typescript
{
  path: 'admin/search',
  canActivate: [authGuard],
  data: { requirePhase2: true, requiresPolicy: 'AdminOnly' },
  loadComponent: () => import('./views/admin/registration-search/registration-search.component')
    .then(m => m.RegistrationSearchComponent)
}
// Legacy-compatible route
{
  path: 'search/index',
  canActivate: [authGuard],
  data: { requirePhase2: true, requiresPolicy: 'AdminOnly' },
  loadComponent: () => import('./views/admin/registration-search/registration-search.component')
    .then(m => m.RegistrationSearchComponent)
}
```

### Phase 15: Post-Build — API Model Regeneration

**Status**: [x] Complete

**Action**: Run `.\scripts\2-Regenerate-API-Models.ps1`
- Generates TypeScript types from DTOs
- Switch imports in frontend service from local types to `@core/api`

### Phase 16: Testing & Polish

**Status**: [ ] Pending

**Critical tests**:

**Search & Grid:**
1. **Default load**: All active registrations shown on first load, filter options populated
2. **Name search**: "Smi" matches "Smith" (contains); "John Smith" matches first+last
3. **Multi-criteria**: Name + Role + Team filters combine with AND logic
4. **Owes filter**: "Owes" shows only OwedTotal > 0; "Paid Up" shows OwedTotal <= 0
5. **Date range**: RegDateFrom/To correctly bracket RegistrationTs
6. **Client-side paging**: Page 1 shows items 1-20, page 2 shows 21-40; all data loaded from single API response (capped at 5,000)
7. **Client-side sorting**: Click "Last Name" header → grid sorts locally without API call
8. **Aggregates**: Footer row shows TotalFees/TotalPaid/TotalOwed across ALL matches (computed server-side, not just current page)
9. **Excel export**: Exports current search results to .xlsx with all visible columns
10. **Checkbox selection**: Select individual rows, select-all on current page, persist across pages
11. **Empty state**: "No registrations found" when no results match filters
12. **Large dataset**: Results capped at 5,000 rows → client-side paging keeps UI responsive; verify cap message shown if truncated

**Slide-Over Detail Panel:**
13. **Open/close**: Click row → panel slides in; click X or Escape → panel slides out
14. **Contact edit zone**: Email, Cellphone always editable; player roles also show Uniform# and family email/cellphone inputs
15. **Contact save**: Edit contact fields → Save → all applicable endpoints fire in parallel (demographics, family, profile); toast confirms; grid refreshes
16. **Read-only display**: Demographics (DOB, gender, address), family names, and player profile metadata fields shown as label:value pairs — not editable form inputs
17. **Empty field hiding**: Fields with null/empty values are not rendered; panel is compact for registrations with sparse profiles
17b. **Non-player display**: Non-player roles show populated registration info fields (ClubName, SpecialRequests, etc.) as read-only; no metadata-driven section

**Accounting:**
18. **Payment history**: All RegistrationAccounting records shown chronologically
19. **CC indicator**: Credit card payments show last-4 and "Refund" button
20. **Non-CC payments**: Check/Cash records do NOT show refund button
21. **Add record**: Creates new accounting entry, updates registration PaidTotal/OwedTotal
22. **Financial summary**: Fees/Paid/Owed shown and updated after any accounting change

**Add Payment (CC Charge):**
23. **CC modal opens**: Click "+ Add Payment Record" → modal opens with "Credit Card" tab selected
24. **CC pre-fill**: First/Last name from registration, email/phone from family contact (or demographics fallback)
25. **CC amount validation**: Amount must be > 0 and <= OwedTotal; submit disabled otherwise
26. **CC required fields**: Card number, expiry, CVV, first name, last name required; submit disabled until filled
27. **CC charge success**: Valid card → ADN_Charge succeeds → accounting record created with TransactionId, OwedTotal decremented, toast shown, modal closes, grid refreshes
28. **CC charge failure**: Invalid card or gateway error → error toast with ADN message, no accounting record
29. **CC charge exceeds owed**: Amount > OwedTotal → "MORE THAN IS OWED" error returned

**Add Payment (Check / Correction):**
30. **Check recording**: Select "Check" tab → enter amount + check# → Submit → accounting record with Check method, financial update
31. **Check amount validation**: Amount must be > 0 for checks
32. **Correction positive**: Correction with positive amount → reduces OwedTotal (credit/scholarship)
33. **Correction negative**: Correction with negative amount → increases OwedTotal (penalty/adjustment)
34. **Correction zero blocked**: Amount = 0 → "cannot be zero" error
35. **Comment persistence**: Optional comment saved on accounting record for all payment types

**Edit Accounting Record:**
36. **Edit button visibility**: "Edit" button shown on Check/Correction/Cash rows only; NOT on CC payment rows
37. **Inline edit mode**: Click "Edit" → row switches to inline inputs for Comment and Check#
38. **Save edit**: Modify comment → Save → record updated, grid refreshes, toast confirms
39. **Cancel edit**: Click "Cancel" → row reverts to read-only display without saving
40. **Job ownership validation**: Cannot edit accounting record from a different job (backend 400)

**Refunds & Voids:**
41. **Refund settled transaction**: CC payment with `settledSuccessfully` status → ADN_Refund called → negative accounting record created with CcCreditMethodId
42. **Void unsettled transaction**: CC payment with `capturedPendingSettlement` status → ADN_Void called → original record marked "VOIDED", Payamt zeroed, full amount reversed
43. **Partial refund**: Refund amount < original → partial refund processed on settled transaction
44. **Full refund**: Refund amount = original → full refund processed
45. **Unsupported status**: Transaction with status other than settled/pending → error message "does not support refund/void"
46. **Transaction lookup failure**: ADN_GetTransactionDetails fails → "Could not look up" error, no financial changes
47. **Refund accounting**: Refund creates negative Payamt record; void marks original with "VOIDED" suffix
48. **Financial update**: PaidTotal decremented by reversed amount, OwedTotal recalculated
49. **Error handling**: ADN gateway failure → error toast, no accounting record created

**Batch Email:**
30. **Recipient count**: Shows correct count of selected registrations
31. **Token insertion**: Clicking token chip inserts token at cursor position in body
32. **Preview**: Renders template for first 3 recipients with actual values substituted
33. **Send**: Sends to all selected registrations, shows sent/failed counts
34. **Failed addresses**: Displayed in collapsible section after send
35. **Empty selection**: "Email Selected" button disabled when no checkboxes selected

**Mobile Quick Lookup (< 768px):**
36. **Mode switch**: Below 768px, desktop UI (grid + filter panel + slide-over) is hidden; mobile quick lookup shown instead
37. **Name search**: Typing "Smi" after debounce returns matching registrations as cards
38. **Card display**: Each card shows name, role, team, owes badge (green/red)
39. **Tap to expand**: Tapping a card expands it to show email, phone, club, fee breakdown; previous card collapses
40. **Call/Email actions**: "Call" link opens phone dialer; "Email" link opens mail client
41. **Load more**: Tapping "Load more" appends next page of results
42. **No desktop features on mobile**: No refund, no batch email, no Excel export, no profile editing on mobile
43. **Resize behavior**: Resizing window above 768px switches to full desktop UI; below switches back to mobile

**Change Job:**
44. **Modal loads options**: "Change Job" button fetches other jobs under same customer; dropdown populated
45. **Job transfer**: Selecting a job and confirming moves the registration; grid refreshes with updated data
46. **Team matching**: If source team name exists in target job, registration's team auto-assigned; otherwise team cleared
47. **Cross-customer blocked**: Cannot move registration to a job under a different customer

**Delete Registration:**
48. **Player/Staff deletion**: Director/SuperDirector/Superuser can delete Player or Staff registrations
49. **Unassigned Adult restriction**: Only Superuser can delete Unassigned Adult registrations; Director gets error
50. **Accounting records block**: Registration with RegistrationAccounting records → delete blocked with clear message
51. **Store records block**: Registration with StoreCartBatchSkus (DirectToRegId) → delete blocked
52. **Insurance block**: Registration with RegsaverPolicyId → delete blocked with "active insurance policy" message
53. **Clean registration deletes**: Registration with no accounting, store, or insurance records → deletes successfully
54. **Device cleanup**: DeviceRegistrationIds removed before registration entity deleted
55. **Confirmation modal**: Shows registrant name and role; requires explicit confirmation click
56. **Post-delete behavior**: Panel closes, grid refreshes, success toast shown

**General:**
57. **All 8 palettes**: CSS variable themed throughout (grid, panel, modals, mobile cards)
58. **Authorization**: Non-admin users get 403 on all endpoints
59. **Job scoping**: All queries scoped to JWT-derived jobId; cannot access other jobs' registrations
60. **Error handling**: Network errors show toast with context-specific message

---

## 10. Files Summary

### Backend Files

| File | Action | LOC (est.) |
|------|--------|------------|
| `TSIC.Contracts/Dtos/RegistrationSearch/RegistrationSearchDtos.cs` | Create | ~80 |
| `TSIC.Contracts/Dtos/RegistrationSearch/AccountingDtos.cs` | Create | ~70 |
| `TSIC.Contracts/Dtos/RegistrationSearch/RegistrationDetailDtos.cs` | Create | ~140 |
| `TSIC.Contracts/Repositories/IRegistrationRepository.cs` | Edit (add 7 methods) | +40 |
| `TSIC.Infrastructure/Repositories/RegistrationRepository.cs` | Edit (implement) | +220 |
| `TSIC.Contracts/Repositories/IJobRepository.cs` | Edit (add 1 method) | +5 |
| `TSIC.Infrastructure/Repositories/JobRepository.cs` | Edit (implement) | +15 |
| `TSIC.Contracts/Repositories/IRegistrationAccountingRepository.cs` | Edit (add 3 methods) | +15 |
| `TSIC.Infrastructure/Repositories/RegistrationAccountingRepository.cs` | Edit (implement) | +60 |
| `TSIC.Contracts/Services/IRegistrationSearchService.cs` | Create | ~25 |
| `TSIC.API/Services/Admin/RegistrationSearchService.cs` | Create | ~550 |
| `TSIC.API/Controllers/RegistrationSearchController.cs` | Create | ~190 |
| `TSIC.Contracts/Extensions/StringExtensions.cs` | Create | ~15 |
| `TSIC.API/Program.cs` | Edit (1 DI line) | +1 |

### Frontend Files

| File | Action | LOC (est.) |
|------|--------|------------|
| `views/admin/registration-search/components/ladt-tree-filter.component.ts` | Create | ~445 |
| `views/admin/registration-search/services/registration-search.service.ts` | Create | ~125 |
| `views/admin/registration-search/registration-search.component.ts` | Create | ~300 |
| `views/admin/registration-search/registration-search.component.html` | Create | ~280 |
| `views/admin/registration-search/registration-search.component.scss` | Create | ~150 |
| `views/admin/registration-search/components/registration-detail-panel.component.ts` | Create | ~410 |
| `views/admin/registration-search/components/registration-detail-panel.component.html` | Create | ~420 |
| `views/admin/registration-search/components/registration-detail-panel.component.scss` | Create | ~200 |
| `views/admin/registration-search/components/refund-modal.component.ts` | Create | ~120 |
| `views/admin/registration-search/components/refund-modal.component.html` | Create | ~80 |
| `views/admin/registration-search/components/refund-modal.component.scss` | Create | ~40 |
| `views/admin/registration-search/components/batch-email-modal.component.ts` | Create | ~180 |
| `views/admin/registration-search/components/batch-email-modal.component.html` | Create | ~120 |
| `views/admin/registration-search/components/batch-email-modal.component.scss` | Create | ~60 |
| `views/admin/registration-search/components/mobile-quick-lookup.component.ts` | Create | ~150 |
| `views/admin/registration-search/components/mobile-quick-lookup.component.html` | Create | ~100 |
| `views/admin/registration-search/components/mobile-quick-lookup.component.scss` | Create | ~80 |
| `app.routes.ts` | Edit (2 routes) | +12 |
| `core/api/models/` (auto-generated) | Auto | ~12 files |

---

## 11. Key Design Decisions

1. **Syncfusion Grid over Bootstrap table** — the data volume (hundreds to thousands of registrations per job), the need for server-side paging, multi-column sorting, Excel export, aggregate footer rows, and checkbox selection all justify Syncfusion's full grid. Bootstrap tables (used in admin management, discount codes, etc.) work for small datasets but would require rebuilding all these features manually. Syncfusion is already licensed, themed, and proven in the team-registration-wizard.

2. **Custom filter panel above grid (not Syncfusion's built-in filter bar)** — admins need to set multiple criteria before searching, not filter column-by-column. A dedicated filter panel with dropdowns, text inputs, and date pickers is more intuitive than Syncfusion's per-column filter bar. The grid's built-in sorting is still used for column header clicks.

3. **Client-side paging and sorting (revised from server-side)** — originally planned as server-side paging, but switched to client-side after implementation. The backend returns ALL matching results (capped at 5,000 rows) in a single response, and Syncfusion's built-in grid paging/sorting handles pagination and column sorting locally. This avoids repeated round-trips for every page change or sort click, keeps aggregate calculations simple (computed once server-side across the full result set), and leverages Syncfusion's instant client-side sort/page performance. The 5,000-row cap prevents memory issues for extremely large jobs while covering 99%+ of real-world usage.

4. **Aggregates computed server-side across ALL matches** — the footer row shows total Fees/Paid/Owed across the entire filtered result set, not just the current page. This is critical for financial oversight. Computing on the server via SQL aggregate functions (SUM) is orders of magnitude faster than client-side aggregation of all pages.

5. **Slide-over panel instead of separate page** — preserving search context is the #1 UX improvement. The admin can click through multiple registrations without losing filters, scroll position, or mental context. The 480px panel width leaves the grid visible underneath. On mobile, the panel goes full-width (expected, since the grid would be unusable at that viewport anyway).

6. **Read-first detail panel with contact edit zone** — the detail panel is a lookup tool, not an edit form. Admins almost never update registration details — the panel is opened to check info (team, contact, payment status). The redesigned Details tab has a compact **Contact Edit Zone** at the top (email, cellphone, uniform#, family contacts) with a single Save button, and **read-only label:value pairs** for everything else (demographics, profile metadata, registration info). Only fields with values are rendered — empty fields hidden. Metadata from `PlayerProfileMetadataJson` is still parsed for field labels but displayed as plain text instead of form inputs. This dramatically reduces visual noise and makes the primary lookup task faster while keeping the rarely-used edit capability prominent for the fields that actually get updated.

7. **Refund via existing ADN_Refund gateway method** — the `ADN_Refund()` method exists and is tested but has never been called from application code. This migration plan activates it. The refund creates a negative accounting record (matching the existing accounting pattern) and updates registration financials. The original transaction's `AdnTransactionId`, `AdnCc4`, and `AdnCcexpDate` are already stored in `RegistrationAccounting` — all data needed for the refund is available.

8. **Batch email reuses existing TextSubstitutionService** — the token substitution system is comprehensive (25+ tokens including complex HTML tables like `!F-ACCOUNTING`). Rather than building a new email system, we compose templates using the same tokens and render via the existing service. The preview feature lets admins see exactly what recipients will receive before sending.

9. **POST for search endpoint** — search criteria can include multiple optional fields, date ranges, and sort parameters. Using POST with a request body is cleaner than encoding all this as query string parameters. This is a common pattern for complex search/filter APIs.

10. **Search button (not auto-search on filter change)** — admins often set 3-4 filters before searching. Auto-searching on every filter change would fire 3-4 unnecessary API calls and create a janky experience as results shift mid-configuration. The explicit "Search" button lets admins compose their filter criteria completely, then execute once.

11. **Accounting creation separate from refund** — admins need to create accounting records for non-CC payments (check received, cash collected, manual adjustments). This is a different workflow from refunds (which are CC-specific and gateway-integrated). Keeping them as separate actions with distinct UIs prevents confusion.

12. **ProfileValues as Dictionary<string, string?>** — the registration entity has 40+ profile columns with mixed types (string, int, decimal, DateTime, bool). Sending them as a flat dictionary with string values (with type conversion on the server via metadata's `inputType`) keeps the DTO simple and the frontend form generic. The server validates and converts each value based on the metadata schema before writing to the entity.

13. **Desktop/tablet-first with dedicated mobile quick lookup** — this is fundamentally a power-user desktop interface. Processing refunds, editing 40-field forms, composing batch emails, and scanning 10-column financial grids is desk work. Rather than degrading the desktop experience to chase responsive parity (horizontal-scrolling grids, full-width panels that lose context, stacked filter inputs requiring endless scrolling), we build two intentionally different experiences: (a) the full desktop UI at 768px+ with zero mobile compromises, and (b) a purpose-built mobile quick lookup at < 768px optimized for the one thing an admin does on their phone — "which team is this kid on?" / "does this person owe money?" The mobile mode uses the same API endpoints with simplified parameters, so there's no backend duplication. This pattern — separate mobile mode instead of responsive degradation — should be the standard for data-heavy admin tools going forward.

14. **Multi-select filters with count badges (legacy parity, pre-LADT tree)** — the legacy system used checkbox lists with purple count badges showing how many registrations matched each filter option (e.g., "Player (347)", "Active (892)"). The modern implementation uses Syncfusion `ejs-multiselect` with `mode="CheckBox"` and custom `itemTemplate` for count badge pills. All 14 filter categories support multi-select with registration counts computed via parallel `Task.WhenAll` GroupBy queries in the repository. The compact bar shows the 5 most-used filters (Name, Email, Role, Status, Pay Status) always visible, with 13 additional filters in an expandable "More Filters" section. Active filter selections appear as removable chips between the filter panel and grid, giving admins at-a-glance visibility of their current filter criteria. This matches the legacy system's accordion-category checkbox approach while providing a more modern, compact layout.

15. **LADT tree filter over flat dropdowns for Organization** — the legacy system used a single hierarchical LADT checkbox tree for filtering by League/Agegroup/Division/Team, which maintains relationships and shows count context at every level. The flat multi-select dropdowns (Team, Agegroup, Division) in the initial implementation lost this relationship context — you couldn't see which teams belonged to which divisions. The LADT tree restores the legacy hierarchy with modern tri-state checkboxes, expand/collapse, and pill count badges. Club remains a separate dropdown (clubs aren't part of the LADT hierarchy). Backend filter logic changed from AND to OR across LADT levels: if you check agegroup "2027" AND a division in "2028", the results are the union (not intersection). The frontend emits IDs at ALL descendant levels so the backend OR filter catches registrations regardless of which level they're assigned at (`AssignedTeamId` vs `AssignedAgegroupId` vs `AssignedDivId`).

16. **Club roster threshold search (legacy feature restoration)** — the legacy system had a "Search for Club Reps With Teams With Roster Counts <=" feature that was heavily used by directors to find under-rostered clubs. This is a cross-entity correlated subquery: find Teams where `COUNT(active registrations) <= threshold`, then find the club rep registration (`Teams.ClubrepRegistrationid`) for those teams. Optional club name filter narrows to a specific club. The threshold dropdown offers common values (0, 1, 2, 3, 5, 10, 15, 20, 25) rather than a free-text input, because directors typically check for specific roster minimums.

---

## 12. Amendments Log

| # | Change | Reason |
|---|--------|--------|
| 1 | Added dedicated Mobile Quick Lookup mode (Phase 13) | This interface is fundamentally a desktop power-user tool. Rather than degrading the desktop experience with responsive compromises (horizontal-scrolling grids, full-width panels that lose the context-preservation benefit, 8+ stacked filter inputs), we build a separate purpose-built mobile experience: single search input, card-based results with owes badges, tap-to-expand detail with Call/Email actions. No refunds, no batch email, no profile editing on mobile — those are desk work. Desktop UI hidden below 768px; mobile UI hidden above 768px. Slide-over panel removed from mobile entirely. New files: `mobile-quick-lookup.component.{ts,html,scss}` (~330 LOC). New UI standard established: "Mobile Quick Lookup Mode" pattern for data-heavy admin tools. Added design decision #13. Updated test cases 36-47. |
| 2 | Switched from server-side to client-side paging | Originally planned server-side paging via Syncfusion `DataManager` with `skip`/`take`/`sortField`/`sortDirection` parameters. After implementation, switched to client-side paging: backend returns ALL matching results (capped at 5,000), Syncfusion grid handles paging and sorting locally. Eliminates repeated API round-trips for every page change or sort click. Aggregates (TotalFees/TotalPaid/TotalOwed) computed once server-side across full result set. Removed `Skip`, `Take`, `SortField`, `SortDirection` from `RegistrationSearchRequest`. Updated design decision #3. |
| 3 | Multi-select filters with count badges — full legacy parity refactor | Replaced all single-select `<select>` dropdowns with Syncfusion `ejs-multiselect` (mode=CheckBox) with count badges. All 14 filter categories now support multi-select. Backend: `GetFilterOptionsAsync` rewritten with parallel `Task.WhenAll` GroupBy count queries; `SearchAsync` updated from single-value equality to multi-value `.Contains()` predicates. Frontend: compact bar (5 most-used filters always visible) + expandable "More Filters" (13 additional filters in categorized sections) + filter chips strip showing active selections with remove capability. Added `FilterOption.Count` and `FilterOption.DefaultChecked` properties. Active Status defaults to "Active" pre-checked. COVID waiver filter intentionally excluded. Added design decision #14. |
| 4 | LADT tree filter replaces flat Organization dropdowns | Replaced the 4 flat multi-select dropdowns (Team, Agegroup, Division, Club) in the "Organization" filter section with a hierarchical LADT checkbox tree + standalone Club dropdown. The tree shows League > Agegroup > Division > Team with tri-state checkboxes, expand/collapse, count badges (teams in blue, players in primary), and special node handling (Dropped Teams, WAITLIST at bottom). Backend: Team/Agegroup/Division filters changed from AND to OR logic — tree selections at different levels are unioned. Frontend classifies checked nodes at ALL descendant levels (not just highest ancestor) so the OR filter catches registrations assigned at any level. New file: `ladt-tree-filter.component.ts` (~445 LOC). Updated Phase 9 Organization section, added Phase 8b. Added design decision #15. |
| 5 | Club roster threshold search | Added "Club Roster Search" section to More Filters. Helps directors find club reps whose teams are under-rostered. Backend: new `RosterThreshold` and `RosterThresholdClub` fields on `RegistrationSearchRequest`; subquery finds club rep registrations whose teams have `COUNT(active players) <= threshold`, optionally filtered by club name. Frontend: two dropdowns — roster count threshold (0–25) and optional club filter. Filter chips and clear-filters include these new fields. |
| 6 | Syncfusion dark/light theme fix | Removed `@import '@syncfusion/ej2-base/styles/material.css'` and `@import '@syncfusion/ej2-grids/styles/material.css'` from `registration-search.component.scss`. These Material theme imports were overriding the global Bootstrap5 theme (`bootstrap5-lite.css`) which supports dark mode via `e-dark-mode` class. Removing them fixed dark mode and reduced the lazy chunk from 1.65 MB to 311 KB. |
| 7 | Dirty-state "Update Results" button fixes | (a) Added `[changeOnBlur]="false"` to all 14 Syncfusion multi-select dropdowns so the dirty-state button activates immediately when a checkbox is toggled in the dropdown, not when the dropdown closes. (b) Set `lastSearchedRequest` baseline after default filter options load, so filter changes are detected as dirty even before the first search. (c) Button text reads "Update Results" when dirty + results exist, "Search" otherwise. |
| 8 | CellPhone column fix + phone formatting | Fixed blank CellPhone column: backend projection was mapping `r.User.PhoneNumber` (empty ASP.NET Identity field) instead of `r.User.Cellphone`. Applied in both search results and detail endpoint. Created reusable `StringExtensions.FormatPhone()` extension method that formats 10-digit numbers as `xxx-xxx-xxxx`. New file: `TSIC.Contracts/Extensions/StringExtensions.cs`. |
| 9 | Syncfusion sorted header dark-mode readability | Clicking a grid column header to sort made the header text nearly invisible in dark mode (`rgb(33,37,41)` on dark background). Syncfusion applies `e-focus` and `e-mousepointer` classes on sortable/sorted header cells with hardcoded dark text color. Added comprehensive CSS overrides in `_syncfusion.scss` targeting `.e-headercell.e-ascending`, `.e-descending`, `.e-focus`, `.e-focused`, `.e-mousepointer` — forcing `color: var(--bs-body-color)` on `.e-headercelldiv`, `.e-headertext`, and sort icons (`e-icon-ascending`/`e-icon-descending`) to `var(--bs-primary)`. |
| 10 | LADT tree loading spinner | Added `ladtTreeLoading` signal (starts `true`, set `false` on API response). Template wraps tree in `@if (ladtTreeLoading())` showing a Bootstrap spinner with "Loading tree..." text until the `/api/ladt/tree` response arrives. Prevents empty tree container flash on route load. New SCSS: `.tree-loading` flex layout. |
| 11 | Demographics section rework | Removed gender field from the Demographics section (not useful for admin workflows). Kept DOB (editable). Added read-only email and cellphone display from `RegistrationDetailDto` (top-level `email`/`phone` properties from `AspNetUsers`). New 3-column `.demo-info-row` grid layout with `.form-value` class for read-only text display. |
| 12 | Dynamic parent/guardian labels from Jobs entity | Replaced hardcoded "Mother / Guardian 1" and "Father / Guardian 2" family contact headings with dynamic labels from `Jobs.MomLabel` and `Jobs.DadLabel`. Backend: added `MomLabel` (default "Mom") and `DadLabel` (default "Dad") to `RegistrationDetailDto`; populated from `reg.Job.MomLabel`/`reg.Job.DadLabel` in `RegistrationRepository.GetRegistrationDetailAsync()` with fallback when blank. Frontend: headings use `{{ reg.momLabel || 'Mom' }}` / `{{ reg.dadLabel || 'Dad' }}`. API models regenerated. |
| 13 | Change Job feature | Added ability to move a registration from one job to another under the same customer. Backend: `JobOptionDto`, `ChangeJobRequest`, `ChangeJobResponse` DTOs; `IJobRepository.GetOtherJobsForCustomerAsync()` for dropdown options; `IRegistrationRepository.FindMatchingRegistrationTeamAsync()` for automatic team name matching in target job; `ChangeJobAsync()` service method with job-ownership validation; `GET change-job-options` and `POST {id}/change-job` controller endpoints. Frontend: "Change Job" button in detail panel header; modal with job dropdown fetched on open; loading spinner; success toast with new job name; emits `saved` to refresh grid. SCSS: reusable `.change-job-modal` styles with `position: absolute` (not `fixed`) to work within the transformed detail panel. API models regenerated (JobOptionDto, ChangeJobRequest, ChangeJobResponse). |
| 14 | Delete Registration feature | Added role-specific registration deletion with strict pre-conditions. Backend: `DeleteRegistrationResponse` DTO; `IRegistrationRepository.HasAccountingRecordsAsync()`, `HasStoreCartBatchRecordsAsync()`, `GetRegistrationRoleNameAsync()` for pre-condition checks and role lookup; `DeleteRegistrationAsync()` service method with role-based authorization (Player/Staff → Director+, Unassigned Adult → Superuser only), pre-condition enforcement (no accounting records, no store records, no insurance), device cleanup via `IDeviceRepository`, and entity deletion; `DELETE {id}` controller endpoint extracting `callerRole` from JWT `ClaimTypes.Role`. Frontend: "Delete" danger-outline button in detail panel header; confirmation modal with registrant name and warning; loading spinner; success toast; emits `closed` + `saved` to close panel and refresh grid. Server returns human-readable error messages for all rejection cases (role insufficient, accounting records exist, etc.). Added security documentation for role-based delete authorization table. |
| 15 | Detail panel read-first redesign | Redesigned the Details tab from an all-editable form layout (3 sections × 3 save buttons) to a read-first info panel with a compact contact edit zone. Rationale: admins almost never update registration details — the panel is primarily a lookup tool. **Contact Edit Zone** (top, editable): Email + Cellphone (all roles), Uniform # (players, from ALWAYS_INCLUDE_FIELDS), family email + cellphone (players with family link). Single "Save" button fires applicable endpoints in parallel via `forkJoin` (updateDemographics, updateFamilyContact, updateProfile). **Read-Only Info** (below, label:value pairs in 2-column grid): Registration info, Demographics (DOB, gender, address formatted as single line), Family names (read-only — names rarely change), Player Profile metadata fields (excluding waivers and always-include fields), Non-Player fields (ClubName, etc.). Only fields with values rendered — empty/null fields hidden entirely. TypeScript: replaced `isSaving`/`isSavingFamily`/`isSavingDemographics` with single `isSavingContact`; added `readOnlyMetadataFields` computed signal, `hasValue()`, `formatAddress()`, `formatCheckboxValue()`, `familyName()`, `nonPlayerFields()` helpers. SCSS: added `.contact-zone`, `.contact-form`, `.family-contact-row/col`, `.info-section`, `.info-section-title` styles; removed `.demographics-form`, `.non-player-form`, `.family-form`, `.detail-section` styles. Updated ASCII diagram, Phase 10 description, Design Decision #6, UI Standard (Dynamic Metadata-Driven Form → Read-Only Display), test cases 13–17. |

| 16 | Phase 2: Check & Correction payment backend | Added `RecordCheckOrCorrectionAsync` service method. Validates PaymentType ("Check"/"Correction"), resolves payment method GUID from static constants (`CheckMethodId`, `CorrectionMethodId`), creates `RegistrationAccounting` record, updates registration `PaidTotal`/`OwedTotal`. New DTOs: `RegistrationCheckOrCorrectionRequest`, `RegistrationCheckOrCorrectionResponse`. New endpoint: `POST /{registrationId}/record-payment`. |
| 17 | Phase 2: CC Charge backend | Added `ChargeCcAsync` service method. Validates amount <= OwedTotal, builds invoice number using `GetRegistrationWithInvoiceDataAsync` (max 20 chars with fallbacks), creates incomplete RA record, calls `_adnApi.ADN_Charge()`, updates on success or marks inactive on failure. New DTOs: `RegistrationCcChargeRequest` (uses existing `CreditCardInfo`), `RegistrationCcChargeResponse`. New endpoint: `POST /{registrationId}/charge-cc`. |
| 18 | Phase 2: Edit Accounting Record backend | Added `EditAccountingRecordAsync` service method. Loads RA by AId, validates job ownership, updates only `Comment` and `CheckNo` fields. New DTO: `EditAccountingRecordRequest`. New endpoint: `PUT /accounting/{aId}`. |
| 19 | Phase 2: Subscription View & Cancel backend | Added `GetSubscriptionDetailAsync` (loads `AdnSubscriptionId`, calls ADN `GetSubscriptionDetails`, maps to DTO) and `CancelSubscriptionAsync` (calls `ADN_CancelSubscription`, sets `AdnSubscriptionStatus = "canceled"`). New DTO: `SubscriptionDetailDto`. New endpoints: `GET /{registrationId}/subscription`, `POST /{registrationId}/cancel-subscription`. All admin roles can cancel (no Superuser restriction). |
| 20 | Phase 2: Enhance Refund with Void logic | Modified `ProcessRefundAsync` to check transaction status before refunding. Uses `ADN_GetTransactionDetails` to determine: `capturedPendingSettlement` → calls `ADN_Void` (full reversal, marks original record as "VOIDED"), `settledSuccessfully` → calls `ADN_Refund` (partial/full, creates negative RA record with `CcCreditMethodId`), other status → returns error. Matches `TeamSearchService.ProcessRefundAsync` pattern. |
| 21 | Phase 2: Add Payment Modal frontend | New `AddPaymentModalComponent` with 3-mode type selector (Credit Card / Check / Correction). CC mode: card number, expiry, CVV, name, address, ZIP, email, phone fields with formatting validators; pre-fills from registration's name and family contact/demographics. Check mode: check number field. Common: amount (pre-filled from OwedTotal), comment. CC calls `chargeCc()`, Check/Correction calls `recordPayment()`. Button activated in detail panel (removed "Coming soon"). Parent refreshes via `(saved)` → `onDetailSaved()`. New files: `add-payment-modal.component.{ts,html,scss}`. |
| 22 | Phase 2: Edit Accounting Record inline | Added inline edit capability to accounting records table. Edit/Save/Cancel buttons on Check/Correction/Cash rows. `isEditable()` method checks payment method string. `startEditRecord()` populates `editComment`/`editCheckNo` signals; `saveEditRecord()` calls `editAccountingRecord()` service method. SCSS: `.editing-row`, `.inline-edit`, `.action-cell`, `.edit-actions` styles. |
| 23 | Phase 2: ARB Subscription Panel frontend | Added subscription section to Accounting tab (visible when `hasSubscription` is true). Backend: added `HasSubscription` boolean to `RegistrationDetailDto`, populated from `!string.IsNullOrWhiteSpace(reg.AdnSubscriptionId)` in repository. Frontend: loads subscription details lazily when Accounting tab activated; displays status badge, amount/interval, schedule, start date, total; Cancel button (with confirmation spinner) calls `cancelSubscription()`. SCSS: `.subscription-section`, `.subscription-details` with `.detail-row` layout. |
| 24 | Subscription ADN credential fix + logging | Fixed "No subscription details available" for players with ARB subscriptions. Root cause: dev environment uses ADN sandbox credentials, but subscription IDs in the database are from production ADN. Added `bProdOnly: true` to both `GetJobAdnCredentials_FromJobId()` and `GetADNEnvironment()` calls in `GetSubscriptionDetailAsync` and `CancelSubscriptionAsync`. Also enhanced error logging: explicit warnings for null ADN response, non-Ok result code (with ADN error text), and null subscription object — replacing the silent catch-all that returned null with no diagnostic info. |
| 25 | Confirmation guards on all destructive actions | Audited all 8 actions in registration search for confirmation guards. Found 3 critical gaps (CC Charge, Refund, Batch Email) — all irreversible operations that lacked a confirm step. **CC Charge** (`add-payment-modal`): `submit()` now shows inline confirmation alert for CC payments (displays amount + card last 4); Check/Correction submits directly (low risk, reversible). **Refund** (`refund-modal`): renamed `processRefund()` → `requestRefund()` (validates then shows confirm) + `confirmRefund()` (executes); confirmation shows refund amount + card last 4. **Batch Email** (`batch-email-modal`): `sendEmail()` now validates then shows warning-level confirm with recipient count; `confirmSend()` executes. **Cancel Subscription** (detail panel): added `showCancelSubConfirm` signal with inline danger alert showing subscription amount/interval and "Yes, Cancel Subscription" / "No, Keep Active" buttons. All guards follow the same pattern: `showConfirm` signal → trigger method shows confirm → confirm method executes → dismiss method hides. Submit/Send buttons hidden while confirm is visible. |

---

**Status**: Implementation in progress. Phases 1–25 complete (including 8b). Phase 2 Accounting Management (amendments #16–#25) fully implemented end-to-end. Phase 26 (Testing & Polish) pending.
