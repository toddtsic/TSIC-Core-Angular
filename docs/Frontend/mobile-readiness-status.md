# Mobile Readiness — Runbook & Status

Rules and rationale: [mobile-readiness.md](mobile-readiness.md).

This is the repeatable drill for taking a component from "renders on a phone" to "a director
can actually do their job on a phone", without putting desktop at risk.

## Per-component procedure

1. **Name the task.** Write down the one thing a real user must accomplish here on a phone —
   *"a director fixes a division"*, not *"the LADT editor works"*. Everything below is judged
   against that sentence.
2. **Run the guard.** `npm run verify:mobile` — mechanical findings for rules 1, 6, 7.
3. **Walk the 9 checks by hand** against the surface, and **walk the task from step 1 end to
   end on a real device.** This is the step that cannot be automated and the one that finds the
   blockers. LADT's worst defect — tree add/delete gated behind `:hover`, so unreachable on
   touch — is invisible in a feature list and obvious within seconds of trying the task.
4. **Fix Phase 1 only** (mobile-only CSS, zero TS).
5. **Prove desktop is untouched.** `npm run verify:desktop-unchanged` must be clean. No
   exceptions, no "it's obviously fine".
6. **Device pass** on the task from step 1.
7. **Commit.** Then repeat 4–6 for Phase 2 and Phase 3 if justified, each its own commit.
8. **Update the status row below**, including what you deliberately did *not* fix.

## Ordering the queue

Measured across `src/app` when the programme started:

| Signal | Count |
|---|---|
| View folders with **no mobile breakpoint at all** | **9 of 19** — account, accounting, arb, auth, club-rosters, communications, errors, reporting, store |
| `form-control-sm` / `form-select-sm` (iOS zoom on focus) | **243** |
| `:hover` reveals via `display` | 3 files |
| `:hover` reveals via `opacity: 0 → 1` | 28 files (mostly decorative — needs triage) |
| `getBoundingClientRect`-positioned overlays (clamp candidates) | 12 files |
| Elements sized under the 24px AA floor | 57 files (mostly non-interactive — needs triage) |

Take components in order of **whether someone can name a real phone task for them**. Several of
the nine no-breakpoint folders may not be phone surfaces at all; guessing wastes the pass.

The 243-site iOS-zoom problem is a **programme-level rollout**, not a per-component fix. It
stays out of individual commits until the per-component pattern has proven itself.

## Status

| Component | Phase 1 | Phase 2 | Phase 3 | Task it must support | Notes |
|---|---|---|---|---|---|
| `ladt/editor` | ✅ `4b4cd4f6` | ✅ `5172e204` | ⏸ awaiting go/no-go | A director fixes a division | Device pass outstanding. See below. |
| `search/registrations` | ✅ pre-programme | — | — | Find and edit a registrant | Fly-in contract + filters drawer already anchored |
| `search/teams` | n/a | n/a | n/a | — | `// mobile-readiness: desktop-only` — container is `display:none` below 768px |
| everything else | ⬜ | ⬜ | ⬜ | *unnamed* | Name the task before starting |

### `ladt/editor` detail

**Fixed**
- `ac0b44c2` — fly-in adopted the `.detail-panel` contract; panel and tree drawer now start
  below the app header; body scrolls so the sticky Save bar clears iOS Safari's URL bar
- `4b4cd4f6` (Phase 1) — tree add/remove un-gated from `:hover` (**the blocker**); tree targets
  20×20→40×40 and 22px→40px rows; clickable colour dot 12px→24×24; drawer →`min(90vw, 360px)`;
  16px inputs in all four detail components
- `5172e204` (Phase 2) — row ⋮ menu clamped into the viewport, mobile-only

**Deliberately not fixed**
- Grid action buttons stay 24×24 — already meet SC 2.5.8 Level AA. Inflating them would be
  churn dressed as compliance.
- `.ladt-layout`'s `calc(100vh - 120px)` stays. On mobile it leaves the layout ~31px *shorter*
  than available rather than overflowing, so nothing is hidden; swapping one magic number for
  another carries real regression risk for no user-visible gain.

**Open**
- **Phase 3 go/no-go**: team level has 64 + 160 + 160 = **384px of frozen columns on a 390px
  viewport**, leaving ~6px of scrollable area. Division level fits fine at 319px. The route is
  low-risk — `loadSiblings()` already filters columns conditionally, and `frozenCount` is
  computed from `columns()` so it follows automatically, touching no ej2 API — but it changes
  `siblingColumns` from a signal to a computed, which is outside what the fingerprint proves.
- **No device pass yet.** Phase 1 is a claim about touch; only a phone settles it.
