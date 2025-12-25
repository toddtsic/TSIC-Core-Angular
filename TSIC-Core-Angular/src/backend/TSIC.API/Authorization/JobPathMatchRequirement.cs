using Microsoft.AspNetCore.Authorization;

namespace TSIC.API.Authorization;

/// <summary>
/// Authorization requirement that validates the jobPath in the JWT token matches the jobPath in the request route.
/// This prevents users from accessing resources for jobs other than the one they authenticated against.
/// </summary>
public class JobPathMatchRequirement : IAuthorizationRequirement
{
}
