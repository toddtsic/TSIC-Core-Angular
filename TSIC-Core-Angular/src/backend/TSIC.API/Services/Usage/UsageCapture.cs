namespace TSIC.API.Services.Usage;

/// <summary>
/// One request, captured on the hot path. Deliberately raw: this holds only values
/// already in memory when the response ends. Nothing here required a database call,
/// a parse, or an allocation beyond the strings the request already owned.
///
/// Everything that costs something -- resolving JobId from a job path, TeamId from a
/// registration, and classifying the User-Agent -- happens later in
/// <see cref="UsageWriterBackgroundService"/>, against a whole batch at once.
///
/// Positional record struct rather than the house required/init DTO shape: that rule
/// exists so OpenAPI can detect required fields, and this type is never exposed by a
/// controller. It is an internal transport between middleware and the writer.
/// </summary>
public readonly record struct UsageCapture(
    DateTime OccurredAt,
    string? JobPath,
    Guid? RegId,
    string? UserId,
    string? ClientTag,
    string? UserAgent,
    string Controller,
    string Action,
    string? QueryString,
    short StatusCode);
