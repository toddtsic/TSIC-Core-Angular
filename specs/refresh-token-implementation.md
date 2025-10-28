# Refresh Token Implementation

**Implementation Date:** October 28, 2025  
**Architecture:** In-Memory Cache (No Database Changes)

## Overview

Implemented JWT refresh token functionality to provide secure, long-lived authentication sessions without requiring users to re-login frequently. The implementation uses in-memory caching to avoid any database schema changes.

## Key Features

- **Secure Token Generation**: Cryptographically random 64-byte tokens
- **Automatic Token Refresh**: HTTP interceptor handles 401 errors transparently
- **No Database Changes**: Uses IMemoryCache for token storage
- **Configurable Expiration**: 60-minute access tokens, 7-day refresh tokens
- **Token Revocation**: Supports individual and bulk token revocation
- **Sliding Expiration**: Active users get extended refresh token lifetime

## API Implementation (Backend)

### 1. Refresh Token Service

**File:** `TSIC.Application/Services/IRefreshTokenService.cs`

```csharp
public interface IRefreshTokenService
{
    string GenerateRefreshToken(string userId);
    string? ValidateRefreshToken(string refreshToken);
    void RevokeRefreshToken(string refreshToken);
    void RevokeAllUserTokens(string userId);
}
```

**File:** `TSIC.Infrastructure/Services/RefreshTokenService.cs`

- Uses `IMemoryCache` for token storage
- Generates cryptographically secure tokens using `RandomNumberGenerator`
- Stores tokens with absolute (7 days) and sliding (3.5 days) expiration
- Tracks all tokens per user for bulk revocation
- Cache keys: `refresh_token_{token}` and `user_tokens_{userId}`

### 2. Updated DTOs

**File:** `TSIC.API/Dtos/AuthTokenResponse.cs`

```csharp
public record AuthTokenResponse(
    string AccessToken,
    string? RefreshToken = null,
    int? ExpiresIn = null
);
```

**File:** `TSIC.API/Dtos/RefreshTokenRequest.cs`

```csharp
public record RefreshTokenRequest(
    string RefreshToken
);
```

### 3. New Endpoints

**POST /api/auth/refresh**
- Validates refresh token
- Revokes old refresh token
- Issues new access + refresh token pair
- Returns 401 if refresh token is invalid/expired

**POST /api/auth/revoke**
- Revokes a specific refresh token
- Used during logout to invalidate tokens

### 4. Updated Endpoints

**POST /api/auth/login**
- Now returns both `accessToken` and `refreshToken`
- Access token expires in 60 minutes
- Refresh token expires in 7 days

**POST /api/auth/select-registration**
- Now returns both `accessToken` and `refreshToken`
- Maintains same expiration policies

### 5. Configuration

**File:** `TSIC.API/appsettings.json`

```json
"JwtSettings": {
  "SecretKey": "TSIC-Production-Secret-Key-Change-This-To-Something-Secure-67890!",
  "Issuer": "TSIC.API",
  "Audience": "TSIC.Client",
  "ExpirationMinutes": 60,
  "RefreshTokenExpirationDays": 7
}
```

### 6. Service Registration

**File:** `TSIC.API/Program.cs`

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
```

## Client Implementation (Angular)

### 1. Updated Models

**File:** `src/app/core/models/auth.models.ts`

```typescript
export interface AuthTokenResponse {
    accessToken: string;
    refreshToken?: string;
    expiresIn?: number;
}

export interface RefreshTokenRequest {
    refreshToken: string;
}
```

### 2. Enhanced AuthService

**File:** `src/app/core/services/auth.service.ts`

**New Properties:**
```typescript
private readonly REFRESH_TOKEN_KEY = 'refresh_token';
```

**New Methods:**
- `getRefreshToken()`: Retrieves refresh token from localStorage
- `setRefreshToken(token)`: Stores refresh token in localStorage
- `refreshAccessToken()`: Calls `/api/auth/refresh` endpoint

**Updated Methods:**
- `login()`: Stores both access and refresh tokens
- `selectRegistration()`: Stores both access and refresh tokens
- `logout()`: Revokes refresh token on server before clearing storage

### 3. Token Refresh Interceptor

**File:** `src/app/core/interceptors/token-refresh.interceptor.ts`

**Functionality:**
- Intercepts all HTTP responses
- Detects 401 Unauthorized errors
- Skips auth endpoints (login, refresh, revoke)
- Attempts token refresh automatically
- Retries failed request with new access token
- Logs out user if refresh fails

**Flow:**
```
HTTP Request → 401 Error → Refresh Token → Retry Request → Success
                              ↓
                         Refresh Failed → Logout
```

### 4. Interceptor Registration

**File:** `src/app/app.config.ts`

```typescript
provideHttpClient(
  withInterceptors([authInterceptor, tokenRefreshInterceptor])
)
```

**Order matters:** 
1. `authInterceptor` adds the Bearer token
2. `tokenRefreshInterceptor` handles 401 errors

## Authentication Flow

### Initial Login

```
User enters credentials
    ↓
POST /api/auth/login
    ↓
Server validates & generates tokens
    ↓
Returns { accessToken, refreshToken, expiresIn }
    ↓
Client stores both in localStorage
```

### Token Usage

```
Client makes API request
    ↓
authInterceptor adds: Authorization: Bearer {accessToken}
    ↓
Server validates access token
    ↓
Success: Return data
Failure: 401 Unauthorized
```

### Automatic Token Refresh

```
API returns 401 Unauthorized
    ↓
tokenRefreshInterceptor catches error
    ↓
POST /api/auth/refresh { refreshToken }
    ↓
Server validates refresh token
    ↓
Success: New { accessToken, refreshToken }
    ↓
Client stores new tokens
    ↓
Retry original request with new access token
    ↓
Success: Return data to user
```

### Logout Flow

```
User clicks logout
    ↓
POST /api/auth/revoke { refreshToken }
    ↓
Server removes refresh token from cache
    ↓
Client clears localStorage
    ↓
Redirect to /tsic/login
```

## Security Considerations

### Token Storage
- **Access Token**: localStorage (short-lived, 60 minutes)
- **Refresh Token**: localStorage (long-lived, 7 days)
- **Note**: Consider HttpOnly cookies for enhanced security in production

### Token Generation
- Uses `System.Security.Cryptography.RandomNumberGenerator`
- 64 bytes of cryptographically random data
- Base64 encoded (88 characters)

### Token Validation
- Refresh tokens stored server-side (in-memory cache)
- Each refresh revokes old token (rotation)
- Automatic cleanup via sliding expiration
- All user tokens can be bulk revoked

### Attack Mitigation
- **Token Theft**: Short access token lifetime limits exposure
- **Replay Attacks**: Single-use refresh tokens (revoked after use)
- **Session Hijacking**: Bulk revocation on password change/suspicious activity
- **CSRF**: Not vulnerable (tokens in localStorage, not cookies)

## Configuration Options

### Access Token Expiration
```json
"ExpirationMinutes": 60  // 1 hour (configurable)
```

### Refresh Token Expiration
```json
"RefreshTokenExpirationDays": 7  // 7 days (configurable)
```

### Memory Cache Settings
- **Absolute Expiration**: 7 days from creation
- **Sliding Expiration**: 3.5 days (extends on access)
- Active users won't be logged out for up to 7 days

## Testing Checklist

- [ ] Login returns both access and refresh tokens
- [ ] Access token is sent in Authorization header
- [ ] 401 errors trigger automatic refresh
- [ ] Refreshed tokens work for subsequent requests
- [ ] Expired refresh token redirects to login
- [ ] Logout revokes refresh token on server
- [ ] Logout clears both tokens from localStorage
- [ ] Multiple 401s don't cause race conditions
- [ ] Refresh endpoint excludes itself from retry logic
- [ ] User can work uninterrupted for 7 days if active

## Known Limitations

### In-Memory Cache Drawbacks
1. **App Restart**: All refresh tokens lost on server restart
2. **Scalability**: Not suitable for load-balanced environments
3. **No Persistence**: Tokens don't survive deployments

### Solutions for Production
1. **Distributed Cache**: Use Redis or SQL Server distributed cache
2. **Database Storage**: Persist tokens for audit trail
3. **Clustered Cache**: Sync tokens across multiple servers

## Future Enhancements

- [ ] Replace IMemoryCache with IDistributedCache (Redis)
- [ ] Add token family/chain tracking to detect theft
- [ ] Implement device fingerprinting
- [ ] Add geolocation-based anomaly detection
- [ ] Support "Remember Me" with extended refresh tokens
- [ ] Implement refresh token rotation policies
- [ ] Add IP address tracking for security auditing
- [ ] Create admin endpoint to view/revoke user sessions

## Migration Notes

### Backwards Compatibility
- Existing clients without refresh token support will continue to work
- `refreshToken` field is optional in `AuthTokenResponse`
- Old access tokens remain valid until expiration

### Deployment Steps
1. Deploy API with refresh token support
2. Deploy Angular client with interceptor
3. Monitor error logs for token refresh failures
4. Gradually reduce access token lifetime as needed

## Troubleshooting

### Common Issues

**Problem**: 401 errors in an infinite loop
- **Cause**: Refresh endpoint is being intercepted
- **Solution**: Check `tokenRefreshInterceptor` excludes `/auth/refresh`

**Problem**: Users logged out after server restart
- **Cause**: In-memory cache lost
- **Solution**: Switch to distributed cache or accept as limitation

**Problem**: Refresh token not being sent
- **Cause**: Login response doesn't include refreshToken
- **Solution**: Verify API returns refreshToken in AuthTokenResponse

**Problem**: Multiple simultaneous refresh attempts
- **Cause**: Race condition with multiple 401s
- **Solution**: Implement refresh token queue/lock mechanism

## API Endpoints Summary

| Endpoint | Method | Auth | Request | Response |
|----------|--------|------|---------|----------|
| `/api/auth/login` | POST | No | `{ username, password }` | `{ accessToken, refreshToken, expiresIn }` |
| `/api/auth/select-registration` | POST | Yes | `{ regId }` | `{ accessToken, refreshToken, expiresIn }` |
| `/api/auth/refresh` | POST | No | `{ refreshToken }` | `{ accessToken, refreshToken, expiresIn }` |
| `/api/auth/revoke` | POST | No | `{ refreshToken }` | `{ message }` |
| `/api/auth/registrations` | GET | Yes | - | `{ userId, registrations[] }` |

## Files Modified

### Backend (API)
- `TSIC.Application/Services/IRefreshTokenService.cs` (NEW)
- `TSIC.Infrastructure/Services/RefreshTokenService.cs` (NEW)
- `TSIC.API/Dtos/AuthTokenResponse.cs` (MODIFIED)
- `TSIC.API/Dtos/RefreshTokenRequest.cs` (NEW)
- `TSIC.API/Controllers/AuthController.cs` (MODIFIED)
- `TSIC.API/Program.cs` (MODIFIED)
- `TSIC.API/appsettings.json` (MODIFIED)

### Frontend (Angular)
- `src/app/core/models/auth.models.ts` (MODIFIED)
- `src/app/core/services/auth.service.ts` (MODIFIED)
- `src/app/core/interceptors/token-refresh.interceptor.ts` (NEW)
- `src/app/app.config.ts` (MODIFIED)

## References

- [RFC 6749 - OAuth 2.0 Authorization Framework](https://tools.ietf.org/html/rfc6749)
- [RFC 7519 - JSON Web Token (JWT)](https://tools.ietf.org/html/rfc7519)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
