# Fee Schema Redesign

**Status**: APPROVED — Phase 1 in progress (schema created, EF scaffolded)
**Date**: 2026-03-22

---

## Background

The legacy fee fields evolved organically:

1. **Originally**: Single flat team fee
2. **Tournaments needed deposit/balance-due**: Split into `RosterFee` (deposit) and `TeamFee` (balance-due additional)
3. **Player registration bolted on**: `RosterFee` co-opted as default player fee (same field, both pipelines)
4. **Combined jobs needed separation**: `PlayerFeeOverride` added as bandaid

### Legacy Field Translations

| Legacy Field | Actual Meaning |
|---|---|
| `Agegroups.RosterFee` | Team deposit (flat per team, NOT per-player × roster) — also legacy fallback for player fee |
| `Agegroups.TeamFee` | Team balance-due additional |
| `Teams.PerRegistrantFee` | Per-team player fee override |
| `Agegroups.PlayerFeeOverride` | Agegroup-level player fee (bandaid for combined jobs) |
| `Leagues.PlayerFeeOverride` | League-level player fee override |

**Evidence from legacy code** (`TSIC-Unify-2024/PlayerBaseController.cs`):
- Line 1963-1965: Player fee = `Teams.PerRegistrantFee ?? Agegroups.RosterFee`
- Line 2150-2158: `SumAsync(ag.RosterFee)` → variable `sumTeamDeposits`
- Line 1467: `depositDue = (PaidTotal >= ag.RosterFee) ? 0 : ag.RosterFee`

---

## Target Design

### Schema: `fees` — CREATED ✅

Script: `scripts/create-fees-schema.sql`

### `fees.JobFees`

Single table, scoped by nullable FKs. One row per (Job + Role + optional Agegroup + optional Team).

| Column | Type | Notes |
|---|---|---|
| `JobFeeId` | UNIQUEIDENTIFIER PK | DEFAULT NEWID() |
| `JobId` | UNIQUEIDENTIFIER NOT NULL | FK → Jobs.Jobs |
| `RoleId` | NVARCHAR(450) NOT NULL | Player, ClubRep, Coach, ... |
| `AgegroupId` | UNIQUEIDENTIFIER NULL | FK → Leagues.agegroups |
| `TeamId` | UNIQUEIDENTIFIER NULL | FK → Leagues.teams |
| `Deposit` | DECIMAL(18,2) NULL | |
| `BalanceDue` | DECIMAL(18,2) NULL | |
| `Modified` | DATETIME NOT NULL | DEFAULT GETUTCDATE() |
| `LebUserId` | NVARCHAR(450) NULL | FK → dbo.AspNetUsers |

Scope by which FKs are populated:

| AgegroupId | TeamId | Scope |
|---|---|---|
| NULL | NULL | Job-wide default for that role |
| set | NULL | Agegroup-level default |
| set | set | Team-level override |

Unique index: `(JobId, RoleId, AgegroupId, TeamId)`

### `fees.FeeModifiers`

Time-windowed fee adjustments. Child of JobFees (CASCADE DELETE).

| Column | Type | Notes |
|---|---|---|
| `FeeModifierId` | UNIQUEIDENTIFIER PK | DEFAULT NEWID() |
| `JobFeeId` | UNIQUEIDENTIFIER NOT NULL | FK → fees.JobFees (CASCADE) |
| `ModifierType` | NVARCHAR(50) NOT NULL | 'Discount', 'LateFee', 'EarlyBird', ... |
| `Amount` | DECIMAL(18,2) NOT NULL | |
| `StartDate` | DATETIME2 NULL | NULL = always active |
| `EndDate` | DATETIME2 NULL | NULL = no expiry |
| `Modified` | DATETIME NOT NULL | DEFAULT GETUTCDATE() |
| `LebUserId` | NVARCHAR(450) NULL | FK → dbo.AspNetUsers |

### Resolution Rules

**Base fee cascade**: Team → Agegroup → Job (most specific non-null wins, per field)

```
ResolvedDeposit    = TeamRow.Deposit    ?? AgRow.Deposit    ?? JobRow.Deposit
ResolvedBalanceDue = TeamRow.BalanceDue ?? AgRow.BalanceDue ?? JobRow.BalanceDue
```

**Deposit fallback**: If no deposit is set, read from balance-due:

```
EffectiveDeposit = ResolvedDeposit ?? ResolvedBalanceDue
```

Simple (single-phase) jobs: just set `BalanceDue`. Deposit-phase jobs: set both.

**Modifiers**: Only evaluated at NEW registration time. Active = `StartDate <= NOW <= EndDate` (NULLs = unbounded). Modifiers from all cascade levels **stack** (not override).

---

## Fee Application Rules

### Registration & Teams Entities = Materialized Snapshots

Both `Registrations` and `Teams` store identical fee snapshot fields:

| Field | Meaning |
|---|---|
| `FeeBase` | Base fee at time of calculation |
| `FeeDiscount` | Earned discount — stamped once, never recalculated |
| `FeeLatefee` | Late fee — stamped once, never recalculated |
| `FeeDonation` | Voluntary donation — never recalculated |
| `FeeDiscountMp` | Multi-player discount (separate concern) |
| `FeeProcessing` | CC processing fee — recalculated when FeeBase changes |
| `FeeTotal` | `FeeBase + FeeProcessing - FeeDiscount + FeeDonation + FeeLatefee` |
| `PaidTotal` | Amount paid — never changes on swap |
| `OwedTotal` | `FeeTotal - PaidTotal` |

### New Registration (Player or Team)

```
1. ResolveFee(jobId, roleId, agegroupId, teamId)     → base Deposit/BalanceDue
2. EvaluateModifiers(jobFeeId, DateTime.UtcNow)       → active discounts & late fees
3. Stamp FeeBase, FeeDiscount, FeeLatefee
4. Calculate FeeProcessing, FeeTotal, OwedTotal
```

### Roster Swap (Player: Team A → Team B)

```
1. ResolveFee for TARGET team                          → new FeeBase only
2. FeeDiscount   — KEPT (earned at original registration)
3. FeeLatefee    — KEPT (earned at original registration)
4. FeeDonation   — KEPT (player's choice)
5. FeeProcessing — RECALCULATED (function of new FeeBase)
6. FeeTotal, OwedTotal — RECALCULATED
7. PaidTotal     — UNCHANGED
```

**Rationale**: A parent who registered during early-bird keeps their discount. Modifiers are earned at registration time, not re-evaluated on swap.

### Division Swap (Team: Division A → Division B)

Same rule — only FeeBase changes. Modifiers frozen from original registration.

### Admin Bulk Recalc

Updates FeeBase + FeeProcessing only. Modifiers stay frozen.

---

## Admin Cheat Sheet

### Player-Only Job (JobTypeId 1, 4, 6)

| What to set | Where | Field |
|---|---|---|
| What each player pays | Agegroup (default) | `BalanceDue` on Player role row |
| Different fee for specific team | Team (override) | `BalanceDue` on Player role row |
| Deposit required first | Agegroup or Team | `Deposit` |
| Early bird discount | FeeModifiers | Discount with StartDate/EndDate |
| Late fee | FeeModifiers | LateFee with StartDate/EndDate |
| Leave blank | All ClubRep role rows | — |

### Team-Only Job (JobTypeId 2, 3)

| What to set | Where | Field |
|---|---|---|
| Team deposit | Agegroup (default) | `Deposit` on ClubRep role row |
| Balance due | Agegroup (default) | `BalanceDue` on ClubRep role row |
| Different amount for specific team | Team (override) | `Deposit` / `BalanceDue` on ClubRep role row |
| Leave blank | All Player role rows (unless combined) | — |

### Combined Job (Team job + PlayerFeeOverride)

| What to set | Where | Field |
|---|---|---|
| What each player pays | Agegroup | Player role: `BalanceDue` (+ `Deposit` if phased) |
| What club rep pays (deposit) | Agegroup | ClubRep role: `Deposit` |
| What club rep pays (balance) | Agegroup | ClubRep role: `BalanceDue` |
| Per-team overrides | Team | Any role's `Deposit` / `BalanceDue` |

**No shared fields. No ambiguity. Player fees and team fees are fully independent.**

---

## Data Migration

### Job Type Classification

| JobTypeId | Name | Pipeline |
|---|---|---|
| 0 | Customer Root | N/A |
| 1 | Club Sport Registration | Player-only |
| 2 | Tournament Scheduling | Team-only (combined if PlayerFeeOverride set) |
| 3 | League Scheduling | Team-only (combined if PlayerFeeOverride set) |
| 4 | Camp Registration | Player-only |
| 5 | Sales Venue | N/A |
| 6 | Showcase Registration | Player-only |

### Migration Mapping (FINALIZED)

**Player-only jobs (types 1, 4, 6):**
```
Agegroups.RosterFee         → Player.BalanceDue (agegroup level)
Teams.PerRegistrantFee      → Player.BalanceDue (team level, where set)
```

**Team-only jobs (types 2, 3):**
```
Agegroups.RosterFee         → ClubRep.Deposit (agegroup level)
Agegroups.TeamFee           → ClubRep.BalanceDue (agegroup level)
```

**Combined jobs (types 2, 3 with PlayerFeeOverride set):**
```
Same as team-only, plus:
Agegroups.PlayerFeeOverride → Player.BalanceDue (agegroup level)
```

### Discount/Late Fee Migration

**No legacy data to migrate.** Zero instances of DiscountFee/LateFee having been used in production. `FeeModifiers` table ships empty — it's a new capability.

### Migration Script

**TODO** — `scripts/seed-fees-from-legacy.sql`
- Must be idempotent (safe to re-run after prod restore)
- Joins to Jobs via Leagues to determine JobTypeId
- Creates agegroup-level JobFees rows per the mapping above
- Creates team-level JobFees rows for Teams.PerRegistrantFee where set
- Verification query at end: row counts per role, per job type

---

## Code Changes Required

### Phase 1: Read Path (backend)

1. **Entities** — already scaffolded ✅: `JobFees`, `FeeModifiers`
2. **New repository**: `IFeeRepository` / `FeeRepository`
   - `GetResolvedBaseFeeAsync(Guid jobId, string roleId, Guid agegroupId, Guid teamId)` — cascade query
   - `GetActiveModifiersAsync(Guid jobFeeId, DateTime asOfDate)` — for new registrations
   - `GetJobFeesByJobAsync(Guid jobId)` — for LADT display
3. **New service**: `IFeeResolutionService`
   - `ResolveFee(jobId, roleId, agegroupId, teamId)` — base fee cascade
   - `EvaluateModifiers(jobFeeId, asOfDate)` — new registrations only
   - `ApplySwapFees(entity, newBaseFee)` — recalc base + processing, preserve modifiers
   - Replaces `PlayerRegistrationFeeService` and `TeamFeeCalculator`
4. **Wire callers**: RosterSwapperService, TeamRegistrationService, player wizard

### Phase 2: Write Path (LADT refactor)

The LADT editor is the primary admin UI for fee configuration. It currently writes legacy fields directly on Agegroup and Team entities.

#### LADT Agegroup Detail Panel
- **Remove**: `RosterFee`, `RosterFeeLabel`, `TeamFee`, `TeamFeeLabel`, `PlayerFeeOverride` fields
- **Add**: Per-role fee section showing Deposit + BalanceDue
  - For Player role: Deposit (optional) + BalanceDue
  - For ClubRep role: Deposit + BalanceDue
  - Future: Coach role, etc.
- **Add**: Modifier management UI (discount/late fee with date windows)
- **Backend**: New `LadtFeeService` or extend `LadtService` to read/write `fees.JobFees` + `fees.FeeModifiers`

#### LADT Team Detail Panel
- **Remove**: `PerRegistrantFee`, `PerRegistrantDeposit` fields
- **Add**: Optional per-role override fields (only shown if different from agegroup default)
- **Backend**: Write to team-scoped `fees.JobFees` rows

#### LADT Fee Summary
- Consider adding a read-only fee summary view showing the resolved cascade for each agegroup/team — helps admin see what's actually being charged without mental math

### Phase 3: Registration Wizards
- Player wizard reads resolved Player fee from `IFeeResolutionService`
- Team wizard reads resolved ClubRep fee from `IFeeResolutionService`
- Regenerate API models after DTO changes

### Phase 4: Cleanup
- Stop reading legacy columns (behind feature flag or hard cutover)
- Remove `PlayerRegistrationFeeService`, `TeamFeeCalculator`
- Remove `PlayerFeeOverride` concept entirely
- Eventually drop legacy columns from Agegroups, Teams, Leagues

---

## Legacy Fields to Retire (Phase 4)

| Entity | Columns |
|---|---|
| Agegroups | `RosterFee`, `RosterFeeLabel`, `TeamFee`, `TeamFeeLabel`, `PlayerFeeOverride` |
| Agegroups | `DiscountFee`, `DiscountFeeStart`, `DiscountFeeEnd`, `LateFee`, `LateFeeStart`, `LateFeeEnd` |
| Teams | `PerRegistrantFee`, `PerRegistrantDeposit` |
| Teams | `DiscountFee`, `DiscountFeeStart`, `DiscountFeeEnd`, `LateFee`, `LateFeeStart`, `LateFeeEnd` |
| Leagues | `PlayerFeeOverride` |

---

## Scripts

| Script | Status | Purpose |
|---|---|---|
| `scripts/create-fees-schema.sql` | ✅ Run | Creates `fees` schema, `JobFees`, `FeeModifiers` tables |
| `scripts/add-lebUserId-fk.sql` | ✅ Run | Adds missing LebUserId FKs on scheduling + stores tables |
| `scripts/seed-fees-from-legacy.sql` | TODO | Populates `fees.JobFees` from legacy Agegroup/Team fee columns |
