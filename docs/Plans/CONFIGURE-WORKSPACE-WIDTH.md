# Design Brief: Narrower, Consistent Width for Configure / Job Settings

**Status:** Proposal — needs Todd sign-off on width numbers before implementation
**Raised by:** Ann · **Drafted with:** Claude
**Closes punchlist:** ConfigureMenus **PL-022** (Nav-Editor-style capped workspace everywhere) + **PL-034** (Configure tables too much whitespace)

---

## Problem

Most **Configure / Job Settings** pages render **full-viewport width**. On wide monitors the cards and tables stretch edge-to-edge, which reads as loose and inconsistent — out of step with the tighter, centered feel of the **registration wizard** and **Legacy** (what admins are used to).

## Goal

Give every Configure page a **capped, centered workspace** so the area feels tight and familiar, matching the registration process and Legacy — without cramping the data tables.

---

## Current state (why this is easy mechanically)

- **No shared Configure shell.** Every Configure page loads bare into a generic full-width `container-fluid > col-12` in the layout — there's no common wrapper capping width.
- **Most pages set no `max-width`** (`:host { display:block; padding }` only), so they sprawl.
- **The target pattern already exists** in-repo and just isn't applied consistently:
  - Nav Editor: `.menu-admin-container { max-width: 1200px; margin: 0 auto }`
  - Quick Links: `.ql-page { max-width: 720px }`
  - Registration wizard: `.wizard-shell { max-width: 720px; margin: 0 auto }`
  - Customers grid: `.grid-wrap { max-width: 960px }`

So the fix is applying an **existing, proven pattern** uniformly — not inventing anything.

---

## Decisions needed from Todd

1. **Target width(s).** A single global number won't fit both page types well:
   - **Form pages** (Job Settings tabs, Age Ranges, Theme) look best **narrow** — proposed **~960px**.
   - **Grid/table pages** (Administrators, Discount Codes, Customers — multi-column Syncfusion tables) need more room — proposed **~1200px**.
   - **Recommendation: two width tiers**, not one.
2. **Anchor to Legacy?** If "what users know" is the goal, pin the numbers to whatever **Legacy** actually used so it's deliberate.
3. **Scope confirmation.** Configure/Job Settings only — explicitly **not** Dashboard, Reporting, LADT, Store (those stay full-width).

---

## Proposed approach

- Add a small shared stylesheet with **two workspace classes** backed by CSS variables (so widths are tunable in one place):
  - `.configure-workspace--form { max-width: var(--configure-width-form); margin: 0 auto }`
  - `.configure-workspace--wide { max-width: var(--configure-width-wide); margin: 0 auto }`
- Wrap each Configure page's root with the appropriate class (~10–12 one-line edits).
- **Do NOT** cap at the global `LayoutComponent` level — that would also squeeze Dashboard, Reporting, LADT, Store, etc. (out of scope).

## Phased rollout (de-risks the "grids look cramped" worry)

1. **Pilot:** convert **Job Settings tabs** (form tier) + **one grid page** (e.g. Discount Codes, wide tier).
2. **Eyeball together** live — across a few palettes and at a couple of screen widths — and **lock the two numbers**.
3. **Roll out** to the remaining Configure pages using the locked widths.

---

## Effort & risk

- **Effort:** a few hours of implementation once the widths are locked — not a rewrite. The decision + eyeball loop is the real time cost.
- **Main risk — Syncfusion grids.** `tsic-grid-tight` tables inherit container width; capping too tight forces horizontal scroll or cramped columns. The wide tier must clear the widest legitimate table. The pilot's grid page is chosen specifically to surface this before rollout.
- **Low risk otherwise:** isolated to Configure, no logic/data/API changes, fixed-position elements (e.g. the Save FAB) unaffected.

---

## Counterargument — the case AGAINST (read before approving)

Presented deliberately as devil's advocate so the decision is made with both sides in view.

1. **Cosmetic polish on the lowest-visibility surface.** Configure is admin-only (mostly SuperUser/Director) — a few expert users getting a task done. Meanwhile real bugs sit deferred (LOP save/propagate cluster FP-004/007/008, JobClone blockers, accounting multi-table work). "Too wide" is also a taste call — not user-reported.
2. **Wide is arguably *correct* for the data pages.** The wizard is narrow because it's a linear consumer form; Configure grids are a data workbench where seeing many columns at once *is* the task. Capping them forces scroll/truncation/cramping — it fights the primary use. "Match the registration process" conflates two different UI genres.
3. **"Legacy familiarity" cuts both ways.** The app is a deliberate modernization *away* from Legacy; selectively reintroducing its width is inconsistent — and no one has actually measured Legacy's width, so "familiar" is asserted, not verified.
4. **This adds width values, not fewer.** We already have 720/720/960/1200 across the app; the two-tier proposal adds another 960 + 1200. The "form vs table" line is itself a judgment that will drift.
5. **The cheap version rots; the durable version isn't cheap.** A shared *class* every page must remember to apply decays — new pages will forget it. The only thing that *enforces* consistency is a shared Configure **shell/layout component** at the route level, which is a real architectural change, not "a few hours."
6. **"A few hours" is optimistic.** The hidden cost is the eyeball loop across 8 palettes × breakpoints × ~12 pages, plus interaction with the tight-grid density work and Syncfusion's layout math. Width is also the most bikeshed-prone CSS property — it invites endless re-tuning.

**Strongest adversarial position — not "never," but "much less":** if done at all, cap **only the pure form pages** (Job Settings tabs, Age Ranges, Theme) and leave **every grid full-width, untouched**. That captures most of the "feels tight" benefit, sidesteps the entire grid risk, and is ~3–4 files instead of ~12 — no two-tier system, no shared-class-that-rots, no table-width bikeshed. If even that doesn't visibly matter to the people using it, that's the answer on the whole idea.

**Net recommendation from the skeptical seat:** scope it down to forms-only, or spend the time on the deferred bugs instead.

---

## Pages in scope (root wrapper to receive a workspace class)

Job Settings (`configure/job`), Administrators, Discount Codes, Customers, Customer Groups, Age Ranges, Dropdown Options, Theme, Widget Editor, Job Clone. *(Nav Editor and Quick Links already self-cap — fold them onto the shared token for consistency.)*
