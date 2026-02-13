# Migration 008: Search Teams — COMPLETED

## Context

The "Search Teams" admin interface is the **second most-used admin tool** after Registration Search. It allows admins to find teams by filter criteria, view/edit team details, and manage all team accounting (CC charges, refunds, checks, corrections) at the individual transaction, team, and cross-club levels.

Migrated from legacy `SearchTeamsController.cs` (722 lines) + `IPaymentService.cs` cross-club logic (~750 lines) to the modern Angular 21 + .NET API architecture.

**Legacy URL**: `/SearchTeams/Index`

---

## What Was Built

### Backend (.NET API)

#### DTOs
| File | Records |
|------|---------|
| `Contracts/Dtos/TeamSearch/TeamSearchDtos.cs` | `TeamSearchRequest`, `TeamSearchResultDto`, `TeamSearchResponse`, `TeamFilterOptionsDto` |
| `Contracts/Dtos/TeamSearch/TeamAccountingDtos.cs` | `TeamSearchDetailDto`, `ClubTeamSummaryDto`, `EditTeamRequest`, `TeamCcChargeRequest`, `TeamCcChargeResponse`, `TeamCheckOrCorrectionRequest`, `TeamCheckOrCorrectionResponse`, `TeamPaymentAllocation` |

> **Note:** The detail DTO is named `TeamSearchDetailDto` (not `TeamDetailDto`) to avoid a naming collision with the existing LADT editor's `TeamDetailDto` which has different fields (`playerCount`, `divRank`, etc.).

#### Service Interface
- `Contracts/Services/ITeamSearchService.cs` — 9 methods: search, filters, detail, edit, CC charge (team + club), check/correction (team + club), refund, payment methods

#### Repository Additions
- **ITeamRepository** — 5 new methods: `SearchTeamsAsync`, `GetTeamSearchFilterOptionsAsync`, `GetTeamDetailAsync`, `GetActiveClubTeamsOrderedByOwedAsync`, `GetClubTeamSummariesAsync`, plus `TeamDetailQueryResult` record
- **IRegistrationAccountingRepository** — 1 new method: `GetByTeamIdAsync`
- Both implemented in their respective Infrastructure repositories

#### Service Implementation
- `API/Services/Admin/TeamSearchService.cs` (~600 lines)
- Faithfully ports legacy cross-club payment spreading algorithm
- Dependencies: `ITeamRepository`, `IRegistrationAccountingRepository`, `IRegistrationRepository`, `IJobRepository`, `IAdnApiService`
- Payment method GUIDs: CC=`30ECA575...`, CCCredit=`31ECA575...`, Check=`32ECA575...`, Correction=`33ECA575...`

#### Controller
- `API/Controllers/TeamSearchController.cs` — `[Route("api/team-search")]` with `[Authorize(Policy = "AdminOnly")]`

| Method | Route | Purpose |
|--------|-------|---------|
| POST | `/search` | Search teams with filters |
| GET | `/filter-options` | Get filter dropdowns with counts |
| GET | `/{teamId}` | Get team detail + accounting |
| PUT | `/{teamId}` | Edit team properties |
| POST | `/{teamId}/cc-charge` | CC charge single team |
| POST | `/{teamId}/check` | Check/correction single team |
| POST | `/club/{regId}/cc-charge` | CC charge all club teams |
| POST | `/club/{regId}/check` | Check/correction spread across club |
| POST | `/refund` | Refund CC transaction |
| GET | `/payment-methods` | Get payment method options |

#### DI Registration
- `builder.Services.AddScoped<ITeamSearchService, TeamSearchService>()` in `Program.cs`

---

### Frontend (Angular 21)

#### HTTP Service
- `views/admin/team-search/services/team-search.service.ts` — all endpoints + type re-exports from `@core/api`

#### Search Component
- `views/admin/team-search/team-search.component.ts` (+html, +scss)
- **Filter panel**: Club (MultiSelect), LOP (MultiSelect), Active Status (MultiSelect, Active pre-checked), Pay Status (MultiSelect), LADT Tree (reused `LadtTreeFilterComponent`)
- **Syncfusion grid**: Frozen columns (Row#, Active, Club, Team) + scrollable (Agegroup, Div/Pool, LOP, $Paid, $Owed, RegDate, ClubRepName, Email, Phone, Comments)
- **Features**: Filter chips with remove, dirty-state tracking (pulse glow on Search button), Excel export, summary row (total paid/owed), owed color-coding (red=positive, green=zero)

#### Team Detail Panel
- `views/admin/team-search/components/team-detail-panel.component.ts` (+html, +scss)
- Slide-in right panel with **Info** and **Accounting** tabs
- **Info tab**: Editable team name, active toggle, LOP, comments + read-only club rep contact (name, email, phone)
- **Accounting tab**:
  - Scope selector (This Team vs All Club Teams) with prominent visual distinction
  - Financial summary (fee/paid/owed) changes with scope
  - Transaction history table (date, method, due, paid, comment, CC4, refund button)
  - Club team breakdown table (when scope = club)
  - Dynamic action buttons labeled with current scope

#### CC Charge Modal
- `views/admin/team-search/components/cc-charge-modal.component.ts` (+scss)
- Inline CC form: number, exp, CVV, name, address, zip, email, phone
- Shows club team breakdown for club-wide charges
- Calls team vs club endpoint based on scope

#### Check/Correction Modal
- `views/admin/team-search/components/check-payment-modal.component.ts` (+scss)
- Amount, check number (checks only), comment
- Allocation preview for club-wide payments (shows teams ordered by highest balance)
- Handles both Check and Correction types via `paymentType` input

#### Route
- `admin/team-search` registered in `app.routes.ts` with lazy loading

---

## Cross-Club Payment Spreading Algorithm

The most complex piece of this migration — ported from legacy `RecordTx_ClubRep_CheckOrCorrection` (lines 1301-1553):

1. Get all active club teams, ordered by `OwedTotal DESC`
2. For each team, calculate allocation based on deposit/full-payment rules:
   - If `PaidTotal >= RosterFee + TeamFee` → 0 (fully paid)
   - If `PaidTotal >= RosterFee` → TeamFee (if `BTeamsFullPaymentRequired`)
   - Otherwise → RosterFee + TeamFee (if full payment required) or just RosterFee
3. Cap at remaining check balance
4. If `BAddProcessingFees`: reduce team's `FeeProcessing` by `CcProcessingFeePercent * checkAmount`
5. Create `RegistrationAccounting` record per team
6. Update team + club rep `PaidTotal`/`OwedTotal`
7. Call `SynchronizeClubRepFinancialsAsync()` to resync aggregates

CC charges (club-wide) iterate each team with `OwedTotal > 0`, issuing individual ADN charge calls sequentially.

Refunds check ADN transaction status: `capturedPendingSettlement` → VOID (full amount); `settledSuccessfully` → REFUND (partial/full).

---

## Build Status

- **Backend**: 0 errors (115 pre-existing warnings)
- **Frontend**: 0 errors, 0 warnings in team-search components
- **API models**: Regenerated successfully — `TeamSearchDetailDto.ts` separate from LADT's `TeamDetailDto.ts`

## Completed: 2026-02-12
