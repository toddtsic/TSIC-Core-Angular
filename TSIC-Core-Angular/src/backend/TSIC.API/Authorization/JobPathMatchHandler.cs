using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TSIC.API.Authorization;

/// <summary>
/// Authorization handler that validates the jobPath claim in the JWT matches the jobPath in the request route.
/// 
/// Rules:
/// - No jobPath in token: Allow (Phase 1 authentication - user selecting registration)
/// - jobPath in token matches route jobPath: Allow (Phase 2 authentication - correct job)
/// - jobPath in token doesn't match route jobPath: Deny (Phase 2 - cross-job access attempt)
/// 
/// Applied automatically to all [Authorize] attributes via DefaultPolicy.
/// </summary>
public class JobPathMatchHandler : AuthorizationHandler<JobPathMatchRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<JobPathMatchHandler> _logger;

    public JobPathMatchHandler(
        IHttpContextAccessor httpContextAccessor,
        ILogger<JobPathMatchHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        JobPathMatchRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            _logger.LogWarning("JobPathMatchHandler: HttpContext is null");
            context.Fail();
            return;
        }

        // Get jobPath from JWT token claim
        var tokenJobPath = context.User.FindFirst("jobPath")?.Value;

        // Get jobPath from route (e.g., /api/jobs/{jobPath}/...)
        var routeJobPath = httpContext.GetRouteValue("jobPath")?.ToString();

        // Simple rule: If there is a jobPath token, it must match the route jobPath
        if (string.IsNullOrEmpty(tokenJobPath) || tokenJobPath.Equals(routeJobPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "JobPathMatchHandler: User {Username} authorized for {Path}",
                context.User.Identity?.Name,
                httpContext.Request.Path);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning(
                "JobPathMatchHandler: User {Username} with token jobPath '{TokenJobPath}' attempted to access route jobPath '{RouteJobPath}'",
                context.User.Identity?.Name,
                tokenJobPath,
                routeJobPath);

            // Set ProblemDetails response following API standard
            httpContext.Response.StatusCode = 403;
            httpContext.Response.ContentType = "application/problem+json";
            await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 403,
                Type = "JobPathMismatch",
                Title = "Job Path Access Denied",
                Detail = $"You are logged into '{tokenJobPath}' but attempted to access '{routeJobPath}'. Please logout first before moving to '{routeJobPath}'.",
                Extensions =
                {
                    ["tokenJobPath"] = tokenJobPath,
                    ["routeJobPath"] = routeJobPath
                }
            });

            context.Fail();
        }
    }
}
