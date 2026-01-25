# Wizard Documentation

All wizard-related documentation is organized here. Use this index to find what you need.

---

## 🚀 Getting Started

**Starting a new wizard?** → [NEW-WIZARD-CHECKLIST.md](./NEW-WIZARD-CHECKLIST.md)  
**Quick reference?** → [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md)  
**Need patterns?** → [wizard-component-patterns.md](./wizard-component-patterns.md)

---

## 📚 Complete Documentation Index

### For Developers Building Wizards

| Document | Purpose | Time |
|----------|---------|------|
| [NEW-WIZARD-CHECKLIST.md](./NEW-WIZARD-CHECKLIST.md) | Step-by-step 10-phase process | 175 min (3 hrs) |
| [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md) | Copy-paste templates & quick reference | 5 min |
| [wizard-component-patterns.md](./wizard-component-patterns.md) | Reusable component templates | 20 min |
| [wizard-styling-guide.md](./wizard-styling-guide.md) | Comprehensive styling reference | 30 min |

### For Team Leads & Architects

| Document | Purpose |
|----------|---------|
| [WIZARD-STYLING-EXECUTIVE-SUMMARY.md](./WIZARD-STYLING-EXECUTIVE-SUMMARY.md) | High-level overview of the system |
| [WIZARD-STYLING-IMPLEMENTATION.md](./WIZARD-STYLING-IMPLEMENTATION.md) | Technical implementation details |

### Reference & Overviews

| Document | Purpose |
|----------|---------|
| [wizard-theme-system.md](./wizard-theme-system.md) | Theme color system and variables |
| [player-registration-wizard-flow.md](./player-registration-wizard-flow.md) | Player wizard workflow documentation |
| [family-wizard-overview.md](./family-wizard-overview.md) | Family account wizard overview |

---

## 🎯 Quick Navigation

### I want to...

- **Build a new wizard** → Start: [NEW-WIZARD-CHECKLIST.md](./NEW-WIZARD-CHECKLIST.md)
- **Copy HTML/SCSS templates** → See: [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md)
- **Understand component patterns** → Read: [wizard-component-patterns.md](./wizard-component-patterns.md)
- **Learn about styling** → See: [wizard-styling-guide.md](./wizard-styling-guide.md)
- **Understand the system** → See: [WIZARD-STYLING-EXECUTIVE-SUMMARY.md](./WIZARD-STYLING-EXECUTIVE-SUMMARY.md)
- **See implementation details** → See: [WIZARD-STYLING-IMPLEMENTATION.md](./WIZARD-STYLING-IMPLEMENTATION.md)

---

## 📋 What's Available Globally

All wizards automatically get:

### CSS Classes
```scss
.wizard-fixed-header              // Fixed top section
.wizard-scrollable-content        // Scrollable content area
.wizard-action-bar-container      // Action bar wrapper
.card.wizard-theme-YOUR-THEME     // Themed card
.action-bar                       // Action bar styling
.YOUR-wizard-container            // Container layout
```

### SCSS Mixins
```scss
@mixin wizard-card-theme { }      // Card styling
@mixin wizard-container { }       // Container layout
@mixin wizard-title-base { }      // Title styling
```

### CSS Variables (Theme-Aware)
```scss
--radius-lg, --radius-md          // Border radius
--space-2, --space-3, --space-4   // Spacing
--bs-card-bg, --bs-surface        // Colors
--bs-border-color, --bs-body-color
--bs-primary (theme-specific)
```

---

## ✅ System Status

✅ Player Registration Wizard - Complete  
✅ Team Registration Wizard - Complete  
✅ Global Styling System - Complete  
✅ Documentation - Complete  
✅ Build - Passing  

---

## 📞 Questions?

Each document has troubleshooting sections. Start with the relevant guide above.
