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

**Tier 3 — KEEP (not covered):** ~~`Schedule_ByAgegroup` (game-board engine, out of v1)~~ **DONE
2026-06-08** — Game Boards built as a focused `GameBoardsPdfService` (EF + Syncfusion, NOT the list
engine), endpoint swapped off Crystal, badged SF; **verified vs the legacy target PDF on
`lftc-summer-2026`** (in-app runtime check pending) ·
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
| FieldUtilizationAcrossLeaguesTournament | `fieldUtil` preset | present | ✔ USER-VERIFIED 2026-06-05 (after the score-mode-trap fix) + retired in code (`@ExcludeActions`, script-7:239); pending only the Tier-2 script-7 re-run |
| FieldUtilizationAcrossLeaguesByDateTournament | `fieldUtil` + date | present | ✔ USER-VERIFIED 2026-06-05 + retired in code (`@ExcludeActions`, script-7:240); pending script-7 re-run |
| Score_Input | `scoreSheet` preset | present | ✔ USER-VERIFIED 2026-06-05 (Score Entry / Blank preset) + retired in code (`@ExcludeActions`, script-7:241); pending script-7 re-run |
| Schedule_ByAgegroup (game boards) | **focused `GameBoardsPdfService`** — standings write-in box + home/away score boxes + per-agegroup "Championship Round" (NOT the list engine) | present | ✔ BUILT + verified vs target PDF 2026-06-08 (lftc-summer-2026); endpoint EF-swapped + badged SF; in-app check pending. Games group by `DivId` (not `Div2Id`); bracket seed types Z/Y/X/Q/S/F; same-time games sorted by **field** (Allentown-01, 02, … — more usable than legacy's non-deterministic order; the proc sorts by G_Date only) |

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
**`_PositionSchool` RESOLVED 2026-06-09** — it was never a group-by-school report. The legacy
(`rosterPackedPositionSchool-legacy.pdf`, 143 pp) is the SAME team-cards-grouped-by-`AgDiv` layout
as base packed, with the recruiting column set (`# · Player · Position · Grad Yr · GPA · College
Commit`) + the rep contact line — i.e. the existing **"College Commit 2-up"** (`collegeCommit2`)
preset. Two 1-line fidelity fixes vs the legacy: show GPA for committed players
(`PackedRosterPdfService` ResolveCell `gpa` — dropped the `IsCommitted` blank) and flip
`showCoaches` ON in the preset. So the `@ActionMap` remap was CORRECT, not premature; retired by
removing the `type1-report-catalog.ts` entry (DB side already mapped to the Designer SpaComponent).

| Report (endpoint) | Surface | EF entities | Status |
|---|---|---|---|
| TournamentRosterPacked | existing preset | present | ✔ runtime-verified 2026-06-05 (post EF-cutover + footer fix); retire pending |
| TournamentRosterPacked_PositionSchool | `collegeCommit2` preset (recruiting columns, 2-up) | present | ✔ RESOLVED 2026-06-09 — legacy is base-packed layout + recruiting columns (NOT group-by-school); GPA-for-committed + `showCoaches` fixed; catalog entry removed → retired; in-app render check pending |
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
| camp_commuters | **fixed render** (commuter filter + Camp→SkillGroup two-level table) | present | ⏸ DEFERRED (user "skip for now" 2026-06-09). Legacy `umCampCommuters-legacy.pdf` ran **EMPTY** (no live commuters — UM commuter camps are historical/concluded). Skeleton recovered: a WIDE TABLE (B2 family, not packed) titled "COMMUTERS BY [BY] CAMP AND SKILL GROUP" (legacy double-"BY" typo), grouped **Camp → Skill Group**, cols `Camp·LastName·FirstName·grad_year·city·state·roomStatus·uniform_no·nightGroup`. Commuter discriminant: the documented `left(teamName,1)='C'` is WRONG vs real data (UM teams are "UM COMMUTER…"/"UM OVERNIGHT…" → start 'U'); real signal = team name CONTAINS "COMMUTER". Two-level group + filter don't fit B2's single-group model → fixed one-off recommended. Unresolved: Skill Group≈DayGroup? Camp≈teamName? + no live data to verify. **Side-note:** the same `roomStatus=left(teamName,1)` in the shipped `reporting_migrate.camp_excelexport_room_position` likely emits 'U' for UM campers (faithful-to-legacy but dubious) |
| JobRosters_DayGroupsPackedXPO | B1 packed (dayGroup field + sort) | present | ◐ built (approx) |

## E — Fixed one-off renders  (residuals; sequence last)

| Report (endpoint) | EF entities | Status |
|---|---|---|
| Get_JobPlayers_TSICDAILY (daily reg counts) | present (`Registrations`) — **easy** | ✔ **2026-06-09 — user KEPT the grouped render, declined legacy-match.** Legacy is a flat 5-col table (Customer · Job · Role · Count (Daily) · Count (To Date), no totals); user: *"fine to keep your grouping work, you don't have to match legacy for this report."* So the shipped render stays grouped (customer band → job → role rows → job/grand totals). Data layer proc-faithful (proc `reporting.Get_Registrations_TSIC_Today` + ad-hoc SQL + the EF query all return 0 today on TSICV5). **Migrate-in-place, already committed** (no catalog/script-7 change). NOTE: the legacy PDF came from PROD/live data (showed 6/9/2026 activity); TSICV5 on this box is a staler restore (0 regs today; sample job 9,587 vs 9,735) — counts track whichever DB it runs against, not a logic gap |
| TSICFeesYTDByCustomer | `adn.tsicFeesYTDAndLastYear` proc (small) — entities mapped | ✅ **DONE (`427ed1ec`)** — `GetFeeYtdRowsAsync` + `FeeYtdReportPdfService`; render **REBUILT as the Crystal CROSS-TAB** (Customer→month rows × last-year/this-year columns + group/grand totals), **matched to legacy**; money penny-exact vs proc (274 keys, grand $98,380.00). In-place migration (catalog tile + `MIGRATED_EF_ACTIONS`), committed |
| TSICFeesYTDByCustomerAndJob | same proc | ✅ **DONE (`427ed1ec`)** — same service, Customer→Job→month cross-tab with job + customer + grand totals, matched to legacy; shares the penny-exact data. In-place migration, committed |
| Get_Invoices_LastMonth | `adn.rpt_invoice` proc → **full EF reproduction** | ✔ **USER-VERIFIED 2026-06-08** — player branch penny-exact ($6,908.55); **team branch confirmed** (user sees per-team txs render in the last-month run; same base `adn.Txs` reproduction as the verified player branch); layout "looks great" |
| Get_Invoices_LastMonthSummariesOnly | same proc, summary-only render | ✔ **USER-VERIFIED 2026-06-08** — layout "looks great"; shares the verified summary math |

**Financials decision (2026-06-06):** user chose **full EF reproduction** (not render-swap), tips: (1) query base tables, NOT the `adn.vTxs` view; (2) keep ADN's raw-text settlement date as text (year/month via fixed substring positions), no datetime coercion / schema change. `adn.rpt_invoice` reproduced as `GetInvoiceLinesAsync` (player + team flat branches off base `adn.Txs`) + `InvoiceReportPdfService` (landscape itemized + per-venue Accounting Summary). Money verified end-to-end against the ground-truth PDF via a throwaway SqlServer-backed test (deleted). All five `adn` entities were already mapped — no scaffolding needed.

**Fee-YTD (2026-06-07):** `adn.tsicFeesYTDAndLastYear` reproduced as `GetFeeYtdRowsAsync` (pure LINQ off `Jobs`⋈`Customers`⋈`Monthly_Job_Stats`; per-row fee = NewPlayers×perPlayerCharge + NewTeams×perTeamCharge; the proc's `isnumeric(Jobs.year)=1` guard done in C# via `int.TryParse` — no portable LINQ for ISNUMERIC). One `FeeYtdReportPdfService` renders both reports (portrait): customer rollup + customer→job breakout, each a this-year-YTD vs last-year-YTD comparison (months 1..lastMonth both years) with a YoY change column. **Data verified penny-exact** vs the proc through the same connection (throwaway test, deleted): identical 274 (year,customer,job) keys, grand total $98,380.00, per-year 2025 $51,002 / 2026 $47,378. **Layout is INFERRED** — the `lastMonthsNewJobs.xlsx` the user supplied was a different "new jobs" report; there was no `.rpt`/PDF ground truth at that point. **SUPERSEDED by `427ed1ec`:** the render was later rebuilt as the Crystal **cross-tab** and matched to the legacy; the "layout inferred" caveat no longer applies — the pair is DONE + committed.
| AmericanSelectEvaluation | **NO scaffold needed** — `Registrations`+`Families` already mapped | ◐ built — `GetAmericanSelectEvaluationRowsAsync` + `AmericanSelectReportPdfService.GenerateEvaluationAsync`; **live EF test PASSED vs proc** (166=166 rows); **render REBUILT 2026-06-07 as the evaluator scoring sheet (portrait: team→position groups, numeric uniform sort, 5 blank write-in boxes Physical/PsnSpecific/StickSkills/Notes/Total) — VERIFIED exact-match vs the user's target PDF on AS New Jersey 2026.** Prior landscape contact-sheet render was wrong; data layer unchanged. Not committed |
| AmericanSelectMainEventRosters | **NO scaffold** — same graph + family-user city | ✔ **RETIRED 2026-06-08 (`6748c006`)** — consolidated into the packed engine via `GetTournamentRosterRowsAsync(requiresSchedule:false)` + a fixed Player/Position/School 2-up preset; the bespoke single-column renderer, `GetAmericanSelectMainEventRosterRowsAsync`, and its DTO were removed. Verified vs the legacy export (all 12 offer teams, tryouts excluded). Migrated in place (tile was already an EF action; no catalog/script-7 change) |
| PlayerStats_E120 | **NO scaffold** — agegroup/team grouping on the roster graph (team name carries the position; the proc has no position column) | ✔ **USER-VERIFIED 2026-06-09** — render REBUILT as the legacy per-team blank entry form (one page per team titled "{agegroup}: {team}", four underlined stat headers F-Shot/5-10-5/40-YD/300-YD each over a PAIR of blank write-in boxes, "#{uniform} Last, First" rows, never pre-filled); exact roster match vs the legacy export on Players Series:Girls Summer Showcase 2025; legacy "#null" jersey artifact suppressed (bare "#"). Data layer unchanged (already proc-exact: agegroup/team grouping, no position, same filters/sort). **Migrated in place** — catalog tile + SuperUser DB row both route to the EF render via `MIGRATED_EF_ACTIONS` (green SF badge); NO catalog/`@ExcludeActions` change (would only cut access), same as AS Main Event. Committed to master |

**Residual rosters (2026-06-07):** the plan's "scaffold first" assumption was WRONG — all three procs query the already-mapped roster graph (`Registrations`⋈`AspNetRoles`⋈`AspNetUsers`⋈`Teams`⋈`Agegroups`⋈`Jobs`, plus `Families` for the American Select pair). Built three pure-LINQ repo methods modeled on `GetRosterTableRowsAsync` (American Select uses INNER `Families` joins to match the procs; E120 has no team-active filter, faithful to its proc; the main-event master-detail pair is flattened to one query per the subreport→flat guidance). Two render services added (`PlayerStatsReportPdfService`, `AmericanSelectReportPdfService`); 3 controller actions rewired (job from JWT, attrs unchanged incl. the two `[AllowAnonymous]`); DI registered. **Live EF-vs-proc tests PASSED** (throwaway SqlServer-backed test run through the same connection, then deleted): E120 exact 9871-row RegistrationId match; Eval 166=166; MainEvent flat 66 = master-detail pair total (3 teams), zero either-only rows on all three. In-place `dotnet build` clean + **575 tests green**. Layouts INFERRED (no `.rpt` ground truth) → user visual check. Not committed; not retired from catalog.

## Out of scope (Postgres-prep fast-follow)

- ~23 live Excel `reporting_migrate.*` sprocs (`export-sp`) — work today; convert to EF LINQ in a
  separate anti-sproc pass. Not part of Crystal retirement.
- App-wide non-report sprocs (registration / payment / etc.) — separate Postgres workstream.
