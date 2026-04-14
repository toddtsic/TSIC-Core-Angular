using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public sealed class JobFilterTreeRepository : IJobFilterTreeRepository
{
    private readonly SqlDbContext _context;

    public JobFilterTreeRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<JobFilterTreeDto> GetForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        // 1. Flat rows: every active team in the job with its agegroup/division/club context.
        //    Includes teams without a clubrep (ClubName = null) and without a division (DivId = null).
        //    Per-surface filters (scheduled, has-clubrep, exclude-waitlist-dropped) happen client-side.
        var teamRows = await (
            from t in _context.Teams.AsNoTracking()
            where t.JobId == jobId && t.Active == true
            join ag in _context.Agegroups.AsNoTracking()
                on t.AgegroupId equals ag.AgegroupId
            join div in _context.Divisions.AsNoTracking()
                on t.DivId equals (Guid?)div.DivId into divJoin
            from div in divJoin.DefaultIfEmpty()
            join reg in _context.Registrations.AsNoTracking()
                on t.ClubrepRegistrationid equals (Guid?)reg.RegistrationId into regJoin
            from reg in regJoin.DefaultIfEmpty()
            select new TeamRow
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName,
                HasClubRep = t.ClubrepRegistrationid != null,
                AgegroupId = t.AgegroupId,
                AgegroupName = ag.AgegroupName,
                AgegroupColor = ag.Color,
                DivId = div != null ? (Guid?)div.DivId : null,
                DivName = div != null ? div.DivName : null,
                ClubName = reg != null ? reg.ClubName : null
            }
        ).ToListAsync(ct);

        if (teamRows.Count == 0)
        {
            return new JobFilterTreeDto
            {
                Cadt = [],
                Ladt = []
            };
        }

        var teamIds = teamRows.Select(t => t.TeamId).ToList();

        // 2. Scheduled team IDs — set membership for IsScheduled flag.
        //    Two queries unioned (EF can't translate an inline array SelectMany over T1Id/T2Id).
        var t1Ids = await _context.Schedule.AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue && s.T1Id.HasValue && teamIds.Contains(s.T1Id!.Value))
            .Select(s => s.T1Id!.Value)
            .ToListAsync(ct);
        var t2Ids = await _context.Schedule.AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue && s.T2Id.HasValue && teamIds.Contains(s.T2Id!.Value))
            .Select(s => s.T2Id!.Value)
            .ToListAsync(ct);
        var scheduledIds = t1Ids.Concat(t2Ids).ToHashSet();

        // 3. Player counts (RoleId == Player only; matches TeamRepository.GetPublicRosterTreeAsync semantics).
        var playerCounts = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.AssignedTeamId != null
                        && teamIds.Contains(r.AssignedTeamId.Value)
                        && r.BActive == true
                        && r.RoleId == RoleConstants.Player)
            .GroupBy(r => r.AssignedTeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TeamId, g => g.Count, ct);

        // 4. Enrich each row with per-team counts and flags before grouping.
        var enriched = teamRows
            .Select(r => new EnrichedRow
            {
                Row = r,
                PlayerCount = playerCounts.GetValueOrDefault(r.TeamId, 0),
                IsScheduled = scheduledIds.Contains(r.TeamId)
            })
            .ToList();

        // 5. Build CADT (Club → Agegroup → Division → Team). Teams without a club are absent.
        var cadt = enriched
            .Where(e => e.Row.ClubName != null)
            .GroupBy(e => e.Row.ClubName!)
            .OrderBy(g => g.Key)
            .Select(clubGroup => new CadtClubNode
            {
                ClubName = clubGroup.Key,
                TeamCount = clubGroup.Count(),
                PlayerCount = clubGroup.Sum(e => e.PlayerCount),
                Agegroups = BuildCadtAgegroups(clubGroup).ToList()
            })
            .ToList();

        // 6. Build LADT (Agegroup → Division → Team). All teams included.
        var ladt = BuildLadtAgegroups(enriched).ToList();

        return new JobFilterTreeDto
        {
            Cadt = cadt,
            Ladt = ladt
        };
    }

    private static IEnumerable<CadtAgegroupNode> BuildCadtAgegroups(IEnumerable<EnrichedRow> rows)
    {
        return rows
            .GroupBy(e => new { e.Row.AgegroupId, e.Row.AgegroupName, e.Row.AgegroupColor })
            .OrderBy(g => g.Key.AgegroupName)
            .Select(agGroup => new CadtAgegroupNode
            {
                AgegroupId = agGroup.Key.AgegroupId,
                AgegroupName = agGroup.Key.AgegroupName ?? "",
                Color = agGroup.Key.AgegroupColor,
                TeamCount = agGroup.Count(),
                PlayerCount = agGroup.Sum(e => e.PlayerCount),
                IsWaitlist = (agGroup.Key.AgegroupName ?? "").Contains("WAITLIST", StringComparison.OrdinalIgnoreCase),
                IsDropped = (agGroup.Key.AgegroupName ?? "").Contains("DROPPED", StringComparison.OrdinalIgnoreCase),
                Divisions = agGroup
                    .Where(e => e.Row.DivId.HasValue)
                    .GroupBy(e => new { DivId = e.Row.DivId!.Value, e.Row.DivName })
                    .OrderBy(g => g.Key.DivName)
                    .Select(divGroup => new CadtDivisionNode
                    {
                        DivId = divGroup.Key.DivId,
                        DivName = divGroup.Key.DivName ?? "",
                        TeamCount = divGroup.Count(),
                        PlayerCount = divGroup.Sum(e => e.PlayerCount),
                        Teams = divGroup
                            .OrderBy(e => e.Row.TeamName)
                            .Select(e => new CadtTeamNode
                            {
                                TeamId = e.Row.TeamId,
                                TeamName = e.Row.TeamName ?? "",
                                PlayerCount = e.PlayerCount,
                                IsScheduled = e.IsScheduled,
                                HasClubRep = e.Row.HasClubRep
                            })
                            .ToList()
                    })
                    .ToList()
            });
    }

    private static IEnumerable<LadtAgegroupNode> BuildLadtAgegroups(IEnumerable<EnrichedRow> rows)
    {
        return rows
            .GroupBy(e => new { e.Row.AgegroupId, e.Row.AgegroupName, e.Row.AgegroupColor })
            .OrderBy(g => g.Key.AgegroupName)
            .Select(agGroup => new LadtAgegroupNode
            {
                AgegroupId = agGroup.Key.AgegroupId,
                AgegroupName = agGroup.Key.AgegroupName ?? "",
                Color = agGroup.Key.AgegroupColor,
                TeamCount = agGroup.Count(),
                PlayerCount = agGroup.Sum(e => e.PlayerCount),
                IsWaitlist = (agGroup.Key.AgegroupName ?? "").Contains("WAITLIST", StringComparison.OrdinalIgnoreCase),
                IsDropped = (agGroup.Key.AgegroupName ?? "").Contains("DROPPED", StringComparison.OrdinalIgnoreCase),
                Divisions = agGroup
                    .Where(e => e.Row.DivId.HasValue)
                    .GroupBy(e => new { DivId = e.Row.DivId!.Value, e.Row.DivName })
                    .OrderBy(g => g.Key.DivName)
                    .Select(divGroup => new LadtDivisionNode
                    {
                        DivId = divGroup.Key.DivId,
                        DivName = divGroup.Key.DivName ?? "",
                        TeamCount = divGroup.Count(),
                        PlayerCount = divGroup.Sum(e => e.PlayerCount),
                        Teams = divGroup
                            .OrderBy(e => e.Row.TeamName)
                            .Select(e => new LadtTeamNode
                            {
                                TeamId = e.Row.TeamId,
                                TeamName = e.Row.TeamName ?? "",
                                ClubName = e.Row.ClubName,
                                PlayerCount = e.PlayerCount,
                                IsScheduled = e.IsScheduled,
                                HasClubRep = e.Row.HasClubRep
                            })
                            .ToList()
                    })
                    .ToList()
            });
    }

    private sealed class TeamRow
    {
        public required Guid TeamId { get; init; }
        public string? TeamName { get; init; }
        public required bool HasClubRep { get; init; }
        public required Guid AgegroupId { get; init; }
        public string? AgegroupName { get; init; }
        public string? AgegroupColor { get; init; }
        public Guid? DivId { get; init; }
        public string? DivName { get; init; }
        public string? ClubName { get; init; }
    }

    private sealed class EnrichedRow
    {
        public required TeamRow Row { get; init; }
        public required int PlayerCount { get; init; }
        public required bool IsScheduled { get; init; }
    }
}
