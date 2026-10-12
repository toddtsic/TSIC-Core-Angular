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
