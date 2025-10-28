# TSIC Multi-Tenant Routing Strategy

## Overview
The TSIC Angular application implements a three-tier routing strategy that supports multi-tenancy through progressive JWT authentication. This approach separates platform-wide operations (under `/tsic`) from tenant-specific operations (under `/:jobPath`).

## Route Structure

### Platform Routes (`/tsic/*`)
Routes for non-tenant-specific operations where the active context is the TSIC platform itself.

```
/tsic                    → Public landing page (marketing/info)
/tsic/login              → User authentication
/tsic/role-selection     → Choose registration/tenant after login
```

### Tenant Routes (`/:jobPath/*`)
Routes for tenant-specific operations where the active context is a specific job/organization.

```
/:jobPath                → Tenant home page (e.g., /americanselect-california-2026)
/:jobPath/teams          → [Future] Team management
/:jobPath/schedule       → [Future] Schedule/calendar
/:jobPath/reports        → [Future] Reports and analytics
```

## Authentication Flow

### Phase 1: Username Authentication
1. User navigates to `/tsic/login`
2. Provides credentials (username/password)
3. API returns JWT with `username` claim only
4. User is redirected to `/tsic/role-selection`

**Phase 1 JWT Claims:**
```json
{
  "sub": "user-id",
  "username": "user@example.com",
  "jti": "token-id",
  "iat": 1234567890,
  "exp": 1234571490
}
```

### Phase 2: Role/Tenant Selection
1. User sees list of available registrations (tenants they belong to)
2. Selects a registration (e.g., "American Select California 2026")
3. API returns enriched JWT with `username`, `regId`, and `jobPath` claims
4. User is redirected to `/:jobPath` (tenant home)

**Phase 2 JWT Claims:**
```json
{
  "sub": "user-id",
  "username": "user@example.com",
  "regId": "12345",
  "jobPath": "americanselect-california-2026",
  "jti": "token-id",
  "iat": 1234567890,
  "exp": 1234571490
}
```

## Route Guards

### `authGuard`
- **Purpose**: Ensures user has any valid JWT token (Phase 1 or 2)
- **Applied to**: `/tsic/role-selection`
- **Redirects to**: `/tsic/login` if not authenticated

### `roleGuard`
- **Purpose**: Ensures user has Phase 2 token (with regId + jobPath)
- **Applied to**: `/:jobPath` and all tenant-specific routes
- **Redirects to**: 
  - `/tsic/role-selection` if only Phase 1 authenticated
  - `/tsic/login` if not authenticated

### `landingPageGuard`
- **Purpose**: Redirects already-authenticated users from public landing
- **Applied to**: `/tsic`
- **Redirects to**:
  - `/:jobPath` if Phase 2 authenticated (has jobPath)
  - `/tsic/role-selection` if Phase 1 authenticated (no jobPath)
  - Allows access if not authenticated

### `redirectAuthenticatedGuard`
- **Purpose**: Prevents authenticated users from accessing login page
- **Applied to**: `/tsic/login`
- **Redirects to**:
  - `/:jobPath` if Phase 2 authenticated
  - `/tsic/role-selection` if Phase 1 authenticated
  - Allows access if not authenticated

## Navigation Flows

### Unauthenticated User Journey
```
1. Browser → /                              [redirects to /tsic]
2. User   → /tsic                           [shows landing page]
3. User   → clicks "Get Started"            [navigates to /tsic/login]
4. User   → enters credentials              [authenticates]
5. System → stores Phase 1 JWT              [redirects to /tsic/role-selection]
6. User   → selects registration            [gets Phase 2 JWT]
7. System → redirects to /:jobPath          [tenant home page]
```

### Phase 1 Authenticated User (Has Username Token)
```
- /tsic                   → AUTO REDIRECT → /tsic/role-selection
- /tsic/login             → AUTO REDIRECT → /tsic/role-selection
- /tsic/role-selection    → ✓ ACCESSIBLE
- /:jobPath               → AUTO REDIRECT → /tsic/role-selection
```

### Phase 2 Authenticated User (Has Full Token with JobPath)
```
- /tsic                   → AUTO REDIRECT → /:jobPath
- /tsic/login             → AUTO REDIRECT → /:jobPath
- /tsic/role-selection    → ✓ ACCESSIBLE (can change role)
- /:jobPath               → ✓ ACCESSIBLE
```

## Multi-Tenancy Features

### Tenant Isolation
- Each tenant (job/organization) gets its own URL space under `/:jobPath`
- The `jobPath` value comes from the database and is part of the Phase 2 JWT
- Examples: `/americanselect-california-2026`, `/norcal-premier-2025`, etc.

### Role Switching
- Users can have multiple registrations across different tenants
- "Change Role" button on tenant pages navigates to `/tsic/role-selection`
- User can select different tenant without re-authenticating
- New Phase 2 JWT issued with new `regId` and `jobPath`

### Security Model
- **Phase 1 Token**: Limited access, can only view role selection
- **Phase 2 Token**: Full access to specific tenant's data via `jobPath`
- Guards prevent unauthorized access to tenant routes
- Token stored in localStorage, validated on each route navigation

## Component Structure

### Platform Components
```
TsicLandingComponent     → /tsic (public landing page)
LoginComponent           → /tsic/login (authentication)
RoleSelectionComponent   → /tsic/role-selection (tenant picker)
```

### Tenant Components
```
JobHomeComponent         → /:jobPath (tenant dashboard)
[Future Components]
  TeamListComponent      → /:jobPath/teams
  ScheduleComponent      → /:jobPath/schedule
  ReportsComponent       → /:jobPath/reports
```

## Implementation Details

### Lazy Loading
All route components use lazy loading for optimal performance:
```typescript
loadComponent: () => import('./component').then(m => m.Component)
```

### Guard Implementation
Guards use functional pattern with `inject()` for dependency injection:
```typescript
export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  // Guard logic...
};
```

### Token Management
- **Storage**: localStorage with key `auth_token`
- **Decoding**: Base64 URL decoding of JWT payload
- **Reactive State**: BehaviorSubject for current user state
- **Auto-initialization**: Token decoded on app startup

### Logout Behavior
1. Remove token from localStorage
2. Clear user state (BehaviorSubject)
3. Redirect to `/tsic/login`
4. All guards will block access until re-authentication

## Future Enhancements

### Planned Route Additions
```
/:jobPath/teams              → Team roster management
/:jobPath/teams/:teamId      → Individual team details
/:jobPath/schedule           → Game/event schedule
/:jobPath/reports            → Reports and analytics
/:jobPath/settings           → Tenant-specific settings
/:jobPath/members            → Member directory
```

### Additional Guards (Potential)
- `permissionGuard(permission: string)`: Check specific permissions within tenant
- `featureFlagGuard(feature: string)`: Enable/disable routes based on feature flags
- `subscriptionGuard()`: Verify tenant subscription status

### URL-Based Tenant Context
The `jobPath` in the URL provides automatic tenant context:
- No need to store "active tenant" separately
- Bookmarkable URLs for specific tenants
- Shareable links maintain tenant context
- Browser back/forward works as expected

## Benefits of This Architecture

### Developer Experience
- **Clear Organization**: Platform vs Tenant routes clearly separated
- **Type Safety**: Guards use TypeScript for compile-time checking
- **Testability**: Functional guards easy to unit test
- **Maintainability**: Lazy loading keeps bundles small

### User Experience
- **Fast Navigation**: No page reloads, instant route changes
- **Intuitive URLs**: URLs reflect user's context and location
- **Role Switching**: Easy to switch between tenants
- **Secure**: Automatic redirect to login on token expiration

### Multi-Tenancy
- **Scalable**: Unlimited tenants without code changes
- **Isolated**: Each tenant's routes completely separate
- **Flexible**: Easy to add tenant-specific features
- **Secure**: JWT ensures users can only access authorized tenants

## Files Changed

### Created Files
```
src/app/core/guards/auth.guard.ts           → Route guard implementations
src/app/tsic-landing/                       → Public landing page component
src/app/job-home/                           → Tenant home component
```

### Modified Files
```
src/app/app.routes.ts                       → Route configuration
src/app/core/services/auth.service.ts       → Added router for logout redirect
src/app/login/login.component.ts            → Updated navigation paths
src/app/role-selection/role-selection.component.ts → Updated navigation paths
```

## Testing the Flow

### Manual Test Steps
1. Navigate to `https://localhost:4200`
   - Should redirect to `/tsic`
   - Should show landing page

2. Click "Get Started"
   - Should navigate to `/tsic/login`
   - Should show login form

3. Enter credentials and login
   - Should redirect to `/tsic/role-selection`
   - Should show available registrations

4. Select a registration
   - Should redirect to `/:jobPath` (e.g., `/americanselect-california-2026`)
   - Should show tenant home page

5. Click "Change Role"
   - Should navigate to `/tsic/role-selection`
   - Can select different tenant

6. Click "Logout"
   - Should redirect to `/tsic/login`
   - Token should be cleared

### Guard Behavior Tests
1. **Try accessing `/tsic/role-selection` without login**
   - Should redirect to `/tsic/login`

2. **Try accessing `/:jobPath` with only Phase 1 token**
   - Should redirect to `/tsic/role-selection`

3. **Try accessing `/tsic/login` with Phase 2 token**
   - Should redirect to `/:jobPath`

## Conclusion

This routing strategy provides a solid foundation for a multi-tenant SaaS application with:
- Clear separation between platform and tenant operations
- Progressive authentication with appropriate access control
- Scalable architecture supporting unlimited tenants
- Secure token-based authorization
- Excellent user experience with intuitive navigation

The implementation follows Angular best practices with standalone components, functional guards, lazy loading, and reactive state management.
