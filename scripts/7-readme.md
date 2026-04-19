# Reporting Catalog (7-series)

Installs the Type 2 report catalog (`reporting.ReportCatalogue`) and seeds it
with reports discovered from the 2025-2027 legacy menu system.

## What's here

| File | Purpose | Idempotent |
|---|---|---|
| `7a-discover-legacy-reports.sql` | Discovery query against legacy `Jobs.JobMenus` / `JobMenu_Items`. Source of truth for seeded titles/SPs. | Read-only |
| `7a-discover-legacy-reports.csv` | Captured output of 7a as of seed time (Director + SuperDirector + SuperUser, 2025/2026/2027, `Controller = 'Reporting'`). | - |
| `7b-create-reporting-schema.sql` | Creates the `reporting` schema + `reporting.ReportCatalogue` table. | Yes |
| `7c-seed-reporting-catalog.sql` | Inserts 32 Type 2 catalog rows. Dedupes on `Title`. | Yes |

## Where reports run

- **Type 1 (Crystal Reports, legacy)** - NOT in this table. Hard-coded in the
  Angular frontend. Sunsetting over time as each is re-authored as Type 2.
- **Type 2 (stored-proc -> multi-tab XLSX via EPPlus)** - this table. The
  SuperUser catalog editor writes here at runtime; no deploy required to add
  a new report (SP must already exist in the DB).

## Port to prod - runbook

1. **Back up prod** (or confirm a restore point exists).

2. **Run `7b-create-reporting-schema.sql`** against prod.
   Expect: `Created reporting.ReportCatalogue` or `already exists - skipped`.

3. **Re-scaffold EF entities** so the backend sees the new table:
   ```
   .\scripts\3) RE-Scaffold-Db-Entities.ps1
   ```

4. **Run `7c-seed-reporting-catalog.sql`** against prod.
   - The first result set lists each referenced SP with `OK` or `MISSING`.
     Any `MISSING` means that SP doesn't exist in prod under the expected
     name. Resolve per-SP before go-live:
     - Create the SP in prod, OR
     - `UPDATE reporting.ReportCatalogue SET StoredProcName = '...' WHERE Title = '...'`, OR
     - `UPDATE reporting.ReportCatalogue SET Active = 0 WHERE Title = '...'`
       (hide until resolved).
   - The second result set shows final catalog state with per-row SP status.

5. **Populate per-row metadata** (either via SQL or the SuperUser editor UI):
   - `VisibilityRules` - JSON matching `nav.NavItem.VisibilityRules`; gates
     the report per JobType / sport / feature flag. `NULL` = visible to all
     admins on every job.
   - `ParametersJson` - declares how runtime values (jobId, dates, flags)
     bind to the SP's input parameters. **Required** before the report can
     execute end-to-end.

6. **Deploy backend + frontend** so the reports library UI, the
   `/api/reports/catalog` endpoints, and the Type 1 hard-coded list are
   all present.

## Rollback

If only seed rows exist (no user additions/edits), all are identifiable by
`LebUserId IS NULL`:

```sql
DELETE FROM reporting.ReportCatalogue WHERE LebUserId IS NULL;
```

**Do not** drop the schema or table without first confirming no user-added
Type 2 rows exist. Check with:

```sql
SELECT * FROM reporting.ReportCatalogue WHERE LebUserId IS NOT NULL;
```

## Related scripts (outside 7-series)

- `3) RE-Scaffold-Db-Entities.ps1` - regenerates EF entities (step 3 of runbook)
- `5) Re-Set Nav System.ps1` - nav manifest; the "Reports" L1 stub is at
  line 151 there and routes to `reporting/reports-library`.
