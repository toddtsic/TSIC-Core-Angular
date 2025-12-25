# JobPath Authorization Security Implementation

## Overview

Implemented automatic jobPath validation for **every** `[Authorize]` endpoint to prevent users from accessing resources for jobs they didn't authenticate against.

## What Was Implemented

### 1. Created Authorization Infrastructure

**Files Created:**
- `TSIC.API/Authorization/JobPathMatchRequirement.cs` - The authorization requirement
- `TSIC.API/Authorization/JobPathMatchHandler.cs` - The validation logic

### 2. Integrated with Default Policy

**Modified:** `TSIC.API/Program.cs`

Added to the authorization configuration:

```csharp
// Register JobPath validation handler
builder.Services.AddSingleton<IAuthorizationHandler, JobPathMatchHandler>();

builder.Services.AddAuthorization(options =>
{
    // Set default policy that applies to ALL [Authorize] attributes
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new JobPathMatchRequirement())
        .Build();
    
    // ... existing named policies
});
```

## How It Works

### Security Rules

The `JobPathMatchHandler` enforces these rules **automatically** for every `[Authorize]` endpoint:

1. **SuperUser Bypass**: SuperUsers can access any job (bypass validation)
2. **No Route JobPath**: Allowed (endpoint doesn't involve a specific job, e.g., `/api/auth/registrations`)
3. **No Token JobPath**: Denied with 403 (user hasn't selected a job yet)
4. **Token JobPath matches Route JobPath**: Allowed
5. **Token JobPath doesn't match Route JobPath**: Denied with 403 (attempted cross-job access)

### Example Scenarios

#### ✅ Valid Access
```
Token jobPath: "aim-cac-2026"
Route: GET /api/jobs/aim-cac-2026/bulletins
Result: 200 OK (jobPaths match)
```

#### ✅ SuperUser Cross-Job Access
```
Token: { jobPath: "aim-cac-2026", isSuperUser: true }
Route: GET /api/jobs/summer-showcase-2025/menus
Result: 200 OK (SuperUser can access any job)
```

#### ✅ No Job Context Endpoint
```
Token jobPath: "aim-cac-2026"
Route: GET /api/auth/registrations
Result: 200 OK (no jobPath in route, allowed)
```

#### ❌ Cross-Job Access Attempt
```
Token jobPath: "aim-cac-2026"
Route: GET /api/jobs/summer-showcase-2025/bulletins
Result: 403 Forbidden (jobPaths don't match)
```

#### ❌ No Job Selected
```
Token: { username: "user@example.com" } (Phase 1 auth only)
Route: GET /api/jobs/aim-cac-2026/menus
Result: 403 Forbidden (no jobPath in token)
```

## Benefits

### 1. **Security by Default**
- Every `[Authorize]` endpoint automatically validates jobPath
- No need to remember to add validation
- Can't accidentally forget protection

### 2. **Zero Code Changes Required**
- Existing controllers work as-is
- No need to add `[ValidateJobPath]` attributes
- No need to add validation code to every endpoint

### 3. **Comprehensive Logging**
- Warning logs for failed attempts (security monitoring)
- Debug logs for successful authorizations
- Includes username, token jobPath, and route jobPath

### 4. **Proper HTTP Status Codes**
- Returns 403 Forbidden (not 401 Unauthorized) for jobPath mismatches
- Clear distinction between authentication and authorization failures

## Testing

### Test Cases to Verify

1. **Same Job Access** (should work):
   ```bash
   # Login to aim-cac-2026
   POST /api/auth/select-registration { regId: "..." }
   # Access aim-cac-2026 resources
   GET /api/jobs/aim-cac-2026/menus
   ```

2. **Cross-Job Access** (should fail with 403):
   ```bash
   # Login to aim-cac-2026
   POST /api/auth/select-registration { regId: "..." }
   # Try to access different job
   GET /api/jobs/summer-showcase-2025/menus
   ```

3. **SuperUser Cross-Job Access** (should work):
   ```bash
   # Login as SuperUser to aim-cac-2026
   POST /api/auth/select-registration { regId: "..." }
   # Access different job (SuperUser privilege)
   GET /api/jobs/summer-showcase-2025/menus
   ```

4. **No Job Context Endpoint** (should work):
   ```bash
   # Login to any job
   POST /api/auth/select-registration { regId: "..." }
   # Access endpoint without jobPath in route
   GET /api/auth/registrations
   ```

5. **Phase 1 Auth Only** (should fail with 403 when accessing job-specific endpoints):
   ```bash
   # Login but don't select registration
   POST /api/auth/login { username: "...", password: "..." }
   # Try to access job-specific endpoint
   GET /api/jobs/aim-cac-2026/menus
   ```

## Monitoring

### Log Messages to Watch For

**Warning (Potential Security Issue):**
```
JobPathMatchHandler: User 'user@example.com' attempted to access '/api/jobs/summer-showcase-2025/menus' for job 'summer-showcase-2025' but has no jobPath in token
```

```
JobPathMatchHandler: User 'user@example.com' with token jobPath 'aim-cac-2026' attempted to access route jobPath 'summer-showcase-2025'
```

**Debug (Normal Operation):**
```
JobPathMatchHandler: User 'user@example.com' authorized for job 'aim-cac-2026'
```

```
JobPathMatchHandler: SuperUser access granted
```

## Future Enhancements

Potential improvements:

1. **Metrics Collection**: Track frequency of cross-job access attempts
2. **Rate Limiting**: Throttle users making repeated invalid attempts
3. **Admin Notifications**: Alert on suspicious patterns
4. **Audit Trail**: Log all jobPath validations to database for compliance
5. **Custom Exception**: Return more detailed error messages in development mode

## Related Documentation

- [Authorization Policies](./authorization-policies.md)
- [Security Policy](./security-policy-account-separation.md)
- [Authentication Flow](./two-phase-authentication.md)

---

**Implemented:** December 24, 2025  
**Security Level:** Critical - Prevents unauthorized cross-job data access
