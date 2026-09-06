using TSIC.Contracts.Dtos.Usage;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Usage;

/// <summary>
/// The usage-analysis reports. Each takes an already-resolved scope (the controller ran
/// IUsageScopeResolver) and answers one question from TSICLogs, pairing with TSICV5 in
/// memory where a name or role is needed. Nothing here joins the two databases in SQL.
/// </summary>
public interface IUsageAnalysisService
{
    Task<UsersByRoleDto> GetUsersByRoleAsync(
        UsageScopeResolution scope,
        int windowDays,
        bool excludeBots,
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

    public async Task<UsersByRoleDto> GetUsersByRoleAsync(
        UsageScopeResolution scope,
        int windowDays,
        bool excludeBots,
        CancellationToken ct = default)
    {
        if (!_usageRepo.IsAvailable)
            return Empty(scope, windowDays, excludeBots, available: false);

        // Server-local, like OccurredAt. UtcNow would shift the window by the AZ offset.
        var since = DateTime.Now.AddDays(-windowDays);

        // Step 1 (TSICLogs): who -- distinct registration ids that made a request about
        // any job in the scope. Bots are almost never signed in, but the toggle is honoured
        // everywhere so the audit stamp is never a lie.
        var regIds = await _usageRepo.GetDistinctRegistrationIdsAsync(scope.JobIds, since, excludeBots, ct);
        if (regIds.Count == 0)
            return Empty(scope, windowDays, excludeBots, available: true);

        // Step 2 (TSICV5): which role each registration holds. A registration id the
        // application no longer knows (deleted since the request) simply drops out.
        var roles = new List<UsageRegistrationRoleDto>(regIds.Count);
        foreach (var chunk in regIds.Chunk(LookupBatchSize))
            roles.AddRange(await _registrationRepo.GetRolesByRegistrationIdsAsync(chunk, ct));

        var rows = roles
            .GroupBy(r => new { r.RoleId, r.RoleName })
            .Select(g => new UsersByRoleRowDto
            {
                RoleName = g.Key.RoleName,
                Users = g.Select(r => r.RegistrationId).Distinct().Count(),
                IsAdmin = AdminRoleIds.Contains(g.Key.RoleId),
            })
            .OrderBy(r => r.IsAdmin)
            .ThenByDescending(r => r.Users)
            .ThenBy(r => r.RoleName)
            .ToList();

        return new UsersByRoleDto
        {
            WindowDays = windowDays,
            BotsExcluded = excludeBots,
            JobCount = scope.Jobs.Count,
            Rows = rows,
            CustomerUsers = rows.Where(r => !r.IsAdmin).Sum(r => r.Users),
            AdminUsers = rows.Where(r => r.IsAdmin).Sum(r => r.Users),
            UsageLoggingAvailable = true,
        };
    }

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
