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
│  ┌─ Filter Bar ────────────────────────────────────────────────────────────┐│
│  │ Name: [          ]  Email: [          ]  Role: [▼ All    ]              ││
│  │ Team: [▼ All     ]  Agegroup: [▼ All  ]  Division: [▼ All ]            ││
│  │ Club: [▼ All     ]  Status: [▼ Active ]  Owes$: [▼ Any   ]            ││
│  │ Reg Date: [From     ] → [To       ]                                     ││
│  │                                                                          ││
│  │ [🔍 Search]  [Clear]                   [📧 Email Selected] [📥 Export] ││
│  └──────────────────────────────────────────────────────────────────────────┘│
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

When a row is clicked → Slide-over detail panel (right side, 480px):

┌──────────────────────────────────────────────────────────┐
│  John Smith                                        [✕]   │
│  Player • Storm U14 • Active                             │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  [Details]  [Accounting]  [Email]                        │
│  ─────────────────────────────────────────                │
│                                                          │
│  ── Details Tab ──                                       │
│  (Dynamic form fields from PlayerProfileMetadataJson)    │
│                                                          │
│  First Name:      [John          ]                       │
│  Last Name:       [Smith         ]                       │
│  Email:           [j@email.com   ]                       │
│  Grad Year:       [2028          ]                       │
│  Position:        [▼ Attack      ]                       │
│  Jersey Size:     [▼ L           ]                       │
│  Height:          [5'10"         ]                       │
│  ...                                                     │
│  (fields vary by job's PlayerProfileMetadataJson)        │
│                                                          │
│  [Save Changes]                                          │
│                                                          │
│  ── Accounting Tab ──                                    │
│  ┌───────────────────────────────────────────┐           │
│  │ # │ Date     │ Method    │ Due$  │ Paid$ │           │
│  │ 1 │ 2/1/26   │ CC ••4242 │$500   │$250   │           │
│  │ 2 │ 2/15/26  │ CC ••4242 │$0     │$250   │           │
│  │   │          │ Totals:   │$500   │$500   │           │
│  └───────────────────────────────────────────┘           │
│                                                          │
│  Fees: $500  Paid: $500  Owed: $0                        │
│                                                          │
│  ┌─ Actions ───────────────────────────────┐             │
│  │ [+ Add Payment Record]                  │             │
│  │ [↩ Credit/Refund CC Payment]            │             │
│  └─────────────────────────────────────────┘             │
│                                                          │
│  ── Email Tab ──                                         │
│  Subject: [                          ]                   │
│  Body:                                                   │
│  ┌───────────────────────────────────────┐               │
│  │ Dear !PERSON,                        │               │
│  │                                       │               │
│  │ Your balance of !AMTOWED is due...   │               │
│  └───────────────────────────────────────┘               │
│  Available tokens: !PERSON !EMAIL !AMTOWED...            │
│  [Preview] [Send]                                        │
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
| **Server-side paging** | Built-in `DataManager` with server-side operations |
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

- **10x faster workflows**: Server-side paging handles 10,000+ registrations without browser lag
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

- **Server-Side Paged Syncfusion Grid** — first use of Syncfusion's `DataManager` with custom server-side adapter. The adapter sends sort/filter/page parameters to the API and receives `{ result: T[], count: number }`. This pattern establishes the standard for all future data-heavy grids.

- **Inline Refund Workflow** — modal triggered from accounting tab, pre-populated with transaction details, supports partial/full refund amount, processes via `ADN_Refund()`, creates negative accounting record, updates registration financials.

- **Dynamic Metadata-Driven Form** — registration detail form assembled at runtime from `PlayerProfileMetadataJson`. Each field renders based on `inputType` (TEXT, SELECT, DATE, CHECKBOX, etc.), respects `visibility` (public/adminOnly/hidden), and applies `validation` rules. This pattern is reusable for any future metadata-driven form.

- **Batch Email Composer with Token Reference** — modal with template editor, clickable token insertion, preview rendering (substitutes tokens for first N recipients to show admin what the email will look like), and batch send with progress/result feedback.

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

**Status**: [ ] Pending

**File to create**:
- `TSIC.Contracts/Dtos/RegistrationSearch/RegistrationSearchDtos.cs`

**DTOs**:
```csharp
// ── Search request (sent from grid) ──
public record RegistrationSearchRequest
{
    // Text filters
    public string? Name { get; init; }              // Searches FirstName + LastName (contains)
    public string? Email { get; init; }             // Contains match

    // Dropdown filters
    public string? RoleId { get; init; }            // Exact match on AspNetRoles.Id
    public Guid? TeamId { get; init; }              // Exact match
    public Guid? AgegroupId { get; init; }          // Exact match
    public Guid? DivisionId { get; init; }          // Exact match
    public string? ClubName { get; init; }          // Contains match on Registrations.ClubName

    // Status filters
    public bool? Active { get; init; }              // null=all, true=active, false=inactive
    public string? OwesFilter { get; init; }        // "any", "owes" (OwedTotal>0), "paid" (OwedTotal<=0)

    // Date range
    public DateTime? RegDateFrom { get; init; }
    public DateTime? RegDateTo { get; init; }

    // Paging & sorting
    public int Skip { get; init; }                  // Offset (0-based)
    public int Take { get; init; } = 20;            // Page size (default 20)
    public string? SortField { get; init; }         // Column to sort by
    public string? SortDirection { get; init; }     // "asc" or "desc"
}

// ── Search result row ──
public record RegistrationSearchResultDto
{
    public required Guid RegistrationId { get; init; }
    public required int RegistrationAi { get; init; }     // Display ID (#)

    // Person (from AspNetUsers join)
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }

    // Registration context
    public required string RoleName { get; init; }
    public required bool Active { get; init; }
    public string? TeamName { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivisionName { get; init; }
    public string? ClubName { get; init; }

    // Financials
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }

    // Dates
    public required DateTime RegistrationTs { get; init; }
    public DateTime? Modified { get; init; }
}

// ── Paged response wrapper ──
public record RegistrationSearchResponse
{
    public required List<RegistrationSearchResultDto> Result { get; init; }
    public required int Count { get; init; }        // Total matching records (for pager)

    // Aggregates across ALL matching records (not just current page)
    public required decimal TotalFees { get; init; }
    public required decimal TotalPaid { get; init; }
    public required decimal TotalOwed { get; init; }
}

// ── Filter dropdown options (loaded once) ──
public record RegistrationFilterOptionsDto
{
    public required List<FilterOption> Roles { get; init; }
    public required List<FilterOption> Teams { get; init; }
    public required List<FilterOption> Agegroups { get; init; }
    public required List<FilterOption> Divisions { get; init; }
    public required List<string> Clubs { get; init; }    // Distinct club names
}

public record FilterOption
{
    public required string Value { get; init; }     // ID
    public required string Text { get; init; }      // Display name
}
```

### Phase 2: Backend — Accounting & Refund DTOs

**Status**: [ ] Pending

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

**Status**: [ ] Pending

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
```

### Phase 4: Backend — Repository Extensions

**Status**: [ ] Pending

**Files to modify**:
- `TSIC.Contracts/Repositories/IRegistrationRepository.cs` (add methods)
- `TSIC.Infrastructure/Repositories/RegistrationRepository.cs` (implement)
- `TSIC.Contracts/Repositories/IRegistrationAccountingRepository.cs` (add methods)
- `TSIC.Infrastructure/Repositories/RegistrationAccountingRepository.cs` (implement)

**New IRegistrationRepository methods**:
```
SearchAsync(Guid jobId, RegistrationSearchRequest request) → RegistrationSearchResponse
    -- Builds IQueryable<Registrations> with:
    --   .Where(r => r.JobId == jobId)
    --   .Where(r => r.BActive == request.Active) if Active is not null
    --   .Where(name contains) if Name is not null — split on space for first/last
    --   .Where(email contains) if Email is not null
    --   .Where(r => r.RoleId == request.RoleId) if RoleId is not null
    --   .Where(r => r.AssignedTeamId == request.TeamId) if TeamId is not null
    --   .Where(r => r.AssignedAgegroupId == request.AgegroupId) if AgegroupId is not null
    --   .Where(r => r.AssignedDivId == request.DivisionId) if DivisionId is not null
    --   .Where(r => r.ClubName.Contains(request.ClubName)) if ClubName is not null
    --   .Where(r => r.OwedTotal > 0) if OwesFilter == "owes"
    --   .Where(r => r.OwedTotal <= 0) if OwesFilter == "paid"
    --   .Where(r => r.RegistrationTs >= request.RegDateFrom) if RegDateFrom is not null
    --   .Where(r => r.RegistrationTs <= request.RegDateTo) if RegDateTo is not null
    --
    -- Joins: Registrations → AspNetUsers (via UserId) for name/email
    --         Registrations → Teams (via AssignedTeamId) for team name
    --         Registrations → AspNetRoles (via RoleId) for role name
    --         Registrations → Agegroups (via AssignedAgegroupId) for agegroup name
    --         Registrations → Divisions (via AssignedDivId) for division name
    --
    -- Computes Count + Aggregates (TotalFees, TotalPaid, TotalOwed) BEFORE paging
    -- Applies OrderBy/ThenBy based on SortField + SortDirection
    -- Applies Skip/Take for paging
    -- Returns RegistrationSearchResponse with Result + Count + Aggregates
    -- AsNoTracking

GetFilterOptionsAsync(Guid jobId) → RegistrationFilterOptionsDto
    -- Returns distinct roles, teams, agegroups, divisions, club names for this job
    -- Each with counts for contextual relevance
    -- AsNoTracking

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
```

**New IRegistrationAccountingRepository methods**:
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

**Status**: [ ] Pending

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
```

### Phase 6: Backend — Controller

**Status**: [ ] Pending

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

**Authorization**: All endpoints `[Authorize(Policy = "AdminOnly")]`, derive `jobId` from JWT via `GetJobIdFromRegistrationAsync()`.

### Phase 7: Backend — DI Registration

**Status**: [ ] Pending

**File to modify**:
- `TSIC.API/Program.cs`

**Add registration**:
```csharp
builder.Services.AddScoped<IRegistrationSearchService, RegistrationSearchService>();
```

### Phase 8: Frontend — Service

**Status**: [ ] Pending

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

### Phase 9: Frontend — Registration Search Component (Main Grid)

**Status**: [ ] Pending

**Files to create**:
- `src/app/views/admin/registration-search/registration-search.component.ts`
- `src/app/views/admin/registration-search/registration-search.component.html`
- `src/app/views/admin/registration-search/registration-search.component.scss`

**Component imports**:
```typescript
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
// Services: SortService, FilterService, PageService, ToolbarService,
//           ExcelExportService, AggregateService, ResizeService
```

**Component state** (signals):
```typescript
// Filter state
filterOptions = signal<RegistrationFilterOptionsDto | null>(null);
searchRequest = signal<RegistrationSearchRequest>(defaultSearchRequest());
searchResults = signal<RegistrationSearchResponse | null>(null);
isSearching = signal(false);

// Grid state
selectedRegistrations = signal<Set<string>>(new Set());

// Slide-over panel
selectedDetail = signal<RegistrationDetailDto | null>(null);
isPanelOpen = signal(false);
activeTab = signal<'details' | 'accounting' | 'email'>('details');

// Batch email
showBatchEmailModal = signal(false);

// Refund
showRefundModal = signal(false);
refundTarget = signal<AccountingRecordDto | null>(null);
```

**Syncfusion Grid configuration**:
- `allowPaging: true`, `pageSettings: { pageSize: 20 }`
- `allowSorting: true`, `allowMultiSorting: true`
- `allowFiltering: false` (use custom filter panel above grid, not grid's built-in filter bar)
- `allowExcelExport: true`
- Toolbar: `['ExcelExport']`
- Columns: `#` (RegistrationAi), `Last`, `First`, `Email`, `Team`, `Role`, `Active`, `Fees`, `Paid`, `Owed`, `Reg Date`
- Checkbox selection column
- Aggregate row: Sum of FeeTotal, PaidTotal, OwedTotal (from server response, not client-side)
- `queryCellInfo` event: Color-code OwedTotal (green if $0, red if > $0)
- Row click → open slide-over panel with registration detail
- CSS class: `tight-table` for compact density

**Filter panel** (Bootstrap form above grid):
- Two rows of filter inputs (responsive grid)
- Name text input, Email text input
- Role dropdown (from filterOptions), Team dropdown, Agegroup dropdown, Division dropdown
- Club text/dropdown, Active status dropdown, Owes filter dropdown
- Date range pickers (From/To)
- Search button (triggers API call), Clear button (resets all filters)
- "Email Selected" button (enabled when selections exist), "Export" button (Excel export)

**Key behaviors**:
- On component init: load filter options, execute default search (all active registrations)
- Filter changes do NOT auto-search — admin clicks "Search" button (intentional; prevents excessive API calls during filter setup)
- Grid sorting triggers server-side re-query (not client-side sort)
- Page change triggers server-side re-query
- Row click loads registration detail into slide-over panel
- Checkbox selection tracks IDs for batch operations
- Excel export uses current search results

### Phase 10: Frontend — Slide-Over Detail Panel Component

**Status**: [ ] Pending

**Files to create**:
- `src/app/views/admin/registration-search/components/registration-detail-panel.component.ts`
- `src/app/views/admin/registration-search/components/registration-detail-panel.component.html`
- `src/app/views/admin/registration-search/components/registration-detail-panel.component.scss`

**Inputs**: `detail: RegistrationDetailDto`, `isOpen: boolean`
**Outputs**: `closed: EventEmitter<void>`, `saved: EventEmitter<void>`, `refundRequested: EventEmitter<AccountingRecordDto>`

**Tabs**:
1. **Details** — Dynamic form from `ProfileMetadataJson`
   - Parse metadata, render each field by inputType
   - TEXT → `<input type="text">`, SELECT → `<select>` with options, DATE → `<input type="date">`, CHECKBOX → `<input type="checkbox">`, etc.
   - Pre-populate from `ProfileValues` dictionary
   - Respect `visibility` (hide `hidden` fields, mark `adminOnly` fields)
   - Apply `validation` rules (required, pattern, min/max)
   - "Save Changes" button → calls `updateProfile()`

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

**Status**: [ ] Pending

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

**Status**: [ ] Pending

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

**Status**: [ ] Pending

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

**Status**: [ ] Pending

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

**Status**: [ ] Pending

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
6. **Server-side paging**: Page 1 shows items 1-20, page 2 shows 21-40; total count stays consistent
7. **Server-side sorting**: Click "Last Name" header → results re-queried sorted server-side
8. **Aggregates**: Footer row shows TotalFees/TotalPaid/TotalOwed across ALL matches (not just current page)
9. **Excel export**: Exports current search results to .xlsx with all visible columns
10. **Checkbox selection**: Select individual rows, select-all on current page, persist across pages
11. **Empty state**: "No registrations found" when no results match filters
12. **Large dataset**: 5,000+ registrations → server-side paging keeps UI responsive

**Slide-Over Detail Panel:**
13. **Open/close**: Click row → panel slides in; click X or Escape → panel slides out
14. **Dynamic form**: Form fields match job's PlayerProfileMetadataJson; different jobs show different fields
15. **Form save**: Edit profile fields → Save → values persisted; re-open panel → values updated
16. **Validation**: Required fields show validation errors; pattern/min/max enforced
17. **Field types**: TEXT renders input, SELECT renders dropdown with options, DATE renders date picker, CHECKBOX renders toggle

**Accounting:**
18. **Payment history**: All RegistrationAccounting records shown chronologically
19. **CC indicator**: Credit card payments show last-4 and "Refund" button
20. **Non-CC payments**: Check/Cash records do NOT show refund button
21. **Add record**: Creates new accounting entry, updates registration PaidTotal/OwedTotal
22. **Financial summary**: Fees/Paid/Owed shown and updated after any accounting change

**Refunds:**
23. **Refund modal**: Pre-populated with transaction details, max amount enforced
24. **Full refund**: Refund amount = original amount → processes via ADN_Refund
25. **Partial refund**: Refund amount < original → processes partial refund
26. **Refund accounting**: Creates negative Payamt accounting record with "Credit Card Refund" method
27. **Financial update**: Registration.PaidTotal decremented, OwedTotal recalculated
28. **Error handling**: ADN gateway failure → error message shown, no accounting record created
29. **Refund audit**: Refund record shows in accounting tab with refund transaction ID

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

**General:**
44. **All 8 palettes**: CSS variable themed throughout (grid, panel, modals, mobile cards)
45. **Authorization**: Non-admin users get 403 on all endpoints
46. **Job scoping**: All queries scoped to JWT-derived jobId; cannot access other jobs' registrations
47. **Error handling**: Network errors show toast with context-specific message

---

## 10. Files Summary

### Backend Files

| File | Action | LOC (est.) |
|------|--------|------------|
| `TSIC.Contracts/Dtos/RegistrationSearch/RegistrationSearchDtos.cs` | Create | ~80 |
| `TSIC.Contracts/Dtos/RegistrationSearch/AccountingDtos.cs` | Create | ~70 |
| `TSIC.Contracts/Dtos/RegistrationSearch/RegistrationDetailDtos.cs` | Create | ~90 |
| `TSIC.Contracts/Repositories/IRegistrationRepository.cs` | Edit (add 3 methods) | +20 |
| `TSIC.Infrastructure/Repositories/RegistrationRepository.cs` | Edit (implement) | +180 |
| `TSIC.Contracts/Repositories/IRegistrationAccountingRepository.cs` | Edit (add 3 methods) | +15 |
| `TSIC.Infrastructure/Repositories/RegistrationAccountingRepository.cs` | Edit (implement) | +60 |
| `TSIC.Contracts/Services/IRegistrationSearchService.cs` | Create | ~25 |
| `TSIC.API/Services/Admin/RegistrationSearchService.cs` | Create | ~450 |
| `TSIC.API/Controllers/RegistrationSearchController.cs` | Create | ~140 |
| `TSIC.API/Program.cs` | Edit (1 DI line) | +1 |

### Frontend Files

| File | Action | LOC (est.) |
|------|--------|------------|
| `views/admin/registration-search/services/registration-search.service.ts` | Create | ~60 |
| `views/admin/registration-search/registration-search.component.ts` | Create | ~300 |
| `views/admin/registration-search/registration-search.component.html` | Create | ~280 |
| `views/admin/registration-search/registration-search.component.scss` | Create | ~150 |
| `views/admin/registration-search/components/registration-detail-panel.component.ts` | Create | ~250 |
| `views/admin/registration-search/components/registration-detail-panel.component.html` | Create | ~300 |
| `views/admin/registration-search/components/registration-detail-panel.component.scss` | Create | ~100 |
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

3. **Server-side paging and sorting** — the legacy system loaded all registrations client-side, causing browser lag for large jobs. Server-side operations ensure consistent performance regardless of dataset size. The Syncfusion DataManager pattern supports this with `skip`/`take`/`sortField`/`sortDirection` parameters.

4. **Aggregates computed server-side across ALL matches** — the footer row shows total Fees/Paid/Owed across the entire filtered result set, not just the current page. This is critical for financial oversight. Computing on the server via SQL aggregate functions (SUM) is orders of magnitude faster than client-side aggregation of all pages.

5. **Slide-over panel instead of separate page** — preserving search context is the #1 UX improvement. The admin can click through multiple registrations without losing filters, scroll position, or mental context. The 480px panel width leaves the grid visible underneath. On mobile, the panel goes full-width (expected, since the grid would be unusable at that viewport anyway).

6. **Dynamic form from PlayerProfileMetadataJson** — instead of hard-coding 40+ form fields, the detail panel assembles the form at runtime from the job's metadata. This means every job automatically gets the right fields — no code changes needed when a job uses a different profile type (PP10, CAC05, etc.). The form renderer handles all input types, validation rules, and visibility settings defined in the metadata.

7. **Refund via existing ADN_Refund gateway method** — the `ADN_Refund()` method exists and is tested but has never been called from application code. This migration plan activates it. The refund creates a negative accounting record (matching the existing accounting pattern) and updates registration financials. The original transaction's `AdnTransactionId`, `AdnCc4`, and `AdnCcexpDate` are already stored in `RegistrationAccounting` — all data needed for the refund is available.

8. **Batch email reuses existing TextSubstitutionService** — the token substitution system is comprehensive (25+ tokens including complex HTML tables like `!F-ACCOUNTING`). Rather than building a new email system, we compose templates using the same tokens and render via the existing service. The preview feature lets admins see exactly what recipients will receive before sending.

9. **POST for search endpoint** — search criteria can include multiple optional fields, date ranges, and sort parameters. Using POST with a request body is cleaner than encoding all this as query string parameters. This is a common pattern for complex search/filter APIs.

10. **Search button (not auto-search on filter change)** — admins often set 3-4 filters before searching. Auto-searching on every filter change would fire 3-4 unnecessary API calls and create a janky experience as results shift mid-configuration. The explicit "Search" button lets admins compose their filter criteria completely, then execute once.

11. **Accounting creation separate from refund** — admins need to create accounting records for non-CC payments (check received, cash collected, manual adjustments). This is a different workflow from refunds (which are CC-specific and gateway-integrated). Keeping them as separate actions with distinct UIs prevents confusion.

12. **ProfileValues as Dictionary<string, string?>** — the registration entity has 40+ profile columns with mixed types (string, int, decimal, DateTime, bool). Sending them as a flat dictionary with string values (with type conversion on the server via metadata's `inputType`) keeps the DTO simple and the frontend form generic. The server validates and converts each value based on the metadata schema before writing to the entity.

13. **Desktop/tablet-first with dedicated mobile quick lookup** — this is fundamentally a power-user desktop interface. Processing refunds, editing 40-field forms, composing batch emails, and scanning 10-column financial grids is desk work. Rather than degrading the desktop experience to chase responsive parity (horizontal-scrolling grids, full-width panels that lose context, stacked filter inputs requiring endless scrolling), we build two intentionally different experiences: (a) the full desktop UI at 768px+ with zero mobile compromises, and (b) a purpose-built mobile quick lookup at < 768px optimized for the one thing an admin does on their phone — "which team is this kid on?" / "does this person owe money?" The mobile mode uses the same API endpoints with simplified parameters, so there's no backend duplication. This pattern — separate mobile mode instead of responsive degradation — should be the standard for data-heavy admin tools going forward.

---

## 12. Amendments Log

| # | Change | Reason |
|---|--------|--------|
| 1 | Added dedicated Mobile Quick Lookup mode (Phase 13) | This interface is fundamentally a desktop power-user tool. Rather than degrading the desktop experience with responsive compromises (horizontal-scrolling grids, full-width panels that lose the context-preservation benefit, 8+ stacked filter inputs), we build a separate purpose-built mobile experience: single search input, card-based results with owes badges, tap-to-expand detail with Call/Email actions. No refunds, no batch email, no profile editing on mobile — those are desk work. Desktop UI hidden below 768px; mobile UI hidden above 768px. Slide-over panel removed from mobile entirely. New files: `mobile-quick-lookup.component.{ts,html,scss}` (~330 LOC). New UI standard established: "Mobile Quick Lookup Mode" pattern for data-heavy admin tools. Added design decision #13. Updated test cases 36-47. |

---

**Status**: Planning complete. Ready for implementation.
