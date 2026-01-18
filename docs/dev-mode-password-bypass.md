# Dev-Mode Password Bypass

## Overview

Development environment feature that allows testing any club rep account without knowing actual passwords.

## Configuration

**File**: `appsettings.Development.json` (gitignored - safe for dev secrets)

```json
{
  "DevMode": {
    "AllowPasswordBypass": true,
    "BypassPassword": "dev123"
  }
}
```

## Usage

1. **Find username**: Log into any event, browse registrations to find club rep usernames
2. **Login with bypass password**: Use the actual club rep username, but enter `dev123` as the password (instead of their real password)
3. **Test scenarios**: Switch between different club reps instantly without knowing their actual credentials

## Security

- ✅ **Development environment only**: Requires `IWebHostEnvironment.IsDevelopment()`
- ✅ **No production risk**: Feature disabled in production regardless of config
- ✅ **No database modification**: Only bypasses password validation, doesn't change stored passwords
- ✅ **Configuration-based**: Can be disabled by setting `AllowPasswordBypass: false`
- ✅ **Separate bypass password**: Doesn't expose or modify real user passwords

## Implementation Details

**Location**: [AuthController.cs](../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/AuthController.cs) Login method

**Logic flow**:
1. Validate request (username, password required)
2. Find user by username
3. **Dev-mode check**:
   - If Development environment AND AllowPasswordBypass=true AND password matches BypassPassword → Allow login
   - Otherwise → Normal password validation with `CheckPasswordAsync()`
4. Production always uses normal password validation

**Pseudocode**:
```csharp
if (_env.IsDevelopment() && config["DevMode:AllowPasswordBypass"] && password == config["DevMode:BypassPassword"])
{
    // Bypass - allow login without checking actual password
}
else
{
    // Normal validation - check actual password hash
}
```

## Testing Examples

### Scenario: Test Multiple Club Reps
```bash
# Find club reps in event "AIM CAC 2026"
# Browse registrations → see usernames: clubrep1, clubrep2, clubrep3

# Login as clubrep1 (use "dev123" as password, not their real password)
POST /api/auth/login
{ "username": "clubrep1", "password": "dev123" }

# Login as clubrep2 (use "dev123" as password, not their real password)
POST /api/auth/login
{ "username": "clubrep2", "password": "dev123" }

# Login as clubrep3 (use "dev123" as password, not their real password)
POST /api/auth/login
{ "username": "clubrep3", "password": "dev123" }
```

### Scenario: Test Different Customer Scenarios
```bash
# Customer A club rep (username from database, password is "dev123")
POST /api/auth/login
{ "username": "customerA_rep", "password": "dev123" }

# Customer B club rep (username from database, password is "dev123")
POST /api/auth/login
{ "username": "customerB_rep", "password": "dev123" }
```

## Disabling Feature

Set `AllowPasswordBypass: false` in `appsettings.Development.json`:

```json
{
  "DevMode": {
    "AllowPasswordBypass": false,
    "BypassPassword": "dev123"
  }
}
```

Or remove entire `DevMode` section (defaults to disabled).

## Alternative Approaches Considered

1. **Auto-reset password on login** ❌ - Modifies database unnecessarily
2. **Separate dev endpoint** ❌ - Extra endpoint surface, more code
3. **Configuration-based bypass** ✅ - **Chosen approach** - Clean, safe, simple

## Benefits Over Alternatives

- **No database writes**: Doesn't modify password hashes in dev database
- **No extra endpoints**: Reuses existing login flow
- **Self-documenting**: Config file shows feature is enabled
- **Easy toggle**: Single boolean to enable/disable
- **Production-safe**: Impossible to activate in production environment
