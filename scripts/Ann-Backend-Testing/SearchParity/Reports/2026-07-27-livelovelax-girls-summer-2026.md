# Search Parity Audit — Live Love Lax: Girls Summer 2026

- **JobId**: `eb11aca5-9222-4ee3-bc2e-d5337febbe96`
- **JobPath**: `livelovelax-girls-summer-2026`
- **Scope**: 10,212 registrations (9,590 Players, 166 Club Reps, 439 Staff, 13 Directors), targeted filter scenarios
- **Mode**: driven scenarios — Ann/Todd supply jobId + filter params, each run appended here

## Scenario 1 — role: Player (2026-07-27)

Filter: Registration Search, Role = Player (`DAC0C570-94AA-4A88-8D73-6034F1F72F3A`).
Legacy semantics: INNER JOIN Roles/Users base + `RoleId IN (...)`.
New semantics: JobId-only base + `RoleId IS NOT NULL AND RoleId IN (...)`.

| Check | Result |
|---|---|
| Row counts | PASS — 9,590 / 9,590 |
| Membership diffs | PASS — none either direction |
| Field diffs | PASS — none |
| Aggregates (Fees/Paid/Owed) | PASS — identical ($0.00 — free player regs; fees live on club side) |
| Active × PayStatus sub-breakdown | PASS — 9,587 active + 3 inactive, all PAID IN FULL, both sides |
| Dangling/null UserId probe | PASS — none present, so the INNER-vs-LEFT join base difference is moot here |

**Verdict: PASS. Nothing filed.**
