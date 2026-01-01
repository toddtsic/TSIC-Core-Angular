# AI Agent Coding Conventions (CWCC)

**Quick Reference Code: CWCC** - "Comply With Coding Conventions"

Use this shorthand before any coding request to ensure the AI agent follows all established conventions.

---

## 📋 Pre-Coding Checklist

**BEFORE making any code changes, ALWAYS:**

1. ✅ Check `docs/` folder for existing patterns and standards
2. ✅ Review `styles.scss` for brand variables and theming conventions
3. ✅ Follow established architectural patterns in codebase
4. ✅ Use semantic search to understand existing implementations
5. ✅ Read relevant files to understand context before editing

---

## 🎨 Brand & Styling Standards

### CSS Variables & Theming
- **ALWAYS use brand CSS variables**: `--brand-primary-rgb`, `--brand-text`, `--neutral-0-rgb`
- **Support light/dark themes**: Check `styles.scss` for proper variable usage
- **Brand consistency**: Match existing glassmorphic design language
- **No hardcoded colors**: Use CSS custom properties for all colors

### Design System
- **Glassmorphic components**: Use backdrop-blur, subtle gradients, inset highlights
- **Consistent spacing**: Follow `--radius-*` variables for border-radius
- **Animation timing**: Use `cubic-bezier(0.4, 0, 0.2, 1)` for consistent easing
- **Mobile-first**: No hover effects on mobile-only components

---

## 🏗️ Architecture Standards

### Angular Best Practices
- **Standalone components**: Use standalone: true for all new components
- **Signal-based state**: Prefer signals over traditional reactive forms
- **Modern syntax**: Use `@if`, `@for` instead of `*ngIf`, `*ngFor`
- **Dependency injection**: Use `inject()` function in constructors

### File Structure
- **Clean Architecture**: Follow Domain-Application-Infrastructure layers
- **Service Layer**: Use proper service interfaces and implementations  
- **Repository Pattern**: Follow established repository standards
- **API Models**: Auto-generated, don't edit manually

---

## 📱 Mobile & Responsive Design

### Mobile Considerations
- **No hover on mobile**: Use `:active` and touch feedback instead
- **Touch-friendly sizing**: Minimum 44px touch targets
- **Consistent heights**: All buttons in same row should align
- **Proper breakpoints**: Use Bootstrap responsive utilities correctly

### Button Groups
- **Height consistency**: All buttons in header must be same height
- **Icon sizing**: Use consistent icon sizes (typically 1rem for small buttons)
- **Proper semantics**: Use `<a role="button">` for navigation, `<button>` for actions

---

## 🔧 Technical Standards

### Error Handling
- **Graceful degradation**: Return empty arrays/objects instead of 404s when appropriate
- **User-friendly messages**: No technical error messages in UI
- **Proper HTTP codes**: Use correct status codes for different scenarios

### API Conventions
- **Token constants**: No hardcoded strings, use constants for repeated values
- **Null safety**: Use null assertion operators (`!`) appropriately with null checks
- **LINQ optimization**: Use `Where()` clauses to filter before processing
- **Documentation**: Update API comments when changing behavior

### Angular/TypeScript API Models
- **NEVER edit auto-generated files**: Files in `src/app/core/api/models/` are regenerated from Swagger
- **Always regenerate after DTO changes**: Run `2-Regenerate-API-Models.ps1` after backend changes
- **NEVER create local DTO duplicates**: Import from `@core/api`, never define locally
- **Check for stale folders**: Verify no duplicate model folders exist (check tsconfig paths)
- **Audit after major changes**: Search for duplicate class/type definitions across codebase

### Clean Architecture Enforcement
- **Repository pattern is mandatory**: Controllers/Services MUST NEVER use `SqlDbContext` directly
- **Use Func<> delegates for cross-layer behavior**: When Infrastructure needs API layer behavior, inject as `Func<>` delegate from composition root (Program.cs)
- **Factory pattern for complex DI**: Use `services.AddScoped<IRepo>(sp => new Repo(sp.Get<Dep>(), funcDelegate))`
- **Layer dependencies flow one way**: API → Infrastructure → Application → Contracts → Domain

---

## 🎯 Code Quality Standards

### C# Backend
- **SonarQube compliance**: Address analyzer warnings
- **Proper using statements**: Import required namespaces
- **Variable naming**: Use descriptive names, avoid conflicts
- **Method complexity**: Keep cognitive complexity under 15
- **DTO property pattern**: Use `required` keyword with `init` properties (NOT positional parameters) for OpenAPI generation
- **No positional records for DTOs**: `public record MyDto { public required string Prop { get; init; } }` ✅
- **Object initializers**: Use `new MyDto { Prop = value }` not positional syntax

### TypeScript Frontend  
- **Type safety**: Use proper TypeScript types
- **Component lifecycle**: Proper use of OnInit, OnDestroy
- **Memory management**: Unsubscribe from observables
- **Accessibility**: Proper ARIA labels and semantic HTML

---

## 📖 Documentation Requirements

### Code Comments
- **Intent over implementation**: Explain why, not what
- **API documentation**: Update XML docs when changing controllers
- **Breaking changes**: Document any behavioral changes
- **Complex logic**: Comment non-obvious business rules

### Git Standards
- **Descriptive commits**: Use conventional commit format
- **Feature branches**: Work on feature branches, not main
- **Atomic commits**: One logical change per commit
- **Meaningful messages**: Include context and impact

---

## 🔍 Research & Context

### Before Coding
1. **Semantic search** existing codebase for similar implementations
2. **Read related files** to understand integration points
3. **Check docs/** for architectural decisions and patterns
4. **Review styles.scss** for theming and brand variables
5. **Understand user flow** and business requirements
6. **Search for duplicate definitions** - use grep_search to find duplicate classes/types/services
7. **Verify tsconfig path aliases** - ensure they point to correct locations
8. **Check for stale folders** - deleted folders may still have imports pointing to them

### Implementation Strategy
- **Follow existing patterns** rather than creating new ones
- **Maintain consistency** with established conventions
- **Test thoroughly** before declaring complete
- **Consider edge cases** and error scenarios

---

## 🚨 Critical Don'ts

- ❌ **Never hardcode colors or styles** - use brand variables
- ❌ **Don't ignore mobile responsive design** - test all breakpoints  
- ❌ **Avoid hover effects on mobile-only components**
- ❌ **Don't create new patterns** without justification
- ❌ **Never skip reading context** - understand before changing
- ❌ **Don't use inline styles** - use component SCSS files
- ❌ **Avoid breaking existing functionality** - maintain backward compatibility
- ❌ **NEVER edit auto-generated API models** - regenerate with script instead
- ❌ **Don't create local DTO duplicates** - import from @core/api
- ❌ **Never bypass repository pattern** - no direct DbContext usage in controllers/services
- ❌ **Don't sweep errors under rug** - investigate and fix, don't hide problems
- ❌ **Avoid making assumptions** - verify with searches/reads before major changes

---

## 🎉 Quick Win Checklist

When implementing any feature:

- [ ] Uses brand CSS variables for colors/themes
- [ ] Follows existing component patterns
- [ ] Includes proper error handling
- [ ] Works on mobile and desktop
- [ ] Maintains visual consistency
- [ ] Includes accessibility features
- [ ] Follows TypeScript/C# best practices
- [ ] Updated relevant documentation
- [ ] Tested thoroughly
- [ ] Committed with descriptive message

---

**Usage**: Type **"CWCC"** before any coding request to ensure adherence to these conventions.

**Last Updated**: January 1, 2026

**Critical Lessons Learned**:
- Always verify no duplicate auto-generated model folders exist
- Stale folders from refactoring can cause TypeScript to use outdated types
- Deep dive audits prevent catastrophic errors (found 100+ stale files)
- When major changes occur, audit for duplicates and verify path aliases