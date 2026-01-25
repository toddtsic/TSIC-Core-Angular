# Documentation Organization Index

The `docs/` folder is organized into logical subfolders for easy navigation. Use this index to find what you need.

---

## 📚 Folder Organization

### 📂 [Architecture/](./Architecture/)
System design, patterns, and architectural decisions
- Clean architecture implementation & refactoring
- Layout and component architecture
- Domain-specific architecture (player registration, insurance, menu system)
- Security architecture

**Start here for**: Understanding system design, architectural patterns, domain organization

---

### 🎨 [DesignSystem/](./DesignSystem/)
Design system, UI styling, and visual guidelines
- Design system documentation and enforcement
- Design tokens and migration tracking
- Bootstrap icons configuration
- Design standards and quick references

**Start here for**: UI design, styling guidelines, design system usage

---

### 🚀 [Deployment/](./Deployment/)
Infrastructure, deployment methodology, and setup guides
- Deployment processes and checklists
- IIS and Angular deployment guides
- Environment setup and configuration
- iDrive secure deployment

**Start here for**: Deploying the application, infrastructure setup, environment configuration

---

### ✅ [Standards/](./Standards/)
Coding standards, conventions, and development patterns
- AI agent coding conventions (CWCC)
- Repository pattern standards
- Service layer standards
- Authentication & authorization patterns
- Development workflow guidelines

**Start here for**: Coding standards, best practices, development patterns

---

### 🎯 [Frontend/](./Frontend/)
Angular-specific development, components, and features
- Angular 21 patterns (signals, modern syntax)
- Component patterns and refactoring
- Profile editor implementation and migration
- Testing and form previews

**Start here for**: Frontend development, Angular patterns, component development

---

### 🔧 [Features/](./Features/)
Feature implementations and specific workflow documentation
- Feature changelogs and implementation summaries
- Registration workflows and UI
- Payment and team management
- Waivers and user preferences
- Internal linking strategies

**Start here for**: Feature-specific implementations, workflow documentation

---

### 🔒 [Security/](./Security/)
Security setup, authentication, and testing
- Password bypass and testing guides
- GitHub and environment setup authentication
- Rate limiting and API security
- Dev mode security overrides

**Start here for**: Security setup, authentication configuration, testing security features

---

### 📖 [Reference/](./Reference/)
Code examples, patterns, and analysis
- Validation examples (DataAnnotations & FluentValidation)
- Component pattern templates
- Repository pattern audits
- Code structure analysis
- Troubleshooting guides

**Start here for**: Code examples, reference implementations, debugging

---

### 🧙 [Wizards/](./Wizards/)
Wizard component system documentation
- Styling system (global patterns, consolidation, quick reference)
- Component patterns for wizard development
- New wizard creation checklists
- Theme system and workflow

**Start here for**: Building new wizards, understanding wizard styling system

---

### 📁 [component-patterns/](./component-patterns/)
Component pattern library and templates

---

## 📍 Root Level Documents

| File | Purpose |
|------|---------|
| **README.md** | Main repository documentation |
| **STRIP_CHECKLIST.md** | Checklist for code cleanup/stripping |
| **multi-club-rep-design.md** | Multi-club representative design document |
| **dataannotations-to-fluentvalidation-migration.md** | Data validation migration guide |

---

## 🚀 Quick Navigation by Task

### I want to...

- **Build a new wizard** → [Wizards/](./Wizards/) → [NEW-WIZARD-CHECKLIST.md](./Wizards/NEW-WIZARD-CHECKLIST.md)
- **Understand the codebase** → [Architecture/](./Architecture/)
- **Follow coding standards** → [Standards/](./Standards/)
- **Deploy the application** → [Deployment/](./Deployment/)
- **Work on frontend** → [Frontend/](./Frontend/)
- **Implement a feature** → [Features/](./Features/)
- **Find code examples** → [Reference/](./Reference/)
- **Set up security** → [Security/](./Security/)
- **Design UI elements** → [DesignSystem/](./DesignSystem/)

---

## 📊 Documentation Statistics

- **Total folders**: 10
- **Total markdown files**: 62
- **Architecture**: 8 docs
- **Design System**: 8 docs
- **Deployment**: 8 docs
- **Standards**: 8 docs
- **Frontend**: 9 docs
- **Features**: 8 docs
- **Security**: 5 docs
- **Reference**: 6 docs
- **Wizards**: 11 docs
- **Root level**: 4 docs

---

## 💡 Tips for Maintaining Organization

1. **When adding new docs**, place them in the most relevant folder
2. **Create subfolders** within main folders if they grow beyond 10-12 docs
3. **Keep folder-specific README.md** files (like in Wizards/) for navigation within folders
4. **Update this index** if you create new top-level folders
5. **Use consistent naming**: 
   - `UPPERCASE.md` for high-level overview/meta docs
   - `lowercase-hyphenated.md` for specific guides/features

---

## 🔍 Finding Documentation

1. **Search by topic** using this index
2. **Check folder README.md** files for detailed navigation
3. **Use IDE search** (Ctrl+Shift+F) to find specific keywords
4. **Browse by folder** to discover related documentation

---

## Feedback

Documentation grows with the project. If you find docs that should be reorganized, create a folder and move them accordingly.
