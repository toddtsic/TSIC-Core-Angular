# Wizard Component Reference - Shared Patterns

## Overview

This document captures the **proven component patterns** used in Player and Team wizards. Use these as templates for new wizard steps.

---

## 1. Action Bar Component Pattern

### Purpose
Provides consistent Back/Continue navigation with optional badge display.

### Template Files

**action-bar.component.ts**
```typescript
import { Component, input, output, computed } from '@angular/core';

/**
 * Action Bar Component
 * 
 * Provides:
 * - Back button (conditional)
 * - Continue button (conditional)
 * - Optional badge display
 * - Responsive sizing
 * 
 * Styling: Applied via .action-bar class (see src/styles/_action-bar.scss)
 */
@Component({
    selector: 'app-YOUR-action-bar',
    standalone: true,
    imports: [/* CommonModule, etc */],
    templateUrl: './YOUR-action-bar.component.html',
    styleUrls: ['./YOUR-action-bar.component.scss'],
    host: {
        class: 'action-bar'  // ← CRITICAL: Applies global action bar styling
    }
})
export class YourActionBarComponent {
    // Inputs
    readonly canBack = input(false);
    readonly canContinue = input(false);
    readonly continueLabel = input('Continue');
    readonly showContinue = input(true);
    readonly detailsBadgeLabel = input<string | null>(null);
    readonly detailsBadgeClass = input('badge-info');

    // Outputs
    readonly back = output<void>();
    readonly continue = output<void>();

    // Optional: computed visibility
    readonly hasContent = computed(() => 
        this.canBack() || this.showContinue() || this.detailsBadgeLabel()
    );
}
```

**action-bar.component.html**
```html
<div class="d-flex align-items-center justify-content-between gap-2">
  
  <!-- Left Section: Optional Badge/Details -->
  @if (detailsBadgeLabel()) {
  <div class="action-bar-details">
    <span [ngClass]="'badge ' + detailsBadgeClass()">
      {{ detailsBadgeLabel() }}
    </span>
  </div>
  } @else {
  <div></div>
  }

  <!-- Right Section: Navigation Buttons -->
  <div class="btn-group" role="group">
    @if (canBack()) {
    <button 
      type="button" 
      class="btn btn-outline-secondary"
      (click)="back.emit()"
      aria-label="Go back to previous step">
      <i class="bi bi-arrow-left me-2"></i>Back
    </button>
    }
    
    @if (showContinue()) {
    <button 
      type="button" 
      class="btn btn-primary"
      [disabled]="!canContinue()"
      (click)="continue.emit()"
      aria-label="Proceed to next step">
      {{ continueLabel() }}
      <i class="bi bi-arrow-right ms-2"></i>
    </button>
    }
  </div>

</div>
```

**action-bar.component.scss**
```scss
/**
 * Action Bar Component Styles
 * 
 * Global styling applied via .action-bar class in component host.
 * Only component-specific customizations here (rare).
 */

/* Global .action-bar styles apply - no additional CSS needed in most cases */
```

---

## 2. Step Component Pattern

### Purpose
Individual wizard step with form handling and validation.

### Template Files

**YOUR-step.component.ts**
```typescript
import { Component, output, inject, signal, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';

/**
 * Your Step Component
 * 
 * Pattern:
 * - Signals for state management (not observables)
 * - Reactive forms for input
 * - Form validation
 * - Output events for navigation
 */
@Component({
    selector: 'app-YOUR-step',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
    templateUrl: './YOUR-step.component.html',
    styleUrls: ['./YOUR-step.component.scss']
})
export class YourStepComponent implements OnInit {
    // Dependency injection (modern pattern)
    private readonly fb = inject(FormBuilder);

    // State
    readonly form = signal<FormGroup | null>(null);
    readonly isSubmitting = signal(false);
    readonly errorMessage = signal<string | null>(null);

    // Navigation outputs
    readonly next = output<DataToPass>();
    readonly back = output<void>();  // Optional

    ngOnInit() {
        this.form.set(this.fb.group({
            fieldName: ['', [Validators.required, Validators.minLength(2)]],
            optionalField: ['']
        }));
    }

    /**
     * Form submission handler
     * Validates form, shows errors, emits on success
     */
    async onSubmit() {
        const formGroup = this.form();
        if (!formGroup || !formGroup.valid) {
            // Mark all fields as touched to show validation errors
            Object.values(formGroup?.controls || {}).forEach(ctrl => {
                ctrl.markAsTouched();
            });
            return;
        }

        this.isSubmitting.set(true);
        this.errorMessage.set(null);

        try {
            // Your business logic here
            const result = { /* ... */ };
            this.next.emit(result);
        } catch (error) {
            this.errorMessage.set('An error occurred. Please try again.');
        } finally {
            this.isSubmitting.set(false);
        }
    }

    /**
     * Error message for field (for form validation display)
     */
    getFieldError(fieldName: string): string | null {
        const control = this.form()?.get(fieldName);
        if (!control || !control.errors || !control.touched) return null;

        if (control.errors['required']) return `${fieldName} is required`;
        if (control.errors['minlength']) 
            return `${fieldName} must be at least ${control.errors['minlength'].requiredLength} characters`;
        return 'Invalid input';
    }
}
```

**YOUR-step.component.html**
```html
<form [formGroup]="form()" (ngSubmit)="onSubmit()" *ngIf="form()">
  
  <!-- Error Banner -->
  @if (errorMessage()) {
  <div class="alert alert-danger d-flex align-items-start" role="alert">
    <i class="bi bi-exclamation-triangle-fill me-2 mt-1"></i>
    <div>{{ errorMessage() }}</div>
  </div>
  }

  <!-- Form Fields -->
  <div class="mb-3">
    <label for="fieldName" class="form-label">Field Label</label>
    <input 
      type="text" 
      id="fieldName" 
      class="form-control"
      formControlName="fieldName"
      [class.is-invalid]="getFieldError('fieldName') !== null">
    @if (getFieldError('fieldName') as error) {
    <div class="invalid-feedback d-block">{{ error }}</div>
    }
  </div>

  <!-- Submit Button -->
  <button 
    type="submit" 
    class="btn btn-primary w-100"
    [disabled]="!form()?.valid || isSubmitting()">
    @if (isSubmitting()) {
      <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
      Saving...
    } @else {
      Continue <i class="bi bi-arrow-right ms-2"></i>
    }
  </button>

</form>
```

**YOUR-step.component.scss**
```scss
/**
 * Your Step Component Styles
 * 
 * Most styling is handled by global form controls:
 * - .form-label
 * - .form-control
 * - .form-select
 * - .btn
 * - .alert
 * 
 * Add only component-specific styles here.
 */

/* Example: Custom spacing if needed */
form {
    max-width: 100%;
}
```

---

## 3. Section Header Component (Optional)

### Purpose
Consistent styling for section headers within wizard steps.

### Template

```html
<div class="mb-4">
  <h3 class="fw-bold mb-2">Section Title</h3>
  <p class="text-muted fs-6">Optional description or subtitle</p>
</div>
```

### Styling
Automatically styled via global styles:
- `h3`: Font weight, color, transition (dark mode)
- `.text-muted`: Secondary text color

---

## 4. Form Control Patterns

### Text Input
```html
<div class="mb-3">
  <label for="name" class="form-label">Name</label>
  <input type="text" id="name" class="form-control" formControlName="name">
  @if (getFieldError('name') as error) {
  <div class="invalid-feedback d-block">{{ error }}</div>
  }
</div>
```

### Select Dropdown
```html
<div class="mb-3">
  <label for="country" class="form-label">Country</label>
  <select id="country" class="form-select" formControlName="country">
    <option value="">Select a country</option>
    @for (country of countries(); track country.id) {
      <option [value]="country.code">{{ country.name }}</option>
    }
  </select>
</div>
```

### Radio Buttons
```html
<div class="mb-3">
  <label class="form-label">Choose an option</label>
  <div class="btn-group w-100" role="group">
    @for (option of options(); track option.value) {
      <input 
        type="radio" 
        name="choice" 
        [id]="'radio-' + option.value"
        class="btn-check"
        [value]="option.value"
        formControlName="choice">
      <label class="btn btn-outline-secondary" [for]="'radio-' + option.value">
        {{ option.label }}
      </label>
    }
  </div>
</div>
```

### Checkbox
```html
<div class="form-check">
  <input 
    type="checkbox" 
    id="agree" 
    class="form-check-input"
    formControlName="agreeToTerms">
  <label class="form-check-label" for="agree">
    I agree to the terms and conditions
  </label>
</div>
```

---

## 5. Alert/Banner Patterns

### Info Banner
```html
<div class="alert alert-info d-flex align-items-start" role="alert">
  <i class="bi bi-info-circle me-2 mt-1"></i>
  <div>This is an informational message</div>
</div>
```

### Warning Banner
```html
<div class="alert alert-warning d-flex align-items-start" role="alert">
  <i class="bi bi-exclamation-triangle me-2 mt-1"></i>
  <div>Please review this important message</div>
</div>
```

### Success Banner
```html
<div class="alert alert-success d-flex align-items-start" role="alert">
  <i class="bi bi-check-circle me-2 mt-1"></i>
  <div>Operation completed successfully</div>
</div>
```

### Error Banner
```html
<div class="alert alert-danger d-flex align-items-start" role="alert">
  <i class="bi bi-exclamation-circle me-2 mt-1"></i>
  <div>{{ errorMessage() }}</div>
</div>
```

---

## 6. Loading State Pattern

### Spinner
```html
@if (isLoading()) {
  <div class="text-center py-5">
    <div class="spinner-border text-primary mb-3" role="status">
      <span class="visually-hidden">Loading...</span>
    </div>
    <p class="text-muted">Loading data...</p>
  </div>
} @else {
  <!-- Content here -->
}
```

### Loading Button
```html
<button 
  type="submit" 
  class="btn btn-primary"
  [disabled]="isSubmitting()">
  @if (isSubmitting()) {
    <span class="spinner-border spinner-border-sm me-2" role="status"></span>
    Saving...
  } @else {
    Submit
  }
</button>
```

---

## 7. Grid/List Pattern

### Two-Column Layout
```html
<div class="row g-3">
  <div class="col-md-6">
    <!-- Left column content -->
  </div>
  <div class="col-md-6">
    <!-- Right column content -->
  </div>
</div>
```

### Full-Width List
```html
<div class="mb-3">
  @for (item of items(); track item.id) {
    <div class="card mb-2">
      <div class="card-body py-2">
        {{ item.name }}
      </div>
    </div>
  }
</div>
```

---

## CSS Classes You Have

### Form Classes
- `.form-label` - Label styling
- `.form-control` - Input/textarea styling
- `.form-select` - Select dropdown styling
- `.form-check` - Checkbox styling
- `.is-invalid` - Error state highlighting
- `.invalid-feedback` - Error message styling

### Button Classes
- `.btn` - Base button
- `.btn-primary` - Primary action
- `.btn-secondary` - Secondary action
- `.btn-outline-secondary` - Outline button
- `.btn-group` - Grouped buttons
- `.btn-check` - Checkbox button

### Alert Classes
- `.alert` - Base alert
- `.alert-info` - Info style
- `.alert-warning` - Warning style
- `.alert-danger` - Error style
- `.alert-success` - Success style

### Utility Classes
- `.mb-3`, `.mb-4` - Margin bottom
- `.mt-1`, `.mt-2` - Margin top
- `.w-100` - Full width
- `.d-flex`, `.d-block` - Display
- `.text-center`, `.text-muted` - Text styling
- `.fw-bold` - Font weight
- `.fs-6` - Font size

---

## Best Practices

1. **Use signals, not observables** for state in components
2. **Mark fields as touched** after form submission attempt
3. **Show validation errors** only when field is touched
4. **Disable submit button** when form is invalid or submitting
5. **Show loading spinner** during async operations
6. **Emit events, not call methods** on parent component
7. **Use trackBy** in @for loops for performance
8. **Add aria labels** for accessibility
9. **Use theme colors** from global CSS variables
10. **Keep components small** - one step = one component

---

## Common Pitfalls to Avoid

❌ **Don't**: Use BehaviorSubject - Use signals instead  
❌ **Don't**: Call services from template - Call from component methods  
❌ **Don't**: Use *ngIf/*ngFor - Use @if/@for instead  
❌ **Don't**: Hardcode colors - Use CSS variables  
❌ **Don't**: Import multiple services - Keep dependencies minimal  
❌ **Don't**: Add custom CSS for form controls - Use global classes  
❌ **Don't**: Emit complex objects - Keep outputs simple  
❌ **Don't**: Forget aria labels - Add for accessibility  

---

## Summary

Use these patterns as templates:
- ✅ Action bars → Copy `action-bar.component.ts` structure
- ✅ Steps → Copy step component structure
- ✅ Forms → Use form control patterns
- ✅ States → Use signals and computed properties
- ✅ Styling → Use global classes, no custom CSS

This ensures all wizard steps have consistent styling and behavior across the entire application.
