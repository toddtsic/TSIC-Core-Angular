# Wizard Theme System

This project supports per-wizard theming via CSS custom properties, so each wizard can have a distinct accent and header gradient while sharing a consistent structure.

## Theme variables

Global `styles.scss` declares theme scopes that set the following CSS variables:

- `--gradient-primary-start`, `--gradient-primary-end`: used by `.gradient-header`
- `--bs-primary`, `--bs-primary-rgb`: primary color and its RGB for focus rings, etc.

Examples (already defined):

- `.wizard-theme-player`: purple/indigo
- `.wizard-theme-family`: teal/green

The progress bar and other UI elements reference these variables to render the appropriate accent.

## Themed buttons

To ensure buttons match the theme, we override Bootstrap button CSS variables inside the theme scope. A SCSS mixin keeps this DRY:

```
@mixin wizard-button-theme($scope) { /* ... */ }
@include wizard-button-theme('.wizard-theme-family');
@include wizard-button-theme('.wizard-theme-player');
```

This guarantees `.btn-primary` and `.btn-outline-primary` render using the theme’s `--bs-primary` color for bg, border, hover, and active states.

## Applying a theme

Two options:

1) Static host class (current components):

```
@Component({
  host: { class: 'wizard-theme-family' }
})
```

2) Reusable directive (for future wizards):

```
<main [wizardTheme]="'family'"> ... </main>
```

The `WizardThemeDirective` (standalone) applies a `wizard-theme-{name}` class to the host.

## Reusable building blocks

- `.gradient-header`: uses theme gradient variables
- `.rw-progress`: shared progress style; color comes from `--bs-primary`
- `.rw-sticky-header`, `.rw-toolbar`, `.rw-bottom-nav`: shared wizard scaffolding

## Themed Login screen

`LoginComponent` accepts query parameters to theme and relabel the header:

- `theme=family|player`
- `header=...`
- `subHeader=...`
- `returnUrl=...` (post-login navigation)

Example:

```
/tsic/login?theme=family&header=Family%20Account%20Login&subHeader=Sign%20in%20to%20continue&returnUrl=/JOB/home
```

## Creating a new themed wizard

- Add host theme via static class or `[wizardTheme]` directive
- Use the shared structural classes for header, steps, toolbar, and bottom nav
- Rely on `.btn-primary` and `.btn-outline-primary`—the theme will color them automatically
- Add any new theme by creating `.wizard-theme-{new}` in `styles.scss` with the four variables above and including the `@include wizard-button-theme('.wizard-theme-{new}')`

That’s it—new wizards gain consistent structure, header gradient, progress, and button coloring with minimal effort.