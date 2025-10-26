# Use Case: Logging In with Role Selection

## Overview
Users authenticate with username/password, then select from their available roles to complete authentication. This creates a two-phase login process: credential validation followed by role selection, resulting in role-specific JWT tokens with regId and jobPath claims for navigation.

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
- On submit, call authentication service
- Show loading spinner during API call

**API Backend:**
- Receive login request at `POST /api/auth/login`
- Validate credentials against Identity user store
- Query user registrations and associated roles via `IRoleLookupService`
- Return `LoginResponseDto` containing list of available `RegistrationRoleDto` objects
- **DO NOT** include regId or jobPath claims in any token at this stage

### 3. Authentication Success - Show Role Selection
**API Response:**
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

**Angular Frontend:**
- Hide username/password form
- Display "Select Role" interface using Syncfusion Typeahead/Dropdown
- Populate dropdown with available roles from login response
- Format display as: "RoleName - DisplayText (RegId)"
- Allow user to select one role
- Show selected role confirmation

### 4. User Selects Role
**Angular Frontend:**
- User selects role from typeahead dropdown
- On selection, call role selection service
- Send selected registration data to server

**API Backend:**
- Receive role selection at `POST /api/auth/select-role`
- Validate user is authenticated (basic token)
- Generate JWT token with role-specific claims:
  - `regId`: Selected registration ID
  - `jobPath`: Path for role-specific navigation
  - Standard JWT claims (sub, iat, exp, etc.)
- Return updated token to client

### 5. Role Selection Success - Navigate to Application
**API Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "jobPath": "/superuser/dashboard"
}
```

**Angular Frontend:**
- Store JWT token securely
- Extract `jobPath` claim from token
- Navigate to role-specific application path
- Update authentication state with role context
- Show success message

### 4. Authentication Failure
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

### 5. Role Selection Failure
**API Response:**
```json
{
  "error": "Invalid role selection or authentication expired"
}
```

**Angular Frontend:**
- Display error message
- Return to role selection interface
- Allow user to select different role or re-authenticate

## Alternative Flows

### A1. Single Role User
- User has only one available role/registration
- After successful login, automatically select the single role
- Skip role selection UI and proceed directly to role activation
- Generate token with regId/jobPath claims immediately

### A2. Account Locked
- User has exceeded maximum login attempts
- API returns account locked error during credential validation
- Angular shows message with unlock instructions

### A3. Session Timeout During Role Selection
- User authenticated but takes too long to select role
- Role selection API returns authentication expired error
- Angular returns user to login form
- Show message: "Session expired, please login again"

### A4. Invalid Role Selection
- User attempts to select a role they don't have access to
- API validates role belongs to authenticated user
- Return error and allow role reselection

## Postconditions

### Success
- User is fully authenticated with role-specific JWT token
- Token contains regId and jobPath claims for the selected role
- User is navigated to role-specific application path
- Authentication state updated with role context throughout app

### Failure
- User remains unauthenticated
- Error message displayed
- Login form ready for retry

## API Endpoints Required

### POST /api/auth/login
**Request:**
```json
{
  "username": "user123",
  "password": "password123"
}
```

**Response:** See success example above (returns available roles, no token)

### POST /api/auth/select-role
**Request:**
```json
{
  "regId": "REG001",
  "roleName": "Superuser"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "jobPath": "/superuser/dashboard"
}
```

## Angular Components Required

### LoginComponent
- Form handling with reactive forms
- Validation messages for username/password
- Loading states during authentication
- Error handling for login failures
- Transition to role selection on success

### RoleSelectionComponent
- Syncfusion Typeahead/Dropdown for role selection
- Display format: "RoleName - DisplayText (RegId)"
- Single selection mode
- Loading states during role activation
- Error handling for role selection failures
- Navigation to jobPath on success

### AuthService
- HTTP calls to auth endpoints (`/login`, `/select-role`)
- JWT token storage/retrieval
- Authentication state management
- Role selection logic

### AuthGuard
- Route protection for authenticated routes
- Automatic redirect to login for unauthenticated users
- Role-based route protection using token claims

## Data Models

### TypeScript Interfaces
```typescript
interface LoginRequest {
  username: string;
  password: string;
}

interface LoginResponse {
  registrations: RegistrationRole[];
}

interface RegistrationRole {
  roleName: string;
  roleRegistrations: Registration[];
}

interface Registration {
  regId: string;
  displayText: string;
  jobLogo: string;
}

interface RoleSelectionRequest {
  regId: string;
  roleName: string;
}

interface RoleSelectionResponse {
  token: string;
  expiresIn: number;
  jobPath: string;
}
```

### C# Models
```csharp
public record LoginRequest(string Username, string Password);

public record LoginResponseDto(List<RegistrationRoleDto> Registrations);

public record RegistrationRoleDto(string RoleName, List<RegistrationDto> RoleRegistrations);

public record RegistrationDto(string RegId, string DisplayText, string JobLogo);

public record RoleSelectionRequest(string RegId, string RoleName);

public record RoleSelectionResponse(string Token, int ExpiresIn, string JobPath);
```

## Acceptance Criteria

### Functional
- [ ] User can log in with valid TSIC username and password
- [ ] User receives list of available roles upon successful login (no token yet)
- [ ] User sees role selection interface with Syncfusion typeahead dropdown
- [ ] User can select a role from available options
- [ ] User receives JWT token with regId and jobPath claims after role selection
- [ ] User is automatically navigated to role-specific path from jobPath claim
- [ ] User sees appropriate error messages for invalid credentials
- [ ] User sees appropriate error messages for invalid role selection
- [ ] Protected routes redirect unauthenticated users to login
- [ ] Single-role users bypass role selection and go directly to role activation

### Security
- [ ] Passwords are validated against Identity store
- [ ] JWT tokens contain role-specific regId and jobPath claims
- [ ] Tokens are properly signed and validated
- [ ] User can only select roles they actually have access to
- [ ] HTTPS is required for all auth endpoints
- [ ] Rate limiting prevents brute force attacks
- [ ] Failed login/role selection attempts are logged

### UX
- [ ] Login form provides real-time validation feedback
- [ ] Role selection dropdown shows clear role information
- [ ] Loading states prevent multiple submissions during both phases
- [ ] Error messages are user-friendly and actionable
- [ ] Form remembers username on failed login attempts
- [ ] Keyboard navigation works properly in both login and role selection
- [ ] Seamless transition between login and role selection phases

## Implementation Notes

### Angular Implementation
- Use Angular Reactive Forms for login validation
- Implement NgRx for state management (optional but recommended)
- Use Syncfusion Typeahead component for role selection
- Implement Angular Router Guards for route protection
- Store JWT tokens securely (localStorage/sessionStorage)
- Extract claims from JWT tokens for role-based navigation
- Implement proper error handling for both login and role selection phases

### API Implementation
- Use ASP.NET Core Identity for user authentication
- Implement role-based registration lookup via `IRoleLookupService`
- Generate JWT tokens with role-specific claims (regId, jobPath)
- Implement token validation and claim extraction
- Add rate limiting middleware
- Implement proper error handling and logging for both endpoints

### Component Architecture
- **LoginComponent**: Handles initial authentication
- **RoleSelectionComponent**: Handles role selection and final token generation
- **AuthService**: Manages authentication state and API calls
- **AuthGuard**: Protects routes and validates token claims

### Testing
- Unit tests for auth service methods
- Integration tests for login API endpoint
- Integration tests for role selection API endpoint
- E2E tests for complete two-phase login flow
- Security testing for token validation and claim extraction

## Related Use Cases
- User Registration
- Password Reset
- Profile Management
- Role-Based Access Control
- Token Refresh (future)
- Session Management