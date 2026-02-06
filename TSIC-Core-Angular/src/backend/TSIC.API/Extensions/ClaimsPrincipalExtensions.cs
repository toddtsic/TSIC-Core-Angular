using System.Security.Claims;
using TSIC.API.Services.Shared.Jobs;

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
