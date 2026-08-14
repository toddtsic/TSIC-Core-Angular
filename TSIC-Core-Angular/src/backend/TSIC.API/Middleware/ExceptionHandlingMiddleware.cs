using Microsoft.AspNetCore.Mvc;

namespace TSIC.API.Middleware;

/// <summary>
/// The app's only global exception boundary. Two jobs, and the first one is the subtle one.
///
/// <para><b>1. Client aborts are not faults.</b> When a visitor closes the tab mid-load, IIS trips
/// <c>HttpContext.RequestAborted</c>; MVC binds every action's <c>CancellationToken</c> parameter to
/// that same token, so it rides down through the services into EF and out to
/// <c>SqlCommand.ExecuteReaderAsync</c>. The command dies, the exception escapes, and with no
/// boundary here it reached <c>IISHttpServer</c> — which logged <c>ApplicationError</c> at Error and
/// left a 500 in Seq for a request whose user had already left. Nine of those in one day on
/// <c>/pulse</c> + <c>/bulletins/job/{jobPath}</c> (they share <c>GetJobPulseAsync</c>, so one abort
/// logs two 500s) is what prompted this class.</para>
///
/// <para><b>Classify on the TOKEN, never on the exception type.</b> The type depends on WHEN the
/// cancel landed: before the command reaches the wire SqlClient throws
/// <c>OperationCanceledException</c>, but once it is in flight SqlClient must send a TDS attention
/// packet and read the server's acknowledgement (otherwise the pooled connection is poisoned) — and
/// that surfaces as a <c>SqlException</c> reading "A severe error occurred on the current command.
/// The results, if any, should be discarded. Operation cancelled by user." Both happen here, in the
/// same endpoint, depending on timing. So the obvious <c>catch (OperationCanceledException)</c>
/// catches roughly half of them and leaves the rest looking like genuine database faults — worse
/// than no handler at all, because it lends the survivors false credibility. Hence the
/// <c>when (RequestAborted.IsCancellationRequested)</c> filter, which is exact regardless of type.
/// Do not "simplify" it back to a type check.</para>
///
/// <para>Not to be confused with a command timeout, which is <c>SqlException</c> number -2
/// ("Execution Timeout Expired") and does NOT trip RequestAborted — that one is a real fault and
/// falls through to the handler below, as it should.</para>
///
/// <para><b>2. Real faults get a body.</b> Everything else becomes RFC7807 ProblemDetails carrying
/// <c>TraceIdentifier</c>, so a user's "it broke" can be joined to a Seq entry. The frontend already
/// reads this shape (<c>extractHttpErrorMessage</c>: detail → title → message), so no client change
/// was needed; before this, a 500 had an empty body and every user saw the generic fallback.</para>
///
/// <para><b>Never put <c>ex.Message</c> in the response.</b> The exception text that motivated this
/// class contained SQL Server internals. The detail is a fixed string plus the trace id; the message
/// goes to the log, where it belongs.</para>
///
/// <para><b>Registration order matters twice.</b> This must be the OUTERMOST middleware — the
/// <c>IISHttpServer</c> log fires outside the pipeline, so the only way to prevent it is to ensure
/// the exception never gets there. And it is not sufficient on its own: Serilog's request-logging
/// middleware sits INSIDE this one and logs-then-rethrows, so it has already written its Error/500
/// line before this catch runs. The companion <c>GetLevel</c> override on
/// <c>UseSerilogRequestLogging</c> is what silences that half. Change one, revisit the other.</para>
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    /// <summary>
    /// nginx's "Client Closed Request". Non-standard, never delivered (the client is gone by
    /// definition) — it exists so the access log distinguishes an abandoned request from a fault.
    /// </summary>
    private const int StatusClientClosedRequest = 499;

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (context.RequestAborted.IsCancellationRequested)
        {
            // Debug, not Information: this is noise by definition, and the configured
            // MinimumLevel (Information) drops it before it reaches Seq. Lower the level
            // temporarily if you ever need to count aborts.
            _logger.LogDebug(
                ex,
                "Request aborted by client: {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusClientClosedRequest;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception for {Method} {Path} (trace {TraceId})",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);

            // Headers are already on the wire — nothing can be rewritten, and swallowing here
            // would hand the client a truncated 200. Let it escape to the server, which will
            // reset the connection.
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Detail = $"The request could not be completed. Reference: {context.TraceIdentifier}",
                    Instance = context.Request.Path
                },
                context.RequestAborted);
        }
    }
}
