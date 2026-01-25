# GitHub Profile Migration - Rate Limit Fix

**Issue**: `API rate limit exceeded for 69.254.192.62` when fetching profiles from GitHub

**Root Cause**: GitHub authentication token not configured (blank in appsettings.json)

**Status**: ✅ **FIXED** - See solution below

---

## Problem Analysis

### What Was Happening
1. `appsettings.json` had empty `"GitHub": { "Token": "" }`
2. `appsettings.Development.json` was missing (excluded from git)
3. GitHubProfileFetcher fell back to **unauthenticated requests**
4. GitHub API rate limit for unauthenticated IP: **60 requests/hour**
5. After 52 profiles, hit rate limit → `Forbidden` error

### Authentication Status Check

**Unauthenticated Request**:
```
GET https://api.github.com/repos/toddtsic/TSIC-Unify-2024/contents/...
Rate Limit: 60 requests/hour per IP
```

**Authenticated Request**:
```
GET https://api.github.com/repos/toddtsic/TSIC-Unify-2024/contents/...
Authorization: Bearer YOUR_GITHUB_TOKEN
Rate Limit: 5,000 requests/hour
```

**Impact**: 83x more requests allowed with authentication!

---

## Solution

### Step 1: Create GitHub Personal Access Token

1. Visit: https://github.com/settings/tokens?type=classic
2. Click **"Generate new token (classic)"**
3. Configure:
   - **Token name**: `TSIC Profile Migration Dev`
   - **Expiration**: 90 days (recommended)
   - **Scope**: ✅ `repo` (read access to private repos)
4. Click **"Generate token"**
5. **Copy the token immediately** (only shown once)

**Token Format**: Starts with `ghp_` followed by alphanumeric characters

### Step 2: Add Token to appsettings.Development.json

**File**: `TSIC-Core-Angular/src/backend/TSIC.API/appsettings.Development.json`

```json
{
  "GitHub": {
    "RepoOwner": "toddtsic",
    "RepoName": "TSIC-Unify-2024",
    "RepoBranch": "master2025",
    "Token": "ghp_YOUR_ACTUAL_TOKEN_HERE"
  }
}
```

**IMPORTANT**: 
- ✅ File is automatically excluded from git (see `.gitignore`)
- ✅ Each developer creates their own token
- ✅ Token never committed to repository

### Step 3: Verify Configuration

Check that `appsettings.Development.json` exists and contains your token:

```powershell
# PowerShell
Test-Path "TSIC-Core-Angular/src/backend/TSIC.API/appsettings.Development.json"
# Should output: True
```

### Step 4: Restart API

The application reads configuration at startup. Restart the API:

```powershell
# Stop running API (Ctrl+C in terminal)
# Then restart
dotnet run --project TSIC-Core-Angular/src/backend/TSIC.API/TSIC.API.csproj
```

**Or use VS Code task**: `dotnet: run (API)`

### Step 5: Verify Authentication

Check the API startup logs:

```
info: TSIC.API.Services.Metadata.GitHubProfileFetcher[0]
      GitHub authentication configured
```

**If you see**:
- ✅ `"GitHub authentication configured"` → Token is set correctly
- ❌ `"GitHub Token not configured"` → Token is blank/missing

---

## Testing

### Test Profile Migration

```powershell
# Navigate to project root
cd TSIC-Core-Angular

# Run a profile migration (e.g., PP52)
curl -X POST http://localhost:5000/api/profile-migration/migrate/PP52 \
  -H "Authorization: Bearer <your_jwt_token>"
```

### Expected Success Response

```json
{
  "profileType": "PP52",
  "success": true,
  "jobId": "00000000-0000-0000-0000-000000000000",
  "migratedFields": 25
}
```

### Rate Limit Status

Check remaining requests in response headers:

```
X-RateLimit-Limit: 5000
X-RateLimit-Remaining: 4998
X-RateLimit-Reset: 1704096000
```

---

## Configuration Files Reference

### appsettings.json (Committed to Git)
```json
{
  "GitHub": {
    "RepoOwner": "toddtsic",
    "RepoName": "TSIC-Unify-2024",
    "Token": ""  // ← Empty in committed version
  }
}
```

**Purpose**: Template for all environments
**Security**: Token is EMPTY (intentional)

### appsettings.Development.json (Local Only, Excluded from Git)
```json
{
  "GitHub": {
    "Token": "ghp_YOUR_ACTUAL_TOKEN_HERE"  // ← Filled in locally
  }
}
```

**Purpose**: Development-specific secrets
**Security**: `.gitignore` prevents accidental commits
**Scope**: Used when `ASPNETCORE_ENVIRONMENT=Development` (default in VS Code)

### appsettings.Production.json (If Needed)
```json
{
  "GitHub": {
    "Token": ""  // ← Use environment variable instead
  }
}
```

**Security**: Secrets via environment variables, never in config files

---

## Environment-Specific Configuration

### How ASP.NET Core Loads Configuration

```csharp
// Program.cs loads in order (later overrides earlier):
1. appsettings.json          ← Base configuration
2. appsettings.{Environment}.json  ← Environment override
3. Environment variables     ← Highest priority
4. User secrets (dev only)   ← Development-only secrets
```

**Current Environment**:
```csharp
// appsettings.Development.json is loaded when:
ASPNETCORE_ENVIRONMENT=Development  // Default in debug/VS Code
```

---

## Troubleshooting

### ❌ Error: "GitHub Token not configured"

**Cause**: `appsettings.Development.json` missing or token is empty

**Fix**:
1. Verify file exists: `TSIC-Core-Angular/src/backend/TSIC.API/appsettings.Development.json`
2. Verify token is not empty in the file
3. Restart API after creating/updating file

### ❌ Error: "API rate limit exceeded"

**Cause**: Still using unauthenticated requests

**Fix**:
1. Check logs for `"GitHub Token not configured"` warning
2. Verify token in `appsettings.Development.json`
3. Ensure API restarted AFTER updating config
4. Check token hasn't expired (90-day rotation)

### ❌ Error: "401 Unauthorized"

**Cause**: Token is invalid or expired

**Fix**:
1. Generate new token from https://github.com/settings/tokens
2. Update `appsettings.Development.json`
3. Restart API

### ✅ Verify Token Works

Test directly with curl:

```powershell
$token = "ghp_YOUR_TOKEN_HERE"

curl -H "Authorization: Bearer $token" \
  https://api.github.com/user
```

Should return your GitHub user info (not `401` or `403`).

---

## Security Best Practices

### ✅ DO
- ✅ Create **development-specific token** (not production token)
- ✅ Store in `appsettings.Development.json` (local only)
- ✅ Use **90-day expiration** and rotate regularly
- ✅ Grant **minimal required scope** (`repo` for private repos)
- ✅ Add `appsettings.Development.json` to `.gitignore`
- ✅ Log successful authentication (without logging token)

### ❌ DON'T
- ❌ Commit `appsettings.Development.json` to git
- ❌ Log the actual token value
- ❌ Share token in chat/email/tickets
- ❌ Use production token in development
- ❌ Commit token to public repositories
- ❌ Store token in comments or documentation

### Token Rotation Schedule
- **Created**: January 1, 2026
- **Expires**: ~April 1, 2026 (90 days)
- **Renew By**: March 15, 2026 (2-week buffer)

---

## Rate Limit Summary

| Scenario | Limit | Resets Every |
|----------|-------|--------------|
| **Unauthenticated IP** | 60/hour | 1 hour |
| **Authenticated User** | 5,000/hour | 1 hour |
| **Profile Migration Batch** | ~100 profiles | N/A |

**With Token**: Can migrate 50x more profiles per hour ⭐

---

## Code Reference

### GitHubProfileFetcher.cs - Constructor

```csharp
public GitHubProfileFetcher(
    HttpClient httpClient,
    IMemoryCache cache,
    ILogger<GitHubProfileFetcher> logger,
    IConfiguration configuration)
{
    _httpClient = httpClient;
    _cache = cache;
    _logger = logger;
    _configuration = configuration;

    // Add auth token if configured
    var githubToken = _configuration["GitHub:Token"];
    if (!string.IsNullOrEmpty(githubToken))
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", githubToken);
        _logger.LogInformation("GitHub authentication configured");
    }
    else
    {
        _logger.LogWarning("GitHub Token not configured - can only access public repositories");
    }
}
```

**How It Works**:
1. Read token from configuration
2. If token is non-empty, add `Authorization: Bearer {token}` header
3. Log success/warning
4. All subsequent API requests use the authenticated header

---

## Next Steps

1. ✅ Create GitHub Personal Access Token
2. ✅ Update `appsettings.Development.json` with token
3. ✅ Restart API
4. ✅ Verify logs show `"GitHub authentication configured"`
5. ✅ Re-run profile migrations (PP52+)

---

## Related Documentation

- [GitHub API Rate Limiting](https://docs.github.com/en/rest/overview/resources-in-the-rest-api#rate-limiting)
- [GitHub Personal Access Tokens](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token)
- [github-authentication-setup.md](./github-authentication-setup.md)
- [Profile Migration Architecture](./profile-migration-angular-implementation.md)

---

**Status**: RESOLVED ✅  
**Date Fixed**: January 1, 2026  
**Root Cause**: Missing GitHub authentication token configuration  
**Solution**: Create token + update appsettings.Development.json
