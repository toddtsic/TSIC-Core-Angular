namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// Usage report -- User Requests by Route. SIGNED-IN requests (a login on the request, with
/// or without a registration) against the scoped live events in the window, keyed by event
/// and grouped by the API route they hit. The exact complement of Public Requests by Route.
///
/// Role names the person doing the asking, and the page's Role dropdown narrows the fetch to
/// one. Role comes from the request's registration where it carried one. Where it did not,
/// the login decides: a family account is "Family" (the player wizard runs on a Family token
/// that never carries a regId), anything else is "No registration" (adults self-registering).
///
/// Counts requests, plus the distinct PEOPLE behind them -- a person being the registration,
/// or the login when there was none -- so a route hammered by one director reads differently
/// from a route used by hundreds.
/// </summary>
public record UserRequestsByRouteDto
{
    public required int WindowDays { get; init; }

    /// <summary>Live events the numbers cover -- the resolved scope, restated for the audit stamp.</summary>
    public required int JobCount { get; init; }

    /// <summary>
    /// Every role present in the scope/window/client, BEFORE the role lens is applied -- what the
    /// Role dropdown offers. The chosen role is always among these or the lens is ignored.
    /// </summary>
    public required List<UserRequestRoleDto> Roles { get; init; }

    /// <summary>The role lens this answer was filtered to, or null for every role.</summary>
    public string? Role { get; init; }

    /// <summary>One row per (event, route) with at least one signed-in request. Unordered; the page sorts for display.</summary>
    public required List<UserRouteRowDto> Rows { get; init; }

    /// <summary>One row per route across the whole scope. Requests sum; People is distinct across events.</summary>
    public required List<UserRouteTotalDto> Totals { get; init; }

    /// <summary>Signed-in requests that succeeded (status below 400), whole scope.</summary>
    public required int TotalRequests { get; init; }

    /// <summary>Signed-in requests that failed (status 400 and up), whole scope.</summary>
    public required int FailedRequests { get; init; }

    /// <summary>Distinct people behind the succeeded requests, whole scope.</summary>
    public required int TotalPeople { get; init; }

    /// <summary>False when TSICLogs is not configured on this server -- a missing source, not zero traffic.</summary>
    public required bool UsageLoggingAvailable { get; init; }
}

public record UserRequestRoleDto
{
    public required string RoleName { get; init; }

    /// <summary>Succeeded requests under this role, whole scope.</summary>
    public required int Requests { get; init; }
}

public record UserRouteRowDto
{
    public required Guid JobId { get; init; }

    public required string JobName { get; init; }

    /// <summary>Controller/Action as logged -- the API endpoint, not the page that called it.</summary>
    public required string Route { get; init; }

    /// <summary>Signed-in requests on this route about this event that succeeded (status below 400).</summary>
    public required int Requests { get; init; }

    /// <summary>Signed-in requests on this route about this event that failed (status 400 and up).</summary>
    public required int FailedRequests { get; init; }

    /// <summary>Distinct people behind this row's succeeded requests.</summary>
    public required int People { get; init; }
}

/// <summary>A scope-wide route total for UserRequestsByRouteDto.Totals.</summary>
public record UserRouteTotalDto
{
    public required string Route { get; init; }

    public required int Requests { get; init; }

    public required int FailedRequests { get; init; }

    /// <summary>Distinct people across every event -- not the sum of the rows' People.</summary>
    public required int People { get; init; }
}

/// <summary>Repository shape: signed-in request counts per (event, route, login, registration), split by outcome.</summary>
public record UsageSignedInRouteCountDto
{
    public required Guid JobId { get; init; }

    public required string Controller { get; init; }

    public required string Action { get; init; }

    public required string UserId { get; init; }

    /// <summary>Null when the login worked without a registration (the family player wizard).</summary>
    public Guid? RegistrationId { get; init; }

    public required int Requests { get; init; }

    public required int FailedRequests { get; init; }
}
