using System.Security.Claims;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Services;

namespace TSIC.API.Extensions;

/// <summary>
/// Extension methods for extracting secure claims from ClaimsPrincipal.
/// These methods derive contextual data from JWT claims to prevent parameter tampering.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extracts the JobId from the user's regId claim by querying the Registrations table.
    /// This is the most secure way to derive job context since regId is immutable.
    /// </summary>
    /// <param name="user">The ClaimsPrincipal (typically from Controller.User)</param>
    /// <param name="jobLookupService">The job lookup service</param>
    /// <returns>JobId if found, null if regId claim missing or registration not found</returns>
    public static async Task<Guid?> GetJobIdFromRegistrationAsync(
        this ClaimsPrincipal user,
        IJobLookupService jobLookupService)
    {
        var regId = user.GetRegistrationId();
        if (regId == null)
        {
            return null;
        }

        return await jobLookupService.GetJobIdByRegistrationAsync(regId.Value);
    }

    /// <summary>
    /// Whether this caller (anonymous or logged in) may see <paramref name="jobId"/>'s schedule —
    /// <see cref="IViewScheduleService.CanViewScheduleAsync"/> applied to the caller's own job and role.
    /// Every endpoint serving schedule-derived data asks this, so none can drift from the rule.
    /// </summary>
    public static async Task<bool> CanViewScheduleAsync(
        this ClaimsPrincipal user,
        Guid jobId,
        IJobLookupService jobLookupService,
        IViewScheduleService viewScheduleService,
        CancellationToken ct = default)
    {
        // Null when anonymous — no regId claim, no lookup.
        var callerJobId = await user.GetJobIdFromRegistrationAsync(jobLookupService);
        // Both mapped and unmapped role claim types: .NET 10's JsonWebTokenHandler may not remap "role".
        var callerRole = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role");
        return await viewScheduleService.CanViewScheduleAsync(jobId, callerJobId, callerRole, ct);
    }

    /// <summary>
    /// Team the caller is rostered on, derived from the immutable regId claim -- never from the
    /// route. Used to confine Player and Staff to their own team on the team-authoring routes.
    /// </summary>
    public static async Task<Guid?> GetTeamIdFromRegistrationAsync(
        this ClaimsPrincipal user,
        IJobLookupService jobLookupService,
        CancellationToken ct = default)
    {
        var regId = user.GetRegistrationId();
        if (regId == null)
        {
            return null;
        }

        return await jobLookupService.GetTeamIdByRegistrationAsync(regId.Value, ct);
    }

    /// <summary>
    /// Extracts the RegistrationId (regId) from JWT claims.
    /// </summary>
    /// <param name="user">The ClaimsPrincipal</param>
    /// <returns>RegistrationId if found and valid, null otherwise</returns>
    public static Guid? GetRegistrationId(this ClaimsPrincipal user)
    {
        var regIdClaim = user.FindFirst("regId")?.Value;
        if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
        {
            return null;
        }
        return regId;
    }

    /// <summary>
    /// Extracts the jobPath from JWT claims.
    /// </summary>
    /// <param name="user">The ClaimsPrincipal</param>
    /// <returns>JobPath string if found, null otherwise</returns>
    public static string? GetJobPath(this ClaimsPrincipal user)
    {
        return user.FindFirst("jobPath")?.Value;
    }
}
