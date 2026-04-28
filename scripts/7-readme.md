# Reporting Catalog (7-series)

Installs the Type 2 report catalog (`reporting.ReportCatalogue`) and seeds it
with reports surfaced from the legacy menu system.

## Install order

Three scripts, run in order. All idempotent (skip-existing on Title), safe to
re-run.

| Step | File | Effect |
|---|---|---|
| 1 | `7b-create-reporting-schema.sql` | Creates `reporting` schema + `ReportCatalogue` table |
| 2 | `7c-seed-reporting-catalogue.sql` | Inserts 32 job-scoped reports (`bUseJobId=true`, no role gate) |
| 3 | `7e-seed-cross-customer-reports.sql` | Inserts 13 SuperUser-only cross-customer reports (`bUseJobId=false`, `requiresRoles=['Superuser']`) |

```bash
sqlcmd -S .\SS2016 -d <db> -I -i "scripts/7b-create-reporting-schema.sql"
sqlcmd -S .\SS2016 -d <db> -I -i "scripts/7c-seed-reporting-catalogue.sql"
sqlcmd -S .\SS2016 -d <db> -I -i "scripts/7e-seed-cross-customer-reports.sql"
```

After step 1, re-scaffold EF entities so the backend sees the new table:

```powershell
.\scripts\3) RE-Scaffold-Db-Entities.ps1
```

## Type 1 vs Type 2

- **Type 1 (Crystal Reports, legacy)** — NOT in this table. Hard-coded in
  `src/app/core/reporting/type1-report-catalog.ts`. Each entry maps to a
  `ReportingController [HttpGet("X")]` route and renders a PDF/RTF/Excel.
- **Type 2 (stored-proc → multi-tab XLSX via EPPlus)** — this table. The
  SuperUser catalog editor (`reporting/report-catalogue-editor`) writes
  here at runtime; no deploy required to add a new report (SP must already
  exist in the DB).

## Per-row metadata

Both seeds populate sensible defaults. After install, individual rows may need
tuning:

- **`VisibilityRules`** (JSON, same shape as `nav.NavItem.VisibilityRules`)
  — gates per Sport / JobType / customer / feature flag / caller role.
  `NULL` = visible to all admins on every job. The 13 cross-customer rows
  (7e) carry `{"requiresRoles":["Superuser"]}` so non-SU admins don't see
  them in the library.
- **`ParametersJson`** — declares how runtime values bind to the SP's input
  parameters. Today the only honored field is `bUseJobId` (default `true`,
  set `false` for cross-customer SPs). `NULL` is fine for the 32 job-scoped
  reports.

## Rollback

Seed rows are identifiable by `LebUserId IS NULL`:

```sql
-- Drop only the SU cross-customer rows (7e)
DELETE FROM reporting.ReportCatalogue
WHERE LebUserId IS NULL AND SortOrder >= 1000;

-- Or drop everything seeded (7c + 7e)
DELETE FROM reporting.ReportCatalogue WHERE LebUserId IS NULL;
```

**Do not** drop the schema or table without first confirming no user-added
Type 2 rows exist:

```sql
SELECT * FROM reporting.ReportCatalogue WHERE LebUserId IS NOT NULL;
```

## Related scripts (outside 7-series)

- `3) RE-Scaffold-Db-Entities.ps1` — regenerates EF entities after schema changes.
- `5) Re-Set Nav System.ps1` — nav manifest. The `Reports` L1 stub routes to
  `reporting/reports-library`; cross-customer reports are NOT in the manifest
  (they live in the catalogue and surface via the library).
