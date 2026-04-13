# Bulletin Exemplars

This file is the **visual vocabulary** for AI-drafted and AI-reformatted bulletins.
`AiComposeService` embeds it verbatim into the system prompt on every draft/format
call, so edits here take effect on the next API restart — no code change needed.

## Why this file exists

AI models have no mental model of "what TSIC looks like." Given a list of CSS class
names with no guidance, they pick arbitrarily (and usually reach for Bootstrap
defaults like `alert-warning`, which are not palette-aware). This file gives the
model concrete, palette-safe examples to pattern-match against.

## When to edit

- You notice the AI producing something off-brand → add a counter-example
- Design system gains a new palette-safe pattern → add it here as an exemplar
- A shape below no longer matches the site's look → update the HTML

## Rules for any example you add

1. **CSS variables only.** No `#hex`, no `rgba()` literals, no Bootstrap default alerts.
   Every color/spacing token must come from the design system.
2. **Palette-tested.** The shape must render correctly under all 8 palettes.
   Classes known safe: `wizard-callout`, `wizard-callout-info`, `wizard-callout-danger`,
   `card` + `card-body`, `card-header-info-subtle`, `card-header-success-subtle`,
   `card-header-neutral`, `bg-info-subtle`, `bg-success-subtle`, `bg-warning-subtle`,
   `bg-primary-subtle`, `text-muted`, `tip`, `wizard-tip`.
3. **Short.** This whole file ships on every AI call. Keep each example tight.
4. **Annotated.** Every example needs a "when to use" line so the model picks
   the right shape for the right content.

---

## Example 1 — Informational announcement

**When to use:** general news, schedule updates, roster postings, "here's what's happening."
This is the **default** — if no stronger signal applies, use this shape.

```html
<div class="wizard-callout wizard-callout-info">
  <i class="bi bi-info-circle"></i>
  <div>
    <strong>Rosters are now posted.</strong>
    Club Reps can view their assigned teams under the Club Rep dashboard.
  </div>
</div>
```

**Why this shape:** `wizard-callout-info` uses the palette's `--bs-info-rgb` at low
opacity — subtle blue tint that adapts to every theme. Icon + bold headline + short
body reads at a glance.

---

## Example 2 — Deadline / required action

**When to use:** the reader must do something by a specific date, or miss out.
Stronger visual weight than an announcement because inaction has consequences.

```html
<div class="wizard-callout wizard-callout-danger">
  <i class="bi bi-exclamation-triangle"></i>
  <div>
    <strong>Registration closes Friday, August 15.</strong>
    Players not registered by midnight will not be rostered for the Summer 2026 season.
  </div>
</div>
```

**Why this shape:** `wizard-callout-danger` uses `--bs-danger-rgb` with a heavier
2px border — visually louder than info. Reserve this for true deadlines or
consequence-bearing content. Don't use it for routine reminders.

---

## Example 3 — Multi-step instructions

**When to use:** procedural content the reader must follow in order
(how to pay, how to register, how to access something).

```html
<div class="card">
  <div class="card-header card-header-info-subtle">
    <strong>Pay Team Balance Due</strong>
  </div>
  <div class="card-body">
    <ol class="mb-0">
      <li>Log in (must use the same username used to register teams initially).</li>
      <li>Select your Club Rep tournament role.</li>
      <li>Under "CLUB REP" in the upper right, select "PAY BALANCE DUE".</li>
    </ol>
  </div>
</div>
```

**Why this shape:** a titled card gives procedural content a clear boundary
and a scannable header. `card-header-info-subtle` tints the header to match
the palette. Ordered list (`<ol>`) — not bulleted — because steps are sequential.

---

## Example 4 — Call-to-action with token

**When to use:** you want the reader to *do* a thing that has a token available
(`!REGISTER_PLAYER`, `!REGISTER_CLUBREP`, `!SCHEDULE`, etc.). **Always prefer the
token over a hand-written anchor** — tokens render as palette-styled buttons
and stay in sync with backend state (hidden when registration closes, etc.).

```html
<p>The !JOBNAME season is open. Sign up your player today:</p>

!REGISTER_PLAYER
```

**Why this shape:** the token replaces what would otherwise be "click here to
register" — an unstyled link prone to going stale. The resolver emits a
styled, palette-aware CTA block and respects pulse gating automatically.

---

## Example 5 — Neutral info block (no callout needed)

**When to use:** informational content that doesn't need visual emphasis —
a welcome paragraph, background context, a thank-you note.

```html
<p>Welcome to <strong>!JOBNAME</strong>! We're excited to host players and families
from across the region for another great season.</p>

<p class="text-muted">Questions? Reach out to your Club Rep or the tournament office.</p>
```

**Why this shape:** plain paragraphs with `text-muted` for secondary content.
Not every bulletin needs a callout — over-using colored boxes dilutes their
signal value. When in doubt, prefer this shape and let the reader's eye rest.

---

## Shapes to AVOID

- **`alert alert-warning` / `alert alert-info`** — Bootstrap defaults are not
  consistently palette-aware in this codebase. Use `wizard-callout-*` instead.
- **`<h1>`–`<h6>`** — the bulletin title is rendered separately by the page shell.
  Use `<strong>` inside a callout or a card header instead.
- **Inline `style="..."` attributes** — every color/spacing value must come from a class.
- **Hand-written anchors to registration/schedule pages** — use the matching `!TOKEN`.
