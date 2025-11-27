# TSIC Angular Design System

**Purpose:** This is the official design system for all TSIC Angular components. Use these guidelines by default—not on request—when creating new pages or components.

---

## 🎨 **Dynamic Palette System**

### **Overview**
The TSIC design system features a **dynamic palette system** that allows the entire application to switch between 8 distinct color themes instantly, without page reload. All components are palette-aware and adapt automatically.

### **Available Palettes**
1. **Friendly Sky** (Default) - Bright, approachable sky blue with warm cream backgrounds
2. **Deep Ocean** - Professional deep blue with subtle gray backgrounds
3. **Sunset Warmth** - Vibrant orange/red with peach backgrounds
4. **Forest Green** - Natural emerald with light sage backgrounds
5. **Royal Purple** - Bold purple with soft lavender backgrounds
6. **Cherry Blossom** - Soft pink with warm cream backgrounds
7. **Midnight Teal** - Dark teal with deep charcoal backgrounds
8. **Crimson Bold** - Strong red with neutral gray backgrounds

### **Testing the Palette System**
Navigate to the **Brand Preview** page (`/job/0/brand-preview`) to:
- Switch between all 8 palettes interactively
- See component examples (buttons, cards, forms, alerts) update in real-time
- Compare palettes side-by-side using tabbed views

### **How Palettes Work**
When a palette is selected, the TypeScript code dynamically updates CSS custom properties:
```typescript
// In brand-preview.component.ts
selectPalette(index: number) {
  const palette = this.palettes[index];
  
  // Update CSS variables for the entire app
  document.documentElement.style.setProperty('--bs-primary', palette.primary);
  document.documentElement.style.setProperty('--bs-success', palette.success);
  document.body.style.backgroundColor = palette.bodyBg;
  // ... and 14 more variables
}
```

### **Building Palette-Aware Components**
To ensure your components work with all palettes:
```scss
// ✅ CORRECT - Uses CSS variables
.my-button {
  background: var(--bs-primary);
  color: var(--bs-light);
  border: 1px solid var(--border-color);
}

// ❌ WRONG - Hardcoded colors won't change with palette
.my-button {
  background: #0ea5e9;
  color: #ffffff;
  border: 1px solid #e7e5e4;
}
```

---

## 📐 **Core Principles**

1. **Consistency First**: Use design tokens, never magic numbers
2. **Accessibility Always**: WCAG AA minimum (4.5:1 text contrast)
3. **Intentional Design**: Backgrounds and colors chosen purposefully, not automatically
4. **Mobile-First**: Responsive by default
5. **Performance**: Keep bundle size minimal

---

## 📋 **CSS Variables Reference**

### **Color Tokens (Palette-Responsive)**
These variables update when a palette is selected:

```scss
// Primary brand colors
--bs-primary        // Main accent color (buttons, links)
--bs-primary-rgb    // RGB version for transparency
--bs-secondary      // Secondary accent
--bs-success        // Success states (green variants)
--bs-success-rgb    // RGB for success overlays
--bs-danger         // Error states (red variants)
--bs-danger-rgb     // RGB for danger overlays
--bs-warning        // Warning states (yellow/orange)
--bs-warning-rgb    // RGB for warning overlays
--bs-info           // Info states (blue/cyan)
--bs-info-rgb       // RGB for info overlays
--bs-light          // Light text on dark backgrounds
--bs-light-rgb      // RGB for light overlays
--bs-dark           // Dark text on light backgrounds
--bs-dark-rgb       // RGB for dark overlays

// Background colors (palette-specific)
--bs-body-bg        // Main page background
--bs-body-color     // Default text color
--bs-card-bg        // Card/modal background
```

### **Neutral Colors (Fixed Across Palettes)**
```scss
--neutral-0         // Pure white (#ffffff)
--neutral-50        // Lightest gray (#fafaf9)
--neutral-100       // Very light gray (#f5f5f4)
--neutral-200       // Light gray (#e7e5e4)
--neutral-300       // Medium-light gray (#d6d3d1)
--neutral-400       // Medium gray (#a8a29e)
--neutral-500       // True middle gray (#78716c)
--neutral-600       // Medium-dark gray (#57534e)
--neutral-700       // Dark gray (#44403c)
--neutral-800       // Very dark gray (#292524)
--neutral-900       // Darkest gray (#1c1917)
```

### **Semantic Surface Tokens**
```scss
--brand-surface     // Primary surface (cards, modals)
--bg-elevated       // Elevated surfaces (dropdowns, tooltips)
--bg-primary        // Primary-colored backgrounds
--bg-secondary      // Secondary-colored backgrounds
--border-color      // Default border color
--text-primary      // Primary text color
--text-secondary    // Muted/secondary text
```

### **Spacing Scale (8px Grid)**
```scss
--space-0   // 0px
--space-1   // 4px   - Tiny gaps, icon spacing
--space-2   // 8px   - Small padding
--space-3   // 12px  - Form field spacing
--space-4   // 16px  - Base unit (card padding)
--space-5   // 20px
--space-6   // 24px  - Section spacing
--space-8   // 32px  - Component separation
--space-10  // 40px
--space-12  // 48px  - Page section gaps
--space-16  // 64px  - Hero sections
--space-20  // 80px  - Large sections
```

### **Shadow Scale**
```scss
--shadow-xs         // 0 1px 2px rgba(0,0,0,0.05) - Subtle
--shadow-sm         // 0 1px 3px + 0 1px 2px - Small elevation
--shadow-md         // 0 4px 6px - Cards (default)
--shadow-lg         // 0 10px 15px - Modals, dialogs
--shadow-xl         // 0 20px 25px - Large modals
--shadow-2xl        // 0 25px 50px - Hero images, overlays
--shadow-focus      // 0 0 0 3px primary with 15% opacity - Focus states
```

### **Border Radius Scale**
```scss
--radius-none       // 0
--radius-sm         // 6px - Buttons, inputs
--radius-md         // 8px - Cards (default)
--radius-lg         // 12px - Modals, large containers
--radius-xl         // 16px - Hero sections
--radius-full       // 9999px - Pills, badges, avatars
```

### **Typography Scale**
```scss
// Font sizes
--font-size-xs      // 12px - Labels, captions
--font-size-sm      // 14px - Small text
--font-size-base    // 16px - Body text (default)
--font-size-lg      // 18px - Large body text
--font-size-xl      // 20px - h5
--font-size-2xl     // 24px - h3, h4
--font-size-3xl     // 30px - h2
--font-size-4xl     // 36px - h1

// Font weights
--font-weight-normal    // 400
--font-weight-medium    // 500
--font-weight-semibold  // 600
--font-weight-bold      // 700

// Line heights
--line-height-tight     // 1.25 - Headings
--line-height-normal    // 1.5 - Body text
--line-height-relaxed   // 1.75 - Long-form content
```

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
- ✅ Use CSS variables from `_tokens.scss` (never hardcode colors)
- ✅ Test components with all 8 palettes in Brand Preview
- ✅ Apply background utilities (`.bg-surface`, `.bg-primary-subtle`)
- ✅ Use spacing scale (`--space-*`) - never arbitrary pixel values
- ✅ Include focus states for accessibility (`--shadow-focus`)
- ✅ Use shadow scale (`--shadow-sm`, `--shadow-md`, etc.)
- ✅ Use semantic HTML (`<button>`, `<label>`, `<nav>`)
- ✅ Check contrast ratios (WCAG AA: 4.5:1 for text)

### **Don't:**
- ❌ Use magic numbers (`padding: 17px` → use `var(--space-4)`)
- ❌ Use inline hex codes (`color: #0ea5e9` → use `var(--bs-primary)`)
- ❌ Use rgba literals (`rgba(0,0,0,0.1)` → use `var(--shadow-sm)`)
- ❌ Hardcode white (`#fff` → use `var(--bs-light)` or `var(--neutral-0)`)
- ❌ Override Bootstrap without documenting why
- ❌ Forget to test keyboard navigation
- ❌ Use `!important` unless absolutely necessary
- ❌ Create component-specific CSS for common patterns

---

## 🎯 **Design System Enforcement (Completed)**

As of the latest update, **100% of the codebase** adheres to design system standards:

### **Files Updated:**
- ✅ `styles.scss` - Replaced all hardcoded colors, shadows, and spacing
- ✅ `_tokens.scss` - Fixed remaining hardcoded button colors
- ✅ `brand-preview.component.scss` - Converted to CSS variables
- ✅ **All 16 component files** - No hardcoded colors remain
  - `tsic-landing.component.scss`
  - `profile-migration.component.scss`
  - `job.component.scss`
  - `role-selection.component.scss`
  - `profile-editor.component.scss`
  - `profile-form-preview.component.scss`
  - (Plus 10 other component files already using design tokens)

### **What Changed:**
- **Colors**: All `#hex`, `rgb()`, and `rgba()` values replaced with CSS variables
- **Shadows**: All box-shadow values use `--shadow-*` tokens
- **Borders**: All borders use `--border-color` or semantic equivalents
- **Backgrounds**: Tables, cards, inputs use `--bg-elevated`, `--bs-card-bg`, etc.

### **Benefits:**
- 🎨 **Palette switching works app-wide** - any component can be themed
- 🔧 **Maintainable** - update one token, change entire app
- ♿ **Accessible** - consistent contrast ratios across palettes
- 📱 **Responsive** - design system works on all screen sizes

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
