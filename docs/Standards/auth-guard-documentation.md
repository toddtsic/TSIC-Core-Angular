# Authentication Guard Documentation

## Overview

The TSIC application uses a **single unified authentication guard** (`authGuard`) that handles all authentication and authorization scenarios. This guard consolidates what were previously three separate guards into one flexible, data-driven guard.

**Location:** `src/app/core/guards/auth.guard.ts`

## Guard Consolidation History

Previously, the application used three separate guards:
1. `authGuard` - Basic authentication checking
2. `redirectAuthenticatedGuard` - Redirecting logged-in users away from login/landing pages
3. `superUserGuard` - Checking for administrator privileges

These have been **consolidated into a single `authGuard`** that uses route data flags to determine behavior.

## Authentication Phases

The application uses a two-phase authentication system:

### Phase 1: Basic Authentication
- **Requirements:** Username only
- **Token Claims:** `sub` (username), `exp`, `iat`, `nbf`
- **Access:** Can access role selection and basic authenticated routes

### Phase 2: Full Authentication
- **Requirements:** Username + Registration ID + Job Path
- **Token Claims:** All Phase 1 claims plus `regId` and `jobPath`
- **Access:** Can access job-specific features and registration flows

## Route Data Flags

The guard's behavior is controlled by data flags set on routes in `app.routes.ts`:

### `allowAnonymous: true`
- **Purpose:** Allows unauthenticated users to access the route
- **Use Case:** Registration flows, job landing pages, public content
- **Behavior:**
  - Unauthenticated users can proceed without being redirected to login
  - Does NOT clear localStorage or call `logoutLocal()`
  - Authenticated users can also access these routes
- **Example:**
  ```typescript
  {
    path: ':jobPath',
    canActivate: [authGuard],
    data: { allowAnonymous: true },
    children: [...]
  }
  ```

### `requirePhase2: true`
- **Purpose:** Requires Phase 2 authentication (regId + jobPath)
- **Use Case:** Job-specific features, player registration, family management
- **Behavior:**
  - Phase 1 users are redirected to `/tsic/role-selection`
  - Phase 2 users can proceed
- **Example:**
  ```typescript
  {
    path: 'registration',
    canActivate: [authGuard],
    data: { requirePhase2: true }
  }
  ```

### `requireSuperUser: true`
- **Purpose:** Requires SuperUser/administrator privileges
- **Use Case:** Admin panels, system configuration, user management
- **Behavior:**
  - Non-SuperUsers see an error toast and are redirected
  - Checks `authService.isSuperuser()` which validates the `isSuperUser` claim in the JWT
  - Requires the user to have a jobPath selected
- **Example:**
  ```typescript
  {
    path: 'admin',
    canActivate: [authGuard],
    data: { requireSuperUser: true }
  }
  ```

### `redirectAuthenticated: true`
- **Purpose:** Redirects authenticated users away from login/landing pages
- **Use Case:** Login page, main landing page
- **Behavior:**
  - **Authenticated users:**
    - Checks for `returnUrl` query parameter and honors it if valid
    - If user has a real job (not 'tsic'), redirects to `/${jobPath}`
    - If no job selected, redirects to `/tsic/role-selection`
  - **Unauthenticated users:**
    - Checks for `force`, `returnUrl`, or `intent` query parameters
    - If present, allows access to the page
    - Otherwise, checks for `last_job_path` in localStorage
    - If found, redirects to that job path
    - If not found, allows access to the page
- **Example:**
  ```typescript
  {
    path: 'login',
    component: LoginComponent,
    data: { redirectAuthenticated: true }
  }
  ```

### No Flags (Default Behavior)
- **Purpose:** Requires Phase 1 authentication (username only)
- **Use Case:** Basic authenticated routes like role selection
- **Behavior:**
  - Unauthenticated users are redirected to `/tsic/login`
  - Attempts to refresh token if refresh token exists
  - Phase 1 authenticated users can proceed

## Guard Logic Flow

### 1. Initial Checks
```typescript
const user = authService.getCurrentUser();
const isAuth = authService.isAuthenticated();
const requirePhase2 = route.data['requirePhase2'] === true;
const allowAnonymous = route.data['allowAnonymous'] === true;
const redirectAuthenticated = route.data['redirectAuthenticated'] === true;
const requireSuperUser = route.data['requireSuperUser'] === true;
```

### 2. Handle `redirectAuthenticated` Routes
When `redirectAuthenticated: true`:

**For Authenticated Users:**
1. Check for `returnUrl` query parameter
2. Validate and redirect to returnUrl if present and valid
3. Check if user has a real job (not 'tsic')
4. Redirect to job home page if yes
5. Redirect to role selection if no job

**For Unauthenticated Users:**
1. Check for force flags (`force=1`, `returnUrl`, `intent` query params)
2. If present, allow access
3. Check for `last_job_path` in localStorage
4. If found, redirect to that job path
5. Otherwise, allow access

### 3. Handle Unauthenticated Users (No `redirectAuthenticated`)
When user is not authenticated:

**If `allowAnonymous: true`:**
- Return `true` (allow access)
- Does NOT call `logoutLocal()`

**If `allowAnonymous: false` or undefined:**
1. Call `authService.logoutLocal()` to clear state
2. Check for refresh token
3. If refresh token exists:
   - Attempt to refresh access token
   - On success: Check Phase 2 requirements if needed
   - On failure: Redirect to `/tsic/login?returnUrl=...`
4. If no refresh token:
   - Redirect to `/tsic/login?returnUrl=...`

### 4. Validate Job Path in URL
For authenticated users:
```typescript
const urlJobPath = route.paramMap.get('jobPath');
if (urlJobPath && user.jobPath && urlJobPath !== user.jobPath) {
    authService.logout();
    toastService.show(
        `You were logged out because you navigated to a different job...`,
        'warning'
    );
    return router.createUrlTree(['/tsic/login'], { queryParams: { returnUrl: state.url } });
}
```

**Purpose:** Ensures the job path in the URL matches the job path in the JWT token.

**Why:** Prevents users from accessing a different job's data with their current token. This is a security measure.

### 5. Check Phase 2 Requirements
```typescript
if (requirePhase2 && (!user.regId || !user.jobPath)) {
    return router.createUrlTree(['/tsic/role-selection']);
}
```

### 6. Check SuperUser Requirements
```typescript
if (requireSuperUser) {
    if (!authService.isSuperuser()) {
        toastService.show('Access denied. SuperUser privileges required.', 'danger');
        return router.createUrlTree([user.jobPath ? `/${user.jobPath}/home` : '/tsic/role-selection']);
    }
    if (!user.jobPath) {
        return router.createUrlTree(['/tsic/role-selection']);
    }
}
```

### 7. Allow Access
If all checks pass, return `true` to allow navigation.

## Common Route Patterns

### Public Job Landing Page
```typescript
{
    path: ':jobPath',
    canActivate: [authGuard],
    data: { allowAnonymous: true },
    component: JobLandingComponent
}
```
- Accessible to both authenticated and unauthenticated users
- Used for registration flows where users start unauthenticated

### Protected Job Home Page
```typescript
{
    path: ':jobPath',
    canActivate: [authGuard],
    data: { allowAnonymous: true },
    children: [
        {
            path: 'home',
            canActivate: [authGuard],
            data: { requirePhase2: true },
            component: JobHomeComponent
        }
    ]
}
```
- Parent allows anonymous (for landing)
- Child requires Phase 2 auth (for home)

### Admin Routes
```typescript
{
    path: ':jobPath',
    canActivate: [authGuard],
    data: { allowAnonymous: true },
    children: [
        {
            path: 'admin',
            canActivate: [authGuard],
            data: { requireSuperUser: true },
            loadChildren: () => import('./admin/admin.routes')
        }
    ]
}
```

### Login Page
```typescript
{
    path: 'tsic',
    canActivate: [authGuard],
    data: { allowAnonymous: true },
    children: [
        {
            path: 'login',
            component: LoginComponent,
            data: { redirectAuthenticated: true }
        }
    ]
}
```
- Redirects authenticated users
- Allows unauthenticated access
- Handles `returnUrl` query parameter

## Query Parameters

### `returnUrl`
- **Purpose:** Specifies where to redirect after successful authentication
- **Used By:** Guard when redirecting to login
- **Validated:** Yes - must be same origin
- **Example:** `/tsic/login?returnUrl=/demo-job/registration`

### `force`
- **Purpose:** Forces the landing/login page to show even if user has a last job path
- **Values:** `1` or `true`
- **Example:** `/tsic?force=1`

### `intent`
- **Purpose:** Indicates user intent, prevents auto-redirect from landing page
- **Example:** `/tsic?intent=register`

## LocalStorage Integration

### `last_job_path`
- **Managed By:** `LastLocationService`
- **Purpose:** Remembers the last job a user visited
- **Used By:** Guard's `redirectAuthenticated` logic
- **Behavior:** Unauthenticated users visiting `/tsic` are redirected to their last job if one exists

## Security Features

### Token Validation
- Guard checks token expiration via `authService.isAuthenticated()`
- Attempts automatic refresh if refresh token is available
- Clears state and redirects to login on validation failure

### Job Path Enforcement
- URL job path must match token job path
- Prevents cross-job data access
- Logs user out if mismatch detected

### SuperUser Verification
- Checks `isSuperUser` claim in JWT token
- Server-side validation required for actual authorization
- Toast notification on access denial

## Dependencies

### Services
- **AuthService:** Token management, user state, authentication checks
- **Router:** Navigation and URL tree creation
- **ToastService:** User notifications (success, info, warning, danger)
- **LastLocationService:** Tracks last visited job path

### RxJS
- **map:** Transform refresh token response
- **catchError:** Handle refresh failures

## Testing Scenarios

### Scenario 1: Unauthenticated User Visits Job Landing
- Route has `allowAnonymous: true`
- User is not authenticated
- **Expected:** Access granted, no redirect

### Scenario 2: Unauthenticated User Visits Protected Route
- Route has no `allowAnonymous` flag
- User is not authenticated
- **Expected:** Redirect to `/tsic/login?returnUrl=...`

### Scenario 3: Phase 1 User Visits Phase 2 Route
- Route has `requirePhase2: true`
- User has Phase 1 token (no regId/jobPath)
- **Expected:** Redirect to `/tsic/role-selection`

### Scenario 4: Non-SuperUser Visits Admin Route
- Route has `requireSuperUser: true`
- User is not a SuperUser
- **Expected:** Toast notification + redirect to home or role selection

### Scenario 5: Authenticated User Visits Login
- Route has `redirectAuthenticated: true`
- User is authenticated with a job
- **Expected:** Redirect to `/${jobPath}`

### Scenario 6: User Changes Job in URL
- User token has `jobPath: 'demo-job'`
- User navigates to `/other-job/home`
- **Expected:** Logout + toast + redirect to login

### Scenario 7: Token Expired, Refresh Available
- User token expired
- Refresh token exists and valid
- **Expected:** Automatic token refresh + allow access

### Scenario 8: Unauthenticated Visits Landing with Last Job
- User not authenticated
- `last_job_path` in localStorage exists
- No force/intent query params
- **Expected:** Redirect to last job path

## Migration Notes

When the guard was consolidated, the following changes were made:

### Removed Guards
- `redirectAuthenticatedGuard` - logic merged into `redirectAuthenticated` flag
- `superUserGuard` - logic merged into `requireSuperUser` flag
- `resolveAuthRedirect` helper function - no longer needed

### Route Updates
All routes were updated to use the single `authGuard` with appropriate data flags:

**Before:**
```typescript
{
    path: 'login',
    canActivate: [redirectAuthenticatedGuard],
    component: LoginComponent
}
```

**After:**
```typescript
{
    path: 'login',
    component: LoginComponent,
    data: { redirectAuthenticated: true }
}
```

### Benefits of Consolidation
1. **Single Source of Truth:** All authentication logic in one place
2. **Easier Testing:** One guard to test instead of three
3. **Better Maintainability:** Consistent patterns and less code duplication
4. **Flexible Configuration:** Data flags provide clear, declarative intent
5. **Reduced Bundle Size:** Less code to ship to the browser

## Best Practices

### 1. Use Parent Route Guards
Apply the guard at the parent level when multiple child routes share the same requirements:

```typescript
{
    path: ':jobPath',
    canActivate: [authGuard],
    data: { allowAnonymous: true },
    children: [
        { path: '', component: JobLandingComponent },
        { path: 'register', component: RegisterComponent }
        // Child routes inherit allowAnonymous
    ]
}
```

### 2. Override on Child Routes
Child routes can override parent data flags:

```typescript
{
    path: ':jobPath',
    canActivate: [authGuard],
    data: { allowAnonymous: true },
    children: [
        { path: '', component: JobLandingComponent },
        {
            path: 'home',
            canActivate: [authGuard],
            data: { requirePhase2: true } // Overrides allowAnonymous
        }
    ]
}
```

### 3. Combine Flags Thoughtfully
Some flag combinations don't make logical sense:
- `allowAnonymous: true` + `requirePhase2: true` - Contradictory
- `requireSuperUser: true` + `allowAnonymous: true` - Contradictory

### 4. Always Set `returnUrl`
When programmatically navigating to login, include the return URL:

```typescript
this.router.navigate(['/tsic/login'], {
    queryParams: { returnUrl: this.router.url }
});
```

### 5. Test Edge Cases
Always test:
- Expired tokens with valid refresh tokens
- Expired tokens with expired refresh tokens
- Job path mismatches
- Anonymous access scenarios
- SuperUser vs regular user access

## Troubleshooting

### Issue: Infinite Redirect Loop
**Cause:** Route configuration error or missing flags
**Solution:** Check that landing/login routes have `redirectAuthenticated: true` and parent routes have `allowAnonymous: true`

### Issue: Always Redirected to Login
**Cause:** Missing `allowAnonymous: true` flag
**Solution:** Add `allowAnonymous: true` to routes that should be accessible to unauthenticated users

### Issue: Can't Access Admin Routes
**Cause:** User doesn't have SuperUser claim in token
**Solution:** Verify backend is issuing `isSuperUser` claim for admin users

### Issue: Job Path Mismatch Logout
**Cause:** User manually changed job in URL or clicked link to different job
**Solution:** This is expected behavior - user must log in again for the new job

### Issue: Lost After Refresh
**Cause:** Refresh token expired or invalid
**Solution:** User must log in again - refresh tokens have limited lifetime

## Future Enhancements

Potential improvements to consider:

1. **Role-Based Access Control:** Add `requireRoles: ['coach', 'parent']` flag
2. **Permission-Based Access:** Add `requirePermissions: ['edit-teams']` flag
3. **Time-Based Access:** Add date/time restrictions for certain features
4. **Feature Flags:** Integrate with feature flag system
5. **Analytics:** Track guard denials for security monitoring

## Related Documentation

- [Authentication Service](./auth-service-documentation.md)
- [Authorization Policies](./authorization-policies.md)
- [Two-Phase Authentication](./two-phase-authentication.md)
- [Security Best Practices](./security-best-practices.md)

---

**Last Updated:** December 24, 2025  
**Version:** 2.0 (Consolidated Guard)  
**Maintainer:** Development Team
