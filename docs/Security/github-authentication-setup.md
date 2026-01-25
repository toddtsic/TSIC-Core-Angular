# GitHub Authentication Setup

**⚠️ OBSOLETE AS OF JANUARY 4, 2026**

This document is preserved for historical reference only. The system now uses a local git submodule instead of GitHub API access.

**Date:** November 1, 2025  
**Original Purpose:** Configure GitHub Personal Access Token for fetching private repository files

---

## ⚠️ MIGRATION NOTICE

**The GitHub API integration has been replaced with a local git submodule.**

### What Changed (January 2026)

- **Old approach:** Fetched files via GitHub API (required authentication token)
- **New approach:** Reads files directly from `reference/TSIC-Unify-2024/` submodule
- **No token needed:** Files are accessed from local filesystem
- **No API limits:** No rate limiting or authentication issues

### Setup Instructions (New Approach)

The TSIC-Unify-2024 repository is now included as a git submodule:

```bash
# Already added - no action needed
git submodule update --init --recursive

# To update to latest code from production
git submodule update --remote reference/TSIC-Unify-2024
```

Files are accessed at:
- Models: `reference/TSIC-Unify-2024/TSIC-Unify-Models/`
- Views: `reference/TSIC-Unify-2024/TSIC-Unify/Views/`

---

## Historical Documentation (Obsolete)

The following content describes the old GitHub API authentication system.

## Overview

The Profile Migration system fetches POCO class source files from the private GitHub repository `toddtsic/TSIC-Unify-2024`. This requires authentication via a GitHub Personal Access Token (PAT).

## GitHub Token Creation

1. Go to GitHub Settings → Developer Settings → Personal Access Tokens → Tokens (classic)
2. Click "Generate new token (classic)"
3. Configure token:
   - **Name:** `TSIC Profile Migration - Dev`
   - **Expiration:** 90 days (recommended)
   - **Scopes:**
     - ✅ `repo` (Full control of private repositories)
       - Required to read files from private repos
4. Click "Generate token"
5. **IMPORTANT:** Copy token immediately (only shown once)

## Configuration

### appsettings.Development.json

Add GitHub configuration section:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\\SS2016;Database=TSICV5;..."
  },
  "JwtSettings": { ... },
  "GitHub": {
    "RepoOwner": "toddtsic",
    "RepoName": "TSIC-Unify-2024",
    "Token": "YOUR_GITHUB_TOKEN_HERE"
  }
}
```

### Security

**appsettings.Development.json is excluded from git commits**

Verify `.gitignore` contains:

```gitignore
# Development settings with secrets
**/appsettings.Development.json
```

This prevents accidental commits of sensitive tokens.

## Backend Implementation

### GitHubProfileFetcher.cs

The token is read from configuration and added to HTTP requests:

```csharp
public class GitHubProfileFetcher
{
    private readonly HttpClient _httpClient;
    private readonly string _token;
    
    public GitHubProfileFetcher(IConfiguration config)
    {
        _token = config["GitHub:Token"] ?? string.Empty;
        
        if (string.IsNullOrEmpty(_token))
        {
            _logger.LogWarning("GitHub token not configured - API calls may fail for private repos");
        }
    }
    
    public async Task<string> FetchSourceFileAsync(string profileType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        
        if (!string.IsNullOrEmpty(_token))
        {
            request.Headers.Authorization = 
                new AuthenticationHeaderValue("Bearer", _token);
            _logger.LogInformation("Using GitHub authentication");
        }
        
        var response = await _httpClient.SendAsync(request);
        // ...
    }
}
```

### Why Private Repos Return 404

GitHub API returns `404 Not Found` for private repositories when:
- No authentication provided
- Invalid/expired token
- Token lacks required permissions

This is **intentional** - GitHub doesn't reveal whether a private repo exists to unauthenticated requests (security by obscurity).

## Token Rotation

GitHub recommends rotating tokens every 90 days:

1. Generate new token with same scopes
2. Update `appsettings.Development.json` with new token
3. Restart API (dotnet watch will auto-reload)
4. Revoke old token in GitHub settings

## Troubleshooting

### 404 Errors Despite Token

**Symptoms:**
```
Failed to fetch file from GitHub: Response status code does not indicate success: 404 (Not Found)
```

**Causes:**
1. Token not in configuration
2. Wrong repository owner/name
3. File path incorrect
4. Token expired or revoked

**Debugging:**
- Check logs for "Using GitHub authentication" message
- Verify token in `appsettings.Development.json`
- Test token with curl:
  ```bash
  curl -H "Authorization: Bearer YOUR_TOKEN" \
    https://api.github.com/repos/toddtsic/TSIC-Unify-2024/contents/
  ```

### Token in Logs

The fetcher logs success/failure but **never logs the actual token**:

```csharp
// ✅ Safe logging
_logger.LogInformation("Using GitHub authentication");

// ❌ NEVER do this
_logger.LogInformation($"Token: {_token}");
```

## File Naming Patterns

Different profile types use different naming conventions:

### CAC Profiles (Clinical/Camp)
```
Models/Profile/CAC/CAC04ViewModels.cs  (plural "ViewModels")
Models/Profile/CAC/CAC05ViewModels.cs
Models/Profile/CAC/CAC06ViewModels.cs
```

### PP Profiles (Player Profile)
```
Models/Profile/PP/PP10ViewModel.cs     (singular "ViewModel")
Models/Profile/PP/PP17ViewModel.cs
```

The fetcher handles both patterns automatically.

## Production Deployment

### Environment Variables

For production, use environment variables instead of appsettings:

```bash
export GitHub__Token="prod_token_here"
```

ASP.NET Core configuration will read from environment variables using `__` separator.

### Azure App Service

Set in Application Settings:
- **Name:** `GitHub:Token`
- **Value:** `<your production token>`
- **Deployment Slot Setting:** ✅ (if using slots)

### Docker

Pass as environment variable:

```yaml
# docker-compose.yml
services:
  tsic-api:
    environment:
      - GitHub__Token=${GITHUB_TOKEN}
```

```bash
# .env file (excluded from git)
GITHUB_TOKEN=prod_token_here
```

## Related Documentation

- [Profile Migration Architecture](./profile-migration-angular-implementation.md)
- [Complete Deployment Methodology](./Complete-Deployment-Methodology.md)
- GitHub API Documentation: https://docs.github.com/en/rest

## Current Configuration

**Token Created:** November 1, 2025  
**Expires:** ~February 1, 2026 (90 days)  
**Scope:** Full repo access to toddtsic/TSIC-Unify-2024  
**Storage:** appsettings.Development.json (local only, excluded from git)

**REMINDER:** Token is stored locally and NOT committed to repository. Each developer must configure their own token.
