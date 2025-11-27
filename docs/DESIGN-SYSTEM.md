# TSIC Angular Design System

**Purpose:** This is the official design system for all TSIC Angular components. Use these guidelines by default—not on request—when creating new pages or components.

---

## 📐 **Core Principles**

1. **Consistency First**: Use design tokens, never magic numbers
2. **Accessibility Always**: WCAG AA minimum (4.5:1 text contrast)
3. **Intentional Design**: Backgrounds and colors chosen purposefully, not automatically
4. **Mobile-First**: Responsive by default
5. **Performance**: Keep bundle size minimal

---

## 🎨 **Color System**

### **Palette Philosophy**
- **Accent colors** (primary, success, warning, etc.) are controlled by palette selection
- **Backgrounds** are intentional and independent of palette
- Never use hex codes directly—always use CSS variables

### **Usage Guidelines**

#### **When to Use Each Background**

| Class | Use Case | Example |
|-------|----------|---------|
| `.bg-surface` | Default card/modal background | Cards, modals, dialogs |
| `.bg-surface-alt` | Secondary panels | Sidebars, alternate sections |
| `.bg-elevated` | Overlays, dropdowns | Popovers, tooltips |
| `.bg-neutral-0` | Pure white surfaces | Data tables, form inputs |
| `.bg-neutral-50` | Page background | Body, main container |
| `.bg-neutral-100` | Disabled states | Inactive buttons |
| `.bg-neutral-200` | Dividers, separators | Section breaks |
| `.bg-primary-subtle` | Subtle emphasis | Selected items, hover states |
| `.bg-success-subtle` | Positive feedback | Success messages (10% wash) |
| `.bg-warning-subtle` | Caution zones | Pending items |
| `.bg-danger-subtle` | Error context | Validation errors |
| `.bg-info-subtle` | Informational | Tips, hints |

#### **Text Color Guidelines**
```scss
// Primary text (headings, body)
color: var(--brand-text);  // or var(--neutral-900)

// Secondary text (labels, captions)
color: var(--brand-text-muted);  // or var(--neutral-500)

// On colored backgrounds
color: var(--bs-primary);  // matches the accent color
```

---

## 📏 **Spacing System**

**Use the 8px grid system.** Never use arbitrary pixel values.

```scss
// ✅ CORRECT
padding: var(--space-4);  // 16px
margin-bottom: var(--space-6);  // 24px
gap: var(--space-3);  // 12px

// ❌ WRONG
padding: 17px;  // arbitrary number
margin-bottom: 23px;  // not on the scale
```

### **Spacing Scale**
| Token | Value | Usage |
|-------|-------|-------|
| `--space-1` | 4px | Tight spacing, icons |
| `--space-2` | 8px | Button padding, small gaps |
| `--space-3` | 12px | Form field spacing |
| `--space-4` | 16px | **Base unit** - card padding |
| `--space-6` | 24px | Section spacing |
| `--space-8` | 32px | Component separation |
| `--space-12` | 48px | Page section gaps |
| `--space-16` | 64px | Hero sections |

---

## 🔤 **Typography**

### **Type Scale**
```scss
// Headings
.h1, h1 { font-size: var(--font-size-4xl); font-weight: var(--font-weight-bold); }
.h2, h2 { font-size: var(--font-size-3xl); font-weight: var(--font-weight-semibold); }
.h3, h3 { font-size: var(--font-size-2xl); font-weight: var(--font-weight-semibold); }
.h4, h4 { font-size: var(--font-size-xl); font-weight: var(--font-weight-medium); }
.h5, h5 { font-size: var(--font-size-lg); font-weight: var(--font-weight-medium); }
.h6, h6 { font-size: var(--font-size-base); font-weight: var(--font-weight-semibold); }

// Body text
.text-base { font-size: var(--font-size-base); }  // 16px - default
.text-sm { font-size: var(--font-size-sm); }      // 14px - captions
.text-xs { font-size: var(--font-size-xs); }      // 12px - labels
```

### **Line Height**
```scss
// Headings: tight
line-height: var(--line-height-tight);  // 1.25

// Body text: normal
line-height: var(--line-height-normal);  // 1.5

// Long-form content: relaxed
line-height: var(--line-height-relaxed);  // 1.75
```

---

## 🔘 **Border Radius**

Use consistent rounding for a cohesive feel.

```scss
// Buttons, inputs, small elements
border-radius: var(--radius-sm);  // 6px

// Cards, panels (DEFAULT)
border-radius: var(--radius-md);  // 8px

// Modals, large containers
border-radius: var(--radius-lg);  // 12px

// Pills, badges
border-radius: var(--radius-full);  // 9999px
```

---

## 🌑 **Shadows (Elevation)**

Use shadows sparingly for depth.

```scss
// Hover states, small dropdowns
box-shadow: var(--shadow-sm);

// Cards (default)
box-shadow: var(--shadow-md);

// Modals, dialogs
box-shadow: var(--shadow-lg);

// Popovers, tooltips
box-shadow: var(--shadow-xl);

// Focus states (always include!)
&:focus-visible {
    outline: none;
    box-shadow: var(--shadow-focus);
}
```

---

## 📦 **Component Patterns**

### **Cards**
```html
<div class="card shadow-sm">
    <div class="card-header bg-surface border-0 py-3">
        <h5 class="mb-0">Card Title</h5>
    </div>
    <div class="card-body">
        <p class="text-muted mb-0">Card content goes here.</p>
    </div>
</div>
```

### **Buttons**
```html
<!-- Primary action -->
<button class="btn btn-primary">Save Changes</button>

<!-- Secondary action -->
<button class="btn btn-outline-secondary">Cancel</button>

<!-- Danger action -->
<button class="btn btn-danger">Delete</button>

<!-- Subtle action (no border) -->
<button class="btn btn-link text-primary">Learn More</button>
```

### **Forms**
```html
<div class="mb-3">
    <label class="form-label fw-medium">Email Address</label>
    <input type="email" class="form-control" placeholder="you@example.com">
    <small class="form-text text-muted">We'll never share your email.</small>
</div>
```

### **Stat Cards (Dashboard)**
```html
<div class="card bg-primary-subtle">
    <div class="card-body">
        <h6 class="text-muted mb-2">Total Revenue</h6>
        <h3 class="mb-0 text-primary">$12,450</h3>
        <small class="text-success">↑ 12% from last month</small>
    </div>
</div>
```

---

## ✅ **Do's and Don'ts**

### **Do:**
- ✅ Use CSS variables from `_tokens.scss`
- ✅ Apply background utilities (`.bg-surface`, `.bg-primary-subtle`)
- ✅ Use spacing scale (`--space-*`)
- ✅ Include focus states for accessibility
- ✅ Test components in all palette presets
- ✅ Use semantic HTML (`<button>`, `<label>`, `<nav>`)

### **Don't:**
- ❌ Use magic numbers (`padding: 17px`)
- ❌ Use inline hex codes (`color: #0ea5e9`)
- ❌ Override Bootstrap without good reason
- ❌ Forget to test keyboard navigation
- ❌ Use `!important` unless absolutely necessary
- ❌ Create component-specific CSS for common patterns

---

## 🧪 **Testing Your Components**

Before committing:
1. **Palette Test**: Switch all 8 palettes—colors should adapt correctly
2. **Responsive Test**: Check mobile (375px), tablet (768px), desktop (1440px)
3. **Accessibility Test**: Tab through with keyboard, check screen reader labels
4. **Browser Test**: Chrome, Firefox, Safari, Edge

---

## 📚 **Resources**

- **Live Preview**: `src/app/job-home/brand-preview/brand-preview.component.ts`
- **Tokens**: `src/styles/_tokens.scss`
- **Utilities**: `src/styles/_utilities.scss`
- **Bootstrap Docs**: https://getbootstrap.com/docs/5.3/

---

## 🎯 **Quick Reference**

```scss
/* Spacing Example */
.my-component {
    padding: var(--space-4);  // 16px
    margin-bottom: var(--space-6);  // 24px
    gap: var(--space-3);  // 12px
}

/* Typography Example */
.my-heading {
    font-size: var(--font-size-2xl);  // 24px
    font-weight: var(--font-weight-semibold);  // 600
    line-height: var(--line-height-tight);  // 1.25
    color: var(--brand-text);
}

/* Background Example */
.my-card {
    background-color: var(--brand-surface);  // or use .bg-surface
    border-radius: var(--radius-md);  // 8px
    box-shadow: var(--shadow-md);
}

/* Focus State (REQUIRED for accessibility) */
.my-button:focus-visible {
    outline: none;
    box-shadow: var(--shadow-focus);
}
```

---

**Questions?** Review the Brand Preview component or ask the team lead.
