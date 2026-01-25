# Design System Enforcement Guide

How to make the TSIC Design System **mandatory** in your Angular project (not optional).

---

## 🛡️ **Enforcement Strategy**

### **Level 1: Documentation (Current)**
✅ DESIGN-SYSTEM.md  
✅ COMPONENT-TEMPLATE.md  
✅ DESIGN-SYSTEM-QUICK-REFERENCE.md  

**Pros:** Easy to implement, flexible  
**Cons:** Relies on developer discipline

---

### **Level 2: Code Review Checklist** (Recommended)

Add to your PR template (`.github/PULL_REQUEST_TEMPLATE.md`):

```markdown
## Design System Compliance Checklist

- [ ] Uses CSS variables from `_tokens.scss` (no hardcoded colors/spacing)
- [ ] Uses utility classes (`.bg-surface`, `.mb-4`, etc.)
- [ ] Tested with all 8 color palettes (screenshot attached)
- [ ] Responsive on mobile (375px), tablet (768px), desktop (1440px)
- [ ] Keyboard navigation works (tab through)
- [ ] Focus states visible (`:focus-visible`)
- [ ] No inline styles (use classes)
- [ ] No magic numbers (17px, 23px, etc.)
- [ ] Follows patterns from COMPONENT-TEMPLATE.md

**Palette Test Screenshot:**
[Attach screenshot showing component in 2-3 different palettes]
```

---

### **Level 3: Stylelint Rules** (Automated)

Install Stylelint to catch violations:

```bash
npm install --save-dev stylelint stylelint-config-standard-scss stylelint-scss
```

**stylelint.config.js:**
```javascript
module.exports = {
  extends: 'stylelint-config-standard-scss',
  rules: {
    // Disallow hardcoded colors (force CSS variables)
    'color-named': 'never',
    'color-no-hex': true,
    
    // Disallow arbitrary spacing values
    'declaration-property-value-disallowed-list': {
      '/^(margin|padding|gap)/': [
        '/^[0-9]+(px|rem|em)$/',  // Disallow arbitrary numbers
      ],
    },
    
    // Require CSS variables for common properties
    'function-disallowed-list': [
      'rgb',
      'rgba',
      'hsl',
      'hsla',
    ],
    
    // Warn on !important (rarely needed)
    'declaration-no-important': true,
    
    // Enforce consistent border-radius
    'declaration-property-value-allowed-list': {
      'border-radius': [
        'var(--radius-none)',
        'var(--radius-sm)',
        'var(--radius-md)',
        'var(--radius-lg)',
        'var(--radius-xl)',
        'var(--radius-full)',
      ],
    },
  },
};
```

**package.json:**
```json
{
  "scripts": {
    "lint:styles": "stylelint \"src/**/*.scss\"",
    "lint:styles:fix": "stylelint \"src/**/*.scss\" --fix"
  }
}
```

---

### **Level 4: ESLint for Angular Templates** (Advanced)

Install Angular ESLint:

```bash
ng add @angular-eslint/schematics
```

**Custom ESLint Rule (example):**
Create a custom rule to disallow inline styles in templates:

**.eslintrc.json:**
```json
{
  "overrides": [
    {
      "files": ["*.html"],
      "extends": [
        "plugin:@angular-eslint/template/recommended"
      ],
      "rules": {
        "@angular-eslint/template/no-inline-styles": "error"
      }
    }
  ]
}
```

---

### **Level 5: Pre-Commit Hooks** (Strictest)

Use Husky to enforce checks before commits:

```bash
npm install --save-dev husky lint-staged
npx husky install
```

**.husky/pre-commit:**
```bash
#!/bin/sh
. "$(dirname "$0")/_/husky.sh"

# Run linters
npm run lint:styles
npm run lint

# Check for hardcoded colors
if git diff --cached --name-only | grep -E '\.(scss|css)$' | xargs grep -E '#[0-9a-fA-F]{3,6}'; then
  echo "❌ ERROR: Hardcoded hex colors found! Use CSS variables instead."
  exit 1
fi

# Check for inline styles in templates
if git diff --cached --name-only | grep -E '\.html$' | xargs grep 'style='; then
  echo "⚠️  WARNING: Inline styles found. Consider using utility classes."
fi
```

**package.json:**
```json
{
  "lint-staged": {
    "*.scss": [
      "stylelint --fix",
      "git add"
    ],
    "*.ts": [
      "eslint --fix",
      "git add"
    ],
    "*.html": [
      "eslint --fix",
      "git add"
    ]
  }
}
```

---

## 🎯 **Recommended Implementation Path**

### **Phase 1: Documentation (Week 1)** ✅ Complete
- [x] DESIGN-SYSTEM.md
- [x] COMPONENT-TEMPLATE.md
- [x] DESIGN-SYSTEM-QUICK-REFERENCE.md

### **Phase 2: Team Training (Week 2)**
- [ ] Team meeting: Present design system
- [ ] Live demo: Show Brand Preview component
- [ ] Code walkthrough: Build one component together
- [ ] Q&A: Address concerns

### **Phase 3: Soft Enforcement (Week 3-4)**
- [ ] Add PR checklist template
- [ ] Assign design system "champion" for reviews
- [ ] Create Slack/Teams channel for questions
- [ ] Track violations (log, don't block)

### **Phase 4: Hard Enforcement (Week 5+)**
- [ ] Install Stylelint
- [ ] Configure pre-commit hooks
- [ ] Block PRs that fail linting
- [ ] Automated CI/CD checks

---

## 📊 **Measuring Compliance**

### **Metrics to Track:**

1. **Design Token Adoption**
   ```bash
   # Count CSS variable usage
   grep -r "var(--" src/**/*.scss | wc -l
   
   # Count hardcoded hex colors (bad)
   grep -rE "#[0-9a-fA-F]{3,6}" src/**/*.scss | wc -l
   ```

2. **Utility Class Usage**
   ```bash
   # Count utility classes in templates
   grep -rE "class=\".*bg-(surface|primary-subtle|neutral).*\"" src/**/*.html | wc -l
   ```

3. **Inline Style Violations**
   ```bash
   # Count inline styles (should be 0)
   grep -r "style=" src/**/*.html | wc -l
   ```

### **Weekly Compliance Report:**
```
Week of: [Date]
- Design token usage: 847 occurrences (+12% from last week)
- Hardcoded colors: 3 instances (-5 from last week)
- Inline styles: 0 violations (🎉 Clean!)
- Utility classes: 1,234 uses (+8% from last week)
```

---

## 🚨 **Common Violations & Fixes**

### **Violation 1: Hardcoded Hex Colors**
```scss
/* ❌ WRONG */
.my-component {
  background-color: #0ea5e9;
  color: #292524;
}

/* ✅ CORRECT */
.my-component {
  background-color: var(--bs-primary);
  color: var(--brand-text);
}
```

---

### **Violation 2: Magic Number Spacing**
```scss
/* ❌ WRONG */
.my-component {
  padding: 17px;
  margin-bottom: 23px;
}

/* ✅ CORRECT */
.my-component {
  padding: var(--space-4);  /* 16px */
  margin-bottom: var(--space-6);  /* 24px */
}
```

---

### **Violation 3: Inline Styles**
```html
<!-- ❌ WRONG -->
<div style="padding: 20px; background: white;">
  Content
</div>

<!-- ✅ CORRECT -->
<div class="p-4 bg-surface">
  Content
</div>
```

---

### **Violation 4: Component-Specific CSS for Common Patterns**
```scss
/* ❌ WRONG - Creating custom card styles */
.my-card {
  background: white;
  border-radius: 8px;
  box-shadow: 0 4px 6px rgba(0,0,0,0.1);
  padding: 16px;
}

/* ✅ CORRECT - Use Bootstrap card + utilities */
// No component CSS needed!
// HTML: <div class="card shadow-sm p-4">
```

---

## 🎓 **Training Resources**

### **Self-Paced Learning:**
1. Read DESIGN-SYSTEM.md (10 min)
2. Explore Brand Preview component (15 min)
3. Build practice component from COMPONENT-TEMPLATE.md (30 min)
4. Test with all 8 palettes (10 min)

### **Team Workshops:**
1. **Workshop 1: "Design System Overview"** (1 hour)
   - Why we need a design system
   - Tour of _tokens.scss
   - Live palette switching demo
   
2. **Workshop 2: "Building with Utilities"** (1 hour)
   - Build a dashboard card live
   - Common patterns walkthrough
   - Q&A
   
3. **Workshop 3: "Testing & Accessibility"** (1 hour)
   - Responsive testing
   - Keyboard navigation
   - Screen reader basics

---

## 📝 **Onboarding Checklist**

For new developers joining the team:

- [ ] Read DESIGN-SYSTEM-README.md
- [ ] Read DESIGN-SYSTEM.md
- [ ] Pin DESIGN-SYSTEM-QUICK-REFERENCE.md in VS Code
- [ ] Run app and explore Brand Preview component
- [ ] Build one component from COMPONENT-TEMPLATE.md
- [ ] Submit PR with design system checklist
- [ ] Shadow code review on 2-3 PRs
- [ ] Attend "Design System Overview" workshop

---

## 🏆 **Success Criteria**

You'll know the design system is successful when:

✅ **Consistency:** All pages look cohesive  
✅ **Speed:** Developers build faster (copy templates)  
✅ **Quality:** Fewer visual bugs in QA  
✅ **Accessibility:** WCAG AA compliance by default  
✅ **Maintainability:** Global changes easy (update tokens)  
✅ **Adoption:** 95%+ utility class usage, <5 hardcoded colors  

---

**Next Steps:**
1. Choose your enforcement level (start with Level 2)
2. Set up PR checklist
3. Train the team
4. Gradually increase automation

**Questions?** Add them to the design system discussion channel.
