using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Extensions;

/// <summary>
/// The one caller-side entry to <see cref="IViewScheduleService.CanViewScheduleAsync"/>. Every endpoint serving
/// schedule-derived data asks this, so none can drift from the rule or read the caller differently.
/// </summary>
public static class ScheduleVisibilityExtensions
{
    /// <summary>
    /// Request header carrying a schedule-preview invite token. A header, not a query parameter, so the token
    /// never lands in server logs on the per-request API calls.
    /// </summary>
    public const string SchedulePreviewHeader = "X-Schedule-Preview";

    /// <summary>Whether this request's caller (anonymous or logged in) may see <paramref name="jobId"/>'s schedule.</summary>
    public static async Task<bool> CanViewScheduleAsync(
        this HttpContext http,
        Guid jobId,
        IJobLookupService jobLookupService,
        IViewScheduleService viewScheduleService,
        CancellationToken ct = default)
    {
        var user = http.User;
        var viewer = new ScheduleViewer
        {
            // Null when anonymous — no regId claim, no lookup.
            JobId = await user.GetJobIdFromRegistrationAsync(jobLookupService),
            RegistrationId = user.GetRegistrationId(),
            UserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            // Both mapped and unmapped role claim types: .NET 10's JsonWebTokenHandler may not remap "role".
            Role = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role"),
            PreviewToken = http.Request.Headers[SchedulePreviewHeader].FirstOrDefault()
        };
        return await viewScheduleService.CanViewScheduleAsync(jobId, viewer, ct);
    }
}
