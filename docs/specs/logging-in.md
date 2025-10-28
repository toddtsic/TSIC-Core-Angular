# Use Case: Logging In with Role Selection

## Overview
Users authenticate with username/password and receive a minimal JWT token (Phase 1). They then select from their available roles via an API call using that token (Phase 2). Upon role selection, the API returns an enriched JWT token containing regId and jobPath claims for role-specific navigation. This two-phase JWT approach cleanly separates authentication (who you are) from authorization (what you can access).

## Actors
- **Primary Actor**: User (registered system user with multiple roles)
- **Secondary Actor**: Authentication System (ASP.NET Core Identity + JWT)

## Preconditions
- User has a valid account with username/password
- User has multiple roles/registrations assigned
- User has access to the login page
- The application is running and accessible

## Main Success Scenario

### 1. User Accesses Login Page
**Angular Frontend:**
- User navigates to `/login` route
- Login component renders with:
  - TeamSportsInfo.com Username (TSIC) username input field
  - Password input field
  - Submit button

### 2. User Submits Credentials
**Angular Frontend:**
- Form validation ensures:
  - username is provided and valid format (3-50 chars, alphanumeric + underscore)
  - Password is provided and meets minimum requirements (6 characters min)
- On submit, call `authService.login(credentials)`
- Show loading spinner during API call

**API Backend:**
- Receive login request at `POST /api/auth/login`
- Validate credentials against Identity user store
- Generate minimal JWT token containing only username claim
- Return `AuthTokenResponse` with `accessToken` and optional `expiresIn`
- **DO NOT** include regId or jobPath claims in Phase 1 token

### 3. Authentication Success - Navigate to Role Selection
**API Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VybmFtZSI6InVzZXIxMjMiLCJleHAiOjE3MzAwMDAwMDB9...",
  "expiresIn": 3600
}
```

**Angular Frontend (LoginComponent):**
- `authService.login()` automatically stores token in localStorage (key: 'auth_token')
- Navigate to `/role-selection` route
- No manual sessionStorage or state management needed

**Angular Frontend (RoleSelectionComponent):**
- On `ngOnInit()`, call `authService.getAvailableRegistrations()`
- HTTP interceptor automatically adds `Authorization: Bearer {token}` header
- API validates token and returns user's available registrations
- Display registrations using Syncfusion DropDownList component
- Group by role name, show job logo and display text in item template
- Enable filtering for typeahead search functionality

### 4. User Selects Role
**Angular Frontend:**
- User selects role from Syncfusion dropdown
- On selection, call `authService.selectRegistration(regId)`
- HTTP interceptor automatically includes Phase 1 token in Authorization header
- Show loading state during API call

**API Backend:**
- Receive role selection at `POST /api/auth/select-registration`
- Validate Phase 1 token from Authorization header
- Extract username from Phase 1 token claims
- Validate selected regId belongs to authenticated user
- Generate enriched JWT token with complete claims:
  - `username`: User's username
  - `regId`: Selected registration ID
  - `jobPath`: Path for role-specific navigation
  - Standard JWT claims (iat, exp, etc.)
- Return `AuthTokenResponse` with new token

### 5. Role Selection Success - Navigate to Application
**API Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VybmFtZSI6InVzZXIxMjMiLCJyZWdJZCI6IlJFRzAwMSIsImpvYlBhdGgiOiIvc3VwZXJ1c2VyL2Rhc2hib2FyZCIsImV4cCI6MTczMDAwMDAwMH0...",
  "expiresIn": 3600
}
```

**Angular Frontend:**
- `authService.selectRegistration()` automatically replaces old token with new enriched token in localStorage
- Decode token to extract jobPath claim using `authService.getJobPath()`
- Automatically navigate to role-specific path: `this.router.navigate([jobPath])`
- Authentication state now includes full role context
- All subsequent API calls include enriched token via interceptor

### 6. Authentication Failure
**API Response:**
```json
{
  "errors": [
    {
      "field": "Username",
      "message": "Username is required"
    },
    {
      "field": "Password", 
      "message": "Password must be at least 6 characters"
    }
  ]
}
```

**Angular Frontend:**
- Display validation error messages
- Clear password field
- Keep username field populated
- Allow retry

### 7. Role Selection Failure
**API Response:**
```json
{
  "error": "Invalid role selection or authentication expired"
}
```

**Angular Frontend:**
- Display error message
- Remain on role selection page
- Allow user to select different role
- If token expired, redirect to login with appropriate message

## Alternative Flows

### A1. Single Role User
- User has only one available role/registration
- After successful login, navigate to `/role-selection` as normal
- `getAvailableRegistrations()` returns single registration
- Automatically call `selectRegistration()` without showing dropdown UI
- Generate enriched token with regId/jobPath claims immediately
- Navigate directly to jobPath

### A2. Account Locked
- User has exceeded maximum login attempts
- API returns 401 Unauthorized with account locked error during Phase 1
- Angular shows message with unlock instructions
- Token never generated

### A3. Session Timeout During Role Selection
- User authenticated with Phase 1 token but takes too long to select role
- Phase 1 token expires before `selectRegistration()` API call
- API returns 401 Unauthorized during Phase 2
- HTTP interceptor or error handler detects 401
- Clear token from localStorage
- Redirect to login route
- Show message: "Session expired, please login again"

### A4. Invalid Role Selection
- User attempts to select a role they don't have access to
- API validates regId belongs to authenticated user (from Phase 1 token username)
- Return 403 Forbidden error
- Angular shows error and allows role reselection

## Postconditions

### Success - Phase 1 (Login)
- User has minimal JWT token stored in localStorage
- Token contains only username claim
- User navigated to role-selection route
- HTTP interceptor ready to inject token in subsequent requests

### Success - Phase 2 (Role Selection)
- User has enriched JWT token stored in localStorage (replaces Phase 1 token)
- Token contains username, regId, and jobPath claims
- User is navigated to role-specific application path from jobPath
- All subsequent API calls include enriched token automatically
- Full authentication and authorization complete

### Failure
- User remains unauthenticated
- No token stored in localStorage
- Error message displayed
- Login form ready for retry

## API Endpoints Required

### POST /api/auth/login (Phase 1)
**Request:**
```json
{
  "username": "user123",
  "password": "password123"
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VybmFtZSI6InVzZXIxMjMiLCJleHAiOjE3MzAwMDAwMDB9...",
  "expiresIn": 3600
}
```

**Token Claims (Phase 1):**
- `username`: User's username
- `exp`: Token expiration timestamp
- `iat`: Token issued at timestamp

### GET /api/auth/registrations (Phase 2 - Step 1)
**Headers:**
```
Authorization: Bearer {phase1Token}
```

**Response:**
```json
{
  "registrations": [
    {
      "roleName": "Superuser",
      "roleRegistrations": [
        {
          "regId": "REG001",
          "displayText": "Super User Registration",
          "jobLogo": "superuser-logo.png"
        }
      ]
    },
    {
      "roleName": "Director",
      "roleRegistrations": [
        {
          "regId": "DIR001",
          "displayText": "League Director",
          "jobLogo": "director-logo.png"
        }
      ]
    }
  ]
}
```

### POST /api/auth/select-registration (Phase 2 - Step 2)
**Headers:**
```
Authorization: Bearer {phase1Token}
```

**Request:**
```json
{
  "regId": "REG001"
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VybmFtZSI6InVzZXIxMjMiLCJyZWdJZCI6IlJFRzAwMSIsImpvYlBhdGgiOiIvc3VwZXJ1c2VyL2Rhc2hib2FyZCIsImV4cCI6MTczMDAwMDAwMH0...",
  "expiresIn": 3600
}
```

**Token Claims (Phase 2):**
- `username`: User's username
- `regId`: Selected registration ID
- `jobPath`: Role-specific navigation path (e.g., "/superuser/dashboard")
- `exp`: Token expiration timestamp
- `iat`: Token issued at timestamp

## Angular Components Required

### LoginComponent
- Form handling with reactive forms
- Validation messages for username/password
- Loading states during authentication
- Error handling for login failures
- Calls `authService.login()` which automatically:
  - Stores Phase 1 token in localStorage
  - Returns observable for success/error handling
- Navigates to `/role-selection` on successful login

### RoleSelectionComponent
- Syncfusion DropDownList for role selection with filtering enabled
- Display format using custom item template showing job logo and display text
- Groups registrations by role name
- Loading state during API calls
- Calls `authService.getAvailableRegistrations()` on init
- Calls `authService.selectRegistration(regId)` on selection
- Automatic navigation to jobPath from enriched token claims
- Error handling for registration loading and selection failures

### AuthService
Core authentication service managing JWT token lifecycle:

**Methods:**
- `login(credentials)`: Phase 1 authentication, returns Observable<AuthTokenResponse>
- `getAvailableRegistrations()`: Phase 2 step 1, fetches user's roles (requires Phase 1 token)
- `selectRegistration(regId)`: Phase 2 step 2, returns enriched token with regId/jobPath
- `getToken()`: Retrieves current token from localStorage
- `isAuthenticated()`: Checks if valid token exists
- `hasSelectedRole()`: Checks if token contains regId claim (Phase 2 complete)
- `getJobPath()`: Extracts jobPath claim from enriched token
- `logout()`: Clears token from localStorage
- `decodeToken(token)`: Private utility to extract JWT claims

**Storage:**
- Uses localStorage with key 'auth_token'
- Automatically manages token replacement during Phase 1 → Phase 2 transition

### AuthInterceptor (HttpInterceptorFn)
- Functional interceptor registered in app.config.ts
- Automatically injects `Authorization: Bearer {token}` header on all HTTP requests
- Reads token from AuthService.getToken()
- Skips header injection if no token exists

### AuthGuard (Route Guard)
- Protects routes requiring authentication
- Checks `authService.isAuthenticated()`
- Redirects to `/` (login) if unauthenticated
- Can be enhanced with role-based checks using `hasSelectedRole()`

## Data Models

### TypeScript Interfaces (auth.models.ts)
```typescript
// Phase 1: Login request and response
export interface LoginCredentials {
  username: string;
  password: string;
}

export interface AuthTokenResponse {
  accessToken: string;
  expiresIn?: number;
}

// Phase 2: Registration data structures
export interface RegistrationRole {
  roleName: string;
  roleRegistrations: Registration[];
}

export interface Registration {
  regId: string;
  displayText: string;
  jobLogo: string;
}

export interface AvailableRegistrationsResponse {
  registrations: RegistrationRole[];
}

// Phase 2: Role selection
export interface RoleSelectionRequest {
  regId: string;
}

// Decoded token claims
export interface AuthenticatedUser {
  username: string;
  regId?: string;      // Only present after Phase 2
  jobPath?: string;    // Only present after Phase 2
}
```

### C# Models (API Backend)
```csharp
// Phase 1: Login
public record LoginRequest(string Username, string Password);

public record AuthTokenResponse(string AccessToken, int? ExpiresIn);

// Phase 2: Registrations
public record AvailableRegistrationsResponse(List<RegistrationRoleDto> Registrations);

public record RegistrationRoleDto(string RoleName, List<RegistrationDto> RoleRegistrations);

public record RegistrationDto(string RegId, string DisplayText, string JobLogo);

// Phase 2: Role selection
public record RoleSelectionRequest(string RegId);
```

## Acceptance Criteria

### Functional
- [ ] User can log in with valid TSIC username and password
- [ ] User receives Phase 1 JWT token containing only username claim
- [ ] Phase 1 token automatically stored in localStorage
- [ ] User automatically navigated to `/role-selection` after successful login
- [ ] Role selection component calls API to fetch registrations (requires Phase 1 token)
- [ ] HTTP interceptor automatically adds Authorization header to registration fetch
- [ ] User sees Syncfusion dropdown with available roles grouped by role name
- [ ] Dropdown displays job logo and display text in item template
- [ ] Typeahead filtering works for role search
- [ ] User can select a role from dropdown
- [ ] Selection triggers API call with Phase 1 token in header
- [ ] User receives enriched Phase 2 JWT token with username, regId, and jobPath claims
- [ ] Phase 2 token automatically replaces Phase 1 token in localStorage
- [ ] User is automatically navigated to role-specific path from jobPath claim
- [ ] User sees appropriate error messages for invalid credentials (Phase 1)
- [ ] User sees appropriate error messages for invalid role selection (Phase 2)
- [ ] Protected routes redirect unauthenticated users to login
- [ ] Single-role users can auto-select without showing dropdown (optional enhancement)

### Security
- [ ] Passwords are validated against Identity store
- [ ] Phase 1 JWT tokens contain minimal claims (username only)
- [ ] Phase 2 JWT tokens contain enriched claims (username, regId, jobPath)
- [ ] Tokens are properly signed and validated
- [ ] User can only select roles they actually have access to (validated server-side)
- [ ] HTTP interceptor includes Authorization header on all API requests
- [ ] HTTPS is required for all auth endpoints
- [ ] Rate limiting prevents brute force attacks
- [ ] Failed login/role selection attempts are logged
- [ ] Token expiration handled gracefully with redirect to login

### UX
- [ ] Login form provides real-time validation feedback
- [ ] Role selection dropdown shows clear role information with logos
- [ ] Loading states prevent multiple submissions during both phases
- [ ] Error messages are user-friendly and actionable
- [ ] Form remembers username on failed login attempts
- [ ] Keyboard navigation works properly in both login and role selection
- [ ] Seamless transition between login and role selection phases
- [ ] Token management is transparent to user (no manual intervention needed)

## Implementation Notes

### Two-Phase JWT Token Flow
**Phase 1 - Initial Authentication:**
1. User submits credentials via LoginComponent
2. API validates credentials, returns minimal JWT (username claim only)
3. AuthService stores token in localStorage
4. User navigated to RoleSelectionComponent

**Phase 2 - Role Selection & Authorization:**
1. RoleSelectionComponent calls `getAvailableRegistrations()`
2. HTTP interceptor adds Phase 1 token to request header
3. API validates Phase 1 token, returns user's available roles
4. User selects role from Syncfusion dropdown
5. Component calls `selectRegistration(regId)`
6. HTTP interceptor adds Phase 1 token to request header
7. API validates Phase 1 token and regId ownership
8. API returns enriched JWT with username, regId, jobPath claims
9. AuthService replaces Phase 1 token with Phase 2 token in localStorage
10. Component extracts jobPath and navigates automatically

### Why localStorage vs sessionStorage?
- **localStorage**: Persists across browser tabs and page refreshes
  - Users remain authenticated when opening new tabs
  - Page refresh doesn't require re-authentication
  - Token survives browser restart (until expiration)
  
- **sessionStorage**: Tab-isolated, cleared on tab close
  - Would require re-login for every new tab
  - Page refresh would clear authentication
  - More secure but less user-friendly for web apps

**Decision**: Using localStorage for better UX, relying on token expiration and secure HTTP-only cookies for sensitive refresh tokens (future enhancement).

### HTTP Interceptor Architecture
**Purpose**: Centralize Authorization header injection
**Implementation**: Functional interceptor (Angular 14+)
```typescript
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).getToken();
  if (token) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    });
  }
  return next(req);
};
```

**Benefits**:
- No manual header management in service methods
- Consistent authentication across all HTTP calls
- Easy to add error handling for 401/403 responses
- Supports token refresh logic in one place

### Angular Implementation
- Use Angular Reactive Forms for login validation
- Implement Syncfusion DropDownListModule with filtering
- Use Standalone Components architecture (Angular 14+)
- Implement Angular Router Guards for route protection
- Store JWT tokens in localStorage (key: 'auth_token')
- Extract claims from JWT tokens using base64 decode
- Implement proper error handling for both login and role selection phases
- Use HttpInterceptorFn for automatic header injection

### API Implementation
- Use ASP.NET Core Identity for user authentication
- Implement role-based registration lookup via `IRoleLookupService`
- Generate JWT tokens with progressive claim enrichment:
  - Phase 1: username, exp, iat
  - Phase 2: username, regId, jobPath, exp, iat
- Protect `/api/auth/registrations` and `/api/auth/select-registration` with `[Authorize]`
- Validate Phase 1 token and extract username in Phase 2 endpoints
- Implement token validation and claim extraction
- Add rate limiting middleware
- Implement proper error handling and logging for both endpoints

### Component Architecture
- **LoginComponent**: Handles Phase 1 authentication
- **RoleSelectionComponent**: Handles Phase 2 registration selection
- **AuthService**: Manages authentication state, token storage, API calls
- **authInterceptor**: Automatically injects Authorization headers
- **AuthGuard**: Protects routes requiring authentication

### Role Switching Without Re-authentication
Users can switch roles without logging out:
1. Add "Switch Role" button in app header/menu
2. Button navigates back to `/role-selection`
3. Phase 1 token still valid in localStorage
4. `getAvailableRegistrations()` re-fetches roles using existing token
5. User selects different role
6. `selectRegistration()` returns new enriched token with different regId/jobPath
7. Navigate to new jobPath

**Benefits**: No password re-entry required, seamless role switching

### Testing
- Unit tests for AuthService methods (login, getAvailableRegistrations, selectRegistration, token decode)
- Unit tests for authInterceptor header injection
- Integration tests for login API endpoint (Phase 1)
- Integration tests for registrations API endpoint (Phase 2 step 1)
- Integration tests for select-registration API endpoint (Phase 2 step 2)
- E2E tests for complete two-phase login flow
- Security testing for token validation and claim extraction
- Test token expiration handling and error scenarios

## Related Use Cases
- User Registration
- Password Reset
- Profile Management
- Role-Based Access Control
- Token Refresh (future)
- Session Management