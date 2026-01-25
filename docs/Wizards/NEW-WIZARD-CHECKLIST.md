# New Wizard Creation Checklist

Use this checklist when creating a new wizard (Family, Admin, etc.).

---

## Phase 1: Planning (5 minutes)

- [ ] Define wizard name (e.g., "family-account-wizard")
- [ ] Define short code (e.g., "fw-" for family wizard)
- [ ] Define theme name (e.g., "family")
- [ ] Choose theme color from design system
- [ ] Define step sequence
- [ ] Identify forms needed
- [ ] Plan navigation flow

---

## Phase 2: Setup (10 minutes)

### Create Folder Structure
```
src/app/views/registration/wizards/YOUR-wizard/
  ├── YOUR-wizard.component.ts
  ├── YOUR-wizard.component.html
  ├── YOUR-wizard.component.scss
  ├── action-bar/
  │   ├── YOUR-action-bar.component.ts
  │   ├── YOUR-action-bar.component.html
  │   └── YOUR-action-bar.component.scss
  ├── services/
  │   └── YOUR.service.ts
  └── steps/
      ├── step-one/
      │   ├── YOUR-step-one.component.ts
      │   ├── YOUR-step-one.component.html
      │   └── YOUR-step-one.component.scss
      └── step-two/
          ├── YOUR-step-two.component.ts
          ├── YOUR-step-two.component.html
          └── YOUR-step-two.component.scss
```

### Register Theme
- [ ] Open `src/styles/_variables.scss`
- [ ] Find `$wizard-themes` map
- [ ] Add entry:
  ```scss
  'your-theme': (
      primary: var(--your-color),
      primary-rgb: R G B,
      gradient-start: var(--your-color-light),
      gradient-end: var(--your-color-dark)
  ),
  ```
- [ ] Verify color variables exist in color palette

---

## Phase 3: Main Wizard Component (15 minutes)

### Create `YOUR-wizard.component.ts`
- [ ] Use `@Component({ standalone: true })`
- [ ] Use signals for state (not observables)
- [ ] Inject services with `inject()`
- [ ] Define step definitions
- [ ] Implement navigation (back/next)
- [ ] Handle form validation
- [ ] Export in module or standalone imports

### Create `YOUR-wizard.component.html`
- [ ] Copy structure from `docs/wizard-styling-quick-reference.md`
- [ ] Update class name: `class="YOUR-wizard-container"`
- [ ] Update aria-labelledby: `aria-labelledby="YOUR-title"`
- [ ] Update theme: `[wizardTheme]="'your-theme'"`
- [ ] Update title ID: `id="YOUR-title"`
- [ ] Add step cases in `@switch`
- [ ] Import all step components

### Create `YOUR-wizard.component.scss`
- [ ] Copy from `docs/wizard-styling-quick-reference.md`
- [ ] Update class names to match HTML
- [ ] Add comments referencing global styles
- [ ] **Keep minimal - don't add custom styling**

---

## Phase 4: Action Bar Component (10 minutes)

### Create `action-bar/YOUR-action-bar.component.ts`
- [ ] Use pattern from `docs/wizard-component-patterns.md`
- [ ] Add `host: { class: 'action-bar' }` **CRITICAL**
- [ ] Define inputs: canBack, canContinue, continueLabel, showContinue
- [ ] Define inputs: optional detailsBadge, detailsBadgeClass
- [ ] Define outputs: back, continue

### Create `action-bar/YOUR-action-bar.component.html`
- [ ] Use pattern from `docs/wizard-component-patterns.md`
- [ ] Left section: badge (optional)
- [ ] Right section: back/continue buttons
- [ ] Add aria-labels

### Create `action-bar/YOUR-action-bar.component.scss`
- [ ] Add comment: "Global .action-bar styles apply"
- [ ] Leave empty (no custom styling)

---

## Phase 5: Step Components (30 minutes per step)

### For Each Step:

#### Create `steps/YOUR-step-X/YOUR-step-X.component.ts`
- [ ] Use pattern from `docs/wizard-component-patterns.md`
- [ ] Use `@Component({ standalone: true })`
- [ ] Create FormGroup in ngOnInit
- [ ] Define form validation rules
- [ ] Implement onSubmit() method
- [ ] Handle errors with errorMessage signal
- [ ] Emit output on success: `this.next.emit(data)`
- [ ] Include getFieldError() helper for validation display

#### Create `steps/YOUR-step-X/YOUR-step-X.component.html`
- [ ] Use pattern from `docs/wizard-component-patterns.md`
- [ ] Start with `<form [formGroup]="form()" (ngSubmit)="onSubmit()">`
- [ ] Add error banner if errorMessage()
- [ ] Add form fields with validation display
- [ ] Submit button with loading state: `[disabled]="!form()?.valid || isSubmitting()"`
- [ ] Use spinner for loading state
- [ ] Add aria-labels for accessibility

#### Create `steps/YOUR-step-X/YOUR-step-X.component.scss`
- [ ] Leave empty or add only component-specific styles
- [ ] Use global form control classes

---

## Phase 6: Services (15 minutes)

### Create `services/YOUR.service.ts`
- [ ] Use `@Injectable({ providedIn: 'root' })`
- [ ] Define business logic methods
- [ ] Return Observables for HTTP calls
- [ ] Store step data using signals in main component
- [ ] Keep service focused on one domain

---

## Phase 7: Routing (5 minutes)

### Update `app.routes.ts`
- [ ] Add route: 
  ```typescript
  {
    path: 'your-wizard',
    component: YourWizardComponent,
    data: { requirePhase2: true }  // or appropriate guard
  }
  ```
- [ ] Add route guard if needed
- [ ] Add breadcrumb data if applicable

---

## Phase 8: Testing (20 minutes)

### Visual Testing
- [ ] Load wizard in browser
- [ ] Verify card styling matches other wizards
- [ ] Verify action bar styling and positioning
- [ ] Verify step indicator styling
- [ ] Verify responsive layout on mobile
- [ ] Test dark mode toggle

### Functional Testing
- [ ] Test form validation
- [ ] Test back/continue navigation
- [ ] Test error handling
- [ ] Test form submission
- [ ] Test loading states
- [ ] Test dark mode colors
- [ ] Test accessibility (keyboard nav, aria labels)

### Cross-Browser Testing
- [ ] Test in Chrome
- [ ] Test in Firefox
- [ ] Test in Edge
- [ ] Test on mobile browser

---

## Phase 9: Documentation (5 minutes)

### Update Docs
- [ ] Add wizard to `WIZARD-STYLING-IMPLEMENTATION.md`
- [ ] Document any custom business logic
- [ ] Update team wiki/confluence if applicable

---

## Phase 10: Code Review Checklist

Before submitting for review:

- [ ] No hardcoded colors (use CSS variables)
- [ ] No duplicate SCSS (uses global classes)
- [ ] All form fields have validation
- [ ] All async operations show loading state
- [ ] All errors are displayed to user
- [ ] Component SCSS is minimal
- [ ] Action bar has `host: { class: 'action-bar' }`
- [ ] No deprecated patterns (*ngIf, *ngFor, BehaviorSubject, etc.)
- [ ] All form controls use global styling
- [ ] All outputs are properly emitted
- [ ] TypeScript strict mode passes
- [ ] Build passes with no warnings
- [ ] Dark mode works correctly
- [ ] Mobile responsive layout works
- [ ] Accessibility standards met

---

## Time Estimates

| Phase | Time | Cumulative |
|-------|------|-----------|
| Planning | 5 min | 5 min |
| Setup | 10 min | 15 min |
| Main Component | 15 min | 30 min |
| Action Bar | 10 min | 40 min |
| Step Components | 30 min × N | 40 + (30×N) |
| Services | 15 min | 55 + (30×N) |
| Routing | 5 min | 60 + (30×N) |
| Testing | 20 min | 80 + (30×N) |
| Documentation | 5 min | 85 + (30×N) |
| Code Review | - | - |

**Example**: 3-step wizard = 85 + (30×3) = **175 minutes (~3 hours)**

---

## Key Resources

Keep these documents open while building:

1. 📄 **Quick Reference**: `docs/wizard-styling-quick-reference.md`
   - Copy-paste templates
   - Class reference
   - Variable reference

2. 📄 **Component Patterns**: `docs/wizard-component-patterns.md`
   - Action bar pattern
   - Step component pattern
   - Form control patterns
   - Alert patterns

3. 📄 **Styling Guide**: `docs/wizard-styling-guide.md`
   - Complete styling reference
   - Customization examples
   - Troubleshooting

4. 📄 **Implementation**: `docs/WIZARD-STYLING-IMPLEMENTATION.md`
   - What's included globally
   - Benefits
   - Summary

---

## Success Criteria ✅

Your wizard is ready when:

- ✅ All steps render correctly
- ✅ Styling matches player/team wizards
- ✅ Form validation works
- ✅ Navigation works (back/continue)
- ✅ Dark mode works
- ✅ Mobile responsive
- ✅ Accessible (keyboard nav, aria labels)
- ✅ All tests pass
- ✅ Code review passes
- ✅ No console warnings/errors

---

## Common Issues & Solutions

### Issue: Styling looks different
**Solution**: Check that `class="wizard-theme-YOUR-THEME"` is on the card element and theme is registered in `_variables.scss`

### Issue: Action bar not sticky
**Solution**: Ensure action bar component has `host: { class: 'action-bar' }` and is inside `.wizard-action-bar-container`

### Issue: Dark mode colors wrong
**Solution**: Use CSS variables (--bs-*, --color-*), not hardcoded colors

### Issue: Mobile layout broken
**Solution**: Use Bootstrap grid classes (col-md-*, g-3) and responsive utilities

### Issue: Form validation not showing
**Solution**: Mark fields as touched: `control.markAsTouched()` and check `.is-invalid` class

### Issue: Loading state flickering
**Solution**: Use signal, not observable, and ensure loading state is properly set/cleared

---

## Next: Create Your Wizard

Use this checklist to create a new wizard. When done, verify against the **Success Criteria** section above.

**Questions?** See docs or ask the team.
