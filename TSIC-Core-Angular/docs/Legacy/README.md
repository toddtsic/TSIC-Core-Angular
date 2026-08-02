# Legacy Job-Clone Stored Procedures (archival)

Scripted out of the live SQL Server on 2026-08-02 during the job-clone refactor.
These sprocs were **never in source control** — this folder is the only record of
legacy clone semantics. They are reference material, not deployable code.

| File | Purpose |
|---|---|
| `CloneJob_Unify.sql` | Legacy job clone: copy-every-column via `sys.columns` dynamic SQL (Jobs, JobDisplayOptions, JobOwlImages, Bulletins, Menus, JobAgeRanges, admin Registrations), plus dated force-reset patches (e.g. 8/2024: bulletins inactive, `bRegistrationAllow*` zeroed). Known bug: `@RegForm_from = @RegForm_from` self-assignment — the from-address never applied. |
| `LADT_Clone_Unify_CreateNewLeague.sql` | Legacy LADT clone: League → Agegroups → Divisions → Teams with `#temp`-table ID maps, WAITLIST/dropped exclusion, `@TeamsToClone` modes (0=none / 1=Registration teams / else all). |
| `JobCloneQA.sql` | Legacy post-clone QA: field-by-field readout of a cloned job for manual verification — the ancestor of the new stack's verify-then-release checklist. |

The new-stack implementation lives in `src/backend/TSIC.API/Services/Admin/JobClone*`
and adopts the same copy-everything philosophy with a versioned reset-rules list
(`JobCloneResetRules.cs`).
