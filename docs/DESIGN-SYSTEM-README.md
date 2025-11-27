# TSIC Design System Documentation

Welcome to the TSIC Angular Design System. This directory contains all the documentation you need to build consistent, accessible, and maintainable UI components.

---

## 📚 **Documentation Files**

### **1. [DESIGN-SYSTEM.md](./DESIGN-SYSTEM.md)** ⭐ **Start Here**
**Comprehensive design system guide** covering:
- Core principles (consistency, accessibility, intentional design)
- Color system (palettes, backgrounds, text colors)
- Spacing system (8px grid)
- Typography scale (font sizes, weights, line heights)
- Border radius and shadows
- Component patterns (cards, buttons, forms, tables, modals, alerts)
- Do's and Don'ts
- Testing checklist

**When to use:** Your primary reference for design decisions and best practices.

---

### **2. [DESIGN-SYSTEM-QUICK-REFERENCE.md](./DESIGN-SYSTEM-QUICK-REFERENCE.md)** ⚡ **Keep This Open**
**One-page cheat sheet** with:
- Color classes at a glance
- Spacing utilities (8px grid)
- Typography utilities
- Border radius and shadow classes
- Common HTML patterns (copy/paste ready)
- Pre-commit checklist

**When to use:** Pin this while coding for instant lookup of class names and utilities.

---

### **3. [COMPONENT-TEMPLATE.md](./COMPONENT-TEMPLATE.md)** 🛠️ **Copy & Paste**
**Ready-to-use component templates:**
- Standard card component
- Dashboard stat card
- Form component (with validation)
- Table component
- Modal component
- Alert/notification component
- List group component
- Empty state component
- Pre-commit checklist

**When to use:** Starting a new component. Copy the closest template and modify.

---

## 🎨 **Live Preview**

See the design system in action:
```
src/app/job-home/brand-preview/brand-preview.component.ts
```

This component showcases:
- 8 color palette presets (switch between them live)
- Background utility showcase (all 12+ background classes)
- Component examples (buttons, cards, forms, alerts, typography)
- Tabbed interface demonstrating layout patterns

**Run the app and navigate to the Brand Preview page to test palette changes.**

---

## 🏗️ **Design Token Architecture**

### File Structure
```
src/styles/
├── _tokens.scss         ← All CSS variables (colors, spacing, typography)
├── _utilities.scss      ← Utility classes (backgrounds, spacing, shadows)
├── styles.scss          ← Global styles and Bootstrap overrides
└── variables.scss       ← Bootstrap variable overrides (if needed)
```

### Load Order (important!)
1. **_tokens.scss** - CSS variables loaded first
2. **_utilities.scss** - Utility classes use tokens
3. **Bootstrap** - Framework styles
4. **styles.scss** - Project-specific overrides

---

## 🚀 **Quick Start**

### For New Components:
1. Open [COMPONENT-TEMPLATE.md](./COMPONENT-TEMPLATE.md)
2. Find the closest template (card, form, table, etc.)
3. Copy the HTML/TypeScript
4. Customize with your content
5. Test with all 8 palettes (use Brand Preview)
6. Check pre-commit checklist

### For Design Decisions:
1. Check [DESIGN-SYSTEM-QUICK-REFERENCE.md](./DESIGN-SYSTEM-QUICK-REFERENCE.md) for class names
2. If unclear, read [DESIGN-SYSTEM.md](./DESIGN-SYSTEM.md) for context
3. Still unclear? Check the Brand Preview component source code
4. Ask the team lead if needed

---

## 📐 **Key Principles**

### **1. Intentional Design**
- Backgrounds are **fixed** and chosen purposefully (`.bg-surface`, `.bg-neutral-50`)
- Accent colors (primary, success, etc.) are **palette-controlled**
- This separation ensures visual stability while allowing theme customization

### **2. Design Tokens Over Magic Numbers**
```scss
/* ✅ CORRECT */
padding: var(--space-4);       /* 16px from design system */
color: var(--brand-text);      /* Semantic token */
border-radius: var(--radius-md); /* 8px from design system */

/* ❌ WRONG */
padding: 17px;                 /* Arbitrary number */
color: #292524;                /* Hardcoded hex */
border-radius: 7px;            /* Off-grid value */
```

### **3. Utility Classes Over Component Styles**
```html
<!-- ✅ CORRECT - Uses utility classes -->
<div class="card shadow-sm mb-4">
  <div class="card-body p-4">
    <h5 class="mb-2 font-semibold">Title</h5>
    <p class="text-muted mb-0">Description</p>
  </div>
</div>

<!-- ❌ WRONG - Requires custom CSS -->
<div class="my-custom-card">
  <div class="my-custom-body">
    <h5 class="my-custom-title">Title</h5>
    <p class="my-custom-text">Description</p>
  </div>
</div>
```

### **4. Accessibility First**
- All interactive elements must have focus states (`:focus-visible`)
- Use semantic HTML (`<button>`, `<nav>`, `<label>`)
- Maintain WCAG AA contrast (4.5:1 minimum)
- Test keyboard navigation (tab through)
- Include `aria-label` for screen readers

---

## 🎨 **Color Palette System**

### Available Palettes (8 presets):
1. **Friendly Sky** - Bright, welcoming blues (default)
2. **Deep Ocean** - Deep teal, professional
3. **Sunset Warmth** - Warm oranges and corals
4. **Forest Green** - Earthy, natural greens
5. **Royal Purple** - Sophisticated purples
6. **Cherry Blossom** - Soft pinks
7. **Midnight Teal** - Dark, modern teal
8. **Crimson Bold** - Bold reds

### What Changes with Palette:
- ✅ Accent colors (primary, success, warning, danger, info)
- ✅ Text colors (body text adapts)
- ✅ Button colors (automatically themed)
- ✅ Badge and alert colors
- ✅ Subtle background washes (`.bg-primary-subtle`)

### What Stays Fixed:
- ❌ Page background (always `--neutral-50`)
- ❌ Card backgrounds (always `--neutral-0` white)
- ❌ Neutral grays (`.bg-neutral-*`)
- ❌ Semantic surfaces (`.bg-surface`, `.bg-surface-alt`)

---

## 🧪 **Testing Your Components**

### Pre-Commit Checklist:
1. **Palette Test**: Switch all 8 palettes - colors adapt correctly
2. **Responsive Test**: Mobile (375px), Tablet (768px), Desktop (1440px)
3. **Accessibility Test**: 
   - Tab through with keyboard
   - Focus states visible
   - Screen reader labels present
4. **Browser Test**: Chrome, Firefox, Safari, Edge
5. **Code Review**:
   - No magic numbers
   - No hardcoded colors
   - Using design tokens
   - Following component patterns

---

## 📖 **Additional Resources**

- **Bootstrap 5 Docs**: https://getbootstrap.com/docs/5.3/
- **WCAG 2.1 Guidelines**: https://www.w3.org/WAI/WCAG21/quickref/
- **Accessible Color Palette**: https://coolors.co/contrast-checker
- **Angular Docs**: https://angular.dev/

---

## 🆘 **Troubleshooting**

### "My colors aren't changing with the palette"
- Check if you're using hardcoded hex codes (use CSS variables instead)
- Ensure you're using Bootstrap's color classes (`.text-primary`, `.btn-success`)
- Backgrounds are intentionally fixed - only accent colors change

### "Spacing looks off on mobile"
- Use responsive spacing utilities (`.mb-4`, `.p-3`)
- Test at 375px width minimum
- Use Bootstrap's responsive grid (`.col-md-6`)

### "I need a custom color not in the palette"
- Don't add it! Use the existing palette colors
- If truly needed, propose it to the team and add to `_tokens.scss`

### "Where do I put custom component styles?"
- Only add to component `.scss` if truly unique
- Prefer utility classes first
- Check if the pattern exists in COMPONENT-TEMPLATE.md

---

## 🎯 **Next Steps**

1. **Read** [DESIGN-SYSTEM.md](./DESIGN-SYSTEM.md) (10 minutes)
2. **Pin** [DESIGN-SYSTEM-QUICK-REFERENCE.md](./DESIGN-SYSTEM-QUICK-REFERENCE.md) (keep open)
3. **Explore** Brand Preview component (see it live)
4. **Build** your first component using [COMPONENT-TEMPLATE.md](./COMPONENT-TEMPLATE.md)
5. **Test** with all 8 palettes
6. **Ship** with confidence! 🚀

---

**Questions?** Ask the team lead or review the Brand Preview component source code.
