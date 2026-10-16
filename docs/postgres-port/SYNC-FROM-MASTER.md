# Postgres port: syncing master into the branch

`feature/postgres-port` lives until the Postgres cutover. Until then, nothing goes from the
branch to master; master's changes come *into* the branch by merge, on a regular cadence.
The longer between syncs, the bigger and riskier each one gets.

## The routine

```powershell
git switch feature/postgres-port
.\scripts\pg-port-sync-check.ps1                  # 1. preview (changes nothing)
.\scripts\pg-port-sync-check.ps1 -Merge -Verify   # 2. merge + full verification (~10-15 min)
```

The preview **fetches origin first**. Local `master` goes stale; always compare against
`origin/master`. It then shows:

| Check | What to do with it |
|---|---|
| Commits master added | Context: what's coming in |
| Dry-run merge | Conflicts listed: resolve by hand (`git merge origin/master`), then rerun with `-Verify` |
| Files changed on both sides | Read these after merging; an auto-merge can still be wrong |
| SQL Server-only constructs in master's new C# | Each needs the same treatment the port gave the original (see below) |
| Schema changes (`.sql`, `Entities/`, DbContext) | New tables go in `tools/pg-migrate/tables-tsicv5.txt`; then `-Verify` re-runs pg-migrate |

`-Merge` makes a local merge commit (never pushed), then builds and runs the tests.
`-Verify` refreshes Postgres from SQL Server with `tools/pg-migrate` (this **drops and recreates
the local `tsicv5`/`tsiclogs` Postgres databases**, so stop any F5 session running on Postgres first;
add `-SkipMigrate` to re-check without refreshing), then runs both A/B checks:

- `tools/pg-ab/equivalence`: every repository method the port rewrote, run on both databases.
  Must report `0 differing`.
- `tools/pg-ab/sweep`: every safe GET endpoint on both databases, compared against
  `tools/pg-ab/sweep/baseline.json`. Must report `No change from baseline`.

## When the scan flags something

Treat it the way the original code was treated (details in `AB-SCAFFOLDING.md`):

| Flagged | Fix |
|---|---|
| `EF.Functions.DateDiff*` / `DateFromParts` / `Collate` | Provider-neutral LINQ (see `RegistrationRepository` bucketing, `CustomerJobRevenueRepository` month keys) |
| `SqlConnection`, `SqlBulkCopy`, raw SQL, stored procs | Postgres arm beside the SQL Server one; SQL Server arm marked `PG-AB` |
| `SqlException` error numbers | `TSIC.Infrastructure.Data.DbErrors` |
| `sp_getapplock` | `TSIC.Infrastructure.Data.DbAppLock` |
| `LIKE` with `[...]` character classes | Rewrite without them (Postgres reads them literally) |
| `HasColumnType("money"/"datetime")` | Already handled by `SqlDbContext.Postgres.cs`; check new store types |

Then add a case for it to `tools/pg-ab/equivalence` so it stays covered.

## When the sweep changes from baseline

- A real difference: fix it, as above.
- An expected change (e.g. master added an endpoint, or fixed a bug on one side): regenerate the
  baseline and commit it with the merge: `.\scripts\pg-port-sync-check.ps1 -Verify -WriteBaseline`.

## Known, accepted differences (in the baseline)

- **Trailing spaces**: SQL Server ignores them in comparisons; Postgres does not, so
  `"Club "` and `"Club"` are two values (registration/team search club filters, dashboard club count).
  Decision pending: trim in pg-migrate.
- **Sort order**: ties come back in a different order; Postgres does not ignore hyphens and
  apostrophes when sorting. 20-odd endpoints show `ORDER`.
- **Files and HTML error pages** are compared by status only: their bytes carry timestamps and
  stack traces that change every run. Same for the `-- Generated:` line in export scripts.
- **Order is not stable run to run** for ties, so the baseline comparison treats `ORDER` as `SAME`.
- **`Rescheduler/affected-count`, `recipient-count`**: 500 on SQL Server without dates
  (`SqlDateTime overflow`, a master bug); 200 on Postgres.

## History

| Date | Commits | Result |
|---|---|---|
| 2026-10-16 | 22 (incl. `vTxs` → `adn.Txs`) | Clean merge `758554e0`; no SQL Server-only code incoming; equivalence + sweep unchanged; 1293 tests pass |
