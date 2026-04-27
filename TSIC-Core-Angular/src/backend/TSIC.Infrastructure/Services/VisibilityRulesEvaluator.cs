using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Services;

/// <summary>
/// Shared evaluator for nav + reports. Extracted from NavRepository so the
/// reports catalogue applies identical gating rules without duplicating logic.
///
/// Flag derivation (from Jobs entity):
///   BEnableStore      -> storeEnabled
///   AdnArb            -> adnArb
///   BenableStp        -> mobileEnabled
///   CoreRegformPlayer -> teamEligibilityByAge (when 2nd pipe == 'BYAGERANGE')
///   JobTypeId in (1,4,6) -> playerSiteOnly
/// </summary>
public class VisibilityRulesEvaluator : IVisibilityRulesEvaluator
{
    private readonly SqlDbContext _context;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public VisibilityRulesEvaluator(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<JobNavContext?> BuildJobContextAsync(
        Guid jobId,
        IEnumerable<string> callerRoles,
        CancellationToken cancellationToken = default)
    {
        var raw = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                SportName = j.Sport.SportName,
                JobTypeName = j.JobType.JobTypeName,
                CustomerName = j.Customer.CustomerName,
                j.JobTypeId,
                j.BEnableStore,
                j.AdnArb,
                j.BenableStp,
                j.CoreRegformPlayer
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (raw == null) return null;

        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (raw.BEnableStore == true) flags.Add("storeEnabled");
        if (raw.AdnArb == true) flags.Add("adnArb");
        if (raw.BenableStp == true) flags.Add("mobileEnabled");

        if (!string.IsNullOrEmpty(raw.CoreRegformPlayer))
        {
            var parts = raw.CoreRegformPlayer.Split('|');
            if (parts.Length >= 2 && string.Equals(parts[1], "BYAGERANGE", StringComparison.OrdinalIgnoreCase))
                flags.Add("teamEligibilityByAge");
        }

        if (raw.JobTypeId is 1 or 4 or 6) flags.Add("playerSiteOnly");

        var roleSet = new HashSet<string>(callerRoles, StringComparer.OrdinalIgnoreCase);

        return new JobNavContext(raw.SportName, raw.JobTypeName, raw.CustomerName, flags, roleSet);
    }

    public bool Passes(string? rulesJson, JobNavContext context)
    {
        if (string.IsNullOrEmpty(rulesJson)) return true;

        NavItemVisibilityRules? rules;
        try
        {
            rules = JsonSerializer.Deserialize<NavItemVisibilityRules>(rulesJson, JsonOpts);
        }
        catch
        {
            return true; // malformed JSON = fail-open
        }

        if (rules == null) return true;

        if (rules.Sports is { Count: > 0 } && context.SportName != null
            && !rules.Sports.Contains(context.SportName, StringComparer.OrdinalIgnoreCase))
            return false;

        if (rules.JobTypes is { Count: > 0 } && context.JobTypeName != null
            && !rules.JobTypes.Contains(context.JobTypeName, StringComparer.OrdinalIgnoreCase))
            return false;

        if (rules.CustomersDeny is { Count: > 0 } && context.CustomerName != null
            && rules.CustomersDeny.Contains(context.CustomerName, StringComparer.OrdinalIgnoreCase))
            return false;

        if (rules.RequiresFlags is { Count: > 0 })
        {
            foreach (var flag in rules.RequiresFlags)
            {
                if (!context.ActiveFlags.Contains(flag)) return false;
            }
        }

        if (rules.RequiresRoles is { Count: > 0 }
            && !rules.RequiresRoles.Any(r => context.CallerRoles.Contains(r)))
            return false;

        return true;
    }
}
