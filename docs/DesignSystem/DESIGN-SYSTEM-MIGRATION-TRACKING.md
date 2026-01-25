# Design System Migration Tracking

**Created:** December 30, 2025  
**Status:** In Progress  
**Total Components:** 33 SCSS files  
**Components with Violations:** 8 (24%)

---

## 🎯 Priority Queue

### 🔴 Critical (Immediate) - 12+ violations

| Component | File | Hex Colors | Status | Assigned | Notes |
|-----------|------|------------|--------|----------|-------|
| Team Wizard - Step Indicator | `tw-step-indicator.component.scss` | 12 | 🔴 Not Started | - | Hardcoded grays, Bootstrap fallbacks |
| Club Team Management | `club-team-management.component.scss` | 14 | 🔴 Not Started | - | Complex component, many fallback colors |
| Club Team Add Modal | `club-team-add-modal.component.scss` | 11 | 🔴 Not Started | - | Modal styling needs review |

### 🟡 High Priority (This Week) - 5-11 violations

| Component | File | Hex Colors | Status | Assigned | Notes |
|-----------|------|------------|--------|----------|-------|
| Teams Step | `teams-step.component.scss` | 10 | 🟡 Not Started | - | Team registration step component |
| Bulletins | `bulletins.component.scss` | 7 | 🟡 Not Started | - | Content display component |
| Team Wizard Main | `team-registration-wizard.component.scss` | 5 | 🟡 Not Started | - | Main wizard container |

### 🟢 Medium Priority (Next Week) - 1-4 violations

| Component | File | Hex Colors | Status | Assigned | Notes |
|-----------|------|------------|--------|----------|-------|
| Job Landing | `job-landing.component.scss` | 3 | 🟢 Not Started | - | Public landing page |
| Menus | `menus.component.scss` | 2 | 🟢 Not Started | - | Menu display component |

### ✅ Clean (No Action Needed)

25 components have zero hardcoded colors - already compliant! 🎉

---

## 📊 Migration Statistics

```
Total Components:        33
With Violations:          8 (24%)
Clean Components:        25 (76%)

Total Hex Violations:    64
Average per file:         8

Critical (12+):           3 components
High (5-11):              3 components  
Medium (1-4):             2 components
```

---

## 🔧 Next Actions

### Today (December 30, 2025)
1. ✅ Created migration plan document
2. ✅ Scanned all component SCSS files
3. ✅ Generated violation report
4. ⏳ Start with highest priority: `tw-step-indicator.component.scss`

### This Week
1. Convert all 🔴 Critical components (3 files)
2. Convert all 🟡 High Priority components (3 files)
3. Test in all 6 wizard themes + dark mode

### Next Week
1. Convert 🟢 Medium Priority components (2 files)
2. Set up Stylelint for enforcement
3. Document any edge cases

---

## 🎨 Conversion Reference

### Common Pattern: Bootstrap Fallbacks

**❌ Before:**
```scss
background-color: var(--bs-primary, #0d6efd);
border-color: var(--bs-success, #198754);
color: var(--text-primary, #1f2937);
```

**✅ After:**
```scss
background-color: var(--bs-primary);
border-color: var(--bs-success);
color: var(--text-primary);
```

### Common Pattern: Gray Scale

**❌ Before:**
```scss
background-color: #dee2e6;
border-color: #f8f9fa;
color: #6c757d;
```

**✅ After:**
```scss
background-color: var(--neutral-200);
border-color: var(--neutral-100);
color: var(--neutral-500);
```

### Common Pattern: Component-specific Fallbacks

**❌ Before:**
```scss
background: var(--bg-secondary, #f9fafb);
border: 1px solid var(--border-color, #dce1e8);
color: var(--text-primary, #1f2937);
```

**✅ After:**
```scss
background: var(--bg-secondary);
border: 1px solid var(--border-color);
color: var(--text-primary);
```

---

## 📋 Component Details

### 1. tw-step-indicator.component.scss (12 violations)

**Path:** `views/registration/wizards/team-registration-wizard/step-indicator/`

**Violations:**
- `#dee2e6` → `var(--neutral-200)`
- `#f8f9fa` → `var(--neutral-100)`
- `#6c757d` → `var(--neutral-500)`
- `#0d6efd` (fallback) → Remove fallback
- `#198754` (fallback) → Remove fallback

**Testing Required:**
- [ ] Wizard step navigation works
- [ ] Current/completed/pending states visible
- [ ] Dark mode rendering
- [ ] All 6 wizard themes

---

### 2. club-team-management.component.scss (14 violations)

**Path:** `views/registration/wizards/team-registration-wizard/club-team-management/`

**Violations:**
- Multiple gray scale colors (#f8f9fa, #dee2e6, #495057)
- Many component-specific fallbacks
- Table styling with hardcoded colors

**Testing Required:**
- [ ] Team list display
- [ ] Add/edit/delete actions
- [ ] Table sorting/filtering
- [ ] Responsive layout
- [ ] Dark mode

---

### 3. club-team-add-modal.component.scss (11 violations)

**Path:** `views/registration/wizards/team-registration-wizard/club-team-add-modal/`

**Violations:**
- Modal header/body/footer colors
- Form input styling
- Button styling
- Border colors

**Testing Required:**
- [ ] Modal opens/closes
- [ ] Form validation styling
- [ ] Button hover states
- [ ] Dark mode

---

### 4. teams-step.component.scss (10 violations)

**Path:** `views/registration/wizards/team-registration-wizard/teams-step/`

**Violations:**
- Similar patterns to club-team components
- Card/list styling

**Testing Required:**
- [ ] Team selection UI
- [ ] Dark mode
- [ ] Wizard theme variations

---

### 5. bulletins.component.scss (7 violations)

**Path:** `views/bulletins/`

**Violations:**
- Content display styling
- Border colors
- Background colors

**Testing Required:**
- [ ] Bulletin display
- [ ] Dark mode
- [ ] Responsive layout

---

### 6. team-registration-wizard.component.scss (5 violations)

**Path:** `views/registration/wizards/team-registration-wizard/`

**Violations:**
- Wizard container styling
- Background colors
- Border colors

**Testing Required:**
- [ ] Wizard navigation
- [ ] Step transitions
- [ ] All 6 themes work

---

### 7. job-landing.component.scss (3 violations)

**Path:** `views/home/job-landing/`

**Violations:**
- Landing page card styling
- Border colors
- Background colors

**Testing Required:**
- [ ] Landing page renders
- [ ] Card layouts
- [ ] Dark mode

---

### 8. menus.component.scss (2 violations)

**Path:** `views/menus/`

**Violations:**
- Menu item styling
- Border colors

**Testing Required:**
- [ ] Menu display
- [ ] Dark mode

---

## ✅ Success Criteria

**Component migration is complete when:**

1. ✅ Zero hardcoded hex colors (`#abc123`)
2. ✅ Zero hardcoded RGB/RGBA values
3. ✅ No component-specific fallback colors
4. ✅ Uses design system tokens exclusively
5. ✅ Tested in light mode
6. ✅ Tested in dark mode
7. ✅ Tested in all applicable wizard themes
8. ✅ No visual regression
9. ✅ Stylelint passes
10. ✅ Peer reviewed

---

## 🚀 Workflow

### For Each Component:

1. **Read** the component SCSS file
2. **Identify** all hardcoded values
3. **Map** to appropriate design tokens:
   - Colors → `_tokens.scss` variables
   - Spacing → `--space-*` tokens
   - Borders → `--radius-*` tokens
   - Shadows → `--shadow-*` tokens
4. **Replace** hardcoded values
5. **Test** in browser (light + dark mode)
6. **Verify** in all relevant wizard themes
7. **Commit** with message: `style(component-name): migrate to design system tokens`
8. **Update** this tracking document

### Testing Checklist Per Component:

```markdown
- [ ] Component renders correctly
- [ ] Light mode: ✅
- [ ] Dark mode: ✅
- [ ] Mobile (375px): ✅
- [ ] Tablet (768px): ✅
- [ ] Desktop (1440px): ✅
- [ ] Wizard theme: player ✅
- [ ] Wizard theme: team ✅
- [ ] Wizard theme: family ✅
- [ ] No console errors: ✅
- [ ] No visual regression: ✅
```

---

## 📝 Notes

### Design Decisions Made:
- Remove all component-level color fallbacks (tokens already have defaults)
- Bootstrap variable fallbacks can be removed (already mapped in `_tokens.scss`)
- Magic numbers replaced with closest matching token
- Arbitrary colors mapped to nearest neutral scale value

### Edge Cases:
- **None identified yet** - will document as encountered

### Questions/Blockers:
- **None** - ready to proceed

---

**Last Updated:** December 30, 2025  
**Next Review:** After first 3 critical components completed
