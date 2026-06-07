# Crystal Reports Retirement — EF Completion Tracker

> **Living tracker.** Plan: `polymorphic-wandering-gizmo` (approved 2026-06-04). Report→source
> reference: `docs/Reports/Reports_DataSources.md`. Supersedes `PACKED-ROSTER-BOLD-MIGRATION.md`
> (Bold/RDL approach dropped — everything is now EF + flat dataset + Syncfusion-direct).

## Standard for every report

One **flat EF query** (`AsNoTracking`, pure LINQ — no `FromSqlRaw`, no SQL-Server-only funcs →
keeps the Postgres path open) → a **purpose-built flat DTO** → grouped/shaped in C# → hand-drawn
Syncfusion PDF. A report is **Retired** only when its replacement is verified **and** it's removed
from `type1-report-catalog.ts` (+ `@ExcludeActions`/`@ActionMap` in `scripts/7-…sql`).

**Status legend:** ☐ todo · ◐ building · ✔ verified (not yet retired) · ✅ retired

## Retirement log

**Mechanism (confirmed 2026-06-05).** A Crystal report appears on TWO surfaces: regular
roles (Director/SuperDirector) read Crystal tiles ONLY from the hard-coded frontend
`type1-report-catalog.ts`; SuperUser reads them ONLY from the DB `reporting.JobReports`
(the frontend catalog is suppressed for SU — `reports-library.component.ts`). So a FULL
retire = (1) remove the entry from `type1-report-catalog.ts` AND (2) add its `[Action]` to
`@ExcludeActions` in `scripts/7` + re-run script 7 (`sqlcmd -S "lpc:.\SS2016" -d <db> -I -i
scripts/7-…sql`). Endpoints stay live → fully reversible (drop the `@ExcludeActions` line +
re-run restores the rows; legacy `Jobs.JobMenu_Items` is read-only and untouched). Match is
case-insensitive (DB stores e.g. `tournycheckin`; excluding `N'TournyCheckin'` still hits).

**Cut-line decision (2026-06-05): "Tier 1 now, grind Tier 2."** 35 catalog entries → 11
retire-now (fully verified) / 13 engine-covered (preset not individually eyeballed — grind
one at a time, test→retire) / 11 keep (genuine gaps).

**Tier 1 — RETIRED 2026-06-05, CONFIRMED** (11 frontend entries removed + 10 actions added to
`@ExcludeActions`; user ran script 7 on TSICV5 → verified the 559 legacy rows are gone (559→0)
and the Designer tiles are intact, 106/339/339/105/339): `Job_Club_Rosters`, `camp_daygroups`,
`Get_JobRosters_PackedByPositionAGNoClubPlayers`, `Get_JobRosters_PackedByPosition_XPO`,
`TournamentRecruitingReport`, `TournyCheckin`, `AmericanSelectTournyCheckin`, `Job_CampCheckin`,
`JobRosters_TryoutsCheckReport`, `ISP_CheckinFlat`. NOTE: `TournamentRosterPacked` is also
retired but via its EXISTING `@ActionMap` remap (NOT `@ExcludeActions`) — only its frontend
entry was removed.

**Tier 2 — grind queue (test preset → then retire).** RETIRED in code 2026-06-05:
`FieldUtilizationAcrossLeaguesTournament` + `…ByDateTournament` (Schedule List Designer
"Field Utilization" preset; user-verified after fixing the score-mode trap — checking a
score column now auto-flips Score Mode off "Hidden" to "Printed" in `toggleField`, because
"Hidden" stripped score columns from preview + PDF). Plus `Score_Input` (Score Entry Sheets /
Blank preset, user-verified) RETIRED in code → **schedule designer Tier-2 COMPLETE (3/3)**.
**Roster Table designer Tier-2 ALSO COMPLETE 2026-06-05** (user-verified): No-Medical=`Job_Rosters_NoMedical`,
Coaches=`clubrostersNoMedicalII`, Sizes=`Get_JobPlayers_STEPS`, Recruiting tabular=`Get_JobRosters_RecruitingReport`+`_XPO`.
**Camp Tier-2 ALSO retired 2026-06-05** (user: "assume all ok" — Roommate has data [912 in
`um-summercamps-2025`], Night empty in every camp job, stacked/packed-XPO = accepted-approx of the
verified Day Group): `camp_daygroups_pdf`, `camp_nightgroups`, `camp_nightgroups_pdf`, `camp_roomies`,
`JobRosters_DayGroupsPackedXPO`. **TIER 2 COMPLETE — all 13 retired in code; total pending deletion =
522 rows across 13 actions** (one script-7 run clears them; not yet run as of this note). DB-only sibling
`FieldUtilizationWithNominations` (4 rows, not in frontend catalog) NOT retired (user didn't opt in).
**Frontend catalog now = the 11 Tier-3 keeps only** (verified by grep).

**Tier 3 — KEEP (not covered):** `Schedule_ByAgegroup` (game-board engine, out of v1) ·
`TournamentRosterPacked_PositionSchool` (group-by-school not built — ALSO: its `@ActionMap`
remap to the Designer is premature; consider un-remapping until the feature lands) ·
`camp_commuters` (team-name filter, not built) · 8 E-residuals (American Select eval/main-event,
E120, daily-reg-counts, 4 financials).

## Vehicles

| | Vehicle | Kind | Notes |
|---|---|---|---|
| **A** | Schedule List Designer | exists | cut `ScheduleList_Flat` proc → EF first |
| **B1** | PackedRoster Designer (packed cards) | exists | tight team-cards; extend toggles/presets |
| **B2** | Roster Table Designer (wide table) | **new 2026-06-05** | full-width per-player table; one broad EF query + presets |
| **C** | Check-In (live station) | exists | EF-backed; verify coverage + retire |
| **D** | Camp Groups | **folded into B2** | camp reports = rosters grouped by Day/Night/Roommate → B2 + camp preset/tile (no new designer) |
| **E** | Fixed one-off EF render | per-report | residuals; some need entity scaffolding |

## A — Schedule List Designer  (cut to EF, then absorb)

**Runtime-verified 2026-06-05** (lftc-summer-2026, Master preset; user "looks good"). The pass fixed
four things in `ScheduleListReportService`: footer `Page X / Y` (Bounds right-align); score cells now
always a boxed field (white fill + drop shadow, value inside in Printed); the **time** column renders
`ddd M/d  h:mm tt` ("Date / Time") so multi-day groups aren't ambiguous (+ dropped the redundant date
col from Flat); and pagination reserves the footer height so the last row + score box never clip.
`fieldUtil`/`scoreSheet` presets ride the same verified engine (spot-check at retirement).

| Report (endpoint) | Surface | EF entities | Status |
|---|---|---|---|
| _**proc→EF cutover**: `ScheduleList_Flat` → `GetScheduleListGamesAsync`_ | — | present | ✔ runtime-verified 2026-06-05 (lftc-summer-2026, Master preset) |
| FieldUtilizationAcrossLeaguesTournament | `fieldUtil` preset | present | ☐ |
| FieldUtilizationAcrossLeaguesByDateTournament | `fieldUtil` + date | present | ☐ |
| Score_Input | `scoreSheet` preset | present | ☐ |
| Schedule_ByAgegroup (game boards) | game-board matrix — future Game Board engine | present | ☐ out of v1 list engine (per design memory) |

> **Render-shape discovery (2026-06-05).** The "Rosters" family is **not** one designer — it's
> **two render shapes**: (B1) tight **packed cards** (existing `PackedRosterPdfService`) and (B2) a
> **wide full-width per-player table** (STEPS / club rosters / recruiting dumps). One table engine +
> presets serves all of B2. The user chose the **full family incl. STEPS + recruiting tabular**, so
> B2 was built net-new this session.

### B1 — PackedRoster Designer (packed cards; extend)

Extended **2026-06-05**: added a **club-affiliation toggle** ("NAME / CLUB", default off — proc
`JobRosters_Get_Teamplayers_Withcoach` bakes the player's own `club_name`/`ClubTeamName`), a
**`dayGroup`** field, a **within-card sort** (Uniform / Position / Name), and two presets
(**By Position**, **Packed XPO**). Backend: 3 new fields on `TournamentRosterRowDto` +
`GetTournamentRosterRowsAsync`; `ShowClubAffiliation`/`SortBy` on `PackedRosterRequestDto`.
`dotnet build` clean; models regenerated; `ng build` clean. (XPO `.rpt` not in repo → the XPO
preset is **approximate**, not a pixel clone — per the user's accepted trade-off.)

**Runtime-verified 2026-06-05** (`lftc-summer-2026`): packed render + footer fix (`Page X / Y`)
confirmed, plus the affiliation toggle, within-card sort, and By-Position / Packed-XPO presets.
Still open: `_PositionSchool` (**group-by-school**) — that grouping mode is **not built**, so the
variant stays ◐ (do **not** retire it yet).

| Report (endpoint) | Surface | EF entities | Status |
|---|---|---|---|
| TournamentRosterPacked | existing preset | present | ✔ runtime-verified 2026-06-05 (post EF-cutover + footer fix); retire pending |
| TournamentRosterPacked_PositionSchool | by-school grouping | present | ◐ (group-by-school not yet added) |
| TournamentRecruitingReport (recruiter cards) | recruiter mode | present | ✔ runtime-verified 2026-06-05 (SAT=sum confirmed); retire pending |
| Get_JobRosters_PackedByPositionAGNoClubPlayers | affiliation toggle OFF | present | ✔ runtime-verified 2026-06-05; retire pending |
| Get_JobRosters_PackedByPosition_XPO | Packed XPO preset (approx) | present | ✔ runtime-verified 2026-06-05 (approximate accepted); retire pending |

### B2 — Roster Table Designer (wide table; **NEW — built 2026-06-05**)

One broad EF query (`GetRosterTableRowsAsync`, players±staff) → `RosterTableRowDto` (~45-field flat
superset) → `RosterTablePdfService` full-width table (portrait/landscape, column-pickable, grouped).
Route `reporting/roster-table-designer`; catalog tile seeded in `scripts/7` (additive, retires
nothing yet). Backend `dotnet build` clean; 3 models regenerated; **`ng build` clean 2026-06-05**.

**Runtime-verified 2026-06-05** — Club Roster preset on `lftc-summer-2026` (~9.6k players / 557
teams); user: "excellent first pass." That pass fixed the PDF footer (now `Page X / Y`, right-aligned
inside the margin — a `PdfCompositeField` only honors alignment within its `Bounds`; the **same fix
was applied to `PackedRosterPdfService`**) and relabeled the **STEPS** preset → **Sizes**. The sibling
presets are column/group configuration over the *same* verified EF query + render engine, so each
needs only a quick confirming render at its retirement step.

| Report (endpoint) | Preset | EF entities | Status |
|---|---|---|---|
| Job_Club_Rosters | Club Roster | present | ✔ runtime-verified 2026-06-05 (lftc-summer-2026); footer fixed; retire pending |
| Job_Rosters_NoMedical | No-Medical | present | ◐ built; verify + retire pending |
| clubrostersNoMedicalII | No-Medical | present | ◐ built; verify + retire pending |
| JobRosters_Get_Teamplayers_Withcoach | Coaches | present | ◐ built; verify + retire pending |
| Get_Rosters_WithClubRep (`Rosters_WithClubRep_A` — not in catalog) | With Club Rep | present | ◐ built; verify pending |
| Get_JobPlayers_STEPS | Sizes (was STEPS) | present | ◐ built; verify + retire pending |
| Get_JobRosters_RecruitingReport | Recruiting (tabular) | present | ◐ built; verify + retire pending |
| Get_JobRosters_RecruitingReport_XPO | Recruiting (tabular) | present | ◐ built; verify + retire pending |

## C — Check-In  (verify live tool covers, then retire)

**Confirmed working 2026-06-05** (user opened the live `tools/checkin` station — "working"). Pre-existing
EF-backed station; per-variant coverage (American Select / ISP / tryouts / camp) accepted by the user.
Ready to retire the 5 legacy check-in reports in the catalog-removal batch.

| Report (endpoint) | Surface | EF entities | Status |
|---|---|---|---|
| TournyCheckin | live station | present | ✔ station confirmed 2026-06-05; retire pending |
| AmericanSelectTournyCheckin | live station | present | ✔ station confirmed 2026-06-05; retire pending |
| ISP_CheckinFlat | live station | present | ✔ station confirmed 2026-06-05; retire pending |
| Job_CampCheckin | live station | present | ✔ station confirmed 2026-06-05; retire pending |
| JobRosters_TryoutsCheckReport | live station | present | ✔ station confirmed 2026-06-05; retire pending |

## D — Camp Groups  (**folded into B2**, built 2026-06-05 — NOT a new designer)

**Runtime-verified 2026-06-05** (um-summercamps-2025, Day Group, ~990 campers; user "ok"). Campers
*are* team-assigned, so the shared roster query needs no camp-specific variant. Two fixes from this
pass (in `RosterTablePdfService`, so they also benefit the Roster Table designer): camp group-bys
(Day/Night/Roommate) now **drop rows with no value** for the selected field (no "(No …)" bucket); and
pagination reserves the footer height so the last row no longer clips. **Night Group has no data in
any camp job** → grouping by Night correctly yields an empty report (mechanism verified, data absent).

Discovery: `Get_Campers_DayGroups`/`…NightGroups` are just **players grouped by a camp field**
(the COMMUTER/OVERNIGHT split lives in the *team name*, stripped at render). `Registrations` already
carries `DayGroup`/`NightGroup`/`RoommatePref`. So a standalone designer would duplicate ~90% of B2 —
instead B2 gained **Day Group / Night Group / Roommate** group-by keys + `nightGroup`/`roommate`
fields + a **Camp preset**, and a Camp-Registration-gated tile **Camp Groups (Designer)** deep-links to
`reporting/roster-table-designer/camp` (route `data.mode='camp'`). Tile **seeded (105 rows)**;
`dotnet build` + `ng build` clean. No regen (GroupBy is a string).

| Report (endpoint) | Surface | EF entities | Status |
|---|---|---|---|
| camp_daygroups | B2 group-by Day Group | present (`Registrations`) | ✔ runtime-verified 2026-06-05 (um-summercamps-2025, ~990 campers) |
| camp_daygroups_pdf | B2 grouped (stacked layout approx) | present | ◐ built (approx) |
| camp_nightgroups | B2 group-by Night Group | present | ◐ built; verify pending |
| camp_nightgroups_pdf | B2 grouped (stacked layout approx) | present | ◐ built (approx) |
| camp_roomies | B2 group-by Roommate | present | ◐ built; verify pending |
| camp_commuters | team-name "COMMUTER " filter | present | ☐ NOT built (deferred — a filter, not a grouping) |
| JobRosters_DayGroupsPackedXPO | B1 packed (dayGroup field + sort) | present | ◐ built (approx) |

## E — Fixed one-off renders  (residuals; sequence last)

| Report (endpoint) | EF entities | Status |
|---|---|---|
| Get_JobPlayers_TSICDAILY (daily reg counts) | present (`Registrations`) — **easy** | ◐ built — EF+Syncfusion, **migrate-in-place** (no Designer; tile/endpoint unchanged); verify pending |
| TSICFeesYTDByCustomer | `adn.tsicFeesYTDAndLastYear` proc (small) — entities mapped | ◐ built — `GetFeeYtdRowsAsync` + `FeeYtdReportPdfService` (customer rollup); money **VERIFIED penny-exact vs proc** (274 keys, grand $98,380.00, 2025 $51,002 / 2026 $47,378); **layout INFERRED** (no .rpt/PDF ground truth) — user visual check pending |
| TSICFeesYTDByCustomerAndJob | same proc | ◐ built — same service (customer→job breakout, YoY change col); shares verified proc-exact data; layout inferred, user check pending |
| Get_Invoices_LastMonth | `adn.rpt_invoice` proc → **full EF reproduction** | ◐ built — player branch **VERIFIED penny-exact vs legacy PDF** (Camps&Clinics May-2026 balance $6,908.55); team branch built-to-spec, UNVERIFIED; PDF layout not yet user-checked |
| Get_Invoices_LastMonthSummariesOnly | same proc, summary-only render | ◐ built — shares verified summary math; layout not yet user-checked |

**Financials decision (2026-06-06):** user chose **full EF reproduction** (not render-swap), tips: (1) query base tables, NOT the `adn.vTxs` view; (2) keep ADN's raw-text settlement date as text (year/month via fixed substring positions), no datetime coercion / schema change. `adn.rpt_invoice` reproduced as `GetInvoiceLinesAsync` (player + team flat branches off base `adn.Txs`) + `InvoiceReportPdfService` (landscape itemized + per-venue Accounting Summary). Money verified end-to-end against the ground-truth PDF via a throwaway SqlServer-backed test (deleted). All five `adn` entities were already mapped — no scaffolding needed.

**Fee-YTD (2026-06-07):** `adn.tsicFeesYTDAndLastYear` reproduced as `GetFeeYtdRowsAsync` (pure LINQ off `Jobs`⋈`Customers`⋈`Monthly_Job_Stats`; per-row fee = NewPlayers×perPlayerCharge + NewTeams×perTeamCharge; the proc's `isnumeric(Jobs.year)=1` guard done in C# via `int.TryParse` — no portable LINQ for ISNUMERIC). One `FeeYtdReportPdfService` renders both reports (portrait): customer rollup + customer→job breakout, each a this-year-YTD vs last-year-YTD comparison (months 1..lastMonth both years) with a YoY change column. **Data verified penny-exact** vs the proc through the same connection (throwaway test, deleted): identical 274 (year,customer,job) keys, grand total $98,380.00, per-year 2025 $51,002 / 2026 $47,378. **Layout is INFERRED** — the `lastMonthsNewJobs.xlsx` the user supplied was a different "new jobs" report; there is no `.rpt`/PDF ground truth for the fee pair, only the proc defines the data. Needs a user visual check.
| AmericanSelectEvaluation | scaffold (only a 2021 archive entity today) | ☐ |
| AmericanSelectMainEventRosters | scaffold (master-detail → flat) | ☐ |
| PlayerStats_E120 | scaffold | ☐ |

## Out of scope (Postgres-prep fast-follow)

- ~23 live Excel `reporting_migrate.*` sprocs (`export-sp`) — work today; convert to EF LINQ in a
  separate anti-sproc pass. Not part of Crystal retirement.
- App-wide non-report sprocs (registration / payment / etc.) — separate Postgres workstream.
