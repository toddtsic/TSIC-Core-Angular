namespace TSIC.API.Services.Usage;

/// <summary>
/// How the requests-by-route reports name and sort API routes. Code-owned on purpose: which
/// calls are page shell is a fact about the Angular app, read from its source, not data.
///
/// PAGE SHELL = calls the app makes because someone arrived in an event, logged in, switched
/// role or refreshed a token -- on whatever page they landed on -- never because they chose a
/// screen. They are counted apart (one total) and left out of the routes, where they would
/// otherwise top every chart and bury the screens people actually used. Traced 2026-09-16:
///   Jobs/GetJobPulse         client-header-bar: on every job or user change
///   Jobs/GetJobMetadata      client layout: on entering a job (the landing and dashboard pages
///                            also ask; the log cannot tell those apart, arrival is the bulk)
///   Nav/GetMergedNav         client layout: job or user change, once a role is chosen
///   ReferenceData/GetStates  app initializer: once per app start
///   Auth/RefreshToken        auth timer / interceptor
///   Auth/RevokeToken         logout
///
/// NOT shell: Bulletins/GetJobBulletins is requested only by the job landing (home) page and the
/// dashboard, so it is the log's nearest thing to a home-page view and stays, under a name that
/// says so. Login and role-picker calls are things people chose to do and stay as routes.
/// </summary>
public static class UsageRoutes
{
    private static readonly HashSet<string> Shell = new(StringComparer.OrdinalIgnoreCase)
    {
        "Jobs/GetJobPulse",
        "Jobs/GetJobMetadata",
        "Nav/GetMergedNav",
        "ReferenceData/GetStates",
        "Auth/RefreshToken",
        "Auth/RevokeToken",
    };

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bulletins/GetJobBulletins"] = "Home page (bulletins)",
    };

    /// <summary>The logged Controller/Action.</summary>
    public static string Key(string controller, string action) => controller + "/" + action;

    /// <summary>True when the route is page shell: counted apart, never shown as a route.</summary>
    public static bool IsShell(string routeKey) => Shell.Contains(routeKey);

    /// <summary>The name a report shows: a plain-language name where one is true, else Controller/Action.</summary>
    public static string DisplayName(string routeKey) =>
        DisplayNames.TryGetValue(routeKey, out var name) ? name : routeKey;
}
