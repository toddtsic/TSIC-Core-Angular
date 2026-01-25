# TSIC Layout Architecture

## Overview

The TSIC Angular application uses two distinct layout components to provide different user experiences based on the authentication and job context:

1. **PublicLayoutComponent** - For non-job-specific routes (login, role selection, help)
2. **LayoutComponent** - For job-specific routes (team management, scheduling, reports)

---

## 1. PublicLayoutComponent

### Purpose
Provides a clean, minimal layout for pages that don't require job context:
- Login (`/tsic/login`)
- Role selection (`/tsic/role-selection`)
- Public landing page (`/tsic`)
- Help documents
- TeamSportsInfo.com marketing/info pages

### Structure
```
┌──────────────────────────────────────────────┐
│  TeamSportsInfo.com Header                   │
│  - Brand logo & name                         │
│  - Theme toggle (light/dark)                 │
└──────────────────────────────────────────────┘
┌──────────────────────────────────────────────┐
│                                              │
│         Content Area (centered)              │
│         <router-outlet />                    │
│                                              │
└──────────────────────────────────────────────┘
┌──────────────────────────────────────────────┐
│  Footer (copyright, etc.)                    │
└──────────────────────────────────────────────┘
```

### Features
- **TeamSportsInfo.com Branding**: Purple gradient header with logo and tagline
- **Theme Toggle**: Light/dark mode switcher integrated in header
- **Responsive Container**: Uses Bootstrap `.container` for consistent spacing
- **Clean Footer**: Simple copyright notice
- **No Authentication UI**: No user info, logout, or role menus

### Key Files
- Component: `src/app/layouts/public-layout/public-layout.component.ts`
- Template: `src/app/layouts/public-layout/public-layout.component.html`
- Styles: Global styles in `src/styles.scss` (`.public-*` classes)

### Route Configuration
```typescript
{
  path: 'tsic',
  component: PublicLayoutComponent,  // Wraps all /tsic routes
  children: [
    { path: '', component: TsicLandingComponent },
    { path: 'login', component: LoginComponent },
    { path: 'role-selection', component: RoleSelectionComponent }
  ]
}
```

---

## 2. LayoutComponent (Job-Specific)

### Purpose
Provides a feature-rich layout for authenticated users working within a specific job context:
- Job home page (`/:jobPath` or `/tsic/home`)
- Team management
- Scheduling
- Reports and analytics

### Structure
```
┌────────────────────────────────────────────────────────────────────┐
│  Job-Specific Header                                               │
│  ┌─────────────┬────────────────────────┬──────────────────────┐  │
│  │ Job Logo &  │ Role Navigation        │ User Info & Actions  │  │
│  │ Banner      │ (Parent/Director/etc.) │ - Username           │  │
│  │             │                        │ - Switch Role        │  │
│  │             │                        │ - Logout             │  │
│  │             │                        │ - Theme Toggle       │  │
│  └─────────────┴────────────────────────┴──────────────────────┘  │
└────────────────────────────────────────────────────────────────────┘
┌────────────────────────────────────────────────────────────────────┐
│  Sidebar (Future)                    │  Content Area              │
│  - Job-specific menu                 │  <router-outlet />         │
│    (from JobMenuItems)               │                            │
│  - Context-aware navigation          │                            │
└────────────────────────────────────────────────────────────────────┘
```

### Current Features
1. **Job Branding**
   - Job logo image
   - Job banner image
   - Job name display

2. **Role Navigation**
   - Horizontal role tabs (Parent, Director, Club Rep, etc.)
   - Active role highlighting
   - Click to switch role context

3. **User Info Section**
   - Current username display
   - "Switch Role" button (navigates to role selection)
   - "Logout" button
   - Theme toggle button

4. **Content Area**
   - `<router-outlet>` for nested routes
   - Full-width layout for job-specific components

### Planned Enhancements

#### A. Current User Component
Display detailed user information:
```html
<div class="current-user">
  <img [src]="user.avatarUrl" alt="User avatar" class="user-avatar">
  <div class="user-details">
    <span class="user-name">{{ user.fullName }}</span>
    <span class="user-role">{{ user.currentRole }}</span>
    <span class="user-email">{{ user.email }}</span>
  </div>
</div>
```

#### B. Current Job Component
Display comprehensive job context:
```html
<div class="current-job">
  <img [src]="job.logoPath" alt="Job logo" class="job-logo">
  <div class="job-info">
    <h3 class="job-name">{{ job.jobName }}</h3>
    <p class="job-path">{{ job.jobPath }}</p>
    <span class="job-season">{{ job.season }}</span>
  </div>
</div>
```

#### C. Non-Specific Menu
Static menu items available to all users:
```html
<nav class="global-menu">
  <a routerLink="help" class="menu-item">
    <i class="icon-help"></i>
    Help & Documentation
  </a>
  <a routerLink="settings" class="menu-item">
    <i class="icon-settings"></i>
    User Settings
  </a>
  <button (click)="logout()" class="menu-item">
    <i class="icon-logout"></i>
    Logout
  </button>
</nav>
```

#### D. Job-Specific Menu (Dynamic)
Built from `JobMenuItems` entity in database:
```typescript
interface JobMenuItem {
  id: number;
  jobId: number;
  label: string;
  route: string;
  icon: string;
  order: number;
  requiredRoles: string[];  // Which roles can see this item
  parentId?: number;        // For nested menus
}
```

Example implementation:
```html
<nav class="job-menu">
  <div *ngFor="let item of jobMenuItems" class="menu-section">
    <!-- Top-level menu item -->
    <a *ngIf="!item.parentId && hasRequiredRole(item)" 
       [routerLink]="item.route" 
       routerLinkActive="active"
       class="menu-item">
      <i [class]="item.icon"></i>
      {{ item.label }}
    </a>
    
    <!-- Nested items -->
    <div *ngIf="hasChildren(item)" class="submenu">
      <a *ngFor="let child of getChildren(item)" 
         [routerLink]="child.route"
         routerLinkActive="active"
         class="submenu-item">
        {{ child.label }}
      </a>
    </div>
  </div>
</nav>
```

### Key Files
- Component: `src/app/layout/layout.component.ts`
- Styles: `src/app/layout/layout.component.scss`

### Route Configuration
```typescript
{
  path: ':jobPath',
  component: LayoutComponent,  // Wraps all job-specific routes
  children: [
    { path: '', component: JobHomeComponent },
    { path: 'teams', component: TeamListComponent },
    { path: 'schedule', component: ScheduleComponent }
  ]
}
```

---

## Layout Selection Logic

The routing configuration automatically selects the appropriate layout:

```typescript
// PUBLIC LAYOUT - Used for /tsic/* routes
/tsic                    → PublicLayoutComponent → TsicLandingComponent
/tsic/login              → PublicLayoutComponent → LoginComponent
/tsic/role-selection     → PublicLayoutComponent → RoleSelectionComponent

// JOB-SPECIFIC LAYOUT - Used for /:jobPath/* routes
/tsic/home               → LayoutComponent → JobHomeComponent (TSIC job)
/american-select-2026    → LayoutComponent → JobHomeComponent (specific job)
/american-select-2026/teams → LayoutComponent → TeamListComponent
```

---

## Design Principles

### PublicLayoutComponent
1. **Minimal & Clean**: No distractions, focus on the task (login, select role)
2. **Branding**: Strong TeamSportsInfo.com identity
3. **Accessibility**: High contrast, clear CTAs
4. **Responsive**: Mobile-first design with Bootstrap

### LayoutComponent (Job-Specific)
1. **Context-Aware**: User always knows which job and role they're in
2. **Dynamic**: Menu adapts based on job configuration and user role
3. **Efficient**: Quick access to common actions (logout, switch role, help)
4. **Scalable**: Can handle complex nested menus from JobMenuItems

---

## Implementation Roadmap

### Phase 1: Public Layout ✅ COMPLETE
- [x] Create PublicLayoutComponent
- [x] TeamSportsInfo.com header with branding
- [x] Theme toggle integration
- [x] Route configuration for /tsic routes
- [x] Global styles for public layout

### Phase 2: Job Layout Enhancement (CURRENT)
Current LayoutComponent has basic structure. Next steps:

- [ ] **Refactor to use ThemeService** (currently has own theme logic)
- [ ] **Extract components**:
  - [ ] `CurrentUserComponent` - Show user avatar, name, role, email
  - [ ] `CurrentJobComponent` - Show job logo, name, season info
  - [ ] `GlobalMenuComponent` - Static menu (help, settings, logout)
  - [ ] `JobMenuComponent` - Dynamic menu from JobMenuItems entity

- [ ] **Add sidebar layout option**:
  - [ ] Collapsible sidebar for job menu
  - [ ] Toggle between sidebar and top navigation
  - [ ] Persist user preference (collapsed/expanded)

- [ ] **Implement JobMenuItems**:
  - [ ] Backend API to fetch menu items for current job
  - [ ] Frontend service to manage menu state
  - [ ] Role-based filtering (show/hide based on user role)
  - [ ] Support nested menu items
  - [ ] Icon library integration

### Phase 3: Advanced Features
- [ ] Breadcrumb navigation
- [ ] Contextual help tooltips
- [ ] Job switching dropdown (if user has multiple jobs)
- [ ] Notifications/alerts in header
- [ ] Quick search (jobs, teams, players)
- [ ] Mobile-responsive drawer navigation

---

## Best Practices

### When to Use PublicLayoutComponent
- User is NOT authenticated
- Page doesn't require job context
- Marketing/informational content
- Authentication flows (login, password reset)

### When to Use LayoutComponent
- User IS authenticated
- Working within a specific job
- Accessing job-specific data (teams, schedules, reports)
- Need access to job menu and user context

### Styling Guidelines
1. **Use Global Utility Classes**: Defined in `src/styles.scss`
2. **Bootstrap First**: Leverage Bootstrap utilities before custom CSS
3. **Component Styles**: Only for truly component-specific styling
4. **CSS Variables**: Use theme variables (`--bg-primary`, `--text-primary`, etc.)
5. **Dark Mode**: Always test both light and dark themes

---

## Migration Notes

If migrating an existing component to use the new layouts:

### Before (Old App Component Header)
```html
<div class="app-container">
  <header>TSIC Next Generation</header>
  <router-outlet />
</div>
```

### After (PublicLayoutComponent)
```html
<!-- App Component: Just the router outlet -->
<router-outlet />

<!-- Routes wrapped in PublicLayoutComponent automatically get header -->
```

No changes needed to individual components - the layout is applied via routing!

---

## Related Documentation
- [Routing Strategy](./routing-strategy.md)
- [Bootstrap Usage Guidelines](./bootstrap-usage-guidelines.md) _(to be created)_
- [Theme System](./theme-system.md) _(to be created)_
