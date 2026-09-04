using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Controllers;
using TSIC.API.Services.Usage;

namespace TSIC.API.Middleware;

/// <summary>
/// Records one row per served API request into logs.AppUsage, via <see cref="UsageQueue"/>.
///
/// Middleware, not an action filter, for one reason: an IAsyncActionFilter's next()
/// returns BEFORE result execution, so Response.StatusCode is not final there. Reading
/// it after await _next(context) here is the only placement that records what the
/// client actually received.
///
/// Registered after UseAuthentication/UseAuthorization so HttpContext.User is populated,
/// and before MapControllers so the endpoint runs inside this call.
///
/// Nothing in here may affect the request. The capture is wrapped in a catch-all and
/// the queue write is non-blocking, so a fault in usage logging can slow nothing and
/// break nothing.
/// </summary>
public sealed class UsageLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly UsageQueue _queue;
    private readonly ILogger<UsageLoggingMiddleware> _logger;

    /// <summary>
    /// Query-string keys that may be recorded. FAIL CLOSED: anything not listed is
    /// dropped, and an empty list means QueryString is always NULL. Query strings are
    /// the one place PII reaches this table by accident -- an email in a search box, a
    /// token in a reset link -- so adding a key here is a privacy decision, not a
    /// refactor. Left deliberately empty until an allowlist is agreed.
    ///
    /// <see cref="UsageClassifier.ClientTagQueryKey"/> must NEVER be added: its content
    /// is already parsed into the AppClient/Platform/AppVersion columns, and storing it
    /// again as text would duplicate the dimensions in a free-text field. Because this
    /// is an allowlist, that is structural today -- xc cannot be stored unless someone
    /// adds it here.
    /// </summary>
    private static readonly string[] AllowedQueryKeys = [];

    /// <summary>logs.AppUsage.QueryString is NVARCHAR(400).</summary>
    private const int MaxQueryStringLength = 400;

    public UsageLoggingMiddleware(
        RequestDelegate next,
        UsageQueue queue,
        ILogger<UsageLoggingMiddleware> logger)
    {
        _next = next;
        _queue = queue;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Request START, server-local -- matches the OccurredAt column comment.
        var occurredAt = DateTime.Now;

        await _next(context);

        try
        {
            Capture(context, occurredAt);
        }
        catch (Exception ex)
        {
            // The response is already written; this cannot be surfaced to the caller,
            // and must never be rethrown into the pipeline.
            _logger.LogError(ex, "Usage capture failed; request unaffected.");
        }
    }

    private void Capture(HttpContext context, DateTime occurredAt)
    {
        // Preflights are protocol noise, not usage. Client identity rides as a query
        // parameter precisely so it adds none of them (rev 3), but authenticated
        // traffic preflights on its own account and those still stay out.
        if (HttpMethods.IsOptions(context.Request.Method)) return;

        // No route matched: 404s, static files, swagger, hubs. Nothing to attribute a
        // controller and action to, so nothing worth a fact row.
        var descriptor = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (descriptor is null) return;

        var user = context.User;

        // jobPath resolves from the route first, then the token. Public endpoints take
        // it as a route value and carry no token at all; authenticated requests carry
        // it as a claim. Checking the route first is what keeps anonymous traffic
        // attributable to a job instead of collapsing into Guid.Empty.
        var jobPath = context.Request.RouteValues.TryGetValue("jobPath", out var routeJobPath)
            ? routeJobPath as string
            : null;
        jobPath ??= user?.FindFirst("jobPath")?.Value;

        // MapInboundClaims = true remaps "sub" onto NameIdentifier. Reading "sub"
        // directly returns null and would log every signed-in request as anonymous.
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        Guid? regId = Guid.TryParse(user?.FindFirst("regId")?.Value, out var parsedRegId)
            ? parsedRegId
            : null;

        var capture = new UsageCapture(
            OccurredAt: occurredAt,
            JobPath: jobPath,
            RegId: regId,
            UserId: userId,
            ClientTag: context.Request.Query[UsageClassifier.ClientTagQueryKey].ToString() is { Length: > 0 } client ? client : null,
            UserAgent: context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
            Controller: descriptor.ControllerName,
            Action: descriptor.ActionName,
            QueryString: FilterQueryString(context.Request.Query),
            StatusCode: (short)context.Response.StatusCode);

        _queue.TryWrite(capture);
    }

    private static string? FilterQueryString(IQueryCollection query)
    {
        if (AllowedQueryKeys.Length == 0 || query.Count == 0) return null;

        var parts = new List<string>();
        foreach (var key in AllowedQueryKeys)
        {
            if (query.TryGetValue(key, out var value))
                parts.Add($"{key}={value}");
        }

        if (parts.Count == 0) return null;

        var joined = string.Join('&', parts);
        return joined.Length <= MaxQueryStringLength
            ? joined
            : joined[..MaxQueryStringLength];
    }
}
