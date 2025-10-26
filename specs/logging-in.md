# Use Case: Logging In (TSIC Two-Phase Authentication)

## Overview
Users need to authenticate to access the TSIC application using a **two-phase authentication flow**:
1. **Phase 1**: Username/password validation
2. **Phase 2**: Role selection from available user roles

This use case covers the complete login flow from the Angular frontend through API authentication.

## Implementation Phases

### Phase 1 (Current Implementation)
- Basic login endpoint with username/password validation
- JWT token generation with user claims
- Role selection endpoint
- Angular login and role selection components
- Basic validation and error handling

### Phase 2 (Future)
- Auth guard and HTTP interceptor
- Refresh token support
- Remember me functionality

### Phase 3 (Future)
- Rate limiting
- Account locking
- Email verification
- Two-factor authentication

## Actors
- **Primary Actor**: User (registered system user with one or more roles)
- **Secondary Actor**: TSIC SQL Server Database (legacy authentication)

## Preconditions
- User has a valid account in the TSIC database with username and password
- User has at least one assigned role
- User has access to the login page
- The application is running and accessible

## Main Success Scenario

### 1. User Accesses Login Page
**Angular Frontend:**
- User navigates to `/login` route
- Login component renders with:
  - Username input field
  - Password input field
  - Submit button

### 2. User Submits Credentials (Phase 1)
**Angular Frontend:**
- Form validation ensures:
  - Username is provided
  - Password is provided
- On submit, call authentication service
- Show loading spinner during API call

**API Backend:**
- Receive login request at `POST /api/auth/login`
- Validate credentials against TSIC database user store
- Query available roles for the user
- Return user info and available roles (no token yet)

### 3. Phase 1 Success - Show Role Selection
**API Response:**
```json
{
  "userId": "user_id",
  "username": "jdoe",
  "firstName": "John",
  "lastName": "Doe",
  "availableRoles": [
    {
      "roleId": "role1",
      "roleName": "Administrator",
      "displayText": "Admin Access"
    },
    {
      "roleId": "role2",
      "roleName": "User",
      "displayText": "Standard User"
    }
  ]
}
```

**Angular Frontend:**
- Store temporary user info (no tokens yet)
- Navigate to role selection component
- Display available roles for user to choose

### 4. User Selects Role (Phase 2)
**Angular Frontend:**
- User selects one role from the list
- Submit role selection to API

**API Backend:**
- Receive role selection at `POST /api/auth/select-role`
- Validate user session and selected role
- Generate JWT access token with:
  - User claims (userId, username, name)
  - Role claims
  - Token expiration
- Determine jobPath based on role
- Return authentication token

### 5. Phase 2 Success - Authentication Complete
**API Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "user": {
    "userId": "user_id",
    "username": "jdoe",
    "firstName": "John",
    "lastName": "Doe",
    "selectedRole": "Administrator",
    "jobPath": "/path/to/job"
  }
}
```

**Angular Frontend:**
- Store access token in sessionStorage
- Update authentication state
- Redirect to dashboard or intended page
- Show success message

### 6. Authentication Failure
**API Response (Phase 1 - Invalid Credentials):**
```json
{
  "error": "Invalid username or password"
}
```

**API Response (Phase 2 - Invalid Role):**
```json
{
  "error": "Selected role is not available for this user"
}
```

**Angular Frontend:**
- Display error message
- For Phase 1: Clear password field, allow retry
- For Phase 2: Return to role selection, allow re-selection

## Alternative Flows

### A1. User Has Only One Role
- After Phase 1 validation, if user has only one role
- API automatically selects that role
- Skip role selection UI
- Return token immediately

### A2. Session Timeout Between Phases
- User takes too long to select role
- Temporary session expires
- Redirect back to login page
- Show message: "Session expired, please log in again"

## Postconditions

### Success
- User is authenticated with selected role
- JWT access token stored in sessionStorage
- User redirected to application dashboard
- Authentication state updated throughout app
- jobPath determined based on selected role

### Failure
- User remains unauthenticated
- Error message displayed
- User can retry from appropriate phase

## API Endpoints Required

### POST /api/auth/login
**Request:**
```json
{
  "username": "jdoe",
  "password": "password123"
}
```

**Success Response (200):**
```json
{
  "userId": "user_id",
  "username": "jdoe",
  "firstName": "John",
  "lastName": "Doe",
  "availableRoles": [
    {
      "roleId": "role1",
      "roleName": "Administrator",
      "displayText": "Admin Access"
    }
  ]
}
```

**Error Response (401):**
```json
{
  "error": "Invalid username or password"
}
```

### POST /api/auth/select-role
**Request:**
```json
{
  "userId": "user_id",
  "roleId": "role1"
}
```

**Success Response (200):**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "user": {
    "userId": "user_id",
    "username": "jdoe",
    "firstName": "John",
    "lastName": "Doe",
    "selectedRole": "Administrator",
    "jobPath": "/path/to/job"
  }
}
```

**Error Response (400):**
```json
{
  "error": "Invalid role selection"
}
```

## Angular Components Required

### LoginComponent
- Form handling with reactive forms for username/password
- Validation messages
- Loading states
- Error handling
- Navigation to role selection on success

### RoleSelectionComponent
- Display available roles
- Role selection UI
- Loading states during token generation
- Error handling
- Navigation to dashboard on success

### AuthService
- HTTP calls to auth endpoints (`/login`, `/select-role`)
- Token storage/retrieval in sessionStorage
- Authentication state management
- User info management

## Data Models

### TypeScript Interfaces
```typescript
interface LoginRequest {
  username: string;
  password: string;
}

interface LoginResponse {
  userId: string;
  username: string;
  firstName: string;
  lastName: string;
  availableRoles: Role[];
}

interface Role {
  roleId: string;
  roleName: string;
  displayText: string;
}

interface RoleSelectionRequest {
  userId: string;
  roleId: string;
}

interface AuthTokenResponse {
  accessToken: string;
  expiresIn: number;
  user: AuthenticatedUser;
}

interface AuthenticatedUser {
  userId: string;
  username: string;
  firstName: string;
  lastName: string;
  selectedRole: string;
  jobPath: string;
}
```

### C# Models (DTOs)
```csharp
public class LoginRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
}

public class LoginResponse
{
    public string UserId { get; set; }
    public string Username { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public List<RoleDto> AvailableRoles { get; set; }
}

public class RoleDto
{
    public string RoleId { get; set; }
    public string RoleName { get; set; }
    public string DisplayText { get; set; }
}

public class RoleSelectionRequest
{
    public string UserId { get; set; }
    public string RoleId { get; set; }
}

public class AuthTokenResponse
{
    public string AccessToken { get; set; }
    public int ExpiresIn { get; set; }
    public AuthenticatedUserDto User { get; set; }
}

public class AuthenticatedUserDto
{
    public string UserId { get; set; }
    public string Username { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string SelectedRole { get; set; }
    public string JobPath { get; set; }
}
```

## Acceptance Criteria

### Phase 1 - Functional
- [ ] User can submit username and password
- [ ] API validates credentials against TSIC database
- [ ] API returns user info and available roles on success
- [ ] User sees appropriate error message for invalid credentials
- [ ] User navigates to role selection on successful login

### Phase 1 - Security
- [ ] Passwords are never stored in plain text
- [ ] Password comparison uses secure hashing
- [ ] HTTPS is used for all auth endpoints (in production)
- [ ] Failed login attempts are logged

### Phase 1 - UX
- [ ] Login form provides validation feedback
- [ ] Loading states prevent multiple submissions
- [ ] Error messages are user-friendly
- [ ] Form clears password on failed attempts
- [ ] Keyboard navigation works properly

### Phase 2 - Functional
- [ ] User can select from available roles
- [ ] API generates JWT token with correct claims
- [ ] API determines correct jobPath for selected role
- [ ] User is redirected to dashboard after role selection
- [ ] Token is stored in sessionStorage
- [ ] If user has only one role, auto-select and skip UI

### Phase 2 - Security  
- [ ] JWT tokens are properly signed and validated
- [ ] Token includes user and role claims
- [ ] Token expiration is set correctly
- [ ] Invalid role selections are rejected

### Phase 2 - UX
- [ ] Role selection UI is clear and intuitive
- [ ] Loading states during token generation
- [ ] Error messages for invalid selections
- [ ] Smooth navigation flow from login → roles → dashboard

## Implementation Notes

### Angular Implementation
- Use Angular Reactive Forms for validation
- Store tokens in sessionStorage (not localStorage for security)
- Implement route guards in Phase 2
- Implement HTTP interceptors in Phase 2
- Use RxJS for async state management

### API Implementation
- Use existing TSIC SQL Server database for user authentication
- Implement JWT token generation with System.IdentityModel.Tokens.Jwt
- Use secure password hashing (already in database)
- Implement proper error handling and logging
- Use FluentValidation for request validation
- Query user roles from existing database tables

### Database Schema (Existing TSIC Tables)
- User authentication table (password hashes)
- User roles/permissions tables
- jobPath mapping based on role types

### Testing
- Unit tests for auth service methods (Angular)
- Unit tests for AuthController (API)
- Integration tests for login and role selection endpoints
- E2E tests for complete two-phase flow
- Test single-role auto-selection scenario

## Related Use Cases
- User Registration (Future)
- Password Reset (Future)
- Profile Management (Future)
- Session Management (Phase 2)