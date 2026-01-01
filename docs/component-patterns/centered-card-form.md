# Centered Card Form - Pattern Analysis

**Component Type**: Form View > Authentication  
**Primary Example**: [login.component.ts](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/auth/login/login.component.ts)  
**Location**: `src/app/views/auth/login/`  
**Chrome Dependencies**: None (fully isolated)  
**Reusability Score**: High  

---

## Visual Pattern

```
┌──────────────────────────────────────┐
│    Welcome Back (Theme-aware)        │  ← Gradient header (theme color)
│    Sign in to continue               │
│                                      │
│  ┌────────────────────────────────┐  │
│  │ Username [text input]          │  │ ← Semantic form labels
│  └────────────────────────────────┘  │
│                                      │
│  ┌────────────────────────────────┐  │
│  │ Password [password input]  [👁]│  │ ← Toggle visibility button
│  └────────────────────────────────┘  │
│                                      │
│  [Error messages if validation fail] │ ← Conditional error display
│                                      │
│        [Sign In] [Register]          │ ← Action buttons
└──────────────────────────────────────┘
```

---

## Chrome Compatibility Analysis

### ✅ Header Compatibility (client-header-bar)

**Relationship**: Optional - component works standalone or within routed layouts

**Vertical Spacing**: No collision with fixed header
- Uses `container` wrapper with standard padding (`py-5`)
- No `position: fixed` or absolute positioning
- Flexbox centering doesn't conflict with header z-index

**Mobile Considerations**:
- Uses responsive utility classes
- Touch-friendly input sizes (`form-control-lg`)
- Password toggle button properly sized (40px × 40px minimum)

### ✅ Banner Compatibility (client-banner)

**Relationship**: Either/or - banner is optional, component adapts

**Background Assumptions**: None
- Centered card design works on any background
- No assumptions about viewport color or imagery
- Gradient header is self-contained within card (not fullwidth)

### ✅ Theme System Response

**Palette Switching**: Full support via 8 dynamic palettes
- Uses `@HostBinding('class.wizard-theme-*')` for theme application
- Gradient header color adapts: `--gradient-header` + `--gradient-primary-start`/`--gradient-primary-end`
- Button colors respect theme via `--bs-primary` CSS variable

**Supported Themes**:
- `wizard-theme-login` (blue/cyan)
- `wizard-theme-player` (purple/indigo)
- `wizard-theme-family` (teal/green)
- Default: `login` if none specified

**Dark Mode**: Inherits from global `[data-bs-theme="dark"]` context
- Card background uses `--bs-card-bg` (palette-aware)
- Text colors adapt to `--brand-text`, `--brand-text-muted`
- Input borders use `--bs-border-color`

### ✅ Layout Contract Compliance

**Container Isolation**: Works in any parent container
- Uses `container` class (respects viewport width)
- No assumptions about parent padding/margins
- Flexbox centering (`d-flex justify-content-center align-items-center`) works on any viewport

**Responsive Behavior**:
- Mobile: Full-width card with standard padding
- Tablet+: Centered card with max-width container
- No hardcoded dimensions—all relative sizing

**Z-Index Harmony**: No conflicts
- Card uses default stacking (no z-index specified)
- Password toggle button is `position-relative` (scoped to input group)
- No modal or dropdown z-index layers introduced

### ✅ Chrome Contract Checklist

- ✅ Respects vertical rhythm (no collision with header/footer)
- ✅ Uses semantic color tokens (no hardcoded colors)
- ✅ Works in isolation (no context assumptions)
- ✅ Handles responsive chrome (mobile-friendly touch targets)
- ✅ Layers correctly (default stacking order maintained)

---

## Code Structure

```typescript
@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, TextBoxModule, ButtonModule, RouterModule],
  styleUrls: ['./login.component.scss'],
})
export class LoginComponent implements OnInit, AfterViewInit, OnDestroy {
  // Signals for component state
  submitted = signal(false);           // Form validation state
  showPassword = signal(false);        // UI toggle state

  // Themeable inputs (via @Input or query params)
  @Input() theme: 'login' | 'player' | 'family' | '' = '';
  @Input() headerText = 'Welcome Back';
  @Input() subHeaderText = 'Sign in to continue';

  // Dynamic theme class binding
  @HostBinding('class.wizard-theme-login') get isLoginTheme() { 
    return this.theme === 'login'; 
  }
  // ... similar for player, family

  // Dependency Injection (modern pattern)
  constructor(
    private readonly authService: AuthService,
    private readonly fb: FormBuilder,
    private readonly autofill: AutofillMonitor
  ) {
    this.form = this.fb.group({
      username: [savedUsername, [Validators.required]],
      password: ['', [Validators.required]],
    });
  }

  // Smart autofill detection
  ngAfterViewInit() {
    this.autofill.monitor(this.usernameInput)
      .subscribe(event => {
        if (event.isAutofilled) { /* sync form */ }
      });
  }
}
```

---

## Template Patterns

```html
<!-- Modern control flow syntax -->
@if (submitted() && form.get('username')?.invalid) {
  <div class="invalid-feedback">Username is required</div>
}

<!-- Accessible toggle button -->
<button type="button" 
  (click)="toggleShowPassword()"
  [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'">
  <!-- SVG icon content -->
</button>

<!-- Theme-aware error display -->
@if (auth.loginError()) {
  <div class="alert alert-danger">{{ auth.loginError() }}</div>
}
```

---

## Styling Contract

### CSS Variables Used
- `--bs-primary` (theme color for buttons, links)
- `--bs-card-bg` (card background, palette-aware)
- `--border-color` (input borders)
- `--shadow-lg` (card elevation)
- `--brand-text` (text color, dark mode aware)
- `--gradient-primary-start`, `--gradient-primary-end` (header gradient)

### Bootstrap Utilities

**Layout**: `container`, `d-flex`, `justify-content-center`, `align-items-center`, `flex-column`

**Spacing**: `py-5`, `p-4`, `mb-4`, `mt-2`

**Cards**: `card`, `card-header`, `card-body`, `border-0`

**Forms**: `form-control`, `form-control-lg`, `form-label`, `fw-semibold`

**Positioning**: `position-relative`, `top-50`, `translate-middle-y`, `end-0`

**Shadows**: `shadow-lg`

### Custom SCSS

```scss
/* login.component.scss is empty (utility-only pattern) */
/* All styling via global utilities + Bootstrap defaults */
```

**No Component Styles**: Demonstrates minimalist approach—purely utility-based styling leverages design tokens.

---

## Reusable Abstractions

### 1. Centered Card Form Pattern
**Applicable to:**
- Password reset / recovery flows
- MFA verification screens
- Multi-step authentication flows
- Can be extracted to `src/app/shared/components/centered-card-form/`

### 2. Query Parameter Theming
**Applicable to:**
- Any view that needs multi-context rendering
- Wizard step screens
- Modal dialogs embedded in different contexts

### 3. Autofill Detection Pattern
**Applicable to:**
- Any form that needs to sync browser-autofilled values
- Credential managers (1Password, Bitwarden, etc.)

### 4. Password Toggle Pattern
**Applicable to:**
- Registration forms
- Password change dialogs
- Confirmation screens

---

## Chrome Integration Summary

- **Header Spacing**: No special consideration needed—standard `py-5` padding sufficient
- **Banner Compatibility**: Component is agnostic to banner presence
- **Theme Response**: Full palette support via `@HostBinding` + CSS variables
- **Mobile Behavior**: Responsive utilities handle all breakpoints
- **Layout Contract**: Standalone component with no parent assumptions

---

## Anti-Patterns for This Type

- ❌ **Don't hardcode theme colors** in component SCSS (use `--bs-primary` variable)
- ❌ **Don't assume header spacing**—add explicit padding in this component
- ❌ **Don't use `*ngIf` instead of `@if`** (use modern control flow syntax)
- ❌ **Don't create multiple card form copies**—use inputs/query params for customization
- ❌ **Don't bypass autofill detection**—monitor focus changes for proper form sync

---

**Last Updated**: January 1, 2026
