# Bold Reports — Evaluation

**Demo:** 2026-05-27, noon
**Trial license:** received
**Decision needed:** is Bold Reports the right tool to migrate the remaining ~38 Crystal Reports PDFs off `cr2025.teamsportsinfo.com`?

## Pre-call finding (the headline)

**No automated `.rpt` → RDL converter exists.** Bold's own KB says so: ["Bold Reports does not have an automated option or tool to convert Crystal Reports to RDL"](https://help.boldreports.com/report-viewer-sdk/faq/how-to-convert-crystal-reports-to-rdl-for-bold-reports/). The marketing page hints at a "Word-to-RDL" pipeline (export `.rpt` → Word → utility), but that's partial structure recovery, not migration.

**Implication:** whichever tool we pick, the work is ~38 manual rebuilds. Demo should focus on **per-report build time**, not bulk import.

## Our backlog → Bold Reports concepts

Live backlog = ~38 PDF reports in `reporting.JobReports` with `Kind='CrystalReport'`. The `.rpt` collection is a superset (some `.rpt` files aren't linked in any live job menu). Pattern breakdown below is based on the `.rpt` collection — expect some phantoms when we run the live count.

| Pattern | Example reports | Bold concept |
|---|---|---|
| **A — Master-detail roster pack** | JobRosters_PackedByPosition_XPO, Job_Club_Rosters, TournamentRosterPacked*, AmericanSelectMainEventRosters, camp_daygroups_pdf, camp_nightgroups_pdf | Tablix with nested parent/child groups (one SP, one dataset) **or** Subreport per master row (separate RDL, one SP call per team) |
| **B — Schedule grids** | ScheduleByAgDiv, ScheduleByDay, ScheduleMaster, FieldUtilization*, Schedule_Export, Schedule_Export_Public, schedule_gamecards | Matrix Tablix — dynamic columns (date × division × game) |
| **C — Check-in / signoff** | tournycheckin, CovidTournyCheckin, AmericanSelectTournyCheckin, ISPCheckinFlat, Job_CampCheckin(II), JobRosters_TryoutsCheckReport, Score_Input | Plain Tablix with empty bordered text boxes for pen-and-paper columns |
| **D — Invoices / financials** | invoices2015, invoices2015SummariesOnly, invoicesOld, customerInvoiceDataPerMonth, tsicTSICFeesYTD(ByCustomer), CustomerJobRevenueRollups/Table, CustomerJobPlayerRollup | Header textboxes (first row) + Tablix line items + group footer totals (canonical RDL use case) |
| **E — Labels / per-player cards** | StoreLabels3, StorePerPlayerPickup(-OLD), StorePerPlayerPivot, StorePickupSignoff | List item or multi-column Tablix |
| **F — Specialty / curated** | AmericanSelectEvaluation, PlayerStats_E120, UniformData, LeagueForfeitReport, LeagueRefReport, ClubRep_BalanceDue_ByAgegroupTeamFee, JobPlayers_STEPS, JobPlayers_TSICDaily, League_Standings | Case-by-case (mostly A/D variants with different field layouts) |
| **Blocked** | ISPGameCheckin, JobRosters_PackedByPositionAG | Source proc missing from TSICV5 — datasource problem, not a tooling problem |

Bottom line: virtually every remaining report maps to a standard SSRS-RDL pattern. Bold Reports is technically viable. The question is per-report cost.

## POC challenge — ask them to build this LIVE on the call

**Report:** `JobRosters_PackedByPosition_XPO`

**Why this one:** Tier 3 master-detail (team header + player roster subreport) + stored procedure data source + packed N-teams-per-PDF layout. If Bold handles this cleanly, patterns A + C + E all work. If they stumble, the gap is visible inside an hour.

**Data they'll need:**
- Master SP: `reporting.JobRosters_Get_Teams`
- Detail SP: `reporting.JobRosters_Get_Teamplayers_Withcoach`
- Param: `@jobID uniqueidentifier`
- DB: `TSICV5` on `.\SS2016` (will need a connection or sample data)

**Signal:** if they can't get to a live build in 60 minutes, that itself is the answer.

## Questions to get answered on the call

### Build time & fidelity
- [ ] Walk through building the master-detail roster pack from scratch — typical time?
- [ ] PDF fidelity: page breaks across nested groups, `KeepTogether` semantics, page-X-of-Y across grouped reports
- [ ] Designer location — desktop app, browser-based, or VS extension? Who builds reports — devs only, or business users?
- [ ] Crystal-style "shared formulas" — is there an equivalent for cross-report logic, or duplicated per report?

### Data source
- [ ] SQL Server stored procedure with `@jobID uniqueidentifier` parameter — design-time vs runtime binding
- [ ] Single SP returning multiple result sets to different report regions (we use this in the SP-Excel path; want to keep one SP per report if possible)
- [ ] Connection string — stored in the report, or externalized?

### Angular embedding
- [ ] Our app is **Angular 21 standalone components, signals, no NgModules**. The `@boldreports/angular-reporting-components` package requires `@types/jquery` — does the viewer work clean in a standalone-component shell, or does it require a NgModule shim?
- [ ] Bundle size impact, production-gzipped
- [ ] Auth model: viewer hits **our** backend for report data (preferred), or does it talk to SQL Server directly?
- [ ] How does the viewer pass our JWT to its backend service?

### Licensing & deployment
- [ ] **Pricing — get a number on the call, not "we'll send a quote."** Per-server, per-developer, per-report?
- [ ] Is the trial license what we'd ship to production, or do those differ?
- [ ] On-prem install on Windows Server 2019 / IIS (TSIC-PHOENIX is prod, this box is dev/staging)
- [ ] Cloud option — data residency, callback to their servers, offline behavior

### Risk
- [ ] Reference customer with **Crystal → Bold** migration at ~30+ report scale. (Their headline 600+ win is SSRS → .NET Core, not Crystal.)
- [ ] Roadmap commitment to RDL fidelity + the Angular package through 2030
- [ ] Has the Angular component had breaking changes in the last 12 months?

## Decision rubric — score after the call

| Signal | Green | Yellow | Red |
|---|---|---|---|
| Per-report build time, pattern A | < 2 hrs | 2–4 hrs | > 4 hrs |
| Angular 21 standalone integration | clean demo | works w/ caveats | requires module shim or 3rd-party wrapper |
| Pricing | quoted on call, fits budget | quote within 48h | "custom" with no number |
| Crystal-scale customer reference | named + contactable | named only | none |
| SP data source + multi-result-set | both demo'd | one demo'd | neither |

**3+ green:** proceed to trial week. **2+ red:** look at alternatives (Telerik Reporting, ActiveReports, GrapeCity, SSRS-direct).

## Trial-week plan (if green-lit)

1. Build the POC report yourself in the trial: `JobRosters_PackedByPosition_XPO`. **Time it end-to-end.**
2. Embed the Angular viewer in a throwaway page in this repo. Verify bundle size, standalone-component compatibility, JWT pass-through.
3. Build 3 more reports across patterns **B, D, E**. Stop the clock on each.
4. Extrapolate: `avg time × 38 = total effort`. Decide if tractable.

## References

- [Bold Reports Crystal Reports alternative page](https://www.boldreports.com/crystal-reports-alternative/)
- [Bold Reports Angular reporting docs](https://help.boldreports.com/embedded-reporting/angular-reporting/)
- [Master-detail with subreports](https://help.boldreports.com/enterprise-reporting/designer-guide/report-designer/how-to/create-master-detail-report-using-subreport/)
- [Tablix report item](https://help.boldreports.com/enterprise-reporting/designer-guide/report-designer/report-items/tablix/)
- [KB 18892 — Convert Crystal Reports to RDL](https://support.boldreports.com/kb/article/18892/how-to-convert-crystal-reports-to-rdl-in-bold-reports)
- Project state: `docs/Reports/Reports_DataSources.md` (the 100-report mapping)
