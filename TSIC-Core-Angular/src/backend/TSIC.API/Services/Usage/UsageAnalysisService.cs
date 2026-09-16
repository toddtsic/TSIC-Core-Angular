using TSIC.Contracts.Dtos.Usage;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Usage;

/// <summary>
/// The usage-analysis reports. Each takes an already-resolved scope (the controller ran
/// IUsageScopeResolver, event lens included) and answers one question from TSICLogs,
/// pairing with TSICV5 in memory where a name or role is needed. Nothing here joins the
/// two databases in SQL.
///
/// <paramref name="appClientId"/> on a report is the page's client lens: null = every
/// client. It is a plain filter on the fact rows, never a grouping.
/// </summary>
public interface IUsageAnalysisService
{
    /// <summary>
    /// Which clients have rows in the scope/window -- what the client lens may offer. With a
    /// <paramref name="bucket"/> the window is that bucket's span (report 03), so the facet
    /// covers exactly what the bucketed report covers.
    /// </summary>
    Task<UsageClientsDto> GetClientsAsync(
        UsageScopeResolution scope,
        int windowDays,
        UsageBucket? bucket,
        CancellationToken ct = default);

    Task<UsersByRoleDto> GetUsersByRoleAsync(
        UsageScopeResolution scope,
        int windowDays,
        int? appClientId,
        CancellationToken ct = default);

    /// <summary>Report 02: anonymous requests per event per API route, split by outcome. Requests, never people.</summary>
    Task<PublicRequestsByRouteDto> GetPublicRequestsByRouteAsync(
        UsageScopeResolution scope,
        int windowDays,
        int? appClientId,
        CancellationToken ct = default);

    /// <summary>
    /// Signed-in requests per event per API route, split by outcome, with the distinct people
    /// behind them. <paramref name="role"/> narrows to one role name (null = every role); the
    /// roles offered are computed before it is applied.
    /// </summary>
    Task<UserRequestsByRouteDto> GetUserRequestsByRouteAsync(
        UsageScopeResolution scope,
        int windowDays,
        int? appClientId,
        string? role,
        CancellationToken ct = default);

    /// <summary>
    /// Report 03: report 01's count once per bucket over the bucket's span. A registration
    /// counts in every bucket it was active in. <paramref name="scope"/> must have been
    /// resolved live-as-of <paramref name="since"/>; an event counts only in the buckets it
    /// was live in.
    /// </summary>
    Task<UsersByRoleOverTimeDto> GetUsersByRoleOverTimeAsync(
        UsageScopeResolution scope,
        UsageBucket bucket,
        DateTime since,
        int? appClientId,
        CancellationToken ct = default);
}

public sealed class UsageAnalysisService : IUsageAnalysisService
{
    /// <summary>Id-list lookups go to TSICV5 in slices so a platform-wide month never ships one giant parameter.</summary>
    private const int LookupBatchSize = 2000;

    private static readonly HashSet<string> AdminRoleIds =
        new(RoleConstants.AdminRoleIds, StringComparer.OrdinalIgnoreCase);

    /// <summary>A signed-in request with no registration from a family account: the player wizard's token role.</summary>
    public const string FamilyRoleName = "Family";

    /// <summary>A signed-in request with no registration from any other login -- adults self-registering.</summary>
    public const string NoRegistrationRoleName = "No registration";

    private readonly IUsageStatsRepository _usageRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IFamilyRepository _familyRepo;

    public UsageAnalysisService(
        IUsageStatsRepository usageRepo,
        IRegistrationRepository registrationRepo,
        IFamilyRepository familyRepo)
    {
        _usageRepo = usageRepo;
        _registrationRepo = registrationRepo;
        _familyRepo = familyRepo;
    }

    public async Task<UsageClientsDto> GetClientsAsync(
        UsageScopeResolution scope,
        int windowDays,
        UsageBucket? bucket,
        CancellationToken ct = default)
    {
        // A bucketed report's window is its span, so the facet is stamped with the days that span actually covers.
        var since = bucket is UsageBucket b ? UsageBuckets.SinceFor(b, DateTime.Now) : Since(windowDays);
        var days = bucket is null ? windowDays : (int)Math.Ceiling((DateTime.Now - since).TotalDays);

        if (!_usageRepo.IsAvailable)
        {
            return new UsageClientsDto
            {
                WindowDays = days,
                JobCount = scope.Jobs.Count,
                Clients = [],
                UsageLoggingAvailable = false,
            };
        }

        var clients = await _usageRepo.GetClientsPresentAsync(scope.GetJobIds(), since, ct);
        return new UsageClientsDto
        {
            WindowDays = days,
            JobCount = scope.Jobs.Count,
            Clients = clients.OrderByDescending(c => c.Requests).ThenBy(c => c.AppClientName).ToList(),
            UsageLoggingAvailable = true,
        };
    }

    public async Task<UsersByRoleDto> GetUsersByRoleAsync(
        UsageScopeResolution scope,
        int windowDays,
        int? appClientId,
        CancellationToken ct = default)
    {
        if (!_usageRepo.IsAvailable)
            return Empty(scope, windowDays, available: false);

        // Step 1 (TSICLogs): who, about which event -- distinct (event, registration) pairs.
        var pairs = await _usageRepo.GetDistinctRegistrationsByJobAsync(
            scope.GetJobIds(), Since(windowDays), appClientId, ct);
        if (pairs.Count == 0)
            return Empty(scope, windowDays, available: true);

        // Step 2 (TSICV5): which role each registration holds.
        var roleByReg = await LookupRolesAsync(pairs.Select(p => p.RegistrationId), ct);

        var jobNames = scope.Jobs.ToDictionary(j => j.JobId, j => j.JobName);

        var joined = pairs
            .Where(p => roleByReg.ContainsKey(p.RegistrationId))
            .Select(p => new
            {
                p.JobId,
                p.RegistrationId,
                Role = roleByReg[p.RegistrationId],
                IsAdmin = AdminRoleIds.Contains(roleByReg[p.RegistrationId].RoleId),
            })
            .ToList();

        var rows = joined
            .GroupBy(x => new { x.JobId, x.Role.RoleId, x.Role.RoleName, x.IsAdmin })
            .Select(g => new UsersByRoleRowDto
            {
                JobId = g.Key.JobId,
                JobName = jobNames.TryGetValue(g.Key.JobId, out var name) ? name : string.Empty,
                RoleName = g.Key.RoleName,
                Users = g.Select(x => x.RegistrationId).Distinct().Count(),
                IsAdmin = g.Key.IsAdmin,
            })
            .ToList();

        // Scope-wide, per role, distinct people -- the "All events" cluster. Not a sum of rows.
        var totals = joined
            .GroupBy(x => new { x.Role.RoleId, x.Role.RoleName, x.IsAdmin })
            .Select(g => new UsersByRoleTotalDto
            {
                RoleName = g.Key.RoleName,
                Users = g.Select(x => x.RegistrationId).Distinct().Count(),
                IsAdmin = g.Key.IsAdmin,
            })
            .ToList();

        return new UsersByRoleDto
        {
            WindowDays = windowDays,
            JobCount = scope.Jobs.Count,
            Rows = rows,
            Totals = totals,
            // Distinct people, not a sum of per-event rows.
            CustomerUsers = joined.Where(x => !x.IsAdmin).Select(x => x.RegistrationId).Distinct().Count(),
            AdminUsers = joined.Where(x => x.IsAdmin).Select(x => x.RegistrationId).Distinct().Count(),
            UsageLoggingAvailable = true,
        };
    }

    public async Task<PublicRequestsByRouteDto> GetPublicRequestsByRouteAsync(
        UsageScopeResolution scope,
        int windowDays,
        int? appClientId,
        CancellationToken ct = default)
    {
        var counts = _usageRepo.IsAvailable
            ? await _usageRepo.GetAnonymousRequestsByRouteAsync(scope.GetJobIds(), Since(windowDays), appClientId, ct)
            : [];

        var jobNames = scope.Jobs.ToDictionary(j => j.JobId, j => j.JobName);

        // Page shell is counted apart and never becomes a route (UsageRoutes).
        var shellRequests = counts.Where(c => UsageRoutes.IsShell(UsageRoutes.Key(c.Controller, c.Action))).Sum(c => c.Requests);

        var rows = counts
            .Where(c => !UsageRoutes.IsShell(UsageRoutes.Key(c.Controller, c.Action)))
            .Select(c => new PublicRouteRowDto
            {
                JobId = c.JobId,
                JobName = jobNames.TryGetValue(c.JobId, out var name) ? name : string.Empty,
                Route = UsageRoutes.DisplayName(UsageRoutes.Key(c.Controller, c.Action)),
                Requests = c.Requests,
                FailedRequests = c.FailedRequests,
            })
            .ToList();

        // Requests are additive: the scope total for a route is the sum of its event rows.
        var totals = rows
            .GroupBy(r => r.Route)
            .Select(g => new PublicRouteTotalDto
            {
                Route = g.Key,
                Requests = g.Sum(r => r.Requests),
                FailedRequests = g.Sum(r => r.FailedRequests),
            })
            .ToList();

        return new PublicRequestsByRouteDto
        {
            WindowDays = windowDays,
            JobCount = scope.Jobs.Count,
            Rows = rows,
            Totals = totals,
            TotalRequests = rows.Sum(r => r.Requests),
            FailedRequests = rows.Sum(r => r.FailedRequests),
            ShellRequests = shellRequests,
            UsageLoggingAvailable = _usageRepo.IsAvailable,
        };
    }

    public async Task<UserRequestsByRouteDto> GetUserRequestsByRouteAsync(
        UsageScopeResolution scope,
        int windowDays,
        int? appClientId,
        string? role,
        CancellationToken ct = default)
    {
        // Step 1 (TSICLogs): signed-in counts per (event, route, login, registration).
        var counts = _usageRepo.IsAvailable
            ? await _usageRepo.GetSignedInRequestsByRouteAsync(scope.GetJobIds(), Since(windowDays), appClientId, ct)
            : [];

        // Step 2 (TSICV5): name each row's role. A registration carries its own; a login
        // without one is Family when it is a family account, else No registration.
        var roleByReg = await LookupRolesAsync(counts.Where(c => c.RegistrationId is not null).Select(c => c.RegistrationId!.Value), ct);
        var familyLogins = await LookupFamilyLoginsAsync(counts.Where(c => c.RegistrationId is null).Select(c => c.UserId), ct);

        var jobNames = scope.Jobs.ToDictionary(j => j.JobId, j => j.JobName);

        var named = counts
            .Select(c => new
            {
                c.JobId,
                Route = UsageRoutes.Key(c.Controller, c.Action),
                // A person is the registration, or the login when there was none. Registration
                // is per job, the same unit report 01 counts.
                // Prefixed: login ids are GUID strings too, and the two keys must never meet.
                Person = c.RegistrationId is Guid personReg ? "r:" + personReg : "u:" + c.UserId,
                RoleName = c.RegistrationId is Guid regId
                    ? (roleByReg.TryGetValue(regId, out var r) ? r.RoleName : null)
                    : (familyLogins.Contains(c.UserId) ? FamilyRoleName : NoRegistrationRoleName),
                c.Requests,
                c.FailedRequests,
            })
            // A registration the application no longer holds has no role to name; left out, as in report 01.
            .Where(x => x.RoleName is not null)
            .ToList();

        // Page shell is counted apart and never becomes a route (UsageRoutes). Roles are offered
        // from the routes, so a role that only ever loaded the shell is not a choice.
        var shell = named.Where(x => UsageRoutes.IsShell(x.Route)).ToList();
        var screens = named.Where(x => !UsageRoutes.IsShell(x.Route)).ToList();

        var roles = screens
            .GroupBy(x => x.RoleName!)
            .Select(g => new UserRequestRoleDto { RoleName = g.Key, Requests = g.Sum(x => x.Requests) })
            .ToList();

        // The lens applies only when the role is actually present; otherwise the answer is every role and says so.
        var appliedRole = role is not null && roles.Any(x => x.RoleName == role) ? role : null;
        var lensed = appliedRole is null ? screens : screens.Where(x => x.RoleName == appliedRole).ToList();
        var lensedShell = appliedRole is null ? shell : shell.Where(x => x.RoleName == appliedRole).ToList();

        var rows = lensed
            .GroupBy(x => new { x.JobId, x.Route })
            .Select(g => new UserRouteRowDto
            {
                JobId = g.Key.JobId,
                JobName = jobNames.TryGetValue(g.Key.JobId, out var name) ? name : string.Empty,
                Route = UsageRoutes.DisplayName(g.Key.Route),
                Requests = g.Sum(x => x.Requests),
                FailedRequests = g.Sum(x => x.FailedRequests),
                People = g.Where(x => x.Requests > 0).Select(x => x.Person).Distinct().Count(),
            })
            .ToList();

        // People are distinct across events, so route totals are recomputed from the named rows, never summed.
        var totals = lensed
            .GroupBy(x => x.Route)
            .Select(g => new UserRouteTotalDto
            {
                Route = UsageRoutes.DisplayName(g.Key),
                Requests = g.Sum(x => x.Requests),
                FailedRequests = g.Sum(x => x.FailedRequests),
                People = g.Where(x => x.Requests > 0).Select(x => x.Person).Distinct().Count(),
            })
            .ToList();

        return new UserRequestsByRouteDto
        {
            WindowDays = windowDays,
            JobCount = scope.Jobs.Count,
            Roles = roles,
            Role = appliedRole,
            Rows = rows,
            Totals = totals,
            TotalRequests = lensed.Sum(x => x.Requests),
            FailedRequests = lensed.Sum(x => x.FailedRequests),
            TotalPeople = lensed.Where(x => x.Requests > 0).Select(x => x.Person).Distinct().Count(),
            ShellRequests = lensedShell.Sum(x => x.Requests),
            UsageLoggingAvailable = _usageRepo.IsAvailable,
        };
    }

    public async Task<UsersByRoleOverTimeDto> GetUsersByRoleOverTimeAsync(
        UsageScopeResolution scope,
        UsageBucket bucket,
        DateTime since,
        int? appClientId,
        CancellationToken ct = default)
    {
        var starts = UsageBuckets.Starts(bucket, since);
        var expiryByJob = scope.Jobs.ToDictionary(j => j.JobId, j => j.ExpiryUsers);

        // Step 1 (TSICLogs): who, in which bucket -- distinct (bucket, registration) pairs.
        // Event and client lenses are already inside `scope` / `appClientId`; rows need no job key.
        var pairs = _usageRepo.IsAvailable
            ? await _usageRepo.GetDistinctRegistrationsByBucketAsync(scope.GetJobIds(), since, bucket, appClientId, ct)
            : [];
        // Sequential await, same scoped context: never Task.WhenAll with the call above.
        var firstRecordedAt = _usageRepo.IsAvailable ? await _usageRepo.GetFirstRecordedAtAsync(ct) : null;

        // Step 2 (TSICV5): which role each registration holds.
        var roleByReg = await LookupRolesAsync(pairs.Select(p => p.RegistrationId), ct);

        // An event counts only in the buckets it was live in: ExpiryUsers after the bucket's
        // start. Usage of an event after it concluded (a director tidying up) is not "a live
        // event being used" and is left out, the same rule as the summary reports apply at now.
        var rows = pairs
            .Where(p => roleByReg.ContainsKey(p.RegistrationId)
                        && p.BucketIndex >= 0 && p.BucketIndex < starts.Count
                        && expiryByJob.TryGetValue(p.JobId, out var expiry) && expiry > starts[p.BucketIndex])
            .Select(p => new { p.BucketIndex, p.RegistrationId, Role = roleByReg[p.RegistrationId] })
            .GroupBy(x => new { x.BucketIndex, x.Role.RoleId, x.Role.RoleName })
            .Select(g => new UsersByRoleBucketRowDto
            {
                BucketStart = starts[g.Key.BucketIndex],
                RoleName = g.Key.RoleName,
                Users = g.Select(x => x.RegistrationId).Distinct().Count(),
                IsAdmin = AdminRoleIds.Contains(g.Key.RoleId),
            })
            .ToList();

        return new UsersByRoleOverTimeDto
        {
            Bucket = UsageBuckets.ToWord(bucket),
            Since = since,
            Buckets = starts,
            FirstRecordedAt = firstRecordedAt,
            JobCount = scope.Jobs.Count,
            Rows = rows,
            UsageLoggingAvailable = _usageRepo.IsAvailable,
        };
    }

    /// <summary>
    /// TSICV5: the role each registration holds, in slices. A registration id the
    /// application no longer knows (deleted since the request) is simply absent.
    /// </summary>
    private async Task<Dictionary<Guid, UsageRegistrationRoleDto>> LookupRolesAsync(IEnumerable<Guid> registrationIds, CancellationToken ct)
    {
        var regIds = registrationIds.Distinct().ToList();
        var roleByReg = new Dictionary<Guid, UsageRegistrationRoleDto>(regIds.Count);
        foreach (var chunk in regIds.Chunk(LookupBatchSize))
        {
            foreach (var r in await _registrationRepo.GetRolesByRegistrationIdsAsync(chunk, ct))
                roleByReg[r.RegistrationId] = r;
        }
        return roleByReg;
    }

    /// <summary>TSICV5: which of the logins are family accounts, in slices.</summary>
    private async Task<HashSet<string>> LookupFamilyLoginsAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in ids.Chunk(LookupBatchSize))
        {
            foreach (var id in await _familyRepo.GetFamilyUserIdsAmongAsync(chunk, ct))
                families.Add(id);
        }
        return families;
    }

    /// <summary>Server-local, like OccurredAt. UtcNow would shift the window by the AZ offset.</summary>
    private static DateTime Since(int windowDays) => DateTime.Now.AddDays(-windowDays);

    private static UsersByRoleDto Empty(UsageScopeResolution scope, int windowDays, bool available) => new()
    {
        WindowDays = windowDays,
        JobCount = scope.Jobs.Count,
        Rows = [],
        Totals = [],
        CustomerUsers = 0,
        AdminUsers = 0,
        UsageLoggingAvailable = available,
    };
}
