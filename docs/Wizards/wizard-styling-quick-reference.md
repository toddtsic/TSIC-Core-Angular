# Wizard Styling Quick Reference

## For Creating a New Wizard

### 1. HTML Structure (Copy-Paste Template)

```html
<main class="container-fluid px-3 py-2 YOUR-WIZARD-container" 
      aria-labelledby="YOUR-title" 
      [wizardTheme]="'your-theme'">
  <div class="row justify-content-center">
    <div class="col-12">
      
      <div class="wizard-fixed-header">
        <div class="text-center mb-3">
          <h2 id="YOUR-title" class="mb-1 fw-bold">Your Wizard Title</h2>
          <p class="text-muted mb-0 fs-6">Step {{ index() + 1 }} of {{ total }}</p>
        </div>
        <div class="mb-4">
          <app-step-indicator [steps]="steps()" [currentIndex]="index()" />
        </div>
        @if (index() !== 0) {
        <div class="mb-0">
          <div class="wizard-action-bar-container">
            <app-your-action-bar [canBack]="true" [canContinue]="true" 
              (back)="back()" (continue)="next()" />
          </div>
        </div>
        }
      </div>

      <div class="wizard-scrollable-content">
        <div class="card shadow-lg border-0 card-rounded mb-4 wizard-theme-your-theme">
          <div class="card-body bg-surface px-4 pb-4 pt-3">
            <!-- Step content here -->
          </div>
        </div>
      </div>

    </div>
  </div>
</main>
```

### 2. Component SCSS (Minimal)

```scss
/**
 * Your Wizard Component Styles
 * Uses global patterns from src/styles/_wizard-globals.scss
 */

.YOUR-wizard-container {
    /* Global mixin via _wizard-globals.scss */
}

.card.wizard-theme-your-theme {
    /* Global mixin via _wizard-globals.scss */
}

h2#YOUR-title {
    /* Global mixin via _wizard-globals.scss */
}
```

### 3. Register Theme

Add to `src/styles/_variables.scss`:

```scss
$wizard-themes: (
    // ... existing themes ...
    'your-theme': (
        primary: var(--your-color),
        primary-rgb: 100 120 150,
        gradient-start: var(--your-color-light),
        gradient-end: var(--your-color-dark)
    ),
);
```

### 4. Action Bar Component

```typescript
// your-action-bar.component.ts
import { Component, input, output } from '@angular/core';

@Component({
    selector: 'app-your-action-bar',
    standalone: true,
    templateUrl: './your-action-bar.component.html',
    styleUrls: ['./your-action-bar.component.scss'],
    host: { class: 'action-bar' }  // ← Important!
})
export class YourActionBarComponent {
    readonly canBack = input(false);
    readonly canContinue = input(false);
    readonly continueLabel = input('Continue');
    readonly showContinue = input(true);

    readonly back = output<void>();
    readonly continue = output<void>();
}
```

---

## Global Classes You Have

| Class | Purpose | Usage |
|-------|---------|-------|
| `.wizard-fixed-header` | Fixed top section | Wrap title, steps, action bar |
| `.wizard-scrollable-content` | Scrollable content area | Wrap card with steps |
| `.wizard-action-bar-container` | Action bar wrapper | Inside fixed header |
| `.card.wizard-theme-YOUR-THEME` | Themed card | Apply to card element |
| `.action-bar` | Action bar styling | Add to action bar host |
| `.YOUR-wizard-container` | Container | Main outer container |

---

## What's Already Handled Globally ✅

- ✅ Card borders, shadows, responsive padding
- ✅ Alert styling (info, warning, danger, success)
- ✅ Dark mode colors (--bs-card-bg, --bs-surface, etc.)
- ✅ Action bar styling and layout
- ✅ Responsive breakpoints (desktop/tablet/mobile)
- ✅ Theme colors and gradients
- ✅ Button theming per theme
- ✅ Fixed header positioning
- ✅ Scrollable content with proper padding

---

## What You Need to Add

1. ✅ HTML structure (use template above)
2. ✅ Component SCSS (minimal comments)
3. ✅ Theme registration (add to _variables.scss)
4. ✅ Action bar component (with `host: { class: 'action-bar' }`)
5. ✅ Step components (use form controls with .form-control class)

---

## Common CSS Variables

```scss
// Spacing
--space-2, --space-3, --space-4    // Padding/margin

// Colors (theme-aware)
--bs-card-bg          // Card background
--bs-surface          // Inner content background
--bs-border-color     // Borders
--bs-body-color       // Text
--bs-primary          // Primary color (theme-specific)

// Radius
--radius-lg (16px)    // Large borders
--radius-md (8px)     // Medium borders
```

---

## One File to Understand

Read this for comprehensive patterns:
📄 [Wizard Styling Guide](./wizard-styling-guide.md)
