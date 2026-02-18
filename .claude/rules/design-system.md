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
- Glassmorphic elevated surfaces for chrome components
- WCAG AA compliance (4.5:1 contrast minimum)
- Review `styles.scss` for brand variables before any UI work
