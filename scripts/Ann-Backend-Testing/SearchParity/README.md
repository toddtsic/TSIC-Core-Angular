# Search Parity Audit (Ann's QA recipe)

Verifies that the **new** Search Registrations / Search Teams grids return the
same results the **legacy** system would, for a real job's real data — plus
internal-consistency checks for new-only filters that have no legacy equivalent.

This replaces hand-checking grid results row by row.

## How to run (the easy way)

Open Claude Code and say:

> Run the search parity audit for job `<jobId or jobPath>`

Claude runs both scripts, interprets the output, and writes a report into
`Reports/`. Anything red becomes a punchlist item.

## How to run (by hand)

```powershell
cd scripts\Ann-Backend-Testing\SearchParity
sqlcmd -S .\SS2016 -d TSICV5 -E -W -s"|" -v JobId="<jobId>" -i 01-team-search-parity.sql
sqlcmd -S .\SS2016 -d TSICV5 -E -W -s"|" -v JobId="<jobId>" -i 02-registration-search-parity.sql
```

Find a jobId from a jobPath: `SELECT JobId, JobName FROM Jobs.Jobs WHERE JobPath = '<jobPath>'`

## What it checks

Each script materializes the job's search results twice — once with **legacy
query semantics** (hand-translated from the reference TSIC-Unify controllers),
once with **new query semantics** (hand-translated from the current
repositories) — then diffs them:

| Section | Check |
|---|---|
| T1–T3 / R1–R3 | Membership: same rows returned on both sides |
| T4 / R4 | Field-by-field diffs on shared rows (names, club, pay status, paid/owed, contact info) |
| T5–T9 / R6–R10 | Filter sweeps: per-value counts for every filter dimension (active, pay status, club, LOP, agegroup, role, position) |
| T10 / R11 | Grid aggregate totals (fees / paid / owed) |
| T11 / R12 | Default sort order |
| T12–T14 | New-only team filters (waitlist, scheduled, autopay-failed, payment methods) — counts for eyeballing; no legacy oracle exists |
| R5 | Assignment column: stored legacy string vs new live-composed string |
| R13 | Data sanity: assigned team belongs to a different job |

**Empty diff sections = parity.** The filter sweeps compare *count-per-value*;
since every multi-select filter reduces to equality on a single value, matching
counts for every value proves each single-selection filter returns identical
row sets. Combined filters are pure AND-intersections on both sides.

## Sources of truth (for maintaining the scripts)

- Legacy teams: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Search/SearchTeamsController.cs` → `LookUpQueryResults`
- Legacy registrations: `.../Search/SearchController.cs` → `LookUpQueryResults`
- New teams: `TSIC.Infrastructure/Repositories/TeamRepository.cs` → `SearchTeamsAsync`
- New registrations: `TSIC.Infrastructure/Repositories/RegistrationRepository.cs` → `BuildFilteredQueryAsync` + `SearchAsync`

Both sides are **hand-translated LINQ→SQL**. If a diff appears, first confirm
the translation is faithful to the C# before calling it a bug.

## Known / expected differences (whitelist — NOT bugs)

1. **Assignment format** — legacy stores `Agegroup:TeamName` (colon); new
   composes `ClubRepClub Agegroup TeamName` (spaces) live from the team joins.
   R5 normalizes `:` → space before comparing; only content diffs surface.
2. **Base-set joins** — legacy registrations INNER JOINs Role + User, so a
   registration with a null/dangling RoleId or UserId silently vanishes from
   legacy but appears in new (R2). New behavior is more correct; note but don't
   file unless data looks corrupt.
3. **Teams with dangling agegroupID** would vanish from *new* (INNER JOIN
   Agegroups) but show in legacy (T2). None seen so far; if T2 fires, that IS
   worth a punchlist item.
4. **New-only filters** (AUTOPAY FAILED, SCHEDULED/NOT_SCHEDULED, waitlist
   DDL, payment-method/discount-code, ARB health, VI insurance, USLax, roster
   threshold) have no legacy oracle — T12–T14 print counts for sanity, and the
   in-memory unit tests (`../SearchTeams`, `../SearchRegistrations`) cover
   their logic.
5. **Null-Active multi-select edge** — with BOTH Active+Inactive selected,
   legacy applies no filter (null-bActive rows included); new filters to
   `bActive IS NOT NULL`. Only matters if a job has null-bActive rows (R6
   shows a `(null)` bucket when so).
6. **Phone display formatting** — both format for display; parity compares raw
   `cellphone`.
7. **Sort tiebreakers** — new adds a RegistrationId tiebreaker for stable
   paging; row *content* is unaffected.

## Reports

One markdown file per audit run in `Reports/`, named
`YYYY-MM-DD-<jobPath>.md`. Failures become punchlist items in the usual flow
(currently: Admin-Menus punchlist, AM-xxx).
