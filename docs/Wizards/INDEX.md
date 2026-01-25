# Wizard Styling System - Complete Documentation Index

## Quick Start

**New to wizard development?** Start here:  
📋 [NEW-WIZARD-CHECKLIST.md](./NEW-WIZARD-CHECKLIST.md) - Step-by-step wizard creation guide

**Need quick reference?**  
📋 [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md) - Copy-paste templates and classes

---

## Core Documentation

### 1. Executive Summary
📄 [WIZARD-STYLING-EXECUTIVE-SUMMARY.md](./WIZARD-STYLING-EXECUTIVE-SUMMARY.md)
- What was done
- Impact and benefits
- Consolidated overview
- For: Team leads, architects, decision makers

### 2. Implementation Details
📄 [WIZARD-STYLING-IMPLEMENTATION.md](./WIZARD-STYLING-IMPLEMENTATION.md)
- Complete technical implementation
- Files changed and created
- Global infrastructure overview
- Build verification
- For: Technical leads, code reviewers

### 3. Comprehensive Styling Guide
📄 [wizard-styling-guide.md](./wizard-styling-guide.md)
- Architecture overview
- Global styling files reference
- Step-by-step new wizard creation
- CSS reusable classes
- Customization examples
- Responsive design details
- Troubleshooting
- For: Frontend developers, architect references

### 4. Quick Reference
📄 [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md)
- Copy-paste HTML template
- Minimal SCSS template
- Theme registration code
- Action bar component code
- Global classes reference
- CSS variables reference
- For: Developers in a hurry

### 5. Component Patterns
📄 [wizard-component-patterns.md](./wizard-component-patterns.md)
- Action bar component pattern
- Step component pattern
- Section header pattern
- Form control patterns (text, select, radio, checkbox)
- Alert/banner patterns
- Loading state patterns
- Grid/list patterns
- Best practices
- Common pitfalls
- For: Component developers, code standards

### 6. New Wizard Checklist
📄 [NEW-WIZARD-CHECKLIST.md](./NEW-WIZARD-CHECKLIST.md)
- Phase-by-phase checklist (10 phases)
- Time estimates
- File structure template
- Key resources to reference
- Success criteria
- Common issues & solutions
- For: Developers building new wizards

---

## Global Implementation Files

### Global Styles (src/styles/)
```
_wizard-globals.scss    ← NEW global patterns
_wizard.scss           ← Theme system (existing)
wizard-layout.scss     ← Layout structure (existing)
_action-bar.scss       ← Action bar styling (existing)
_mixins.scss           ← Theme mixins (existing)
_variables.scss        ← Theme definitions (existing)
styles.scss            ← Imports all above
```

### Wizard Component Files
```
player-registration-wizard/
  ├── player-registration-wizard.component.scss (87% reduced)
team-registration-wizard/
  ├── team-registration-wizard.component.scss (71% reduced)
```

---

## How to Use This Documentation

### Scenario 1: Creating a New Wizard
1. Start: [NEW-WIZARD-CHECKLIST.md](./NEW-WIZARD-CHECKLIST.md)
2. Reference: [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md)
3. Components: [wizard-component-patterns.md](./wizard-component-patterns.md)
4. Details: [wizard-styling-guide.md](./wizard-styling-guide.md)

**Time**: ~3 hours for a 3-step wizard

### Scenario 2: Learning the System
1. Start: [WIZARD-STYLING-EXECUTIVE-SUMMARY.md](./WIZARD-STYLING-EXECUTIVE-SUMMARY.md)
2. Deep dive: [WIZARD-STYLING-IMPLEMENTATION.md](./WIZARD-STYLING-IMPLEMENTATION.md)
3. Complete guide: [wizard-styling-guide.md](./wizard-styling-guide.md)
4. Reference: [wizard-component-patterns.md](./wizard-component-patterns.md)

**Time**: ~1 hour for complete understanding

### Scenario 3: Customizing Existing Wizard
1. Reference: [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md)
2. Details: [wizard-styling-guide.md](./wizard-styling-guide.md)
3. Components: [wizard-component-patterns.md](./wizard-component-patterns.md)

**Time**: ~15 minutes for specific customization

### Scenario 4: Code Review
1. Reference: [WIZARD-STYLING-IMPLEMENTATION.md](./WIZARD-STYLING-IMPLEMENTATION.md)
2. Check: [NEW-WIZARD-CHECKLIST.md](./NEW-WIZARD-CHECKLIST.md) - "Code Review Checklist"
3. Patterns: [wizard-component-patterns.md](./wizard-component-patterns.md)

**Time**: Variable based on review scope

---

## Global Classes Reference

### Layout Classes
```scss
.wizard-fixed-header              // Fixed header (title + steps + action bar)
.wizard-scrollable-content        // Scrollable content area
.wizard-action-bar-container      // Action bar wrapper
```

### Card Classes
```scss
.card.wizard-theme-player         // Player wizard card
.card.wizard-theme-team           // Team wizard card
.card.wizard-theme-family         // Family wizard card
.card.wizard-card-player          // Alt class name
.card.wizard-card-team            // Alt class name
.card.wizard-card-YOUR-THEME      // Your custom theme
```

### Container Classes
```scss
.rw-wizard-container              // Player wizard container
.tw-wizard-container              // Team wizard container
.wizard-container                 // Generic container
```

### Action Bar
```scss
.action-bar                       // Apply to action bar component host
.action-bar-details               // Badge/label section
```

---

## CSS Variables Available

### Spacing (Bootstrap)
```scss
--space-2 (8px)
--space-3 (12px)
--space-4 (16px)
```

### Border Radius
```scss
--radius-lg (16px)
--radius-md (8px)
--radius-sm (4px)
```

### Colors (Theme-Aware)
```scss
--bs-card-bg              // Card background
--bs-surface              // Inner content background
--bs-border-color         // Border color
--bs-body-color           // Text color
--bs-body-bg              // Page background
--bs-primary              // Primary color (theme-specific)
--bs-primary-rgb          // Primary color RGB (theme-specific)
--bs-secondary            // Secondary color
--bs-info, --bs-warning, --bs-danger, --bs-success
```

---

## Mixins Available (For Custom Code)

From `src/styles/_wizard-globals.scss`:
```scss
@mixin wizard-card-theme         // Card styling
@mixin wizard-container          // Container layout
@mixin wizard-title-base         // Title styling
```

From `src/styles/_mixins.scss`:
```scss
@mixin wizard-button-theme($scope)    // Button theming
```

---

## Common File Locations

### Component SCSS Template
📄 Player: `src/app/views/registration/wizards/player-registration-wizard/player-registration-wizard.component.scss` (23 lines)  
📄 Team: `src/app/views/registration/wizards/team-registration-wizard/team-registration-wizard.component.scss` (18 lines)

### Global Styles
📄 `src/styles/_wizard-globals.scss` (265 lines)  
📄 `src/styles/_wizard.scss` (262 lines)  
📄 `src/styles/wizard-layout.scss` (145 lines)  
📄 `src/styles/_action-bar.scss` (214 lines)

### Theme Configuration
📄 `src/styles/_variables.scss` - Contains `$wizard-themes` map

---

## Quick Answers

**Q: Where do I add custom wizard styling?**  
A: Only in your component's `.scss` file, and keep it minimal. Most styles are global.

**Q: How do I theme my wizard?**  
A: Add an entry to `$wizard-themes` in `src/styles/_variables.scss`

**Q: What's the HTML structure?**  
A: Copy from [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md)

**Q: How do I create form controls?**  
A: See form patterns in [wizard-component-patterns.md](./wizard-component-patterns.md)

**Q: What about dark mode?**  
A: Automatic - just use CSS variables, no custom dark mode CSS needed

**Q: Is my action bar styled correctly?**  
A: Yes, if you have `host: { class: 'action-bar' }` in your component

**Q: How do I make it responsive?**  
A: Automatic - global styles handle all breakpoints

**Q: Can I customize the card styling?**  
A: Yes, see examples in [wizard-styling-guide.md](./wizard-styling-guide.md)

**Q: What's a step component template?**  
A: See full pattern in [wizard-component-patterns.md](./wizard-component-patterns.md)

---

## Standards & Best Practices

### CWCC (AI-AGENT-CODING-CONVENTIONS)
✅ Applied throughout  
✅ DRY principle (no code duplication)  
✅ Global reusable patterns  
✅ CSS variables for theming  
✅ Responsive design  
✅ Dark mode support  

### Code Quality
✅ No hardcoded colors  
✅ No duplicate SCSS  
✅ Minimal component CSS  
✅ Modern Angular patterns (signals, inject, etc.)  
✅ Accessibility standards met  

---

## Troubleshooting Index

| Problem | Reference | Solution |
|---------|-----------|----------|
| Styling looks different | [wizard-styling-guide.md](./wizard-styling-guide.md#troubleshooting) | Check theme registration |
| Action bar not sticky | [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md) | Add `host: { class: 'action-bar' }` |
| Dark mode colors wrong | [wizard-styling-guide.md](./wizard-styling-guide.md) | Use CSS variables only |
| Mobile layout broken | [wizard-styling-guide.md](./wizard-styling-guide.md#responsive-design) | Use Bootstrap grid classes |
| Form validation not showing | [wizard-component-patterns.md](./wizard-component-patterns.md#step-component-pattern) | Mark fields as touched |
| Cards don't match | [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md) | Use correct theme class |

---

## Document Statistics

| Document | Lines | Focus | Audience |
|----------|-------|-------|----------|
| EXECUTIVE-SUMMARY | 200 | Overview | All |
| IMPLEMENTATION | 250 | Technical | Leads, reviewers |
| styling-guide | 450+ | Complete reference | Frontend devs |
| quick-reference | 200 | Templates | Developers |
| component-patterns | 500+ | Components | Component devs |
| NEW-WIZARD-CHECKLIST | 300+ | Process | Wizard builders |

**Total Documentation**: 2000+ lines of comprehensive guides

---

## What's Included Globally

### From _wizard-globals.scss
✅ Card styling (border, shadow, responsive padding)  
✅ Card-body styling  
✅ Alert styling (all 5 types)  
✅ Container layout  
✅ Title styling  
✅ Responsive adjustments (mobile/tablet/desktop)  
✅ Widget dark mode overrides (VerticalInsure)  

### From _wizard.scss
✅ Theme system (8+ themes)  
✅ Theme color scopes  
✅ Button theming per theme  
✅ Progress bar styling  
✅ Step indicators  

### From wizard-layout.scss
✅ Fixed header layout  
✅ Scrollable content area  
✅ Z-stacking/positioning  
✅ Dark mode support  
✅ Accessibility features (contrast, reduced motion)  

### From _action-bar.scss
✅ Glassmorphic design  
✅ Backdrop blur effect  
✅ Badge styling  
✅ Responsive button sizing  
✅ Shadow coordination  

---

## Getting Started (3 Steps)

1. **Read**: [WIZARD-STYLING-EXECUTIVE-SUMMARY.md](./WIZARD-STYLING-EXECUTIVE-SUMMARY.md) (5 minutes)
2. **Reference**: [NEW-WIZARD-CHECKLIST.md](./NEW-WIZARD-CHECKLIST.md) (while building)
3. **Build**: Your new wizard (2-3 hours)

---

## Support & Questions

- 📚 Check the docs first - they're comprehensive
- 📋 Use the checklists - they guide the process
- 🧩 See component patterns - copy the structure
- 📖 Reference the guides - they have examples

**The system is designed to be self-service.** Everything you need is in these documents.

---

## Version Info

**Implementation Date**: January 25, 2026  
**Status**: Production Ready ✅  
**Build**: Verified (no errors)  
**Documentation**: Complete  
**Standards**: CWCC Applied  

---

## Quick Links to Key Documents

| Need | Link |
|------|------|
| Start building | [NEW-WIZARD-CHECKLIST.md](./NEW-WIZARD-CHECKLIST.md) |
| Quick copy-paste | [wizard-styling-quick-reference.md](./wizard-styling-quick-reference.md) |
| Code templates | [wizard-component-patterns.md](./wizard-component-patterns.md) |
| Full reference | [wizard-styling-guide.md](./wizard-styling-guide.md) |
| Why it matters | [WIZARD-STYLING-EXECUTIVE-SUMMARY.md](./WIZARD-STYLING-EXECUTIVE-SUMMARY.md) |
| Technical details | [WIZARD-STYLING-IMPLEMENTATION.md](./WIZARD-STYLING-IMPLEMENTATION.md) |

---

**Everything you need to build consistent, beautiful wizards is here. Let's create something great! 🚀**
