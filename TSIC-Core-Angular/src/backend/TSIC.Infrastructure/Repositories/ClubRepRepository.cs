using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for ClubReps entity using Entity Framework Core.
/// </summary>
public class ClubRepRepository : IClubRepRepository
{
    private readonly SqlDbContext _context;

    public ClubRepRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<ClubWithUsageInfo>> GetClubsForUserAsync(
        string clubRepUserId,
        CancellationToken cancellationToken = default)
    {
        var clubData = await _context.ClubReps
            .AsNoTracking()
            .Where(cr => cr.ClubRepUserId == clubRepUserId)
            .Select(cr => new { cr.ClubId, ClubName = cr.Club!.ClubName! })
            .ToListAsync(cancellationToken);

        var result = new List<ClubWithUsageInfo>();
        foreach (var cr in clubData)
        {
            // In use = this club has registered teams, found by id:
            //  - any team linked to this club's library (Teams.ClubTeamId → ClubTeams.ClubId), or
            //  - a team with no library link on a Club Rep registration made by one of THIS club's reps
            //    under this club's name (legacy teams predate the library). The name comparison is scoped
            //    to this club's own reps and only ever LOCKS a rename — it never picks a club.
            var clubId = cr.ClubId;
            var clubName = cr.ClubName;
            var hasTeams = await _context.Teams
                .AnyAsync(t =>
                    (t.ClubTeamId != null
                        && _context.ClubTeams.Any(cte => cte.ClubTeamId == t.ClubTeamId && cte.ClubId == clubId))
                    || (t.ClubTeamId == null
                        && t.ClubrepRegistrationid != null
                        && _context.Registrations.Any(r =>
                            r.RegistrationId == t.ClubrepRegistrationid
                            && r.ClubName == clubName
                            && _context.ClubReps.Any(rep => rep.ClubId == clubId && rep.ClubRepUserId == r.UserId))),
                    cancellationToken);

            result.Add(new ClubWithUsageInfo
            {
                ClubId = cr.ClubId,
                ClubName = cr.ClubName,
                IsInUse = hasTeams
            });
        }

        return result;
    }

    public async Task<ClubRepClubResolution> ResolveClubForClubRepRegistrationAsync(
        Guid clubRepRegistrationId,
        CancellationToken cancellationToken = default)
    {
        // 1. By id: the clubs the registration's library-linked teams (any status) belong to.
        var linked = await (
            from t in _context.Teams
            where t.ClubrepRegistrationid == clubRepRegistrationId && t.ClubTeamId != null
            join cte in _context.ClubTeams on t.ClubTeamId equals cte.ClubTeamId
            select new { cte.ClubId, cte.ClubTeamId })
            .AsNoTracking()
            .Distinct()
            .ToListAsync(cancellationToken);

        if (linked.Count > 0)
        {
            var byClub = linked
                .GroupBy(x => x.ClubId)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .ToList();
            return new ClubRepClubResolution { ClubId = byClub[0].Key, SpannedClubCount = byClub.Count, SurvivesRename = true };
        }

        // 2. No library-linked team yet: the registering user's own clubs.
        var reg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == clubRepRegistrationId)
            .Select(r => new { r.UserId, r.ClubName })
            .FirstOrDefaultAsync(cancellationToken);
        if (reg?.UserId == null)
            return new ClubRepClubResolution { ClubId = 0, SpannedClubCount = 0 };

        var myClubs = await _context.ClubReps
            .AsNoTracking()
            .Where(cr => cr.ClubRepUserId == reg.UserId)
            .Select(cr => new { cr.ClubId, ClubName = cr.Club!.ClubName })
            .ToListAsync(cancellationToken);

        var regClubName = reg.ClubName?.Trim();
        var named = myClubs
            .Where(c => string.Equals(c.ClubName?.Trim(), regClubName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var clubId = named.Count == 1 ? named[0].ClubId
            : named.Count > 1 ? await MostUsedClubForUserAsync(reg.UserId, named.Select(c => c.ClubId).ToList(), cancellationToken)
            : myClubs.Count == 1 ? myClubs[0].ClubId
            : 0;
        return new ClubRepClubResolution
        {
            ClubId = clubId,
            SpannedClubCount = 0,
            SurvivesRename = clubId > 0 && myClubs.Count == 1
        };
    }

    public async Task<Dictionary<Guid, int>> ResolveClubsForClubRepRegistrationsAsync(
        IEnumerable<Guid> clubRepRegistrationIds,
        CancellationToken cancellationToken = default)
    {
        var ids = clubRepRegistrationIds.Distinct().ToList();
        var result = new Dictionary<Guid, int>();
        if (ids.Count == 0) return result;

        // Rung 1 - by id: the club each registration's library-linked teams belong to (dominant club
        // wins on a tie-free count; ties break on the lower ClubId, as the single-form does).
        var linked = await (
            from t in _context.Teams
            where t.ClubrepRegistrationid != null && ids.Contains(t.ClubrepRegistrationid.Value) && t.ClubTeamId != null
            join cte in _context.ClubTeams on t.ClubTeamId equals cte.ClubTeamId
            select new { RegId = t.ClubrepRegistrationid!.Value, cte.ClubId, cte.ClubTeamId })
            .AsNoTracking()
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var g in linked.GroupBy(x => x.RegId))
        {
            var byClub = g.GroupBy(x => x.ClubId).OrderByDescending(c => c.Count()).ThenBy(c => c.Key).First();
            result[g.Key] = byClub.Key;
        }

        // Rung 2 - no library-linked team yet: the registering user's own clubs, matched by the
        // registration's club name; a user with exactly one club resolves to it regardless.
        var pending = ids.Where(id => !result.ContainsKey(id)).ToList();
        if (pending.Count == 0) return result;

        var regs = await _context.Registrations
            .AsNoTracking()
            .Where(r => pending.Contains(r.RegistrationId))
            .Select(r => new { r.RegistrationId, r.UserId, r.ClubName })
            .ToListAsync(cancellationToken);

        var userIds = regs.Select(r => r.UserId).Where(u => u != null).Distinct().ToList();
        var clubsByUser = (await _context.ClubReps
            .AsNoTracking()
            .Where(cr => userIds.Contains(cr.ClubRepUserId))
            .Select(cr => new { cr.ClubRepUserId, cr.ClubId, ClubName = cr.Club!.ClubName })
            .ToListAsync(cancellationToken))
            .GroupBy(x => x.ClubRepUserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var reg in regs)
        {
            if (reg.UserId == null || !clubsByUser.TryGetValue(reg.UserId, out var myClubs))
            {
                result[reg.RegistrationId] = 0;
                continue;
            }
            var regClubName = reg.ClubName?.Trim();
            var named = myClubs
                .Where(c => string.Equals(c.ClubName?.Trim(), regClubName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            result[reg.RegistrationId] = named.Count == 1 ? named[0].ClubId
                : named.Count > 1 ? await MostUsedClubForUserAsync(reg.UserId, named.Select(c => c.ClubId).ToList(), cancellationToken)
                : myClubs.Count == 1 ? myClubs[0].ClubId
                : 0;
        }

        return result;
    }

    /// <summary>
    /// The user reps more than one club with the registration's name (e.g. two libraries both named
    /// "True Lacrosse"): pick the candidate whose library teams the user has registered most across all
    /// their events, counted by id. No strict winner = 0.
    /// </summary>
    private async Task<int> MostUsedClubForUserAsync(string userId, List<int> candidateClubIds, CancellationToken cancellationToken)
    {
        var usage = await (
            from t in _context.Teams
            join r in _context.Registrations on t.ClubrepRegistrationid equals r.RegistrationId
            join cte in _context.ClubTeams on t.ClubTeamId equals cte.ClubTeamId
            where r.UserId == userId && candidateClubIds.Contains(cte.ClubId)
            group t by cte.ClubId into g
            select new { ClubId = g.Key, Teams = g.Count() })
            .AsNoTracking()
            .OrderByDescending(x => x.Teams)
            .Take(2)
            .ToListAsync(cancellationToken);

        return usage.Count == 1 || (usage.Count == 2 && usage[0].Teams > usage[1].Teams)
            ? usage[0].ClubId
            : 0;
    }

    public async Task<ClubReps?> GetClubRepForUserAndClubAsync(
        string clubRepUserId,
        int clubId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubReps
            .Where(cr => cr.ClubRepUserId == clubRepUserId && cr.ClubId == clubId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        string clubRepUserId,
        int clubId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubReps
            .AnyAsync(cr => cr.ClubRepUserId == clubRepUserId && cr.ClubId == clubId, cancellationToken);
    }

    public void Add(ClubReps clubRep)
    {
        _context.ClubReps.Add(clubRep);
    }

    public void Remove(ClubReps clubRep)
    {
        _context.ClubReps.Remove(clubRep);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
