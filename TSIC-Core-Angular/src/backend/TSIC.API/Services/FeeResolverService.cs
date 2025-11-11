using Microsoft.EntityFrameworkCore;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public interface IFeeResolverService
{
    /// <summary>
    /// Resolve the "base" fee for a team using hierarchical fallbacks: Team > AgeGroup > League.
    /// Returns 0 when no fee can be determined.
    /// </summary>
    Task<decimal> ResolveBaseFeeForTeamAsync(Guid teamId);
}

public class FeeResolverService : IFeeResolverService
{
    private readonly SqlDbContext _db;
    public FeeResolverService(SqlDbContext db)
    {
        _db = db;
    }

    public async Task<decimal> ResolveBaseFeeForTeamAsync(Guid teamId)
    {
        // Pull only the minimal fields needed for fee resolution
        var data = await _db.Teams
            .Where(t => t.TeamId == teamId)
            .Select(t => new
            {
                TeamFeeBase = t.FeeBase,
                TeamPerRegistrant = t.PerRegistrantFee,
                AgegroupId = t.AgegroupId,
            })
            .FirstOrDefaultAsync();

        if (data == null) return 0m;

        // 1) Team-level fee
        var teamBase = data.TeamFeeBase ?? data.TeamPerRegistrant ?? 0m;
        if (teamBase > 0) return teamBase;

        // 2) AgeGroup-level fee (prefer TeamFee, else RosterFee)
        var age = await _db.Agegroups
            .Where(a => a.AgegroupId == data.AgegroupId)
            .Select(a => new { a.TeamFee, a.RosterFee })
            .FirstOrDefaultAsync();
        if (age != null)
        {
            var agBase = (age.TeamFee ?? age.RosterFee) ?? 0m;
            if (agBase > 0) return agBase;
        }

        // 3) League-level fee: no explicit fee fields currently modeled; return 0 for now
        return 0m;
    }
}
