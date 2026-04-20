# Job Setup — Admin Guide

Creating a new season with the **Job Setup** tool (routes: `/{jobPath}/configure/job-clone`).

## When to use what

| Scenario | Flow |
|---|---|
| New season for an existing customer — carry forward most config | **Clone an existing job** |
| New customer with no prior job | **Start blank** |
| Already-created suspended job that needs flipping live | **Release a suspended job** |

Cross-customer cloning is disallowed by the server. Use Blank if a customer has no prior job.

## The safety contract

Every job created by this tool (clone or blank) lands in the same safe state:

- **`BSuspendPublic = true`** — hidden from public at the job's URL.
- **Customer admins (Director + SuperDirector) `BActive = false`** — they can't log in until you activate them. Superuser registrations (TSIC-central staff) are **not** touched; they stay functional.
- **ClubRep edit/delete/add permissions forced ON** — the source may have had these off from post-schedule lockdown; new seasons start in the registration phase where ClubReps need edit access.
- **Processing fee % reset to the current floor** — prevents stale rates carrying forward from jobs cloned years later.

Those four defaults are **non-negotiable** — no toggle, no checkbox. The plan is simple: the job is safe when it's born, and you flip exactly two switches when you're ready to go live.

## The wizard — 7 steps

**Step 1 — Start.** Pick the source (Clone mode) or enter customer/billing/sport info (Blank mode).

**Step 2 — Identity.** Job path, name, year, season, display name. For Clone, also the league name (the server will infer the separator + token order from the source and substitute your values — e.g. `STEPS-Spring-2025` + your inputs becomes `STEPS-Fall-2026`).

**Step 3 — Dates & year-delta preview.** Admin/user expiry dates. For Clone, the preview panel shows every date the server will shift by `targetYear − sourceYear`:
- Job event start/end, ADN ARB start
- Each bulletin's create/start/end
- Each agegroup's DOB min/max, grad year min/max, early-bird window, late-fee window, and name year-token
- Each fee-modifier window

Feb-29 in non-leap years clamps to Feb-28 automatically.

**Step 4 — LADT scope.** How much of the League/Agegroup/Division/Team hierarchy to carry forward:
- **None** — zero LADT records. You'll build it via the existing LADT admin after release.
- **LAD** — clone League, Agegroups, Divisions. No Teams. Typical for tournament sites where teams re-register each season.
- **LADT** — everything plus Teams, with filters: teams tied to a paid ClubRep registration are excluded (they'd silently carry a financial obligation), and teams in WAITLIST/DROPPED agegroups are excluded with the holding-pen agegroups themselves.

**Step 5 — Fees.** Processing fee % (source / current floor / custom). Store setting (keep / disable — inventory never clones either way).

**Step 6 — People & options.** Checkbox for advancing agegroup grad years + DOB windows by one year. Optional parallax slide-1 removal. Registration-from-email override.

**Step 7 — Review & submit.** Collapsed summary. Affirmation checkbox. The button says *Create job (suspended)* — that word *suspended* is deliberate.

After submit, the wizard transitions directly into **Release mode** for the new job.

## Release mode — two switches

**Switch 1 — Release Site to Public.** One button, one confirmation. Flips `BSuspendPublic = false`. Anyone hitting the job's URL can now reach it.

**Switch 2 — Activate Admin Registrations.** Shows the inactive admin list with per-row checkboxes, plus Select-all / Clear / Activate-N controls. Flips `BActive = true` on the selected rows only. Authorization is enforced server-side: the selected regIds must belong to the target job.

These two switches are independent. You can release the site before or after activating admins, whichever matches the rollout plan for that season.

The Landing screen always lists suspended jobs so you can return to Release mode at any time.

## Endpoints (for reference)

Base route: `/api/job-clone` (SuperUser-only today; Phase D opens up to customer admins with a same-customer guard).

| Method | Path | Purpose |
|---|---|---|
| GET | `/sources` | List cloneable source jobs |
| POST | `/` | Clone a source job |
| POST | `/preview` | Dry-run transforms, no writes |
| POST | `/blank` | Create a brand-new empty job |
| GET | `/suspended` | List suspended jobs for Landing |
| GET | `/{jobId}/admins` | Admin roster for Release screen |
| POST | `/{jobId}/release-site` | Flip `BSuspendPublic = false` |
| POST | `/{jobId}/release-admins` | Flip `BActive = true` on selected regs |

## What this tool does NOT clone

- Player / Adult / ClubRep registrations — rosters rebuild per season.
- Team rosters even under LADT — teams clone as identities (name, agegroup, colors, team-level fees); players re-register.
- Store inventory / SKUs.
- Widget layouts / dashboard.
- Schedule / calendar.
- The customer row itself — Blank flow requires an existing Customer.
