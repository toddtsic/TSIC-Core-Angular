# Postgres port: A/B scaffolding

Branch `feature/postgres-port` only. It never merges into master.

The API runs on either database, picked by `DatabaseProvider` (`SqlServer` | `Postgres`) in
appsettings. Everything that exists only to keep SQL Server runnable alongside Postgres is
listed here and marked in code with `PG-AB`. **Cutover = delete every item below**, then
`grep -rn "PG-AB"` must come back empty.

## Switch between databases

- `appsettings.Development.json`: set `"DatabaseProvider"` to `SqlServer` or `Postgres`, or
  override per run with the environment variable `DatabaseProvider=SqlServer`.
- Refresh Postgres from SQL Server: `dotnet run --project tools/pg-migrate` (~3 min; run it a
  second time with the TSICLogs arguments for the logs DB, see `tools/pg-migrate/Program.cs`).

## Scaffolding to delete at cutover

| Item | Where | At cutover |
|---|---|---|
| `DatabaseProvider` switch + Pg* connection-string swap | `TSIC.API/Program.cs` | Delete the block; register with `UseNpgsql` only |
| `UseSqlServer` arms of the three DbContext registrations | `TSIC.API/Program.cs` | Delete |
| `provider=` in the `[STARTUP-CONFIG] db` audit line | `TSIC.API/Program.cs` | Delete |
| `DatabaseProvider` key; SQL Server `DefaultConnection`/`LogsConnection`; `Pg` prefix | `appsettings.Development.json` | Pg strings become the base ones |
| SQL Server arm of each raw-SQL site (step 2, in progress) | see table below | Delete the arm |
| `Microsoft.EntityFrameworkCore.SqlServer(.NetTopologySuite)`, `Microsoft.Data.SqlClient` | `Directory.Packages.props`, csprojs | Remove |

## Kept after cutover (not scaffolding)

- `tools/pg-migrate/`: schema and data migration
- `SqlDbContext.Postgres.cs`: maps scaffolded SQL Server store types to Postgres types
- `Npgsql.EnableLegacyTimestampBehavior` switch in `Program.cs`
- Provider-neutral rewrites: `EF.Functions` date calls; `jobPath` collation

## Raw-SQL sites (step 2)

Every site branches on the active provider (`Database.IsNpgsql()` or the `DatabaseProvider`
setting). The SQL Server arm is marked `PG-AB`. All rows below were verified on both databases.

| Site | SQL Server arm (`PG-AB`, delete) | Postgres arm (keep) | Verified |
|---|---|---|---|
| `Data/DbAppLock.cs`, used by `ScheduleRepository` slot placement and `TeamChatRepository` thread lock | `sp_getapplock` | `pg_advisory_xact_lock(hashtextextended(..))` under a transaction-local `lock_timeout` | Second tx times out while first holds; acquires after commit (both DBs) |
| `TeamChatRepository.NextSeqSql` | `NEXT VALUE FOR teamchat.MessageSequence` | `nextval('teamchat."MessageSequence"')` | Sequence draws on PG |
| `UsageWriterBackgroundService` | `SqlBulkCopy` (CheckConstraints) | Npgsql binary `COPY logs."AppUsage"` (FKs always enforced) | Live API: 5 requests → 5 rows, values and identity correct |
| `AdnReconciliationRepository` month-end | T-SQL temp tables, 3 result sets | `ReconciliationPgSql`: same 3 result sets, same quirks (EDT-only parse, CI `_M` match) | 6 months (~19k txs, EDT + EST) identical |
| `Data/DbErrors.cs`, used by `ReportingService` unique-key handling and `RegistrationRepository` seat-commit retry | `SqlException` 2627/2601, 1205 | `PostgresException` 23505; 40P01 / 40001 (Serializable conflict) | Real duplicate insert classified on both |

Made provider-neutral (no `PG-AB` arm, kept):

- `JobCloneRepository` ancillary walk quotes identifiers with EF's `ISqlGenerationHelper`, and
  walks only tables that exist in the active database (`information_schema.tables`). On
  Postgres that skips EF-mapped tables the migration excluded (e.g. `adn.jobinvoices`,
  `Jobs.emailFailures`); on SQL Server nothing changes. Goes away with the deferred dead-code
  cleanup of unused entities.

SQL Server only, deliberately not ported (unreachable or SQL Server-targeted):

- Reporting stored-procedure paths (`ExecuteStoredProcedureAsync` fallback, `export-bold`,
  `ExecuteMonthlyReconciliationAsync`): no catalog row or caller reaches them.
- `CustomerJobRevenueRepository.GetLegacySprocDataAsync`: QA comparison against legacy procs.
- `NavEditorService` / `WidgetEditorService` export scripts: generated T-SQL text for deploying
  defaults to the (SQL Server) production database; never executed by the app. Revisit at cutover.
