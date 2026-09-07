namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// Usage report 02 -- Public Requests by Route. ANONYMOUS requests (no registration on
/// the request) against the scoped live events in the window, keyed by the event the
/// request was about and grouped by the API route it hit (Controller/Action).
///
/// Requests, not people. Nothing in the fact table can turn an anonymous request into a
/// visitor -- no session, device or address key -- so this report never claims a count
/// of anonymous users; it counts what the public asked for. Report 01 carries the people.
///
/// Successful and failed requests are split so scanners, stale links and 401s from
/// signed-out sessions never inflate the routes the public actually came for.
/// </summary>
public record PublicRequestsByRouteDto
{
    public required int WindowDays { get; init; }

    public required bool BotsExcluded { get; init; }

    /// <summary>Live events the numbers cover -- the resolved scope, restated for the audit stamp.</summary>
    public required int JobCount { get; init; }

    /// <summary>One row per (event, route) that had at least one anonymous request. Unordered; the page sorts for display.</summary>
    public required List<PublicRouteRowDto> Rows { get; init; }

    /// <summary>One row per route across the whole scope. Requests are additive, so this is the sum of Rows per route.</summary>
    public required List<PublicRouteTotalDto> Totals { get; init; }

    /// <summary>Anonymous requests that succeeded (status below 400), whole scope.</summary>
    public required int TotalRequests { get; init; }

    /// <summary>Anonymous requests that failed (status 400 and up), whole scope.</summary>
    public required int FailedRequests { get; init; }

    /// <summary>False when TSICLogs is not configured on this server -- a missing source, not zero traffic.</summary>
    public required bool UsageLoggingAvailable { get; init; }
}

public record PublicRouteRowDto
{
    public required Guid JobId { get; init; }

    public required string JobName { get; init; }

    /// <summary>Controller/Action as logged -- the API endpoint, not the page that called it.</summary>
    public required string Route { get; init; }

    /// <summary>Anonymous requests on this route about this event that succeeded (status below 400).</summary>
    public required int Requests { get; init; }

    /// <summary>Anonymous requests on this route about this event that failed (status 400 and up).</summary>
    public required int FailedRequests { get; init; }
}

/// <summary>A scope-wide route total for PublicRequestsByRouteDto.Totals.</summary>
public record PublicRouteTotalDto
{
    public required string Route { get; init; }

    public required int Requests { get; init; }

    public required int FailedRequests { get; init; }
}

/// <summary>Repository shape: anonymous request counts per (event, controller, action), split by outcome.</summary>
public record UsageRouteCountDto
{
    public required Guid JobId { get; init; }

    public required string Controller { get; init; }

    public required string Action { get; init; }

    public required int Requests { get; init; }

    public required int FailedRequests { get; init; }
}
