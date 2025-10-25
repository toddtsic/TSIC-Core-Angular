# Use Case: Logging In

## Overview
Users need to authenticate to access the TSIC application. This use case covers the complete login flow from the Angular frontend through API authentication.

## Actors
- **Primary Actor**: User (registered system user)
- **Secondary Actor**: Authentication System (IdentityServer/OpenIddict)

## Preconditions
- User has a valid account with username/email and password
- User has access to the login page
- The application is running and accessible

## Main Success Scenario

### 1. User Accesses Login Page
**Angular Frontend:**
- User navigates to `/login` route
- Login component renders with:
  - Email/username input field
  - Password input field
  - "Remember me" checkbox (optional)
  - "Forgot password" link
  - "Sign up" link for new users
  - Submit button

### 2. User Submits Credentials
**Angular Frontend:**
- Form validation ensures:
  - Email/username is provided and valid format
  - Password is provided and meets minimum requirements
- On submit, call authentication service
- Show loading spinner during API call

**API Backend:**
- Receive login request at `POST /api/auth/login`
- Validate credentials against user store
- Generate JWT tokens (access + refresh)
- Return authentication result

### 3. Authentication Success
**API Response:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "refresh_token_here",
    "expiresIn": 3600,
    "user": {
      "id": "user_id",
      "email": "user@example.com",
      "firstName": "John",
      "lastName": "Doe",
      "roles": ["user"]
    }
  }
}
```

**Angular Frontend:**
- Store tokens in localStorage/sessionStorage
- Update authentication state in NgRx/store
- Redirect to dashboard or intended page
- Show success message

### 4. Authentication Failure
**API Response:**
```json
{
  "success": false,
  "error": {
    "code": "INVALID_CREDENTIALS",
    "message": "Invalid email or password"
  }
}
```

**Angular Frontend:**
- Display error message
- Clear password field
- Keep email field populated
- Allow retry

## Alternative Flows

### A1. Account Locked
- User has exceeded maximum login attempts
- API returns account locked error
- Angular shows message with unlock instructions

### A2. Email Not Verified
- User's email address not verified
- API returns email verification required error
- Angular shows verification prompt with resend option

### A3. Remember Me Selected
- Store refresh token in localStorage (persistent)
- Set longer token expiry
- User stays logged in across browser sessions

### A4. Two-Factor Authentication Required
- After password validation, API requires 2FA
- Angular shows 2FA input form
- User enters verification code
- Complete authentication flow

## Postconditions

### Success
- User is authenticated and authorized
- JWT tokens stored securely
- User redirected to application dashboard
- Authentication state updated throughout app

### Failure
- User remains unauthenticated
- Error message displayed
- Login form ready for retry

## API Endpoints Required

### POST /api/auth/login
**Request:**
```json
{
  "email": "user@example.com",
  "password": "password123",
  "rememberMe": false
}
```

**Response:** See success/failure examples above

### POST /api/auth/refresh (for token refresh)
**Request:**
```json
{
  "refreshToken": "refresh_token_here"
}
```

### POST /api/auth/logout
**Request:**
```json
{
  "refreshToken": "refresh_token_here"
}
```

## Angular Components Required

### LoginComponent
- Form handling with reactive forms
- Validation messages
- Loading states
- Error handling
- Navigation after login

### AuthService
- HTTP calls to auth endpoints
- Token storage/retrieval
- Token refresh logic
- Authentication state management

### AuthGuard
- Route protection for authenticated routes
- Automatic redirect to login for unauthenticated users

### Interceptor (HTTP)
- Automatic token attachment to requests
- Token refresh on 401 responses
- Logout on authentication failure

## Data Models

### TypeScript Interfaces
```typescript
interface LoginRequest {
  email: string;
  password: string;
  rememberMe?: boolean;
}

interface AuthResponse {
  success: boolean;
  data?: {
    accessToken: string;
    refreshToken: string;
    expiresIn: number;
    user: User;
  };
  error?: {
    code: string;
    message: string;
  };
}

interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
}
```

### C# Models
```csharp
public class LoginRequest
{
    public string Email { get; set; }
    public string Password { get; set; }
    public bool RememberMe { get; set; }
}

public class AuthResponse
{
    public bool Success { get; set; }
    public AuthData Data { get; set; }
    public AuthError Error { get; set; }
}

public class AuthData
{
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public UserDto User { get; set; }
}
```

## Acceptance Criteria

### Functional
- [ ] User can log in with valid credentials
- [ ] User sees appropriate error messages for invalid credentials
- [ ] User is redirected to dashboard after successful login
- [ ] User session persists according to "Remember me" setting
- [ ] Protected routes redirect unauthenticated users to login

### Security
- [ ] Passwords are never stored in plain text
- [ ] JWT tokens are properly signed and validated
- [ ] HTTPS is required for all auth endpoints
- [ ] Rate limiting prevents brute force attacks
- [ ] Failed login attempts are logged

### UX
- [ ] Login form provides real-time validation feedback
- [ ] Loading states prevent multiple submissions
- [ ] Error messages are user-friendly and actionable
- [ ] Form remembers email address on failed attempts
- [ ] Keyboard navigation works properly

## Implementation Notes

### Angular Implementation
- Use Angular Reactive Forms for validation
- Implement NgRx for state management (optional but recommended)
- Use Angular Router Guards for route protection
- Implement HTTP Interceptors for automatic token handling

### API Implementation
- Use ASP.NET Core Identity or OpenIddict for authentication
- Implement JWT token generation and validation
- Add rate limiting middleware
- Implement proper error handling and logging

### Testing
- Unit tests for auth service methods
- Integration tests for login API endpoint
- E2E tests for complete login flow
- Security testing for authentication bypass attempts

## Related Use Cases
- User Registration
- Password Reset
- Profile Management
- Session Management