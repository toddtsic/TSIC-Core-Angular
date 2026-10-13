# Stored procedure to EF conversion log

Port prep for the Ubuntu/Postgres branch: each app-reachable stored procedure is reproduced in EF on
`master`, proven against the procedure on SQL Server, and committed alone. The procedure stays
installed. One row per conversion; "What it taught" is the part worth reading before the next one.

## How each one is proven

1. **Parity run**: old procedure and new EF path against the same database, for every job with rows
   plus one job id that matches nothing. Compared: sheet name, column names and order, row count,
   every cell including its CLR type (as a set), and the sort key at every position.
2. **Test suite**: `dotnet test` on the solution.
3. **UI pass (per batch, after an API restart)**: Playwright runs the report from the Reports
   Library and the downloaded file is opened.

The seam is `ReportingService.BuildEfReplacementSheetsAsync`: the catalogue row, entitlement check
and export history keep keying on the procedure name; a converted name is answered from EF and the
procedure is never called.

## Log

| Date | Procedure | Shape | Parity | What it taught |
|---|---|---|---|---|
| 2026-10-12 | `reporting_migrate.Get_JobPlayers_STEPS_Excel` | One select, 9 joins, 40 columns | 870 jobs, 551,815 rows, 0 differences | See below |
| 2026-10-12 | `reporting_migrate.JobPlayers_YJ_Excel` | Same joins as STEPS, 36 columns | 870 jobs, 551,815 rows, 0 differences | Sibling layouts share one query; see below |
| 2026-10-12 | `reporting_migrate.Get_JobPlayers_E120_Excel` | Same joins, 39 columns, 3 concatenated names | 870 jobs, 551,815 rows, 0 differences | T-SQL `+` is null-propagating; see below |
| 2026-10-12 | `reporting_migrate.Get_JobPlayers_Liberty_Excel` | Same joins, 41 columns | 870 jobs, 551,815 rows, 0 differences | Nothing new |
| 2026-10-12 | `reporting_migrate.camp_datadump`, `camp_excelexport_long`, `_short`, `_veryshort`, `_daygroups`, `_roomies`, `_room_position`, `_summer` | Same joins again, 5 to 20 columns each | 870 jobs, 551,815 rows, 0 differences, each of the eight | One layout table; see below |
| 2026-10-12 | `reporting_migrate.Get_Staff_ForExcelExport` | One select, 2 inner + 2 left joins, 10 columns | 1,127 jobs, 22,397 rows, 0 differences | Left joins via optional navigation; see below |
| 2026-10-12 | `reporting_migrate.Get_Player_Transactions_ForExcelExport` | One select, 8 inner joins, 20 columns, one row per accounting record | 1,127 jobs, 224,499 rows, 0 differences | Date-as-text and filter-only joins; see below |
| 2026-10-12 | `reporting_migrate.Get_DiscountedPlayers` | One select, 6 inner + 1 left join, 17 columns | 1,127 jobs, 13,476 rows, 0 differences | Value types through a left join; see below |
| 2026-10-12 | `reporting_migrate.PlayerStats_ParisiResults` | One select, 5 inner joins, 11 columns, filter on division name | 1,127 jobs, 481,982 rows, 0 differences | `!=` drops nulls; see below |
| 2026-10-12 | `reporting_migrate.UsageByJobAndRegistrant` | One select over the mobile device tables, 6 inner joins, 7 columns | 1,127 jobs, 3,017 rows, 0 differences | A join that neither selects nor filters is dropped; see below |
| 2026-10-12 | `reporting_migrate.League_Teams` | One select from teams, 5 inner + 1 left join, 15 columns | 1,127 jobs, 31,570 rows, 0 differences | A uniqueidentifier as text is upper-case; see below |
| 2026-10-12 | `reporting_migrate.Get_Players_ForExcelExport_AllCustomers` | Customer resolved from the job, then one select over every admin-open job of that customer, 8 inner joins, 47 columns | 1,127 jobs, 10,823,419 rows, 0 differences | Cross-job scope and `getdate()`; see below |
| 2026-10-12 | `reporting_migrate.TeamsFieldDistribution` | Aggregate: games joined to both teams, grouped by club, agegroup, team, field and day, 6 columns | 1,127 jobs, 82,675 rows, 0 differences | Grouping and sorting on a formatted date; see below |
| 2026-10-12 | `reporting_migrate.JobRosters_ExportTournament` | One select, 8 inner + 2 left joins, 46 columns, heavy text formatting | 1,127 jobs, 434,018 rows, 0 differences | T-SQL string functions in C#; see below |
| 2026-10-12 | `reporting_migrate.JobRosters_RecruitingReport_DumpExcel` | One select from teams, 7 inner + 2 left joins, 26 columns, sorted on uniform number as an integer | 1,127 jobs, 546,331 rows, 0 differences; on 3 jobs the procedure errors and EF does not | First behaviour change; see below |
| 2026-10-12 | `reporting_migrate.JobRosters_RecruitingReport_Public` | Same rows as the admin dump, 9 public-safe columns | 1,127 jobs, 546,331 rows, 0 differences; same 3 jobs where the procedure errors | Sibling layout over the admin dump's query; nothing new |
| 2026-10-12 | `reporting.ExportReportsHistory` | One select, 4 inner joins, 4 columns | 1,127 jobs, 5,509 rows, 0 differences | Group of eight; see below |
| 2026-10-12 | `reporting.PlayerParentMailingData` | One select, 2 inner joins, 8 columns | 1,127 jobs, 551,815 rows, 0 differences | Group of eight |
| 2026-10-12 | `reporting.StaffExport` | One select, 2 inner joins, 9 columns | 1,127 jobs, 19,429 rows, 0 differences | Group of eight |
| 2026-10-12 | `reporting.StepsCoaches_Export` | One select with a correlated count, 17 columns | 1,127 jobs, 4,506 rows, 0 differences | Group of eight |
| 2026-10-12 | `utility.MaxExposure_ClubCoaches` | One select, 3 inner joins, 15 columns | 1,127 jobs, 638,296 rows, 0 differences | Group of eight |
| 2026-10-12 | `utility.sibling_report` | One select joined to a grouped subquery (`having count(*) > 1`), 3 columns | 1,127 jobs, 102,459 rows, 0 differences | Group of eight |
| 2026-10-12 | `reporting.JobClubRepContacts` | One select, 3 inner joins, 11 columns | 1,127 jobs, 10,010 rows, 0 differences | Group of eight |
| 2026-10-12 | `reporting.CathyCampCheckinWithVacAndMedform` | One select, 6 inner joins, 12 columns, `not like` filter | 1,127 jobs, 550,018 rows, 0 differences | Group of eight |
| 2026-10-12 | `reporting.JobPushTeamResultsSummary` | Two result sets: a count, and a grouped count with a left join | 1,127 jobs, 24,770 rows, 0 differences | First two-sheet export; see below |
| 2026-10-12 | `reporting.TournamentExportClubrepsAndCoachesEmail` | `select distinct` over a `union` of two selects, 7 columns | 1,127 jobs, 16,235 rows, 0 content differences; 2 rows differ in spelling only | `DISTINCT` under a case-insensitive collation; see below |
| 2026-10-12 | `reporting.TLCContactsHx` | Customer-scoped `select distinct`, 8 inner joins, 8 columns | 80 customers, 277,492 rows, 0 content differences; 1,341 rows differ in spelling only | Same |
| 2026-10-12 | `reporting.CathyCampBreakoutPerJob_AmtPaid` | Customer-and-year-scoped `select distinct`, 6 inner joins, 15 columns | 275 customer-years, 27,569 rows, 0 differences | Same |
| 2026-10-12 | `reporting.AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam` | Customer-scoped `group by … with rollup`, 5 columns | 80 customers, 33,283 rows, 0 differences | `WITH ROLLUP` rebuilt in C#; see below |
| 2026-10-12 | `utility.MaxExposure_CountsAtYear_ByTeam` | Fixed-customer grouped count with year-shifted date test, 5 columns | 1 run (ignores its job), 620 rows, 0 content differences; 4 rows differ in spelling only | Hard-wired scope and date arithmetic; see below |
| 2026-10-12 | `reporting.ThePlayers_PlayerShowCaseExport` | One select, 8 inner joins, 43 columns, waitlist filter | 1,127 jobs, 550,018 rows, 0 differences | Three layouts over one query; see below |
| 2026-10-12 | `reporting.ThePlayers_PlayerShowCaseExportII` | Same without the two honors columns, 41 columns | 1,127 jobs, 550,018 rows, 0 differences | Same |
| 2026-10-12 | `reporting.ThePlayers_PlayerShowCaseExport_IncludeInactive` | Same joins, no filter, 12 columns | 1,127 jobs, 638,296 rows, 0 differences | Same |
| 2026-10-12 | `reporting.CustomerEndUsersDump` | Customer-scoped `union` of club reps and rostered registrants, 21 columns | 80 customers, 559,664 rows, 0 content differences; 109 rows differ in spelling only | Union with null-filled columns; see below |
| 2026-10-12 | `reporting.Customer_RegistrationAccounting_Records` | Two branches on a hard-wired job id; 6 inner + 3 left joins; 21 or 19 columns; no `ORDER BY` | 80 customers, 131,416 rows, 0 differences; hard-wired job, 36,995 rows, 0 differences | `if`/`else` procedures, `FORMAT`, dynamic sheet name; see below |
| 2026-10-13 | `reporting.Schedule_Export_ToTournyMachine` | WRITER: runs `Leagues.UpdateTournyTeamFullNames` first; then 8 columns from the schedule | Proc NOT executed (it writes). Against a read-only restatement: 1,126 jobs, 51,250 rows, 0 differences, 0 reordered | Made read-only by ruling; see below |
| 2026-10-13 | `reporting.Schedule_Export_Teams_ToTournyMachine` | WRITER, same rewrite; two `DISTINCT` sheets, 5 inner joins, join on `IN (T1_ID, T2_ID)` | Proc NOT executed. Against a read-only restatement: 1,126 jobs, 53,526 rows over both sheets, 0 differences | Made read-only by ruling; see below |
| 2026-10-13 | `reporting.CathyCampBreakoutPerJob_PreviousMonth` | `getdate()`-relative month; two derived tables left-joined and re-aggregated; table variable; appended total row | 1,126 jobs, 2,502 rows, 0 differences (no `ORDER BY`) | Decimal scale rules; see below |
| 2026-10-13 | `reporting.GetJobTransactionRollup` | Table variable feeding 5 sheets; 6 inner + 2 left joins; two `GROUP BY ROLLUP`; `FORMAT(x, 'C')` | 1,114 jobs, 631,790 rows, 3 rows differ (emoji in a name, see below); proc ERRORS on 12 other jobs | Second and third behaviour changes; see below |
| 2026-10-13 | `reporting.RefAssignmentQA` | Table variable feeding 2 sheets; 6 inner joins; correlated `MIN` subquery; `DATEDIFF`; `GROUP BY … HAVING` | 1,126 jobs, 135 rows over both sheets, 0 differences, 0 reordered | `DATEDIFF`, default date text; see below |
| 2026-10-13 | `utility.AmericanSelect_CountsAtYear_ByTeam` | Three table variables; `PATINDEX`/`SUBSTRING` number parsing; date built by text `REPLACE`; self-join of two counting passes | 79 customers, 503 rows, 0 differences, 0 reordered | Proc sits at the app's 30 s timeout; see below |
| 2026-10-13 | `reporting.RegsaverRegistrants_Charlie` | Ignores its job; hard-wired customer list; `money / 2`; 2 sheets | Run once, 9,628 rows over both sheets, 0 differences | `money` division truncates; see below |
| 2026-10-13 | `reporting.TournyYearOverYearClubTeams` | `GROUPING SETS` into a temp table, then a DYNAMIC `PIVOT` with data-driven columns | 79 customers, 8,190 rows over both sheets, 0 differences, 27 spelling-only; proc ERRORS for 1 customer | First dynamic pivot; see below |
| 2026-10-13 | `utility.JobCloneQA` | 46 single-row inserts into a `varchar` table variable, every type implicitly cast to text | 1,126 jobs, 51,796 rows, 7 rows differ (non-1252 text, see below) | Implicit casts to text; see below |

## UI passes

| Date | Batch | Result |
|---|---|---|
| 2026-10-12 | STEPS, YJ, E120, Liberty | All four run from the Reports Library and downloaded; details below |
| 2026-10-12 | Seven camp exports, plus the batch 1 four again after the move into the layout table | All eleven run and downloaded; details below |

Batch 2: the seven camp exports on `stateonelacrosse-campsandclinics-2026` (SuperUser, all-roles
view) each returned 327 rows against 327 expected, with the procedure's sheet name, column count
(17, 20, 6, 5, 9, 9, 11) and header row. The batch 1 four were re-run and matched their earlier
results. No procedure's execution counter moved. `camp_excelexport_summer` is on no open job's
shelf, so it has no UI run; it is parity-proven only.

Batch 1, run through the browser against the local API:

| Report | Job | Logged in as | Rows in file | Rows expected | Columns |
|---|---|---|---|---|---|
| STEPS | `lftc-summer-2026` | SuperUser, all-roles view | 10,050 | 10,050 | 40 |
| YJ | `yj-fallsignups-2026` | SuperUser, all-roles view | 1,501 | 1,501 | 36 |
| E120 | `irish-prospect-camp-december-2025` | Director | 175 | 175 | 39 |
| Liberty | `tlc-players-2026-2027` | SuperUser, all-roles view | 18 | 18 | 41 |

Sheet names and header rows matched the procedures' in every file. The procedures' execution
counters in `sys.dm_exec_procedure_stats` were identical before and after the four runs, so the
downloads were served from EF and the procedures were not called.

- **The execution counter is the proof that the new path ran.** The file is identical by design,
  so nothing in the download can tell the two paths apart; the counter can.
- **A UI check needs a job that is still open to its users.** Expired jobs do not appear in role
  selection, so pick the job from `Jobs.Jobs` where `ExpiryUsers` is in the future.

### Get_JobPlayers_STEPS_Excel

- **An inner join on a nullable foreign key is a filter.** The procedure inner-joins
  `Leagues.divisions` on `teams.divID`, which is nullable; `t.DivId != null` reproduced it exactly
  on every job. League and agegroup are non-null keys and need no filter.
- **An empty job exports a headerless sheet.** The procedure path learns column names from the first
  data row, so no rows means no header. The EF path returns the bare sheet in that case to match.
- **Row order is only defined up to ties.** 3,478 rows (0.6%) came back in a different position, all
  of them ties on the full sort key (agegroup, team, last name, first name). Compare rows as a set
  and compare the sort key by position; do not compare whole rows by position.
- **`convert(date, …)` belongs in the mapping.** The DTO keeps the full `DateTime`; the sheet
  builder applies `.Date`, and the reader returns a SQL `date` as a `DateTime` too, so types match.
- **The harness needs `UseNetTopologySuite()`** on the context options (spatial column on `Fields`),
  and `FileStorageOptions` has `required` members, so it is created uninitialised.

### Get_Staff_ForExcelExport

- **A LEFT join is an optional navigation.** `r.AssignedTeam!.TeamName` and
  `r.AssignedTeam!.Agegroup.AgegroupName` translate to LEFT joins because `AssignedTeam` is
  optional, and the required `Agegroup` hanging off it becomes a LEFT join too. No explicit
  `DefaultIfEmpty()` needed.
- **Column names come from the SELECT list as typed, not from the table.** `u.Username` returns a
  column named `Username` though the table column is `UserName`. Read the names off the procedure
  (run it once and list `GetName(i)`), do not infer them from the entity.
- **Run parity over every job with any registration**, not only jobs with players; this report's
  rows live on jobs the player filter would have skipped (1,127 jobs against 870).
- **A quarter of the rows are ties here** (the same person holding several staff registrations),
  so comparing by position would have failed loudly for no reason.

### Get_Player_Transactions_ForExcelExport

- **`convert(char(19), d, 120)` is a formatted string, not a date.** The cell is text in the
  export, so the service formats it (`yyyy-MM-dd HH:mm:ss`, invariant culture) and the DTO keeps
  the `DateTime`. Sorting still uses the real datetime in SQL, as the procedure does.
- **Joins that select nothing are still filters.** The procedure inner-joins the family user and
  `Families` and reads no column from either. They stay in the LINQ as explicit `join`s; EF keeps
  an explicit join even when its range variable is unused.
- **Joining a non-null key to a nullable one needs a cast**: `(Guid?)r.RegistrationId equals
  ra.RegistrationId`.
- **`money` columns compare cleanly as `decimal`** once trailing zeros are normalised in the
  comparison (`G29`); the reader returns scale 4.

### Get_DiscountedPlayers

- **Non-nullable columns read through a LEFT join must be cast to nullable.** `dc.active` and the
  two code dates are `bool` and `DateTime` on the entity but null when no code row matches, so the
  projection is `(bool?)r.DiscountCode!.Active`. Without the cast EF throws on the first
  unmatched row.
- **"`x is not null and x != 0`" on a non-null column is just `!= 0`.** Check the entity's
  nullability before copying a null test across.

### The JobPlayers layout family (YJ, E120, Liberty)

- **Procedures cut from one legacy superset share one query.** STEPS, YJ, E120 and Liberty have
  identical joins, filter and order and differ only in column list, so there is one repository
  method (`GetJobPlayersExcelRowsAsync`) and one DTO; each layout is a header list plus a row
  projection in the service. Converting a sibling is then a few lines and cannot drift on row set.
- **T-SQL `a + ' ' + b` is null if either side is null; C# `+` treats null as empty.** The
  `Player`, `Mom` and `Dad` columns are built in the service with a `SqlConcat` helper that returns
  null in that case. Doing it in C# rather than in the query also keeps it provider-neutral
  (Postgres `||` is null-propagating too, but `CONCAT` is not on either engine).
- **Twelve procedures, one query.** The eight camp exports are subsets of the same legacy
  superset as the four player layouts. All twelve now live in one table in `ReportingService`
  (`JobPlayersLayouts`): procedure name to sheet name, header list and row projection. Adding a
  layout is one entry; the eight camp exports were committed together for that reason.
- **Before converting a procedure, check whether it is a sibling of one already done.** Compare
  its FROM, WHERE and ORDER BY against the converted ones first. The May 2026 Crystal retirement
  (`73c9e2c98`) cut many procedures from a few shared sources, so families are the norm.
- **`left(x, n)` needs a null-safe helper** (`SqlLeft`): null stays null and a short value comes
  back whole, where C# range slicing would throw.
- **The harness goes through the routing seam** (`BuildEfReplacementSheetsAsync`), so a procedure
  name that is misspelled in the layout table fails parity instead of silently staying on SQL.
- **Layouts that omit a sort-key column are still order-checked** on the key columns they do
  carry; tied rows share the whole key, so they share any part of it.
- **Re-prove the earlier layout when the shared query changes.** STEPS was re-run after the DTO
  widened; still 0 differences.

### PlayerStats_ParisiResults

- **SQL `x != 'value'` is false for a null `x`; EF's translation is not.** EF adds `OR x IS NULL`
  to keep C# semantics, which would let through rows the procedure drops. Write the null test
  explicitly (`d.DivName != null && d.DivName != "Unassigned"`).
- **A join whose table is filtered on must be an explicit join**, not the `DivId != null`
  shorthand used where the division is only a row filter.
- **`float` columns arrive as `double` on both paths**; no conversion needed.

### UsageByJobAndRegistrant

- **A LEFT join on a unique key that selects nothing can go.** The procedure left-joins the
  assigned team and reads no column from it; it can neither drop nor multiply rows. This is the
  opposite of the INNER joins kept in Player Transactions, which do filter.
- **The harness now runs every job**, not every job with a registration, since this report and
  the team and schedule ones do not start from registrations.

### League_Teams

- **`convert(varchar, <uniqueidentifier>)` is upper-case; `Guid.ToString()` is lower-case.** The
  sheet builder upper-cases it. Postgres renders a `uuid` lower-case, so doing this in C# is what
  keeps the file the same on both engines.
- **`replace(x, char(13), ',')` is a plain character replace** and moves to the sheet builder.
- **The reviewed procedure had already lost its side effect.** The legacy source back-filled
  `teams.FieldID1` before selecting; the May copy dropped that and made the field a LEFT join,
  so this one was a pure read by the time it got here.

### Get_Players_ForExcelExport_AllCustomers

- **A variable assigned from a lookup becomes its own query.** The procedure reads the job's
  customer into `@customerID` first; the repository does the same and returns no rows when the
  job does not exist (the procedure's null variable matches nothing).
- **`getdate()` becomes a `DateTime.Now` parameter**, taken once in the repository. Both are the
  database server's local clock on this box; on a split app/database deployment they would be
  two clocks.
- **A cross-job report makes the parity run long**: each of the 1,127 calls returns a whole
  customer, 10.8 million rows in all and 46 minutes. Nothing to fix, but run it in the background.
- **Not a sibling of the job-players family despite the look**: no division join, and
  `club_name` is the registration's own with no club-rep fallback, so it has its own query.
- **No UI run possible**: the report is on no open job's shelf.

### TeamsFieldDistribution

- **Group on the value, format in the sheet builder.** The procedure groups on
  `convert(char(10), G_Date, 101)`; grouping on the date part is the same grouping, and the
  MM/DD/YYYY text is produced afterwards in C#.
- **Sorting on that text is not chronological.** MM/DD/YYYY as a string sorts month, day, year,
  so the query orders by those three parts in that order. Ordering by the date would have put a
  January 2027 game after a December 2026 one where the procedure puts it before.
- **Keep `GROUP BY` and `ORDER BY` in SQL.** The database collation decides which names are the
  same group and how they sort; doing either in memory would use different rules.
- **`t.teamID in (s.T1_ID, s.T2_ID)` is a correlated `Where` inside the `from`**, which EF turns
  into an inner join on the OR.
- **`count(*)` is `int`** on both paths.

### JobRosters_ExportTournament

- **Fallbacks stay in the query, formatting moves to the sheet builder.** `coalesce(a, b)` is
  `a ?? b` in the projection and translates on any provider. Phone dashes, `lower()`, date text
  and the yes/no flag are done in C# from the raw values.
- **Each T-SQL string function has one null-safe helper**: `SqlLen` (trailing spaces do not
  count), `SqlSubstring` (1-based, cut short at the end), `SqlLeft`, `SqlRight`, `SqlConcat`.
  Use them instead of C# slicing, which throws where T-SQL returns a shorter string.
- **`x = 'literal'` in the procedure is case-insensitive and ignores trailing spaces** under this
  database's collation; `SqlEquals` reproduces that for comparisons done in C#. Postgres compares
  case-sensitively by default, so this is a behaviour the port would otherwise have changed.
- **A LEFT join chain is `join … into … DefaultIfEmpty()` twice**, the second keyed on the first's
  nullable row (`uF.Id equals f.FamilyUserId`), exactly as the procedure chains them.
- **A known defect is preserved, with a comment.** The `sat` column tests `satVerbal is null`
  where "is not null" was meant, so it is always null or empty. The EF path returns the same;
  changing a report's output is a separate decision from moving it.
- **`order by … desc` on one key** is `descending` on that key only.

### JobRosters_RecruitingReport_DumpExcel

- **First behaviour change: the procedure fails on three jobs and the EF path does not.** Its
  `ORDER BY` does `CONVERT(int, …)` on any uniform number `ISNUMERIC` accepts, which raises error
  248 on a value too large for an int (`35000015218568`) and 245 on text `ISNUMERIC` accepts but
  `int` does not. The whole export fails for that job. The EF path treats such a value as
  non-numeric and returns the report. Nothing can depend on an error, so this was taken as is;
  it is the one place so far where old and new differ.
- **`ISNUMERIC` + `CONVERT(int)` is SQL Server-only, so the sort is finished in C#.** The query
  orders by agegroup, team, last, first; the repository then stably re-sorts each team's run of
  rows on the parsed number, nulls first as SQL sorts them. Team boundaries are detected with
  the same case- and trailing-space-insensitive comparison the collation uses.
- **The harness must compare the sort key the procedure sorts on.** Six jobs first showed
  reorders: the same player twice on a team with different uniform text and the same numeric
  key. Those are ties; the harness now compares the parsed number, and a read-only query
  confirmed the tied-row counts matched the reported positions exactly (2, 2, 2, 2, 2, 4).
- **The harness records jobs where the procedure itself errors** instead of aborting.
- **A duplicate column name is legal in a result set and in the sheet** (`grad_year` appears
  twice); the header list simply repeats it.
- **`coalesce(x, '0')`, `replace(x, '#', '')` and unguarded `substring` phone formatting** all
  move to the sheet builder.

### The group of eight simple exports

`ExportReportsHistory`, `PlayerParentMailingData`, `StaffExport`, `StepsCoaches_Export`,
`MaxExposure_ClubCoaches`, `sibling_report`, `JobClubRepContacts` and
`CathyCampCheckinWithVacAndMedform` were written together, proven in one run and committed
together. All eight matched on the first run, which is what the earlier lessons bought.

- **The routing seam is now a lookup table** (`EfExports` in `ReportingService`): procedure name
  to sheet builder. A conversion is one entry, one builder, one repository method and one DTO.
- **`like '%X%'` is case-insensitive on SQL Server and case-sensitive on Postgres.** The camp
  check-in's waitlist filter is written as `name != null && !name.ToUpper().Contains("WAITLIST")`,
  which behaves the same on both.
- **A correlated `select count(*)` is a correlated LINQ `Count()`** in the projection.
- **A derived table with `group by … having count(*) > 1` is a grouped `IQueryable`** used with
  `Contains`; it stays one SQL statement. A null grouping key can never match the outer join, so
  it is filtered out of the inner set explicitly.
- **Defects carried over, each with a comment**: `MaxExposure_ClubCoaches` builds `Dad` from the
  father's first name twice; `sibling_report` names its sheet "Counts Per Team".
- **The harness takes several procedures per run.** One build and one suite run serve the group.

### The aggregate group (push summary, team contacts, contact history, camp breakout, rollup, MaxExposure counts)

- **A procedure with several result sets is several sheets.** `EfMultiSheetExports` holds those;
  the builder returns the list in the procedure's order. A `select count(*)` result always has
  its one row, so that sheet is never headerless.
- **`DISTINCT` and `GROUP BY` under a case-insensitive collation keep one spelling of several
  that compare equal, and SQL Server does not define which one.** `Smith` and `SMITH`, or a
  name with and without a trailing space, are one row; which text survives depends on the plan,
  and it changed between two runs of the same procedure. The harness therefore compares rows
  exactly first and, where that differs, again ignoring case and trailing spaces, and reports
  the gap as "spelling only". These are not content differences. **On Postgres the default
  comparison is case-sensitive, so these exports will return both spellings as separate rows
  unless the columns get a case-insensitive collation; that is a port decision, flagged here.**
- **Keep `Distinct()`, `Union()` and `GroupBy()` in the query**, so the database decides
  equality. EF translates all three over a projection.
- **`WITH ROLLUP` is SQL Server syntax; the subtotals are built in C#.** The query returns the
  per-team counts in key order; the builder emits a grand total, then a subtotal row ahead of
  each job, job-year and agegroup run. Subtotal rows carry nulls, which SQL sorts first, so
  "subtotal before its details" is the procedure's order. An empty input yields no rows at all,
  not a lone grand total. Runs are split with the collation's notion of equal (`RunsOf`).
- **Customer-scoped procedures are checked once per customer** (or per customer and year), not
  once per job; the harness takes a scope prefix. The result cannot differ between two jobs of
  one customer, and this turns a 46-minute run into seconds.
- **`DATEADD(year, n, ts)` is `ts.AddYears(n)` and `CONVERT(int, text)` is `Convert.ToInt32`**
  inside the query; `SUBSTRING(x, 1, CHARINDEX(':', x) - 1)` is
  `x.Substring(0, x.IndexOf(":"))`. All three translate.
- **Defects and hard-wiring carried over, each with a comment**: the team-contacts club-rep half
  joins a registration id to a team id and so returns nothing; the rollup's `teamName` column
  shows the agegroup; the MaxExposure counts ignore the job, read one fixed customer id and
  stop at 2026. The fixed id and year list are named constants in the repository.

### The Players showcase exports and CustomerEndUsersDump

- **Three procedures, one query with a flag.** The two showcase exports and the include-inactive
  one share their joins; the first two add "active Player outside a waitlist agegroup". The
  repository method takes `activePlayersOnly`, and the "II" layout is the first minus two columns.
- **A `union` whose halves select `null as X` translates** when both halves project the same DTO
  with the nulls assigned in the same positions; EF emits typed nulls.
- **De-duplicate on what the procedure de-duplicated on.** The dump is distinct on the date of
  birth as MM/DD/YYYY text, so the query projects the date without its time part; projecting the
  raw value would have kept two rows that differ only in a time of day.
- **`convert(varchar(10), ts, 101)` on a non-null column** is a plain `ToString("MM/dd/yyyy")`.

### Customer_RegistrationAccounting_Records

- **An `if @jobId = '<guid>' … else …` procedure is two layouts over one query.** The hard-wired
  job id and its customer list are named constants in the repository; the DTO says which branch
  ran and the sheet builder picks the layout. Both branches were proven: once per customer, and
  once for the hard-wired job on its own (`TSIC_PARITY_JOB`).
- **`FORMAT(d, 'yyyy-MM-dd hh:mm tt', 'en-US')` is `d.ToString(sameFormat, en-US)`.** T-SQL
  `FORMAT` is .NET formatting underneath, so the format strings carry over unchanged.
- **`convert(decimal(10, 2), money)` rounds half away from zero**:
  `Math.Round(x, 2, MidpointRounding.AwayFromZero)`, not C#'s default banker's rounding.
- **A sheet name built from data can be missing.** `'QA Test: ' + null` is null, the reader then
  sees no marker and names the sheet "SearchResults". The builder does the same when the job or
  its customer name is absent, and trims and truncates the name as the `varchar(80)` and the
  reader did.
- **A LEFT join with two conditions is a correlated `Where(…).DefaultIfEmpty()`** in the `from`.
- **No `ORDER BY` means any order is correct.** The EF path orders by record date so the file is
  stable from run to run; the harness skips the order check when given no sort key.

### The two TournyMachine exports (the first writers)

Catalogue titles: "Export Schedule (Excel)" and "Teams Export (Excel)" / "Teams Export (TM)
(Excel)". Neither title says TournyMachine; only the procedure names do.

- **Ruling (Todd, 2026-10-13): these exports do no rewriting.** Both procedures began by running
  `Leagues.UpdateTournyTeamFullNames`, which overwrote a game's stored team name with
  `club:team` wherever the two had drifted. An export that edits the schedule is a side effect
  nobody asked for. The EF versions compute the name the rewrite would have produced, put it in
  the file, and store nothing. The schedule keeps whatever names it had.
- **The rewrite rule, restated as a read:** a side takes `club:team` when its team is in the
  game's job, has a club rep registration, both names are non-null, the stored name is non-null,
  and the two differ. Otherwise the stored name stands. "Differs" is evaluated in the database
  so it means what it meant to the procedure (case-insensitive on SQL Server; PORT FLAG, a
  case-only drift would count as drift on Postgres).
- **One deliberate difference.** The procedure's `UPDATE … FROM` matched a game once per side,
  and SQL Server applies only one of the two matches, so a game drifted on BOTH sides was fixed
  on one side per run. The EF version resolves both sides at once. On the local copy 61 games
  are drifted on both sides.
- **The proof is weaker than the others, and says so.** A procedure that writes cannot be run
  under the read-only rule, so parity ran against a read-only restatement of each procedure's
  `SELECT` (same joins, filters, columns and order, with the names resolved by the rule above).
  That proves joins, filters, formats, `DISTINCT` and order. It does not independently prove
  the name rule, because both sides state it. 329 of 51,250 team-versus-team games on the local
  copy carry a drifted name, across 14 of 213 scheduled jobs, so the rule was exercised.
- **`t.teamID in (s.T1_ID, s.T2_ID)` is a `from … Where(a || b)`** in LINQ, an inner join on an
  OR condition.
- **Preserved:** the pool filter's `'Dripped Teams'` misspelling, which excludes nothing.
- **Formats:** `convert(varchar, d, 20)` is `yyyy-MM-dd HH:mm:ss`;
  `convert(char(5), convert(time, d))` is `HH:mm`.

### Previous-month camp breakout, job transaction rollup, referee assignment QA

- **Table variables are just staging.** All three load a table variable and report from it. The
  EF version fetches the same rows once and builds every sheet from them in the service.
- **Know the column's real type before copying arithmetic.** The table variable declared
  `money`, but `Registration_Accounting.payamt` is `decimal(18,2)`. `SUM` of that is
  `decimal(38,2)`, and at precision 38 SQL Server has no room to widen the scale, so
  `amount - (5.00 * n) - (0.03 * amount)` comes back at TWO decimals, rounded half away from
  zero, per joined row, before the outer `SUM`. Meanwhile `0.03 * amount` alone keeps four.
  First parity run: 50 jobs off by fractions of a cent in one column. `sql_variant_property(expr,
  'Scale')` on a read-only probe answers this in one query. PORT FLAG: Postgres `numeric` does
  not cap at 38 and will not round here; the C# now owns the rounding, so the port inherits it.
- **Preserved:** the previous-month breakout charges the per-registrant fee once per payment
  method a team was paid by (the fee sits inside a `SUM` over the joined rows).
- **`getdate()`-relative reports** take the clock once in the service and pass month and year to
  the repository. Parity only exercises the month it is run in (September 2026 here).
- **`GROUP BY ROLLUP(x)`** on one column is a row per value then a grand total, and no rows at
  all when nothing qualifies.
- **`FORMAT(x, 'C', 'en-US')` writes negatives as `($5.00)`.** The runtime's own en-US data
  writes `-$5.00`, so the format is pinned (`CurrencyNegativePattern = 0`).
- **An aggregate with no `GROUP BY` always returns one row**, null when nothing matched, so those
  sheets are never headerless.
- **`DATEDIFF(minute, a, b)` counts minute boundaries crossed**, not elapsed whole minutes:
  truncate both to the minute, then subtract.
- **`convert(varchar, datetime)` with no style** is `Oct  5 2026  9:05AM`: day and hour
  right-aligned in two characters.
- **Behaviour change 2: the rollup procedure fails outright on 12 jobs (2 of them open).** Its
  table variable declares `teamName varchar(80)`, and a longer team name raises error 2628
  ("String or binary data would be truncated"). The EF version returns the report.
- **Behaviour change 3: the rollup procedure garbles characters outside the Windows-1252 set.**
  The same `varchar` columns turn an emoji in a first name into `??`. Three rows of 631,790
  differ for that reason and no other; the EF version returns the name as stored.

### American Select counts, RegSaver payouts, year-over-year club teams, job clone QA

- **A dynamic `PIVOT` is a loop in C#.** The procedure built its column list from the data
  (`STUFF … FOR XML PATH`), then `EXEC`'d a pivot. In C#: the distinct years are the columns,
  each tournament-and-club run is a row, a cell is the sum for that year or null. The
  `GROUPING SETS` subtotal is one more row per tournament. No dynamic SQL survives, which is the
  point: Postgres has no `PIVOT` at all.
- **Never convert text to a number inside the query when a bad value exists anywhere.**
  `CONVERT(int, j.year)` is evaluated wherever the optimiser likes, including on rows the
  `WHERE` would have thrown away. One job has the year `all `; the first EF version failed on it
  for every customer, and the procedure itself fails (error 245) for that job's customer. The
  EF version does the conversion in C#, on qualifying rows only.
- **Keep the database's ordering without comparing text in C#.** When rows must be merged or
  re-sorted after the fetch, have SQL return them ordered by the text columns, number the runs
  (a rank), and sort on the ranks. The file then follows the server's collation exactly, on
  either provider.
- **`money / 2` truncates past four decimals** (`0.0003 / 2` is `0.0001`); it does not round.
- **Implicit casts to `varchar`:** `bit` is `1`/`0`, `decimal(18,2)` keeps its two decimals,
  `datetime` takes the default `Oct  5 2026  9:05AM` form.
- **`REPLACE` is case-insensitive under the database collation**; C# needs
  `StringComparison.OrdinalIgnoreCase`. PORT FLAG: Postgres `replace` is case-sensitive.
- **A procedure that ignores its parameter** (RegSaver payouts take a job id and never use it)
  gets a builder that ignores it too; the harness runs it once (`@once|`).
- **The clock again.** American Select "to date" is tomorrow's month and day in each job's own
  year, built in the procedure by swapping year digits in a date string. The repository takes
  the caller's clock and builds one cutoff per job year.
- **Performance, observed not chased:** the American Select procedure ran past the app's 30 s
  command timeout on its own customer on the first parity run, so parity called it directly
  with a longer timeout (the harness prints `REFERENCE` for that run; it was the real
  procedure). The EF version makes one grouped query per job year.
- **Behaviour change 3 again:** `JobCloneQA` holds values in `varchar`, so a refund policy
  containing a character outside Windows-1252 came out with `?` in its place on 7 jobs. The EF
  version returns the text as stored.
- **Behaviour change 4:** `TournyYearOverYearClubTeams` fails (error 245) for the one customer
  owning the job whose year is `all `. The EF version returns that customer's report.
