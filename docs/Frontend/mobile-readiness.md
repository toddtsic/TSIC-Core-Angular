# Mobile Readiness Rules

Applies to any component a user can reach on a phone. Every rule below came from a defect
that shipped in this codebase — none are generic advice.

**Enforced, not merely documented** (from `src/frontend/tsic-app`):

```bash
npm run verify:mobile              # new violations fail; pre-existing ones are baselined
npm run verify:desktop-unchanged   # proves a mobile pass did not alter desktop CSS
```

## The risk ladder (MANDATORY)

A mobile pass must never put desktop at risk. Every change lands in exactly one phase, and
phases are **separate commits** so revert granularity is one component-phase.

| Phase | Allowed | Desktop guarantee |
|---|---|---|
| **1** | CSS **inside** `@media (max-width: 767.98px)` only. **Zero TS.** | Mechanical — `verify:desktop-unchanged` proves the desktop CSS is byte-identical |
| **2** | TS that **early-returns on desktop** via a viewport check, before any mutation | Desktop code path unreachable by construction |
| **3** | Anything else (ej2 bindings, shared state shape) | None. Explicit go/no-go, own commit, manual desktop pass |

Put the work in Phase 1 wherever possible — that is where the proof reaches.

Phase 2 exists because "this is a no-op when it fits" is a correct *argument*, and an argument
is not a guarantee. Prefer an early return that makes the desktop path unreachable.

## Checklist

### Reachability — can the user physically get to the control?

1. **No hover-only affordances.** A control hidden by default and revealed only on `:hover`
   does not exist on touch. LADT's tree add/delete buttons were unreachable on a phone this
   way — you could not add or delete a division at all. Reveal them unconditionally under 768px.
   *Not a violation:* a visible element that merely brightens on hover (`opacity: .5` → `1`).
   That is styling, not an affordance gate. — **enforced**

2. **Clamp `position: fixed` overlays to the viewport.** Anything positioned from
   `getBoundingClientRect()` must clamp both axes. Fixed elements cannot be scrolled to, so an
   overflowing menu is *unreachable*, not merely ugly.

3. **Frozen / fixed column widths must sum to less than the viewport.** An ej2 grid whose
   frozen columns exceed ~360px leaves no scrollable area at all on a phone.

### Sizing

4. **Interactive targets ≥ 24×24 CSS px** — WCAG 2.2 SC 2.5.8, Level **AA**.
   **This is a floor, not a target.** The widely-quoted 44×44 is SC 2.5.5, Level *AAA*, which
   is not the project standard. Do not inflate controls that already meet 24×24 — the LADT
   grid's 24×24 row buttons were deliberately left alone. Be generous on primary navigation
   surfaces, but call that a design choice, not compliance.

5. **Inputs are 16px on mobile.** Below 16px iOS Safari auto-zooms on focus and never zooms
   back. Bootstrap's `.form-control-sm` is 14px. `index.html` deliberately sets no
   `maximum-scale` (that would break pinch-zoom), so font size is the only lever.

### Layout

6. **Overlays use the `.detail-panel` contract** (`src/styles/_flyin.scss`), or — if genuinely
   a different shape, like a left nav drawer — anchor to `--app-header-height-mobile`.
   **Never a private copy of the positional block.** A copy under a different class name is how
   LADT's fly-in escaped the contract and covered the app header. — **enforced**

7. **`dvh`, not `vh`, for full-height fixed overlays.** iOS Safari's collapsing URL bar makes
   `100vh` taller than the visible viewport, hiding the overlay's bottom — exactly where sticky
   footers and Save buttons live. Declare both: `height: 100vh; height: 100dvh;` — **enforced**

8. **The body scrolls, not the panel.** `.detail-panel` is clipped; its `.panel-body` scrolls.
   Invert that and `position: sticky` footers pin to the wrong bottom.

9. **No horizontal pan.** `overflow-y: auto` silently computes `overflow-x` to `auto`, so one
   over-wide child turns the whole surface into a sideways scroller. Wide content (grids with
   frozen columns) scrolls in its **own** container.

## Writing mobile CSS without leaking to desktop

**Use `//` comments, never `/* */`.** Sass **preserves** `/* */` in expanded output, so a block
comment added next to a mobile rule changes the compiled desktop CSS and trips
`verify:desktop-unchanged`. Silent `//` comments are stripped.

**Check `min-width` when overriding a width.** A base rule's `min-width` survives your mobile
`width` override — LADT's drawer needed `min-width: 0` before `min(90vw, 360px)` could win on
a 320px phone.

## Guards

`verify:mobile` automates only what can be decided **without judgement** — rules 1, 6, 7. A
guard that cries wolf gets ignored. Touch-target sizes (57 files, mostly non-interactive), the
243 `form-control-sm` sites, and frozen-column sums need a human on the real surface, and live
in the runbook's manual pass.

It checks **compiled CSS, not raw SCSS**: nesting lets the same defect be written `&:hover {…}`
or `.parent:hover & {…}`, and a text matcher handling one ordering silently misses the other.
A first-pass text detector missed LADT's own violation for exactly that reason, and a later
version tracked only the first branch of `a, b { … }` — both are why this runs post-compile and
iterates every comma branch.

**Baseline** — `scripts/mobile-readiness-baseline.json` records violations that predate the
guard, so it fails only on **new** ones; nobody adopts a guard that fails on day one. A stale
entry (one since fixed) is *also* a failure, so the list can only shrink. Never add to it by
hand — fix the defect. Regenerate only to bootstrap:

```bash
npm run verify:mobile -- --write-baseline
```

**Opt-out** — a file may declare `// mobile-readiness: desktop-only` to skip rules 6 and 7.
Use it only where the view genuinely hides itself below 768px (`search/teams` does). Opt-out
means "does not apply"; baseline means "known debt". Do not confuse them.

## Runbook

Per-component procedure and status table: [mobile-readiness-status.md](mobile-readiness-status.md).

The step that cannot be automated, and matters most: **walk the user's real task, not the
component's feature list.** LADT's worst defect was invisible from a feature list and obvious
within seconds of trying to add a division on a phone.
