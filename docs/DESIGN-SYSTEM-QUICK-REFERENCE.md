# TSIC Design System - Quick Reference Card

**Pin this file** when working on Angular components.

---

## 🎨 **Color Classes**

### Backgrounds (Fixed, palette-independent)
```html
<div class="bg-surface">        <!-- White card background -->
<div class="bg-surface-alt">    <!-- Slightly tinted -->
<div class="bg-neutral-0">      <!-- Pure white -->
<div class="bg-neutral-50">     <!-- Page background (default) -->
<div class="bg-neutral-100">    <!-- Light gray -->
<div class="bg-neutral-200">    <!-- Dividers -->
```

### Backgrounds (Subtle washes - respond to palette)
```html
<div class="bg-primary-subtle">  <!-- 10% primary tint -->
<div class="bg-success-subtle">  <!-- 10% success tint -->
<div class="bg-warning-subtle">  <!-- 10% warning tint -->
<div class="bg-danger-subtle">   <!-- 10% danger tint -->
<div class="bg-info-subtle">     <!-- 10% info tint -->
```

### Text Colors (Respond to palette)
```html
<span class="text-primary">   <span class="text-success">
<span class="text-danger">    <span class="text-warning">
<span class="text-info">      <span class="text-muted">
```

---

## 📏 **Spacing (8px grid)**

```html
<!-- Margin -->
<div class="m-0">   <!-- 0px -->
<div class="m-1">   <!-- 4px -->
<div class="m-2">   <!-- 8px -->
<div class="m-3">   <!-- 12px -->
<div class="m-4">   <!-- 16px (BASE) -->
<div class="m-6">   <!-- 24px -->
<div class="m-8">   <!-- 32px -->

<!-- Directional: mt-4, mb-4, pt-4, pb-4, etc. -->

<!-- Padding -->
<div class="p-0 p-1 p-2 p-3 p-4 p-6 p-8">

<!-- Gap (flexbox/grid) -->
<div class="d-flex gap-2">
<div class="d-flex gap-4">
```

**SCSS Variables:**
```scss
padding: var(--space-4);  /* 16px */
margin: var(--space-6);   /* 24px */
gap: var(--space-3);      /* 12px */
```

---

## 🔤 **Typography**

### Font Sizes
```html
<span class="text-xs">    <!-- 12px -->
<span class="text-sm">    <!-- 14px -->
<span class="text-base">  <!-- 16px (default) -->
<span class="text-lg">    <!-- 18px -->
<span class="text-xl">    <!-- 20px -->
<span class="text-2xl">   <!-- 24px -->
<span class="text-3xl">   <!-- 30px -->
```

### Font Weights
```html
<span class="font-normal">    <!-- 400 -->
<span class="font-medium">    <!-- 500 -->
<span class="font-semibold">  <!-- 600 -->
<span class="font-bold">      <!-- 700 -->
```

### Line Heights
```html
<p class="leading-tight">    <!-- 1.25 - headings -->
<p class="leading-normal">   <!-- 1.5 - body -->
<p class="leading-relaxed">  <!-- 1.75 - long-form -->
```

---

## 🔘 **Border Radius**

```html
<div class="rounded-none">  <!-- 0 -->
<div class="rounded-sm">    <!-- 6px - buttons, inputs -->
<div class="rounded-md">    <!-- 8px - cards (default) -->
<div class="rounded-lg">    <!-- 12px - modals -->
<div class="rounded-xl">    <!-- 16px - hero sections -->
<div class="rounded-full">  <!-- 9999px - pills, badges -->
```

---

## 🌑 **Shadows**

```html
<div class="shadow-none">
<div class="shadow-xs">
<div class="shadow-sm">    <!-- Hover states -->
<div class="shadow-md">    <!-- Cards (default) -->
<div class="shadow-lg">    <!-- Modals -->
<div class="shadow-xl">    <!-- Popovers -->
```

**SCSS:**
```scss
box-shadow: var(--shadow-md);
```

---

## 🎯 **Common Patterns**

### Card (default)
```html
<div class="card shadow-sm">
  <div class="card-header bg-surface border-0 py-3">
    <h5 class="mb-0">Title</h5>
  </div>
  <div class="card-body">
    Content
  </div>
</div>
```

### Stat Card
```html
<div class="card bg-primary-subtle">
  <div class="card-body">
    <h6 class="text-muted mb-2">Revenue</h6>
    <h3 class="mb-0 text-primary">$12,450</h3>
    <small class="text-success">↑ 12%</small>
  </div>
</div>
```

### Buttons
```html
<button class="btn btn-primary">Primary</button>
<button class="btn btn-outline-secondary">Secondary</button>
<button class="btn btn-danger">Delete</button>
<button class="btn btn-link text-primary">Link</button>
```

### Form Field
```html
<div class="mb-4">
  <label class="form-label font-medium">Email</label>
  <input type="email" class="form-control" placeholder="you@example.com">
  <small class="form-text text-muted">Helper text</small>
</div>
```

### Alert
```html
<div class="alert bg-success-subtle border-0" role="alert">
  <strong class="text-success">Success!</strong>
  <p class="mb-0 text-muted">Message here.</p>
</div>
```

### Badge
```html
<span class="badge bg-primary-subtle text-primary rounded-full">New</span>
<span class="badge bg-success">Active</span>
<span class="badge bg-danger">Error</span>
```

---

## ✅ **Do's**

- ✅ Use CSS variables: `var(--space-4)`, `var(--neutral-50)`
- ✅ Use utility classes: `.bg-surface`, `.mb-4`, `.text-primary`
- ✅ Test all 8 palettes (use Brand Preview component)
- ✅ Include focus states for accessibility
- ✅ Use semantic HTML: `<button>`, `<label>`, `<nav>`

## ❌ **Don'ts**

- ❌ No magic numbers: `padding: 17px`
- ❌ No inline hex codes: `color: #0ea5e9`
- ❌ No inline styles (use classes)
- ❌ No `!important` unless absolutely necessary
- ❌ No component-specific CSS for common patterns

---

## 🧪 **Pre-Commit Checklist**

1. [ ] Palette test (switch all 8, colors adapt)
2. [ ] Responsive (375px, 768px, 1440px)
3. [ ] Keyboard navigation (tab through)
4. [ ] Focus states visible
5. [ ] No console errors

---

**Need more?** See [DESIGN-SYSTEM.md](./DESIGN-SYSTEM.md) or [COMPONENT-TEMPLATE.md](./COMPONENT-TEMPLATE.md)
