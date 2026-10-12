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
