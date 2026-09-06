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
    /// <summary>Which clients have rows in the scope/window -- what the client lens may offer.</summary>
    Task<UsageClientsDto> GetClientsAsync(
        UsageScopeResolution scope,
        int windowDays,
        bool excludeBots,
        CancellationToken ct = default);

    Task<UsersByRoleDto> GetUsersByRoleAsync(
        UsageScopeResolution scope,
        int windowDays,
        bool excludeBots,
        int? appClientId,
        CancellationToken ct = default);
}

public sealed class UsageAnalysisService : IUsageAnalysisService
{
    /// <summary>Id-list lookups go to TSICV5 in slices so a platform-wide month never ships one giant parameter.</summary>
    private const int LookupBatchSize = 2000;

    private static readonly HashSet<string> AdminRoleIds =
        new(RoleConstants.AdminRoleIds, StringComparer.OrdinalIgnoreCase);

    private readonly IUsageStatsRepository _usageRepo;
    private readonly IRegistrationRepository _registrationRepo;

    public UsageAnalysisService(IUsageStatsRepository usageRepo, IRegistrationRepository registrationRepo)
    {
        _usageRepo = usageRepo;
        _registrationRepo = registrationRepo;
    }

    public async Task<UsageClientsDto> GetClientsAsync(
        UsageScopeResolution scope,
        int windowDays,
        bool excludeBots,
        CancellationToken ct = default)
    {
        if (!_usageRepo.IsAvailable)
        {
            return new UsageClientsDto
            {
                WindowDays = windowDays,
                BotsExcluded = excludeBots,
                JobCount = scope.Jobs.Count,
                Clients = [],
                UsageLoggingAvailable = false,
            };
        }

        var clients = await _usageRepo.GetClientsPresentAsync(scope.JobIds, Since(windowDays), excludeBots, ct);
        return new UsageClientsDto
        {
            WindowDays = windowDays,
            BotsExcluded = excludeBots,
            JobCount = scope.Jobs.Count,
            Clients = clients.OrderByDescending(c => c.Requests).ThenBy(c => c.AppClientName).ToList(),
            UsageLoggingAvailable = true,
        };
    }

    public async Task<UsersByRoleDto> GetUsersByRoleAsync(
        UsageScopeResolution scope,
        int windowDays,
        bool excludeBots,
        int? appClientId,
        CancellationToken ct = default)
    {
        if (!_usageRepo.IsAvailable)
            return Empty(scope, windowDays, excludeBots, available: false);

        // Step 1 (TSICLogs): who, about which event -- distinct (event, registration) pairs.
        // Bots are almost never signed in, but the toggle is honoured everywhere so the
        // audit stamp is never a lie.
        var pairs = await _usageRepo.GetDistinctRegistrationsByJobAsync(
            scope.JobIds, Since(windowDays), excludeBots, appClientId, ct);
        if (pairs.Count == 0)
            return Empty(scope, windowDays, excludeBots, available: true);

        // Step 2 (TSICV5): which role each registration holds. A registration id the
        // application no longer knows (deleted since the request) simply drops out.
        var regIds = pairs.Select(p => p.RegistrationId).Distinct().ToList();
        var roleByReg = new Dictionary<Guid, UsageRegistrationRoleDto>(regIds.Count);
        foreach (var chunk in regIds.Chunk(LookupBatchSize))
        {
            foreach (var r in await _registrationRepo.GetRolesByRegistrationIdsAsync(chunk, ct))
                roleByReg[r.RegistrationId] = r;
        }

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

        return new UsersByRoleDto
        {
            WindowDays = windowDays,
            BotsExcluded = excludeBots,
            JobCount = scope.Jobs.Count,
            Rows = rows,
            // Distinct people, not a sum of per-event rows.
            CustomerUsers = joined.Where(x => !x.IsAdmin).Select(x => x.RegistrationId).Distinct().Count(),
            AdminUsers = joined.Where(x => x.IsAdmin).Select(x => x.RegistrationId).Distinct().Count(),
            UsageLoggingAvailable = true,
        };
    }

    /// <summary>Server-local, like OccurredAt. UtcNow would shift the window by the AZ offset.</summary>
    private static DateTime Since(int windowDays) => DateTime.Now.AddDays(-windowDays);

    private static UsersByRoleDto Empty(UsageScopeResolution scope, int windowDays, bool excludeBots, bool available) => new()
    {
        WindowDays = windowDays,
        BotsExcluded = excludeBots,
        JobCount = scope.Jobs.Count,
        Rows = [],
        CustomerUsers = 0,
        AdminUsers = 0,
        UsageLoggingAvailable = available,
    };
}
