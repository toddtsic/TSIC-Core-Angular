using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Read-only repository for tournament parking / teams-on-site reporting.
/// </summary>
public sealed class TournamentParkingRepository : ITournamentParkingRepository
{
    private readonly SqlDbContext _context;

    public TournamentParkingRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<TeamGamePresenceDto>> GetTeamGamePresenceAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // T1 side — round-robin teams only (T1Type == "T")
        var t1 = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                     && s.GDate != null
                     && s.FName != null
                     && s.T1Id != null
                     && s.T1Type == "T")
            .Select(s => new TeamGamePresenceDto
            {
                FieldComplex = s.FName!.Contains("-")
                    ? s.FName.Substring(0, s.FName.IndexOf("-")).Trim()
                    : s.FName,
                TeamId = s.T1Id!.Value,
                AgegroupId = s.AgegroupId ?? Guid.Empty,
                GameDate = s.GDate!.Value
            });

        // T2 side
        var t2 = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                     && s.GDate != null
                     && s.FName != null
                     && s.T2Id != null
                     && s.T2Type == "T")
            .Select(s => new TeamGamePresenceDto
            {
                FieldComplex = s.FName!.Contains("-")
                    ? s.FName.Substring(0, s.FName.IndexOf("-")).Trim()
                    : s.FName,
                TeamId = s.T2Id!.Value,
                AgegroupId = s.AgegroupId ?? Guid.Empty,
                GameDate = s.GDate!.Value
            });

        return await t1.Concat(t2).ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, int>> GetGameStartIntervalsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // Get the agegroup IDs that appear in this job's schedule
        var agegroupIds = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.AgegroupId != null)
            .Select(s => s.AgegroupId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (agegroupIds.Count == 0)
            return new Dictionary<Guid, int>();

        // Look up game-start intervals for those agegroups
        var intervals = await _context.TimeslotsLeagueSeasonFields
            .AsNoTracking()
            .Where(t => agegroupIds.Contains(t.AgegroupId))
            .Select(t => new { t.AgegroupId, t.GamestartInterval })
            .ToListAsync(ct);

        // Group by AgegroupId, take MIN interval (same as legacy sproc)
        return intervals
            .GroupBy(x => x.AgegroupId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.GamestartInterval));
    }
}
