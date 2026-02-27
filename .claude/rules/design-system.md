# Design System Rules

## CSS Variables (MANDATORY)

```scss
// CORRECT — palette-responsive
background: var(--bs-primary);
color: var(--brand-text);
padding: var(--space-4);

// WRONG — breaks palette switching
background: #0ea5e9;
color: #1e293b;
padding: 16px;
```

## Key Rules

- NO hardcoded colors, spacing, or shadows — CSS variables only
- 8px spacing grid: `--space-1` through `--space-20`
- 8 dynamic color palettes — test all when changing styles
- Elevated surfaces use gradients, box-shadows, and borders for depth — NOT backdrop-filter
- WCAG AA compliance (4.5:1 contrast minimum)
- Review `styles.scss` for brand variables before any UI work

## BANNED: `backdrop-filter` (NEVER USE)

`backdrop-filter` is **permanently banned** from this codebase. Do not add it under any circumstances.

**Why**: Per CSS spec, `backdrop-filter` creates a new containing block. This traps any `position: fixed` or `position: absolute` descendants inside that element's paint layer — dropdowns, modals, and tooltips become invisible or clipped. This bug occurred **three separate times** in the header/menu chrome before the property was banned project-wide.

**Alternative**: Use solid or high-opacity `background` with `box-shadow` for visual depth. The effect is visually identical since most surfaces are 90%+ opaque anyway.
