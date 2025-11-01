# Authorization Policies - Architecture Guide

## Core Principle

**APIs under `[Authorize(Policy=xx)]` restriction should NOT require parameters that can be derived from JWT token claims.**

This architectural principle ensures:
- Security: Prevents users from tampering with parameters to access resources they shouldn't
- Simplicity: Reduces API surface area and parameter validation logic
- Consistency: Standardizes how we derive context from authenticated users

## Implementation Pattern

### ❌ Anti-Pattern (Don't Do This)
```csharp
[Authorize]
[HttpPost("clone-profile")]
public async Task<ActionResult> CloneProfile(
    string sourceProfileType, 
    Guid jobId)  // ❌ JobId can be derived from token
{
    // User could pass any jobId, not necessarily their own job
}
```

### ✅ Correct Pattern (Do This)
```csharp
[Authorize(Policy = "SuperUserOnly")]
[HttpPost("clone-profile")]
public async Task<ActionResult> CloneProfile(
    [FromBody] CloneProfileRequest request)  // ✅ Only source profile in request
{
    // Extract regId from JWT claims
    var regIdClaim = User.FindFirst("regId")?.Value;
    var regId = Guid.Parse(regIdClaim);
    
    // Get jobId from database using regId
    var registration = await _context.Registrations
        .FirstOrDefaultAsync(r => r.RegistrationId == regId);
    var jobId = registration.JobId;
    
    // Now use the jobId securely
}
```

## Available Policies

All policies are defined in `Program.cs` and use role-based claims:

### 1. **SuperUserOnly**
- **Roles:** Superuser
- **Use Case:** System-wide administrative functions
- **Examples:** Profile migration, global settings, database operations

```csharp
[Authorize(Policy = "SuperUserOnly")]
public class ProfileMigrationController : ControllerBase
```

### 2. **AdminOnly**
- **Roles:** Superuser, Director, SuperDirector
- **Use Case:** Job-level administrative functions
- **Examples:** Job configuration, user management, reports

### 3. **RefAdmin**
- **Roles:** Superuser, Director, Ref Assignor
- **Use Case:** Referee scheduling and management
- **Examples:** Assigning referees to games, managing ref payments

### 4. **StoreAdmin**
- **Roles:** Superuser, Director, Store Admin
- **Use Case:** Store and merchandise management
- **Examples:** Inventory, orders, store settings

### 5. **CanCrossCustomerJobs**
- **Roles:** Superuser, SuperDirector
- **Use Case:** Operations that span multiple customers/organizations
- **Examples:** Multi-job reports, cross-customer analytics

### 6. **TeamMembersOnly**
- **Roles:** Staff, Family, Player
- **Use Case:** Team-specific features restricted to team members
- **Examples:** Viewing team rosters, team messages, schedules

### 7. **TeamMembersAndHigher**
- **Roles:** Staff, Family, Player, Director, SuperDirector, Superuser
- **Use Case:** Features available to team members and admins
- **Examples:** Viewing game results, accessing documents

### 8. **StaffOnly**
- **Roles:** Unassigned Adult, Staff
- **Use Case:** Staff-specific functions
- **Examples:** Check-in processes, volunteer coordination

## Role Constants

Role names are defined in `TSIC.Domain.Constants.RoleConstants.Names`:

```csharp
public static class RoleConstants
{
    // Role IDs (GUIDs) - for database lookups
    public const string Superuser = "CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9";
    public const string Director = "FF4D1C27-F6DA-4745-98CC-D7E8121A5D06";
    // ... etc
    
    // Role Names - for claims and authorization policies
    public static class Names
    {
        public const string SuperuserName = "Superuser";
        public const string DirectorName = "Director";
        public const string SuperDirectorName = "SuperDirector";
        public const string RefAssignorName = "Ref Assignor";
        public const string StoreAdminName = "Store Admin";
        public const string StaffName = "Staff";
        public const string FamilyName = "Family";
        public const string PlayerName = "Player";
        public const string UnassignedAdultName = "Unassigned Adult";
        public const string ClubRepName = "Club Rep";
    }
}
```

## Common JWT Claims

Your JWT tokens should include these claims:

```json
{
  "regId": "GUID",           // Registration ID - primary user identifier
  "userId": "GUID",          // User ID
  "jobId": "GUID",           // Job ID (derived from registration)
  "jobPath": "string",       // Job path (e.g., "tsic")
  "role": "Superuser",       // Role name (matches RoleConstants.Names)
  "customerId": "GUID",      // Customer ID
  "exp": 1234567890          // Expiration timestamp
}
```

## Deriving Context from Claims

### Getting Registration ID
```csharp
var regIdClaim = User.FindFirst("regId")?.Value;
if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
{
    return BadRequest(new { error = "Invalid or missing regId claim" });
}
```

### Getting Job ID from Registration
```csharp
var registration = await _context.Registrations
    .Include(r => r.Job)
    .FirstOrDefaultAsync(r => r.RegistrationId == regId);
    
if (registration == null)
{
    return BadRequest(new { error = "Registration not found" });
}

var jobId = registration.JobId;
var job = registration.Job;
```

### Getting User Role
```csharp
var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
var isSuperuser = roleClaim == RoleConstants.Names.SuperuserName;
```

## Controller Examples

### Example 1: Profile Migration (SuperUser Only)
```csharp
[Authorize(Policy = "SuperUserOnly")]
[Route("api/admin/profile-migration")]
public class ProfileMigrationController : ControllerBase
{
    [HttpPost("clone-profile")]
    public async Task<ActionResult<CloneProfileResult>> CloneProfile(
        [FromBody] CloneProfileRequest request)
    {
        // ✅ Get regId from token
        var regId = GetRegIdFromClaims();
        
        // ✅ Service uses regId to find job and clone profile
        var result = await _migrationService.CloneProfileAsync(
            request.SourceProfileType, 
            regId);
        
        return Ok(result);
    }
}
```

### Example 2: Public Endpoints
```csharp
[AllowAnonymous]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    [HttpGet("{jobPath}")]
    public async Task<ActionResult<JobMetadata>> GetJobMetadata(string jobPath)
    {
        // Public endpoint - no authentication required
        // jobPath is in URL, not derived from token
        var job = await _jobService.GetByPathAsync(jobPath);
        return Ok(job);
    }
}
```

### Example 3: Team Member Access
```csharp
[Authorize(Policy = "TeamMembersOnly")]
[Route("api/teams")]
public class TeamsController : ControllerBase
{
    [HttpGet("my-roster")]
    public async Task<ActionResult<TeamRoster>> GetMyTeamRoster()
    {
        // ✅ Get regId from token
        var regId = GetRegIdFromClaims();
        
        // ✅ Look up user's team assignment
        var registration = await _context.Registrations
            .Include(r => r.AssignedTeam)
            .FirstOrDefaultAsync(r => r.RegistrationId == regId);
            
        if (registration?.AssignedTeam == null)
        {
            return NotFound(new { message = "No team assignment found" });
        }
        
        var roster = await _teamService.GetRosterAsync(registration.AssignedTeam.TeamId);
        return Ok(roster);
    }
}
```

## Security Benefits

1. **Prevents Elevation of Privilege**: Users can't access resources by guessing IDs
2. **Audit Trail**: Claims are verified server-side from database
3. **Simplified Client Code**: Frontend doesn't need to track and pass IDs everywhere
4. **Reduced Attack Surface**: Fewer parameters = fewer validation points = fewer bugs

## Migration Checklist

When adding authorization to existing endpoints:

- [ ] Identify what policy the endpoint needs (SuperUserOnly, AdminOnly, etc.)
- [ ] Add `[Authorize(Policy = "PolicyName")]` attribute
- [ ] Remove parameters that can be derived from JWT claims
- [ ] Extract `regId` or other claims from `User.FindFirst()`
- [ ] Look up related entities (Job, Customer, Team) from database using claims
- [ ] Update Angular services to remove unnecessary parameters
- [ ] Update API documentation/Swagger annotations
- [ ] Test with different role combinations

## Related Documentation

- [Clean Architecture Implementation](./clean-architecture-implementation.md)
- [Development Workflow](./development-workflow.md)
- [Refresh Token Implementation](../specs/refresh-token-implementation.md)
