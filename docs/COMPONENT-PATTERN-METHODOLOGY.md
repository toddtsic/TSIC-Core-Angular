# Component Pattern Analysis Methodology

## Objective
Unify all components through systematic pattern identification and codification. 
Create institutional knowledge for consistent future development.

## Related Documentation
- [DESIGN-SYSTEM.md](DESIGN-SYSTEM.md) - CSS variables, glassmorphic patterns, palette system
- [angular-signal-patterns.md](angular-signal-patterns.md) - Signal-based state management
- [COMPONENT-TEMPLATE.md](COMPONENT-TEMPLATE.md) - Component scaffolding template
- [.github/copilot-instructions.md](../.github/copilot-instructions.md) - Project-wide coding standards
- [layout-architecture.md](layout-architecture.md) - Chrome component contracts

## Analysis Framework

For each component, identify:

### 1. Component Type Classification
- **Primary Type**: What category? (Form View, List View, Widget, Layout, etc.)
- **Subtype**: Specific variant? (Authentication, Multi-step, CRUD, etc.)

### 2. Chrome Compatibility Analysis
Every component must define its relationship with chrome components:
- **Header** (`client-header-bar`): Vertical spacing, z-index, mobile considerations
  - Component: `src/app/layouts/components/client-header-bar/`
  - Fixed positioning considerations, header height variables
- **Banner** (`client-banner`): Either/or relationship, background assumptions
  - Component: `src/app/layouts/components/client-banner/`
  - Hero imagery, gradient overlays, content positioning
- **Theme System**: Palette-responsive tokens, dark mode, wizard themes
  - 8 dynamic palettes (Blue, Purple, Pink, Crimson, Teal, Orange, Olive, Gray)
  - Wizard-specific gradients (`wizard-theme-login`, `wizard-theme-player`, `wizard-theme-family`)
- **Layout Contract**: Responsive behavior, isolation requirements
  - No assumptions about parent container padding/margins
  - Uses semantic spacing tokens (--bs-gap-*, --bs-padding-*)

✅ **Chrome Contract Checklist:**
- Respects vertical rhythm (no collision with header/footer)
- Uses semantic color tokens (no hardcoded colors)
- Works in isolation (no assumptions about context)
- Handles responsive chrome (mobile variations)
- Layer correctly (z-index harmony)

### 3. Abstractable Features
Document what can be reused when this type appears again:
- **Visual patterns**: Card structure, spacing, shadows, borders
- **Interaction patterns**: Hover states, loading states, validation
- **Layout patterns**: Centering, grid, flex arrangements
- **Common elements**: Headers, footers, buttons, alerts

### 4. Styling Rules Inventory
Document actual utility classes used, token dependencies, component-specific overrides:

**CSS Variable Categories:**
- **Color Tokens**: `--bs-primary`, `--bs-success`, `--bs-danger`, `--bs-light`, `--bs-dark`, `--bs-body-bg`, `--bs-card-bg`, `--border-color`
- **Spacing Tokens**: `--bs-gap-2`, `--bs-gap-3`, `--bs-padding-card`, `--bs-border-radius`
- **Elevation Effects**: `--shadow-xs`, `--shadow-sm`, `--shadow-md`, `--shadow-lg`, `--shadow-xl`
- **Surface Tokens**: `--brand-surface`, `--bg-elevated`, `--neutral-0-rgb`
- **Z-Index Layers**: `--z-header`, `--z-modal`, `--z-dropdown`, `--z-tooltip`

**Bootstrap Utility Patterns:**
- **Layout**: `d-flex`, `flex-column`, `gap-*`, `justify-content-*`, `align-items-*`
- **Spacing**: `m-*`, `p-*`, `mb-*`, `mt-*` (prefer tokens over raw Bootstrap)
- **Typography**: `text-center`, `fw-bold`, `fs-*`, `text-muted`
- **Display**: `d-none`, `d-md-block`, responsive variants

**Elevation Design Patterns:**
- **Backdrop blur**: `backdrop-filter: blur(8px)` (use sparingly, GPU-intensive)
- **Inset highlights**: `box-shadow: inset 0 1px 0 rgba(255,255,255,0.1)`
- **Layered backgrounds**: `linear-gradient(...)` for depth
- **Semi-transparent surfaces**: `rgba(var(--neutral-0-rgb), 0.9)`

**When to Use Component Styles vs Global Utilities:**
- **Component SCSS**: Complex pseudo-elements, animations, unique interactions
- **Global Utilities**: Standard spacing, display, flex, basic colors
- **CSS Variables**: ALL colors (never hardcode hex values)

### 5. Anti-Patterns
What NOT to do when building this type and why:

**Styling Anti-Patterns:**
- ❌ **Hardcoded colors** - Breaks palette switching, use CSS variables only
- ❌ **Absolute positioning without isolation** - Collides with chrome components
- ❌ **Fixed dimensions** - Not responsive, use flex/grid with percentages
- ❌ **Important overrides** - Indicates specificity issues, refactor selectors
- ❌ **Inline styles** - Bypasses theme system, defeats design tokens

**Angular Architecture Anti-Patterns:**
- ❌ **BehaviorSubject for component state** - Use signals instead
- ❌ **Old template syntax** (`*ngIf`, `*ngFor`) - Use `@if`, `@for`
- ❌ **Constructor injection** - Use `inject()` function pattern
- ❌ **Manual subscriptions** - Prefer async pipe or signal-based patterns
- ❌ **Non-standalone components** - All new components must be standalone

**Layout Anti-Patterns:**
- ❌ **Assuming parent padding** - Component must work in any container
- ❌ **Magic numbers** - Use design system tokens (`--bs-gap-3`, not `12px`)
- ❌ **Breaking glass containment** - Elements escaping card boundaries
- ❌ **Z-index conflicts** - Use semantic layer variables (`--z-modal`)

**Data Flow Anti-Patterns:**
- ❌ **Direct DbContext access** - Always use repository pattern
- ❌ **Duplicate type definitions** - Import from auto-generated API models
- ❌ **Positional DTOs** - Use `init` properties with `required` keyword

### 6. Angular Architecture Patterns
Document Angular 21 standalone architecture specifics:

**Component Structure:**
- **Signals for State**: Use `signal()` for component-local state, `computed()` for derived values
- **Dependency Injection**: `private readonly service = inject(ServiceName);`
- **Template Syntax**: Modern control flow (`@if`, `@for`, `@switch`)
- **Standalone Imports**: List required modules in `imports: []` array

**State Management Patterns:**
```typescript
// ✅ Service owns domain state
export class DataService {
  readonly items = signal<Item[]>([]);
  readonly loading = signal(false);
}

// ✅ Component reads service signals
export class ListComponent {
  private readonly data = inject(DataService);
  items = this.data.items;  // Direct signal reference
  count = computed(() => this.items().length);
}
```

**Input/Output Contracts:**
- **Inputs**: Use `@Input()` with type safety, provide defaults
- **Outputs**: Use `@Output()` with `EventEmitter<T>` for type safety
- **Two-way binding**: `[(ngModel)]` or signal-based patterns

**Common Imports:**
- **Forms**: `ReactiveFormsModule`, `FormBuilder`, `Validators`
- **Routing**: `Router`, `ActivatedRoute`, `RouterModule`
- **UI Libraries**: `TextBoxModule`, `ButtonModule` (Syncfusion)
- **Utilities**: `AutofillMonitor`, `ElementRef`, `ViewChild`

## Pattern Documentation Template

### [Pattern Name] - [Component Type]

**Component Location**: `src/app/views/...` or `src/app/layouts/...`  
**Chrome Dependencies**: Header / Banner / Theme / None  
**Reusability Score**: High / Medium / Low  
**Example Component**: [component.ts](path/to/component.ts)

#### Visual Pattern
```
┌─────────────────────────────────┐
│        Header Text              │
│        Subheader Text           │
│                                 │
│  ┌──────────────────────────┐   │
│  │  Content Area            │   │
│  │  (Form/Table/etc.)       │   │
│  └──────────────────────────┘   │
│                                 │
│         [Action Button]         │
└─────────────────────────────────┘
```

#### Code Structure
```typescript
// Key architectural patterns (signals, DI, template syntax)
export class ExampleComponent {
  private readonly service = inject(ServiceName);
  
  // Signals for local state
  submitted = signal(false);
  loading = signal(false);
  
  // Computed derived state
  isValid = computed(() => !this.loading() && this.form.valid);
}
```

#### Template Patterns
```html
<!-- Modern control flow -->
@if (loading()) {
  <div class="loading-spinner"></div>
}
@else {
  @for (item of items(); track item.id) {
    <div class="item">{{ item.name }}</div>
  }
}
```

#### Styling Contract
**CSS Variables Used:**
- `--bs-primary`, `--bs-card-bg`, `--border-color`, `--shadow-md`

**Bootstrap Utilities:**
- `d-flex`, `flex-column`, `gap-3`, `mb-4`, `text-center`

**Custom SCSS:**
```scss
.component-class {
  background: rgba(var(--neutral-0-rgb), 0.95);
  backdrop-filter: blur(10px) saturate(180%);
  box-shadow: var(--shadow-lg);
}
```

#### Reusable Abstractions
1. **[Feature Name]** - Can be extracted to `src/app/shared/components/`
2. **[Pattern Name]** - Applicable to [list component types]
3. **[Utility Name]** - Consider directive/pipe if used 3+ times

#### Chrome Integration
- **Header Spacing**: Uses `padding-top: calc(var(--header-height) + 1rem)`
- **Banner Compatibility**: Works with/without banner present
- **Theme Response**: Adapts to all 8 palette themes
- **Mobile Behavior**: Collapses to single column below 768px

#### Anti-Patterns for This Type
- ❌ Don't [specific anti-pattern] - [reason/consequence]
- ❌ Don't [specific anti-pattern] - [reason/consequence]

---

## Current Progress

### Completed Analysis:
- ✅ **Centered Card Form** ([centered-card-form.md](./component-patterns/centered-card-form.md)) - Type: Form View > Authentication
  - Themeable via `@Input() theme` or query params
  - Signals for state (`submitted`, `showPassword`)
  - Wizard theme classes (`wizard-theme-login`, `wizard-theme-player`, `wizard-theme-family`)
  - Elevated card design with backdrop blur
  - Works in isolation (standalone route or embedded in wizard)

### Next Components to Analyze:

**Priority 1: Foundational Patterns** (needed by all features)
- [ ] **Wizard Flow** - Multi-step form container with navigation
  - Location: `src/app/views/registration/wizards/`
  - Why: Core registration pattern, reused 10+ times
- [ ] **Client Header Bar** - Global navigation chrome
  - Location: `src/app/layouts/components/client-header-bar/`
  - Why: Affects all page layouts, z-index authority

**Priority 2: Data Presentation** (CRUD operations)
- [ ] **Data Table** - Sortable, filterable table with actions
  - Example: Team roster, family member list
  - Why: Standard list view pattern
- [ ] **Profile Editor** - Dynamic form with metadata-driven fields
  - Location: `src/app/views/profile/`
  - Why: Complex form with conditional fields, validation

**Priority 3: Marketing/Landing** (public-facing)
- [ ] **Banner Hero** - Full-width hero with background image
  - Location: `src/app/layouts/components/client-banner/`
  - Why: Primary landing page component
- [ ] **Card Grid** - Responsive grid of feature cards
  - Example: Dashboard widgets, service selection
  - Why: Common dashboard pattern

---

**Last Updated**: January 1, 2026