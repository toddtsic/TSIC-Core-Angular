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

| Site | SQL Server | Postgres | Status |
|---|---|---|---|
| (to be filled in as each site is done) | | | |
