# Wizard Styling & Layout Guide

## Overview

All registration wizards (Player, Team, Family, and future wizards) use a unified, DRY styling system. This guide ensures consistent look and feel across all wizards while minimizing code duplication.

**Key principle**: Global styles are in `src/styles/`, component-specific overrides go in the wizard component SCSS file.

---

## Global Styling Architecture

### Core Files (in `src/styles/`)

| File | Purpose | Usage |
|------|---------|-------|
| `_wizard-globals.scss` | Consolidated mixins for all wizard patterns | Import globally via `styles.scss` |
| `_wizard.scss` | Theme system & color scopes | Auto-generates theme variables |
| `wizard-layout.scss` | Fixed header, scrollable content layout | Applied via HTML classes |
| `_action-bar.scss` | Action bar styling (Back/Continue buttons) | Applied to action bar components |
| `_mixins.scss` | Button theming & utilities | Used throughout |

### CSS Variables Used

**Spacing**:
- `--space-2`, `--space-3`, `--space-4` - Responsive padding/margin

**Colors**:
- `--bs-card-bg` - Card background
- `--bs-surface` - Inner surface (body, alerts)
- `--bs-border-color` - Border color
- `--bs-body-color` - Text color
- `--bs-primary`, `--bs-primary-rgb` - Theme-specific primary color

**Radius**:
- `--radius-lg` - Large borders (16px)
- `--radius-md` - Medium borders (8px)

---

## Building a New Wizard

### Step 1: Create Component Files

Create your wizard with this file structure:
```
src/app/views/registration/wizards/my-wizard/
  ├── my-wizard.component.ts
  ├── my-wizard.component.html
  ├── my-wizard.component.scss        (← minimal!)
  ├── action-bar/
  │   ├── my-action-bar.component.ts
  │   └── my-action-bar.component.scss
  └── steps/
      ├── step-one/
      └── step-two/
```

### Step 2: Create HTML Layout

Use this proven structure in your `.component.html`:

```html
<main class="container-fluid px-3 py-2 my-wizard-container" 
      aria-labelledby="mw-title" 
      [wizardTheme]="'my-theme'">
  <div class="row justify-content-center">
    <div class="col-12">
      
      <!-- FIXED HEADER REGION -->
      <div class="wizard-fixed-header">
        <div class="text-center mb-3">
          <h2 id="mw-title" class="mb-1 fw-bold">My Wizard Title</h2>
          <p class="text-muted mb-0 fs-6">Step {{ currentIndex() + 1 }} of {{ steps().length }}</p>
        </div>

        <!-- Step Indicator -->
        <div class="mb-4">
          <app-step-indicator [steps]="stepDefinitions()" [currentIndex]="currentIndex()" />
        </div>

        <!-- Action Bar (fixed at top) -->
        @if (currentIndex() !== 0) {
        <div class="mb-0">
          <div class="wizard-action-bar-container">
            <app-my-action-bar 
              [canBack]="currentIndex() !== 0"
              [canContinue]="canContinue()" 
              [continueLabel]="continueLabel()" 
              [showContinue]="showContinueButton()"
              (back)="back()" 
              (continue)="onContinue()" />
          </div>
        </div>
        }
      </div>

      <!-- SCROLLABLE CONTENT REGION -->
      <div class="wizard-scrollable-content">
        <div class="card shadow-lg border-0 card-rounded mb-4 wizard-theme-my-theme">
          <div class="card-body bg-surface px-4 pb-4 pt-3">
            
            @switch (currentStepId()) {
              @case ('step-one') {
                <app-my-step-one (next)="next()" />
              }
              @case ('step-two') {
                <app-my-step-two (back)="back()" (next)="next()" />
              }
              @default {
                <p>Unknown step</p>
              }
            }

          </div>
        </div>
      </div>

    </div>
  </div>
</main>
```

### Step 3: Create Minimal Component SCSS

Your component SCSS should **only** contain comments referencing global patterns:

**`my-wizard.component.scss`**:
```scss
/**
 * My Wizard Component Styles
 * 
 * This component uses global wizard patterns defined in:
 * - src/styles/_wizard-globals.scss (mixins: wizard-card-theme, wizard-container)
 * - src/styles/wizard-layout.scss (classes: .wizard-fixed-header, .wizard-scrollable-content)
 * - src/styles/_action-bar.scss (class: .action-bar styling)
 * 
 * Only component-specific overrides should be added here.
 */

/* Container layout is provided by global mixin via .my-wizard-container */
.my-wizard-container {
    /* Layout defined globally in _wizard-globals.scss via @mixin wizard-container */
}

/* Card styling is provided by global mixin via .card.wizard-theme-my-theme */
.card.wizard-theme-my-theme {
    /* Card styling defined globally in _wizard-globals.scss via @mixin wizard-card-theme */
}

/* Title styling is provided globally via _wizard-globals.scss */
h2#mw-title {
    /* Styling defined globally via @mixin wizard-title-base */
}
```

### Step 4: Register Theme in `_variables.scss`

Add your wizard theme to the theme map in `src/styles/_variables.scss`:

```scss
$wizard-themes: (
    'player': (
        primary: var(--brand-primary),
        primary-rgb: 14 165 233,
        gradient-start: var(--brand-primary),
        gradient-end: var(--brand-primary-dark)
    ),
    'team': ( /* ... */ ),
    'my-theme': (                          // ← ADD YOUR THEME
        primary: var(--my-color),          // Your theme color
        primary-rgb: 100 120 150,          // RGB values
        gradient-start: var(--my-color-light),
        gradient-end: var(--my-color-dark)
    ),
    // ... other themes
);
```

The `_wizard.scss` file will automatically:
- Generate `.wizard-theme-my-theme` scope with color variables
- Apply button theming via the `@mixin wizard-button-theme()` 
- Set up progress bar colors

### Step 5: Create Action Bar Component

Create a consistent action bar following the existing patterns:

**`my-action-bar.component.ts`**:
```typescript
import { Component, input, output } from '@angular/core';

@Component({
    selector: 'app-my-action-bar',
    standalone: true,
    imports: [],
    templateUrl: './my-action-bar.component.html',
    styleUrls: ['./my-action-bar.component.scss'],
    host: {
        class: 'action-bar'  // ← Apply global action-bar styling
    }
})
export class MyActionBarComponent {
    readonly canBack = input(false);
    readonly canContinue = input(false);
    readonly continueLabel = input('Continue');
    readonly showContinue = input(true);

    readonly back = output<void>();
    readonly continue = output<void>();
}
```

**`my-action-bar.component.scss`**:
```scss
/**
 * My Action Bar Component
 * 
 * Global styling applied via .action-bar class in component host.
 * Only component-specific customizations here.
 */

/* No additional styles needed - global .action-bar styles apply */
```

### Step 6: Create Step Components

Each step follows a consistent pattern:

**`my-step-one.component.ts`**:
```typescript
import { Component, output } from '@angular/core';

@Component({
    selector: 'app-my-step-one',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, /* ... */],
    templateUrl: './my-step-one.component.html',
    styleUrls: ['./my-step-one.component.scss']
})
export class MyStepOneComponent {
    readonly next = output<void>();
}
```

**Step HTML** - use consistent form styling:
```html
<form [formGroup]="form" (ngSubmit)="onSubmit()">
  <div class="mb-3">
    <label for="name" class="form-label">Name</label>
    <input type="text" id="name" class="form-control" formControlName="name">
  </div>
  
  <button type="submit" class="btn btn-primary w-100">
    Continue <i class="bi bi-arrow-right ms-2"></i>
  </button>
</form>
```

---

## CSS Reusable Classes

### Layout Classes (from `wizard-layout.scss`)

```scss
.wizard-fixed-header        // Fixed header containing title, steps, action bar
.wizard-scrollable-content  // Scrollable content area
.wizard-action-bar-container // Optional wrapper for action bar
```

### Card Classes (from `_wizard-globals.scss`)

```scss
.card.wizard-card-player    // Player wizard card styling
.card.wizard-card-team      // Team wizard card styling
.card.wizard-card-family    // Family wizard card styling
.card.wizard-card-my-theme  // Your custom theme card
```

### Container Classes (from `_wizard-globals.scss`)

```scss
.rw-wizard-container        // Player wizard main container
.tw-wizard-container        // Team wizard main container
.wizard-container           // Generic wizard container
```

### Action Bar Classes (from `_action-bar.scss`)

```scss
.action-bar                 // Apply to action bar component host
.action-bar-details         // Badge/label section
.wizard-action-bar-container // Container wrapper (fixed header region)
```

---

## Global Mixins Available

Use these mixins in custom SCSS if needed:

### From `_wizard-globals.scss`

```scss
@mixin wizard-card-theme {
    // Base card styling: border, background, shadow, card-body, alerts
}

@mixin wizard-container {
    // Flex column layout: display flex, flex-direction column, height 100%
}

@mixin wizard-title-base {
    // Title styling: color, transition
}
```

### From `_mixins.scss`

```scss
@mixin wizard-button-theme($scope) {
    // Apply button theming within a scope (e.g., .wizard-theme-player)
}
```

---

## Common Customization Examples

### Custom Card Margin

If your wizard needs different card spacing:

```scss
// my-wizard.component.scss
.card.wizard-theme-my-theme {
    margin-bottom: var(--space-6);  // Override default
}
```

### Custom Action Bar

If your action bar needs a badge or custom layout:

```typescript
// my-action-bar.component.ts
@Component({
    host: {
        class: 'action-bar'  // Still apply global styles
    }
})
export class MyActionBarComponent {
    readonly detailsBadge = input<string | null>(null);
    readonly detailsBadgeClass = input('badge-info');
}
```

**HTML**:
```html
<div class="action-bar-details" *ngIf="detailsBadge()">
    <span [ngClass]="'badge ' + detailsBadgeClass()">{{ detailsBadge() }}</span>
</div>
```

### Dark Mode Overrides

If your wizard has embedded third-party widgets (like VerticalInsure), add to component SCSS:

```scss
// my-wizard.component.scss
#my-third-party-widget {
    * {
        background-color: var(--bs-card-bg) !important;
        color: var(--bs-body-color) !important;
        border-color: var(--bs-border-color) !important;
    }
}
```

---

## Responsive Design

All global styles include responsive breakpoints:

| Breakpoint | Padding | Font Size |
|------------|---------|-----------|
| Desktop (≥768px) | `var(--space-4)` | Default |
| Tablet (≤768px) | `var(--space-3)` | Default |
| Mobile (≤576px) | `var(--space-2)` | Reduced |

No additional CSS needed - responsive styles are automatic.

---

## Testing Your New Wizard

1. **Visual consistency**: Compare your wizard to Player/Team wizards
   - Same card styling ✓
   - Same action bar behavior ✓
   - Same fixed header layout ✓
   - Same scrollable content area ✓

2. **Dark mode**: Test with theme switcher
   - Colors adapt correctly ✓
   - Text contrast is good ✓
   - Borders visible ✓

3. **Responsive**: Test on mobile
   - Padding reduced properly ✓
   - Buttons stack correctly ✓
   - Action bar readable ✓

---

## Troubleshooting

**Q: My card styling looks different**
A: Ensure your HTML class is `wizard-theme-MY-THEME` and the theme is registered in `_variables.scss`.

**Q: Action bar styling is off**
A: Check that the action bar component has `host: { class: 'action-bar' }`.

**Q: Dark mode isn't working**
A: Verify CSS variables are used, not hardcoded colors. Check that the component is within a theme scope.

**Q: Styles not applying**
A: Ensure `_wizard-globals.scss` is imported in `styles.scss` (it is by default).

---

## Summary

✅ **Global**: Use all provided layout, card, action-bar, and container classes  
✅ **Theme**: Register in `_variables.scss`, automatic styling applied  
✅ **Component SCSS**: Keep minimal, only document or override  
✅ **HTML**: Follow the proven layout structure  
✅ **Mixins**: Available but rarely needed - use global classes first  

This approach ensures all wizards have **identical styling, responsiveness, and dark mode support** with **zero duplication**.
