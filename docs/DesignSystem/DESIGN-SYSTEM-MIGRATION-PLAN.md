# Design System Migration Plan

**Goal:** Convert all components and pages to use the centralized design system (`_tokens.scss`, utilities) instead of local hardcoded SCSS.

**Reference:** See [AI-AGENT-CODING-CONVENTIONS.md](./AI-AGENT-CODING-CONVENTIONS.md) (CWCC) for standards.

---

## 🎯 Migration Strategy

### Phase 1: Automated Scanning (Current)
Identify all components with design system violations:

1. **Hardcoded hex colors** (`#ffffff`, `#0d6efd`, etc.)
2. **Hardcoded RGB/RGBA** (`rgb(255, 255, 255)`)
3. **Magic numbers** for spacing (`17px`, `23px`)
4. **Arbitrary border-radius** values
5. **Inline styles** in templates

### Phase 2: Prioritized Migration
Convert components in order of:
1. **Layout/Infrastructure** (headers, footers, menus) - highest visibility
2. **Wizard components** (registration flows) - most complex
3. **Form components** - most reused
4. **Page components** - lower priority
5. **Utility components** - lowest priority

### Phase 3: Prevention
Set up automated enforcement to prevent regression.

---

## 📊 Current Violations Detected

### High Priority Components (50+ violations found)

**Team Registration Wizard:**
- `tw-step-indicator.component.scss` - 11+ hardcoded colors
- `team-registration-wizard.component.scss` - 9 hardcoded colors
- `club-team-management.component.scss` - 20+ hardcoded colors
- `club-team-add-modal.component.scss` - 15+ hardcoded colors
- `teams-step.component.scss` - 10+ hardcoded colors

**Other Components:**
- `job-landing.component.scss` - 3+ hardcoded colors
- `client-menu.component.scss` - duplicate border-bottom property

---

## 🔧 Conversion Guidelines

### Replace Hardcoded Colors

**❌ Before:**
```scss
.step-indicator {
    background-color: #dee2e6;
    color: #6c757d;
    border-color: #f8f9fa;
}
```

**✅ After:**
```scss
.step-indicator {
    background-color: var(--neutral-200);
    color: var(--neutral-500);
    border-color: var(--neutral-100);
}
```

### Replace Fallback Colors

**❌ Before:**
```scss
.card {
    background: var(--bg-secondary, #f9fafb);
    border: 1px solid var(--border-color, #dce1e8);
    color: var(--text-primary, #1f2937);
}
```

**✅ After:**
```scss
.card {
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    color: var(--text-primary);
}
```

**Why?** The design system tokens already have fallback values defined. Component-level fallbacks create maintenance burden and can cause inconsistency.

### Replace Magic Numbers

**❌ Before:**
```scss
.header {
    padding: 12px 16px;
    margin-bottom: 24px;
    border-radius: 8px;
}
```

**✅ After:**
```scss
.header {
    padding: var(--space-3) var(--space-4);
    margin-bottom: var(--space-6);
    border-radius: var(--radius-md);
}
```

### Use Utility Classes When Possible

**❌ Before:**
```scss
// In component.scss
.custom-card {
    background-color: white;
    border-radius: 8px;
    padding: 16px;
    margin-bottom: 16px;
}
```

**✅ After:**
```html
<!-- In component.html - no SCSS needed! -->
<div class="bg-surface rounded-md p-4 mb-4">
    <!-- content -->
</div>
```

---

## 📋 Migration Checklist Template

Use this for each component:

```markdown
### Component: [component-name]

**File:** `src/app/path/to/component.component.scss`

#### Violations Found:
- [ ] 5 hardcoded hex colors
- [ ] 3 hardcoded spacing values
- [ ] 2 arbitrary border-radius values
- [ ] 1 inline style in template

#### Conversion Plan:
1. Replace `#dee2e6` → `var(--neutral-200)`
2. Replace `#f8f9fa` → `var(--neutral-100)`
3. Replace `12px padding` → `var(--space-3)`
4. Replace `8px border-radius` → `var(--radius-md)`
5. Move inline `style="color: red"` to class `text-danger`

#### Testing:
- [ ] Light mode renders correctly
- [ ] Dark mode renders correctly ([data-bs-theme='dark'])
- [ ] All 6 wizard themes work (player, team, family, login, role-select, landing)
- [ ] Responsive on mobile (375px)
- [ ] No visual regression

#### PR Link:
[Add link when created]
```

---

## 🚀 Recommended Execution Plan

### Week 1: Infrastructure & Layout
**Goal:** Fix highest-visibility components

1. **Day 1:** Layout components
   - `client-header-bar.component.scss`
   - `client-menu.component.scss`
   - `client-banner.component.scss`
   - `client-footer.component.scss` (if exists)

2. **Day 2:** Public layout
   - `public-layout.component.scss`
   - `job-landing.component.scss`

3. **Day 3-5:** Team Registration Wizard
   - `tw-step-indicator.component.scss`
   - `team-registration-wizard.component.scss`
   - `club-team-management.component.scss`
   - `club-team-add-modal.component.scss`
   - `teams-step.component.scss`

### Week 2: Form & Utility Components
**Goal:** Fix reusable components

4. **Day 6-7:** Form components
   - All form-related component SCSS files
   - Input wrappers, selects, checkboxes, radios

5. **Day 8-9:** Utility components
   - Modals, tooltips, dropdowns
   - Cards, badges, buttons (if custom styles exist)

### Week 3: Pages & Polish
**Goal:** Complete remaining components

6. **Day 10-12:** Page components
   - All view/page component SCSS files
   - Dashboard, profile, settings, etc.

7. **Day 13-14:** Cleanup & Testing
   - Remove unused SCSS files
   - Test all themes and modes
   - Document any exceptions

### Week 4: Enforcement & Prevention
**Goal:** Prevent regression

8. **Day 15:** Set up Stylelint
   - Install and configure
   - Add to CI/CD pipeline
   - Create pre-commit hooks

9. **Day 16:** Documentation
   - Update COMPONENT-TEMPLATE.md
   - Create migration guide for team
   - Record demo video

10. **Day 17-20:** Buffer/Polish
    - Address any edge cases
    - Team training session
    - Final QA pass

---

## 🔍 Automated Scanning Commands

### Find all hardcoded colors:
```bash
# PowerShell
grep -r "#[0-9a-fA-F]\{3,6\}" src/app --include="*.scss" | Select-String -Pattern "\.component\.scss"
```

### Find all hardcoded spacing:
```bash
# PowerShell
grep -r "\b[0-9]\{1,2\}px\b" src/app --include="*.scss"
```

### Find inline styles:
```bash
# PowerShell
grep -r 'style="' src/app --include="*.html"
```

### Count violations per component:
```bash
# PowerShell
Get-ChildItem -Path "src/app" -Recurse -Filter "*.component.scss" | ForEach-Object {
    $colors = (Select-String -Path $_.FullName -Pattern "#[0-9a-fA-F]{3,6}" -AllMatches).Matches.Count
    if ($colors -gt 0) {
        Write-Output "$($_.Name): $colors violations"
    }
} | Sort-Object
```

---

## 🛡️ Post-Migration Enforcement

### 1. Add to `.stylelintrc.json`:
```json
{
  "extends": "stylelint-config-standard-scss",
  "rules": {
    "color-no-hex": true,
    "color-named": "never",
    "declaration-no-important": true,
    "function-disallowed-list": ["rgb", "rgba", "hsl", "hsla"]
  }
}
```

### 2. Add to PR template:
```markdown
## Design System Compliance
- [ ] No hardcoded colors (uses CSS variables)
- [ ] No magic numbers (uses spacing tokens)
- [ ] Tested in dark mode
- [ ] Uses utility classes where appropriate
```

### 3. Add to CI/CD:
```yaml
# .github/workflows/ci.yml
- name: Lint Styles
  run: npm run lint:styles
```

---

## 📚 Resources

- **Design System Tokens:** [_tokens.scss](../TSIC-Core-Angular/src/frontend/tsic-app/src/styles/_tokens.scss)
- **Utility Classes:** [_utilities.scss](../TSIC-Core-Angular/src/frontend/tsic-app/src/styles/_utilities.scss)
- **Coding Conventions:** [AI-AGENT-CODING-CONVENTIONS.md](./AI-AGENT-CODING-CONVENTIONS.md)
- **Component Template:** [COMPONENT-TEMPLATE.md](./COMPONENT-TEMPLATE.md)
- **Enforcement Guide:** [DESIGN-SYSTEM-ENFORCEMENT.md](./DESIGN-SYSTEM-ENFORCEMENT.md)

---

## ✅ Success Metrics

**You'll know the migration is successful when:**

1. ✅ Zero hardcoded colors in component SCSS files
2. ✅ Zero magic numbers for spacing/sizing
3. ✅ All components work in light AND dark mode
4. ✅ Stylelint passes with zero violations
5. ✅ Team can build new components without adding SCSS
6. ✅ Design changes (color palette swap) take <1 minute to apply globally

---

## 🤝 Need Help?

- **Quick question?** Check [DESIGN-SYSTEM-QUICK-REFERENCE.md](./DESIGN-SYSTEM-QUICK-REFERENCE.md)
- **Not sure which token?** Review [_tokens.scss](../TSIC-Core-Angular/src/frontend/tsic-app/src/styles/_tokens.scss)
- **Complex component?** Reference [COMPONENT-TEMPLATE.md](./COMPONENT-TEMPLATE.md)
- **Agent assistance?** Prefix request with **"CWCC"** to enforce conventions

---

**Last Updated:** December 30, 2025  
**Status:** Ready for execution
