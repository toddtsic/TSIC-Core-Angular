# Testing Password Bypass Feature

## Quick Test Guide

### Prerequisites
1. API running in Development mode (`ASPNETCORE_ENVIRONMENT=Development`)
2. Configuration exists in `appsettings.Development.json`:
   ```json
   "DevMode": {
     "AllowPasswordBypass": true,
     "BypassPassword": "dev123"
   }
   ```

### Test Team Registration (Club Rep Login)

1. **Start the application**:
   ```powershell
   # Terminal 1: Start API
   dotnet run --project TSIC-Core-Angular/src/backend/TSIC.API/TSIC.API.csproj
   
   # Terminal 2: Start Angular
   cd TSIC-Core-Angular/src/frontend/tsic-app
   npm start
   ```

2. **Navigate to team registration**:
   - Open browser: `http://localhost:4200/aim-cac-2026/register-team`
   - Should see "Club Rep Login" step

3. **Test bypass**:
   - Username: Any club rep username from database (e.g., `clubrep1`, `testclubadmin`, etc.)
   - Password: `dev123` ← **This is the bypass password**
   - Click "Continue"
   - ✅ Should successfully login and proceed to "Register Teams" step

### Test Player Registration (Family User Login)

1. **Navigate to player registration**:
   - Open browser: `http://localhost:4200/aim-cac-2026/register-player`
   - Should see "Family account?" step

2. **Select existing account**:
   - Choose radio button: "Yes, I already have one"
   - Login form should appear inline

3. **Test bypass**:
   - Username: Any family user username from database (e.g., `smithfamily`, `johnsonmom`, etc.)
   - Password: `dev123` ← **This is the bypass password**
   - Click "Sign In & Proceed"
   - ✅ Should successfully login and proceed to "Players" step

### Finding Test Usernames

#### Using SQL Server Management Studio (SSMS)
```sql
-- Find club rep users
SELECT TOP 10 u.UserName, u.Email, r.Name as RoleName
FROM AspNetUsers u
INNER JOIN AspNetUserRoles ur ON u.Id = ur.UserId
INNER JOIN AspNetRoles r ON ur.RoleId = r.Id
WHERE r.Name IN ('Director', 'SuperDirector', 'Superuser')
ORDER BY u.UserName;

-- Find family users
SELECT TOP 10 u.UserName, u.Email, r.Name as RoleName
FROM AspNetUsers u
INNER JOIN AspNetUserRoles ur ON u.Id = ur.UserId
INNER JOIN AspNetRoles r ON ur.RoleId = r.Id
WHERE r.Name = 'Family'
ORDER BY u.UserName;
```

#### Using API Browser (after logging in as SuperUser)
- Navigate to user management endpoints
- Browse existing users

### Expected Behavior

**✅ Development Mode (ASPNETCORE_ENVIRONMENT=Development)**:
- Any valid username + `dev123` password → Login succeeds
- Invalid username + `dev123` → Still fails (user must exist)
- Valid username + wrong password (not `dev123`) → Falls back to normal password validation

**✅ Production Mode**:
- Bypass feature is **completely disabled**
- Only actual passwords work
- `dev123` will fail unless it's the actual user's password

### Troubleshooting

#### Issue: Bypass not working
1. **Check environment**:
   ```powershell
   # In API terminal, should see: "Now listening on: http://localhost:5022"
   # Check environment variable:
   $env:ASPNETCORE_ENVIRONMENT  # Should be "Development"
   ```

2. **Check configuration**:
   - Open `TSIC-Core-Angular/src/backend/TSIC.API/appsettings.Development.json`
   - Verify `DevMode.AllowPasswordBypass` is `true`
   - Verify `DevMode.BypassPassword` is `"dev123"`

3. **Check API logs**:
   - API console should show authentication attempts
   - Look for errors or warnings

#### Issue: Username not found
- Verify username exists in database (case-sensitive)
- Check SQL query results above

#### Issue: Still prompts for password after login
- Check browser console for JavaScript errors
- Verify JWT token is being stored in localStorage
- Check AuthService is correctly handling response

### Security Notes

- ⚠️ **Development only**: Feature requires `IWebHostEnvironment.IsDevelopment()`
- ⚠️ **No production risk**: Impossible to activate in production
- ⚠️ **No password changes**: Only bypasses validation, doesn't modify stored passwords
- ⚠️ **User must exist**: Can't create fake users, only login as existing ones

### Related Documentation

- [dev-mode-password-bypass.md](dev-mode-password-bypass.md) - Full feature documentation
- [auth-guard-documentation.md](auth-guard-documentation.md) - Authentication flow details
- [development-workflow.md](development-workflow.md) - General development setup
