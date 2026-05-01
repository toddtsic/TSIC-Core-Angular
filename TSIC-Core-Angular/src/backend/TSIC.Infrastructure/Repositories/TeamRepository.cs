using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.ClubRoster;
using TSIC.Contracts.Dtos.Rankings;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Utilities;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Teams entity using Entity Framework Core.
/// </summary>
public class TeamRepository : ITeamRepository
{
    private readonly SqlDbContext _context;

    // Canonical reference.Accounting_PaymentMethods row for NSF reversals
    // (mirrors AdnSweepService.FailedEcheckPaymentMethodId). Used to detect
    // teams with returned-eCheck reversals so they read as "Autopay Failed"
    // instead of "Scheduled" in admin search.
    private static readonly Guid FailedEcheckPaymentMethodId = Guid.Parse("2FECA575-A268-E111-9D56-F04DA202060D");

    public TeamRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<Teams>> GetTeamsForJobAsync(Guid jobId, IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Teams>> GetTeamsForJobByNamesAsync(Guid jobId, IReadOnlyCollection<string> teamNames, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.TeamName != null && teamNames.Contains(t.TeamName))
            .ToListAsync(cancellationToken);
    }

    public async Task<(decimal? FeeBase, decimal? PerRegistrantFee)> GetTeamFeeInfoAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var data = await _context.Teams
            .Where(t => t.TeamId == teamId)
            .Select(t => new { t.FeeBase, t.PerRegistrantFee })
            .FirstOrDefaultAsync(cancellationToken);

        return data == null
            ? (null, null)
            : (data.FeeBase, data.PerRegistrantFee);
    }

    public async Task<int> GetRegisteredCountForAgegroupAsync(
        Guid jobId,
        Guid agegroupId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .CountAsync(t => t.JobId == jobId && t.AgegroupId == agegroupId, cancellationToken);
    }

    public async Task<int> GetRegisteredCountForClubRepAndAgegroupAsync(
        Guid jobId,
        Guid agegroupId,
        Guid clubRepRegistrationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .CountAsync(t => t.JobId == jobId
                && t.AgegroupId == agegroupId
                && t.ClubrepRegistrationid == clubRepRegistrationId
                && t.Active == true, cancellationToken);
    }

    public async Task<Teams?> GetTeamFromTeamId(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams.FindAsync(new object[] { teamId }, cancellationToken);
    }

    public async Task<Teams?> GetByAdnSubscriptionIdAsync(
        string adnSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Include(t => t.Job)
            .FirstOrDefaultAsync(t => t.AdnSubscriptionId == adnSubscriptionId, cancellationToken);
    }

    public void Add(Teams team)
    {
        _context.Teams.Add(team);
    }

    public void Remove(Teams team)
    {
        _context.Teams.Remove(team);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<AvailableTeamQueryResult>> GetAvailableTeamsQueryResultsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId)
            .Where(t => (t.Active ?? true))
            .Where(t => (t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
            .Where(t => (t.Effectiveasofdate == null || t.Effectiveasofdate <= now)
                        && (t.Expireondate == null || t.Expireondate >= now))
            .Where(t => !t.Agegroup.AgegroupName.StartsWith("Dropped")
                        && !t.Agegroup.AgegroupName.StartsWith("Waitlist"))
            .Select(t => new AvailableTeamQueryResult
            {
                TeamId = t.TeamId,
                Name = t.TeamName ?? t.DisplayName ?? "(Unnamed Team)",
                AgegroupId = t.AgegroupId,
                AgegroupName = t.Agegroup.AgegroupName ?? string.Empty,
                DivisionId = t.DivId,
                DivisionName = t.Div != null ? t.Div.DivName : null,
                MaxCount = t.MaxCount,
                TeamAllowsSelfRostering = t.BAllowSelfRostering,
                AgegroupAllowsSelfRostering = t.Agegroup.BAllowSelfRostering,
                StartDate = t.Startdate,
                EndDate = t.Enddate,
                PerRegistrantFee = t.PerRegistrantFee,
                ClubName = t.ClubrepRegistrationid != null ? t.ClubrepRegistration.ClubName : null
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, string>> GetTeamNameMapAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default)
    {
        if (teamIds.Count == 0) return new Dictionary<Guid, string>();

        return await _context.Teams
            .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
            .ToDictionaryAsync(t => t.TeamId, t => t.TeamName ?? string.Empty, cancellationToken);
    }

    public async Task<List<Teams>> GetTeamsWithJobAndCustomerAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default)
    {
        if (teamIds.Count == 0)
        {
            return new List<Teams>();
        }

        return await _context.Teams
            .Include(t => t.Job)
                .ThenInclude(j => j.Customer)
            .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RegisteredTeamInfo>> GetRegisteredTeamsForPaymentAsync(
        Guid jobId,
        Guid clubRepRegId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Where(t => t.JobId == jobId && t.ClubrepRegistrationid == clubRepRegId)
            .Where(t => t.Active == true)
            .Where(t => (t.FeeTotal ?? 0) > 0)
            .Where(t => string.IsNullOrEmpty(t.ViPolicyId))
            .Select(t => new RegisteredTeamInfo
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName ?? string.Empty,
                AgeGroupId = t.AgegroupId,
                AgeGroupName = t.Agegroup.AgegroupName ?? string.Empty,
                LevelOfPlay = t.LevelOfPlay,
                FeeBase = t.FeeBase ?? 0,
                FeeProcessing = t.FeeProcessing ?? 0,
                FeeDiscount = t.FeeDiscount ?? 0,
                FeeLatefee = t.FeeLatefee ?? 0,
                FeeTotal = t.FeeTotal ?? 0,
                PaidTotal = t.PaidTotal ?? 0,
                OwedTotal = t.OwedTotal ?? 0,
                RegistrationTs = t.Createdate,
                BWaiverSigned3 = t.ClubrepRegistration != null && t.ClubrepRegistration.BWaiverSigned3
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateTeamFeesAsync(List<Teams> teams, CancellationToken cancellationToken = default)
    {
        _context.Teams.UpdateRange(teams);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<RegisteredTeamInfo>> GetRegisteredTeamsForUserAndJobAsync(
        Guid jobId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                      where t.JobId == jobId && reg.UserId == userId
                            && t.Active == true
                            && !ag.AgegroupName!.Contains("DROPPED")
                      orderby ag.AgegroupName, t.TeamName
                      select new RegisteredTeamInfo
                      {
                          TeamId = t.TeamId,
                          TeamName = t.TeamName ?? string.Empty,
                          AgeGroupId = ag.AgegroupId,
                          AgeGroupName = ag.AgegroupName ?? string.Empty,
                          LevelOfPlay = t.LevelOfPlay,
                          FeeBase = t.FeeBase ?? 0,
                          FeeProcessing = t.FeeProcessing ?? 0,
                          FeeDiscount = t.FeeDiscount ?? 0,
                          FeeLatefee = t.FeeLatefee ?? 0,
                          // Use stored totals — RecalcTotals keeps them in sync across every fee mutation.
                          FeeTotal = t.FeeTotal ?? 0,
                          PaidTotal = t.PaidTotal ?? 0,
                          OwedTotal = t.OwedTotal ?? 0,
                          RegistrationTs = t.Createdate,
                          BWaiverSigned3 = reg.BWaiverSigned3,
                          ClubTeamId = t.ClubTeamId
                      })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TeamWithRegistrationInfo>> GetTeamsByClubExcludingRegistrationAsync(
        Guid jobId,
        int clubId,
        Guid? excludeRegistrationId = null,
        CancellationToken cancellationToken = default)
    {
        var query = from t in _context.Teams
                    join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                    where t.JobId == jobId
                      && t.ClubrepRegistrationid != null
                      && _context.ClubReps.Any(cr => cr.ClubRepUserId == reg.UserId && cr.ClubId == clubId)
                    select new TeamWithRegistrationInfo
                    {
                        TeamId = t.TeamId,
                        TeamName = t.TeamName ?? string.Empty,
                        Username = reg.User != null ? reg.User.UserName : null,
                        ClubrepRegistrationid = t.ClubrepRegistrationid
                    };

        if (excludeRegistrationId.HasValue)
        {
            query = query.Where(t => t.ClubrepRegistrationid != excludeRegistrationId.Value);
        }

        return await query
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<HistoricalTeamInfo>> GetHistoricalTeamsForClubAsync(
        string userId,
        string clubName,
        int previousYear,
        CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join j in _context.Jobs on t.JobId equals j.JobId
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                      where reg.UserId == userId
                        && reg.ClubName == clubName
                        && j.Season == previousYear.ToString()
                        && t.TeamName != null
                      orderby t.TeamName
                      select new HistoricalTeamInfo
                      {
                          TeamId = t.TeamId,
                          TeamName = t.TeamName ?? string.Empty,
                          AgegroupName = ag.AgegroupName,
                          Createdate = t.Createdate
                      })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, int>> GetRegistrationCountsByAgeGroupAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Where(t => t.JobId == jobId && (t.Active == true))
            .GroupBy(t => t.AgegroupId)
            .Select(g => new { AgegroupId = g.Key, Count = g.Count() })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.AgegroupId, x => x.Count, cancellationToken);
    }

    public async Task<bool> HasTeamsForClubRepAsync(
        string userId,
        int clubId,
        CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                      where _context.ClubReps.Any(cr => cr.ClubRepUserId == reg.UserId && cr.ClubId == clubId)
                      select t)
            .AnyAsync(cancellationToken);
    }

    public async Task<Guid?> GetTeamJobIdAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => (Guid?)t.JobId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Teams>> GetTeamsWithDetailsForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Include(t => t.Job)
            .Include(t => t.Agegroup)
            .Where(t => t.JobId == jobId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid?> GetTeamAgeGroupIdAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => (Guid?)t.AgegroupId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ── LADT Admin methods ──

    public async Task<List<Teams>> GetByDivisionIdAsync(Guid divId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.DivId == divId)
            .OrderBy(t => t.DivRank)
            .ThenBy(t => t.TeamName)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Teams>> GetByAgegroupIdAsync(Guid agegroupId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.AgegroupId == agegroupId)
            .OrderBy(t => t.DivRank)
            .ThenBy(t => t.TeamName)
            .ToListAsync(cancellationToken);
    }

    public async Task<Teams?> GetByIdReadOnlyAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TeamId == teamId, cancellationToken);
    }

    public async Task<int> GetMaxDivRankAsync(Guid divId, CancellationToken cancellationToken = default)
    {
        var maxRank = await _context.Teams
            .AsNoTracking()
            .Where(t => t.DivId == divId)
            .Select(t => (int?)t.DivRank)
            .MaxAsync(cancellationToken);

        return maxRank ?? 0;
    }

    public async Task<int> GetNextDivRankAsync(Guid divId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.DivId == divId && t.Active == true)
            .CountAsync(cancellationToken) + 1;
    }

    public async Task<bool> HasRosteredPlayersAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .AnyAsync(r => r.AssignedTeamId == teamId && r.BActive == true, cancellationToken);
    }

    public async Task<int> GetPlayerCountAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .CountAsync(r => r.AssignedTeamId == teamId && r.BActive == true, cancellationToken);
    }

    public async Task<Dictionary<Guid, int>> GetPlayerCountsByTeamAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                     && r.BActive == true
                     && r.AssignedTeamId != null
                     && r.RoleId == RoleConstants.Player)
            .GroupBy(r => r.AssignedTeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, cancellationToken);
    }

    public async Task<bool> BelongsToJobAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .AnyAsync(t => t.TeamId == teamId && t.JobId == jobId, cancellationToken);
    }

    public async Task<Dictionary<Guid, string?>> GetClubNamesByJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.ClubrepRegistrationid != null)
            .Join(_context.Registrations,
                t => t.ClubrepRegistrationid,
                r => r.RegistrationId,
                (t, r) => new { t.TeamId, r.ClubName })
            .ToDictionaryAsync(x => x.TeamId, x => x.ClubName, cancellationToken);
    }

    public async Task<Dictionary<Guid, string?>> GetClubRepNamesByJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.ClubrepRegistrationid != null)
            .Join(_context.Registrations,
                t => t.ClubrepRegistrationid,
                r => r.RegistrationId,
                (t, r) => new { t.TeamId, r.UserId })
            .Join(_context.AspNetUsers,
                tr => tr.UserId,
                u => u.Id,
                (tr, u) => new { tr.TeamId, u.FirstName, u.LastName })
            .ToDictionaryAsync(
                x => x.TeamId,
                x => string.IsNullOrWhiteSpace(x.FirstName) && string.IsNullOrWhiteSpace(x.LastName)
                    ? null
                    : $"{x.FirstName} {x.LastName}".Trim(),
                cancellationToken);
    }

    public async Task<string?> GetClubNameForTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId && t.ClubrepRegistrationid != null)
            .Join(_context.Registrations,
                t => t.ClubrepRegistrationid,
                r => r.RegistrationId,
                (t, r) => r.ClubName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsTeamScheduledAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .AnyAsync(s => s.JobId == jobId && (s.T1Id == teamId || s.T2Id == teamId), cancellationToken);
    }

    public async Task<HashSet<Guid>> GetScheduledTeamIdsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var t1Ids = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.T1Id != null)
            .Select(s => s.T1Id!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var t2Ids = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.T2Id != null)
            .Select(s => s.T2Id!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var result = new HashSet<Guid>(t1Ids);
        result.UnionWith(t2Ids);
        return result;
    }

    public async Task<List<Teams>> GetTeamsByClubRepRegistrationAsync(Guid jobId, Guid clubRepRegistrationId, CancellationToken ct = default)
    {
        return await _context.Teams
            .Where(t => t.JobId == jobId && t.ClubrepRegistrationid == clubRepRegistrationId)
            .ToListAsync(ct);
    }

    // ── Club Roster methods ──

    public async Task<List<ClubRosterTeamDto>> GetClubRosterTeamsAsync(Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                && t.ClubrepRegistrationid == clubRepRegistrationId
                && t.Agegroup != null
                && t.Agegroup.AgegroupName != "Dropped Teams")
            .Select(t => new ClubRosterTeamDto
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName ?? "",
                AgegroupName = t.Agegroup != null ? t.Agegroup.AgegroupName ?? "" : "",
                PlayerCount = _context.Registrations
                    .Count(r => r.AssignedTeamId == t.TeamId
                        && r.JobId == jobId
                        && r.BActive == true
                        && r.RoleId == RoleConstants.Player)
            })
            .OrderBy(t => t.AgegroupName)
            .ThenBy(t => t.TeamName)
            .ToListAsync(ct);
    }

    // ── Pool Assignment methods ──

    public async Task<List<Contracts.Dtos.PoolAssignment.PoolTeamDto>> GetPoolAssignmentTeamsAsync(
        Guid divId, Guid jobId, CancellationToken ct = default)
    {
        var clubNames = await GetClubNamesByJobAsync(jobId, ct);
        var clubRepNames = await GetClubRepNamesByJobAsync(jobId, ct);
        var scheduledTeamIds = await GetScheduledTeamIdsAsync(jobId, ct);

        var rawTeams = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.DivId == divId)
            .Select(t => new
            {
                t.TeamId,
                TeamName = t.TeamName ?? "Unnamed Team",
                t.LevelOfPlay,
                t.TeamComments,
                t.NationalRankingData,
                t.Createdate,
                t.AgegroupId,
                AgegroupName = t.Agegroup.AgegroupName ?? "",
                DivId = t.DivId,
                DivName = t.Div != null ? t.Div.DivName : null,
                Active = t.Active ?? true,
                t.DivRank,
                t.MaxCount,
                FeeBase = t.FeeBase ?? 0m,
                FeeTotal = t.FeeTotal ?? 0m,
                OwedTotal = t.OwedTotal ?? 0m,
                RosterCount = _context.Registrations.Count(r => r.AssignedTeamId == t.TeamId && r.JobId == jobId)
            })
            .OrderBy(t => t.DivRank)
            .ThenBy(t => t.TeamName)
            .ToListAsync(ct);

        return rawTeams.Select(t => new Contracts.Dtos.PoolAssignment.PoolTeamDto
        {
            TeamId = t.TeamId,
            TeamName = t.TeamName,
            ClubName = clubNames.GetValueOrDefault(t.TeamId),
            ClubRepName = clubRepNames.GetValueOrDefault(t.TeamId),
            LevelOfPlay = t.LevelOfPlay,
            RegistrationTs = t.Createdate,
            TeamComments = t.TeamComments,
            NationalRankingData = t.NationalRankingData,
            Active = t.Active,
            DivRank = t.DivRank,
            RosterCount = t.RosterCount,
            MaxCount = t.MaxCount,
            FeeBase = t.FeeBase,
            FeeTotal = t.FeeTotal,
            OwedTotal = t.OwedTotal,
            IsScheduled = scheduledTeamIds.Contains(t.TeamId),
            AgegroupId = t.AgegroupId,
            AgegroupName = t.AgegroupName,
            DivId = t.DivId,
            DivName = t.DivName
        }).ToList();
    }

    public async Task<List<Teams>> GetTeamsForPoolTransferAsync(
        List<Guid> teamIds, Guid jobId, CancellationToken ct = default)
    {
        return await _context.Teams
            .Include(t => t.Agegroup)
            .Include(t => t.Div)
            .Include(t => t.Job)
            .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
            .ToListAsync(ct);
    }

    public async Task RenumberDivRanksAsync(Guid divId, CancellationToken ct = default)
    {
        var teams = await _context.Teams
            .Where(t => t.DivId == divId && t.Active == true)
            .OrderBy(t => t.DivRank)
            .ThenBy(t => t.TeamName)
            .ToListAsync(ct);

        for (int i = 0; i < teams.Count; i++)
        {
            teams[i].DivRank = i + 1;
        }

        if (teams.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    public async Task<Teams?> GetTeamByDivRankAsync(Guid divId, int divRank, CancellationToken ct = default)
    {
        return await _context.Teams
            .Where(t => t.DivId == divId && t.DivRank == divRank && t.Active == true)
            .FirstOrDefaultAsync(ct);
    }

    // ── Roster Swapper methods ──

    public async Task<List<SwapperPoolOptionDto>> GetSwapperPoolOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Get club names for teams that have a club rep (TeamId → ClubName)
        var clubNames = await GetClubNamesByJobAsync(jobId, ct);

        // Get all teams with agegroup/division names and roster counts
        var rawTeams = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId)
            .Select(t => new
            {
                t.TeamId,
                TeamName = t.TeamName ?? "Unnamed Team",
                AgegroupName = t.Agegroup.AgegroupName ?? string.Empty,
                DivName = t.Div != null ? t.Div.DivName : null,
                t.AgegroupId,
                t.DivId,
                RosterCount = _context.Registrations.Count(r => r.AssignedTeamId == t.TeamId && r.JobId == jobId),
                t.MaxCount,
                Active = t.Active ?? true
            })
            .OrderBy(t => t.AgegroupName)
            .ThenBy(t => t.DivName)
            .ThenBy(t => t.TeamName)
            .ToListAsync(ct);

        // Compose display name: "{ClubName}:{TeamName}" when club name is available
        var teams = rawTeams.Select(t =>
        {
            var clubName = clubNames.GetValueOrDefault(t.TeamId);
            return new SwapperPoolOptionDto
            {
                PoolId = t.TeamId,
                PoolName = !string.IsNullOrEmpty(clubName) ? $"{clubName}:{t.TeamName}" : t.TeamName,
                IsUnassignedAdultsPool = false,
                AgegroupName = t.AgegroupName,
                DivName = t.DivName,
                AgegroupId = t.AgegroupId,
                DivId = t.DivId,
                RosterCount = t.RosterCount,
                MaxCount = t.MaxCount,
                Active = t.Active
            };
        }).ToList();

        // Add synthetic Unassigned Adults pool entry
        var unassignedCount = await _context.Registrations
            .AsNoTracking()
            .CountAsync(r => r.JobId == jobId && r.Role!.Name == RoleConstants.Names.UnassignedAdultName, ct);

        var unassignedPool = new SwapperPoolOptionDto
        {
            PoolId = Guid.Empty,
            PoolName = "Unassigned Adults",
            IsUnassignedAdultsPool = true,
            AgegroupName = null,
            DivName = null,
            AgegroupId = null,
            DivId = null,
            RosterCount = unassignedCount,
            MaxCount = 0,
            Active = true
        };

        var result = new List<SwapperPoolOptionDto>(teams.Count + 1) { unassignedPool };
        result.AddRange(teams);
        return result;
    }

    public async Task<(Teams Team, Agegroups Agegroup)?> GetTeamWithFeeContextAsync(Guid teamId, CancellationToken ct = default)
    {
        var team = await _context.Teams
            .AsNoTracking()
            .Include(t => t.Agegroup)
            .FirstOrDefaultAsync(t => t.TeamId == teamId, ct);

        if (team == null) return null;
        return (team, team.Agegroup);
    }

    // ── Team Search methods ──

    public async Task<List<TeamSearchResultDto>> SearchTeamsAsync(Guid jobId, TeamSearchRequest request, CancellationToken ct = default)
    {
        var query = _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId)
            .Join(_context.Agegroups, t => t.AgegroupId, ag => ag.AgegroupId, (t, ag) => new { t, ag })
            .GroupJoin(_context.Divisions, x => x.t.DivId, d => d.DivId, (x, divs) => new { x.t, x.ag, divs })
            .SelectMany(x => x.divs.DefaultIfEmpty(), (x, d) => new { x.t, x.ag, d })
            .GroupJoin(_context.Registrations, x => x.t.ClubrepRegistrationid, r => r.RegistrationId, (x, regs) => new { x.t, x.ag, x.d, regs })
            .SelectMany(x => x.regs.DefaultIfEmpty(), (x, r) => new { x.t, x.ag, x.d, r })
            .GroupJoin(_context.AspNetUsers, x => x.r != null ? x.r.UserId : null, u => u.Id, (x, users) => new { x.t, x.ag, x.d, x.r, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new { x.t, x.ag, x.d, x.r, u });

        // Apply filters
        if (request.ActiveStatuses?.Count > 0)
        {
            var boolValues = request.ActiveStatuses.Select(s => bool.TryParse(s, out var b) && b).ToList();
            if (boolValues.Count == 1)
                query = query.Where(x => (x.t.Active ?? false) == boolValues[0]);
        }

        if (request.ClubNames?.Count > 0)
            query = query.Where(x => x.r != null && request.ClubNames.Contains(x.r.ClubName!));

        if (request.LevelOfPlays?.Count > 0)
            query = query.Where(x => x.t.LevelOfPlay != null && request.LevelOfPlays.Contains(x.t.LevelOfPlay));

        if (request.PayStatuses?.Count > 0)
        {
            query = query.Where(x =>
                (request.PayStatuses.Contains("PAID IN FULL") && x.t.OwedTotal == 0)
                || (request.PayStatuses.Contains("UNDER PAID") && x.t.OwedTotal > 0)
                || (request.PayStatuses.Contains("OVER PAID") && x.t.OwedTotal < 0)
                || (request.PayStatuses.Contains("AUTOPAY FAILED")
                    && x.t.AdnSubscriptionId != null
                    && (x.t.OwedTotal ?? 0m) > 0m
                    && (
                        (x.t.AdnSubscriptionStatus != null && x.t.AdnSubscriptionStatus != "active")
                        || _context.RegistrationAccounting.Any(a =>
                            a.TeamId == x.t.TeamId
                            && a.PaymentMethodId == FailedEcheckPaymentMethodId
                            && a.Active == true)
                    )));
        }

        // Payment Method / Discount Code combined filter
        // "dc:*" = any discount code; "dc:CODE" = specific code; others = payment method names
        if (request.PaymentTypes is { Count: > 0 })
        {
            var payMethods = request.PaymentTypes.Where(v => !v.StartsWith("dc:")).ToList();
            var dcValues = request.PaymentTypes.Where(v => v.StartsWith("dc:")).Select(v => v[3..]).ToList();
            var allDc = dcValues.Contains("*");
            var specificDcCodes = dcValues.Where(v => v != "*").ToList();

            if (payMethods.Count > 0 && (allDc || specificDcCodes.Count > 0))
            {
                query = query.Where(x =>
                    _context.RegistrationAccounting.Any(a => a.TeamId == x.t.TeamId && a.Active == true && payMethods.Contains(a.PaymentMethod.PaymentMethod!))
                    || (allDc && x.t.DiscountCode != null)
                    || (specificDcCodes.Count > 0 && x.t.DiscountCode != null && specificDcCodes.Contains(x.t.DiscountCode.CodeName)));
            }
            else if (payMethods.Count > 0)
            {
                query = query.Where(x =>
                    _context.RegistrationAccounting.Any(a => a.TeamId == x.t.TeamId && a.Active == true && payMethods.Contains(a.PaymentMethod.PaymentMethod!)));
            }
            else if (allDc)
            {
                query = query.Where(x => x.t.DiscountCode != null);
            }
            else if (specificDcCodes.Count > 0)
            {
                query = query.Where(x =>
                    x.t.DiscountCode != null && specificDcCodes.Contains(x.t.DiscountCode.CodeName));
            }
        }

        if (request.AgegroupIds?.Count > 0)
            query = query.Where(x => request.AgegroupIds.Contains(x.t.AgegroupId));

        // LADT tree filter — OR across league, agegroup, division, team IDs
        if (request.LeagueIds?.Count > 0 || request.DivisionIds?.Count > 0 || request.TeamIds?.Count > 0)
        {
            var leagueIds = request.LeagueIds ?? new List<Guid>();
            var divisionIds = request.DivisionIds ?? new List<Guid>();
            var teamIds = request.TeamIds ?? new List<Guid>();
            var agegroupIds = request.AgegroupIds ?? new List<Guid>();

            query = query.Where(x =>
                leagueIds.Contains(x.t.LeagueId)
                || agegroupIds.Contains(x.t.AgegroupId)
                || (x.t.DivId != null && divisionIds.Contains(x.t.DivId.Value))
                || teamIds.Contains(x.t.TeamId));
        }

        // CADT tree filter (team ownership via ClubRepRegistrationId)
        if (request.CadtTeamIds is { Count: > 0 })
            query = query.Where(x => request.CadtTeamIds.Contains(x.t.TeamId));

        // Waitlist / Scheduled status (single-value DDL)
        if (!string.IsNullOrEmpty(request.WaitlistScheduledStatus))
        {
            switch (request.WaitlistScheduledStatus)
            {
                case "WAITLISTED":
                    query = query.Where(x => x.ag.AgegroupName != null && x.ag.AgegroupName.Contains("WAITLIST"));
                    break;
                case "NOT_WAITLISTED":
                    query = query.Where(x => x.ag.AgegroupName == null || !x.ag.AgegroupName.Contains("WAITLIST"));
                    break;
                case "SCHEDULED":
                    var scheduledIds = await GetScheduledTeamIdsAsync(jobId, ct);
                    query = query.Where(x => scheduledIds.Contains(x.t.TeamId));
                    break;
                case "NOT_SCHEDULED":
                    var scheduledIds2 = await GetScheduledTeamIdsAsync(jobId, ct);
                    query = query.Where(x => !scheduledIds2.Contains(x.t.TeamId));
                    break;
            }
        }

        var rows = await query
            .OrderBy(x => x.r != null ? x.r.ClubName : "")
            .ThenBy(x => x.ag.AgegroupName)
            .ThenBy(x => x.d != null ? x.d.DivName : "")
            .ThenBy(x => x.t.TeamName)
            .Select(x => new
            {
                Dto = new TeamSearchResultDto
                {
                    TeamId = x.t.TeamId,
                    Active = x.t.Active ?? false,
                    ClubName = x.r != null ? x.r.ClubName : null,
                    TeamName = x.t.TeamName ?? "",
                    AgegroupName = x.ag.AgegroupName ?? "",
                    DivName = x.d != null ? x.d.DivName : null,
                    LevelOfPlay = x.t.LevelOfPlay,
                    PaidTotal = x.t.PaidTotal ?? 0,
                    OwedTotal = x.t.OwedTotal ?? 0,
                    RegDate = x.t.Createdate,
                    ClubRepName = x.u != null ? (x.u.LastName + ", " + x.u.FirstName) : null,
                    ClubRepEmail = x.u != null ? x.u.Email : null,
                    ClubRepCellphone = x.u != null ? x.u.Cellphone.FormatPhone() : null,
                    ClubRepEmailOptOut = x.r != null && (x.r.BemailOptOut),
                    TeamComments = x.t.TeamComments
                },
                AdnSubId = x.t.AdnSubscriptionId,
                AdnStatus = x.t.AdnSubscriptionStatus,
                AdnStart = x.t.AdnSubscriptionStartDate,
                AdnInterval = x.t.AdnSubscriptionIntervalLength,
                AdnOccurrences = x.t.AdnSubscriptionBillingOccurences,
                HasNsfReversal = _context.RegistrationAccounting.Any(a =>
                    a.TeamId == x.t.TeamId
                    && a.PaymentMethodId == FailedEcheckPaymentMethodId
                    && a.Active == true)
            })
            .ToListAsync(ct);

        var today = DateTime.Today;
        return rows.Select(r =>
        {
            var (scheduled, nextDate) = ArbScheduleHelper.ComputeDayBasedSchedule(
                r.AdnSubId, r.AdnStatus, r.AdnStart, r.AdnInterval, r.AdnOccurrences, today);
            // NSF reversal trumps a still-"active" sub status — Phase 5's badge would
            // otherwise misread these as "Scheduled" until ADN gets around to suspending.
            if (r.HasNsfReversal) scheduled = false;

            var statusBroken = !string.IsNullOrEmpty(r.AdnStatus)
                && !string.Equals(r.AdnStatus, "active", StringComparison.OrdinalIgnoreCase);
            var flagged = !string.IsNullOrEmpty(r.AdnSubId)
                && r.Dto.OwedTotal > 0m
                && (statusBroken || r.HasNsfReversal);

            return r.Dto with
            {
                PaymentScheduled = scheduled,
                NextChargeDate = nextDate,
                PaymentFlagged = flagged
            };
        }).ToList();
    }

    public async Task<TeamFilterOptionsDto> GetTeamSearchFilterOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        var baseQuery = _context.Teams.AsNoTracking().Where(t => t.JobId == jobId);

        // Clubs with counts
        var clubs = await baseQuery
            .Where(t => t.ClubrepRegistrationid != null)
            .Join(_context.Registrations, t => t.ClubrepRegistrationid, r => r.RegistrationId, (t, r) => r.ClubName)
            .Where(cn => cn != null)
            .GroupBy(cn => cn!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Level of Play with counts
        var lops = await baseQuery
            .Where(t => t.LevelOfPlay != null && t.LevelOfPlay != "")
            .GroupBy(t => t.LevelOfPlay!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Age groups with counts
        var ageGroups = await baseQuery
            .Join(_context.Agegroups, t => t.AgegroupId, ag => ag.AgegroupId, (t, ag) => ag)
            .GroupBy(ag => new { ag.AgegroupId, ag.AgegroupName })
            .OrderBy(g => g.Key.AgegroupName)
            .Select(g => new FilterOption { Value = g.Key.AgegroupId.ToString(), Text = g.Key.AgegroupName ?? "", Count = g.Count() })
            .ToListAsync(ct);

        // Active statuses with counts
        var activeStatuses = await baseQuery
            .GroupBy(t => t.Active ?? false)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption
            {
                Value = g.Key.ToString(),
                Text = g.Key ? "Active" : "Inactive",
                Count = g.Count(),
                DefaultChecked = g.Key // Active pre-checked
            })
            .ToListAsync(ct);

        // Pay statuses with counts
        var payStatuses = await baseQuery
            .Where(t => t.Active == true)
            .Select(t => t.OwedTotal == 0 ? "PAID IN FULL" : t.OwedTotal > 0 ? "UNDER PAID" : "OVER PAID")
            .GroupBy(s => s)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // "Autopay Failed" is a triage cut of UNDER PAID — same set, narrower lens.
        // Surfaced as its own filter so admins can quickly land on the queue.
        var autopayFailedCount = await baseQuery
            .Where(t => t.Active == true
                && t.AdnSubscriptionId != null
                && (t.OwedTotal ?? 0m) > 0m
                && (
                    (t.AdnSubscriptionStatus != null && t.AdnSubscriptionStatus != "active")
                    || _context.RegistrationAccounting.Any(a =>
                        a.TeamId == t.TeamId
                        && a.PaymentMethodId == FailedEcheckPaymentMethodId
                        && a.Active == true)
                ))
            .CountAsync(ct);

        if (autopayFailedCount > 0)
        {
            payStatuses.Add(new FilterOption
            {
                Value = "AUTOPAY FAILED",
                Text = "AUTOPAY FAILED",
                Count = autopayFailedCount
            });
        }

        // Waitlist / Scheduled status counts (active teams only)
        var activeTeams = baseQuery.Where(t => t.Active == true);

        var waitlistedCount = await activeTeams
            .Join(_context.Agegroups, t => t.AgegroupId, ag => ag.AgegroupId, (t, ag) => ag.AgegroupName)
            .CountAsync(name => name != null && name.Contains("WAITLIST"), ct);

        var activeTotal = await activeTeams.CountAsync(ct);
        var notWaitlistedCount = activeTotal - waitlistedCount;

        var scheduledTeamIds = await GetScheduledTeamIdsAsync(jobId, ct);
        var activeTeamIds = await activeTeams.Select(t => t.TeamId).ToListAsync(ct);
        var scheduledCount = activeTeamIds.Count(id => scheduledTeamIds.Contains(id));
        var notScheduledCount = activeTotal - scheduledCount;

        var waitlistScheduledStatuses = new List<FilterOption>
        {
            new() { Value = "WAITLISTED", Text = "Waitlisted", Count = waitlistedCount },
            new() { Value = "NOT_WAITLISTED", Text = "Non-Waitlisted", Count = notWaitlistedCount },
            new() { Value = "SCHEDULED", Text = "Scheduled", Count = scheduledCount },
            new() { Value = "NOT_SCHEDULED", Text = "Not Scheduled", Count = notScheduledCount }
        };

        // Payment methods — from accounting records
        var paymentMethods = await _context.RegistrationAccounting
            .AsNoTracking()
            .Where(a => a.Team != null && a.Team.JobId == jobId && a.Active == true)
            .GroupBy(a => a.PaymentMethod.PaymentMethod!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Discount codes — from team entity with detail (%, flat, amount)
        var dcRaw = await baseQuery
            .Where(t => t.DiscountCode != null)
            .GroupBy(t => new { t.DiscountCode!.CodeName, t.DiscountCode.BAsPercent, t.DiscountCode.CodeAmount })
            .OrderBy(g => g.Key.CodeName)
            .Select(g => new { g.Key.CodeName, g.Key.BAsPercent, g.Key.CodeAmount, Count = g.Count() })
            .ToListAsync(ct);

        var discountCodeOptions = dcRaw.Select(dc =>
        {
            var detail = dc.BAsPercent
                ? $"{dc.CodeAmount:0}%"
                : $"${dc.CodeAmount:0.00}";
            return new FilterOption { Value = "dc:" + dc.CodeName, Text = $"DC: {dc.CodeName} ({detail})", Count = dc.Count };
        }).ToList();

        var totalDcCount = dcRaw.Sum(dc => dc.Count);
        var paymentTypes = paymentMethods.ToList();
        if (discountCodeOptions.Count > 0)
        {
            paymentTypes.Add(new FilterOption { Value = "dc:*", Text = $"ALL Discount Codes", Count = totalDcCount });
            paymentTypes.AddRange(discountCodeOptions);
        }

        return new TeamFilterOptionsDto
        {
            Clubs = clubs,
            LevelOfPlays = lops,
            AgeGroups = ageGroups,
            ActiveStatuses = activeStatuses,
            PayStatuses = payStatuses,
            PaymentTypes = paymentTypes,
            WaitlistScheduledStatuses = waitlistScheduledStatuses
        };
    }

    public async Task<TeamDetailQueryResult?> GetTeamDetailAsync(Guid teamId, CancellationToken ct = default)
    {
        var row = await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Join(_context.Agegroups, t => t.AgegroupId, ag => ag.AgegroupId, (t, ag) => new { t, ag })
            .GroupJoin(_context.Divisions, x => x.t.DivId, d => d.DivId, (x, divs) => new { x.t, x.ag, divs })
            .SelectMany(x => x.divs.DefaultIfEmpty(), (x, d) => new { x.t, x.ag, d })
            .GroupJoin(_context.Registrations, x => x.t.ClubrepRegistrationid, r => r.RegistrationId, (x, regs) => new { x.t, x.ag, x.d, regs })
            .SelectMany(x => x.regs.DefaultIfEmpty(), (x, r) => new { x.t, x.ag, x.d, r })
            .GroupJoin(_context.AspNetUsers, x => x.r != null ? x.r.UserId : null, u => u.Id, (x, users) => new { x.t, x.ag, x.d, x.r, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new { x.t, x.ag, x.d, x.r, u })
            .Select(x => new
            {
                Result = new TeamDetailQueryResult
                {
                    TeamId = x.t.TeamId,
                    TeamName = x.t.TeamName ?? "",
                    ClubName = x.r != null ? x.r.ClubName : null,
                    AgegroupName = x.ag.AgegroupName ?? "",
                    DivName = x.d != null ? x.d.DivName : null,
                    LevelOfPlay = x.t.LevelOfPlay,
                    Active = x.t.Active ?? false,
                    FeeBase = x.t.FeeBase ?? 0,
                    FeeProcessing = x.t.FeeProcessing ?? 0,
                    FeeTotal = x.t.FeeTotal ?? 0,
                    PaidTotal = x.t.PaidTotal ?? 0,
                    OwedTotal = x.t.OwedTotal ?? 0,
                    TeamComments = x.t.TeamComments,
                    ClubRepRegistrationId = x.t.ClubrepRegistrationid,
                    ClubRepName = x.u != null ? (x.u.LastName + ", " + x.u.FirstName) : null,
                    ClubRepEmail = x.u != null ? x.u.Email : null,
                    ClubRepCellphone = x.u != null ? x.u.Cellphone.FormatPhone() : null,
                    ClubRepStreetAddress = x.u != null ? x.u.StreetAddress : null,
                    ClubRepCity = x.u != null ? x.u.City : null,
                    ClubRepState = x.u != null ? x.u.State : null,
                    ClubRepPostalCode = x.u != null ? x.u.PostalCode : null,
                    JobId = x.t.JobId
                },
                AdnSubId = x.t.AdnSubscriptionId,
                AdnStatus = x.t.AdnSubscriptionStatus,
                AdnStart = x.t.AdnSubscriptionStartDate,
                AdnInterval = x.t.AdnSubscriptionIntervalLength,
                AdnOccurrences = x.t.AdnSubscriptionBillingOccurences,
                HasNsfReversal = _context.RegistrationAccounting.Any(a =>
                    a.TeamId == x.t.TeamId
                    && a.PaymentMethodId == FailedEcheckPaymentMethodId
                    && a.Active == true)
            })
            .FirstOrDefaultAsync(ct);

        if (row == null) return null;

        var (scheduled, nextDate) = ArbScheduleHelper.ComputeDayBasedSchedule(
            row.AdnSubId, row.AdnStatus, row.AdnStart, row.AdnInterval, row.AdnOccurrences, DateTime.Today);
        if (row.HasNsfReversal) scheduled = false;

        var statusBroken = !string.IsNullOrEmpty(row.AdnStatus)
            && !string.Equals(row.AdnStatus, "active", StringComparison.OrdinalIgnoreCase);
        var flagged = !string.IsNullOrEmpty(row.AdnSubId)
            && row.Result.OwedTotal > 0m
            && (statusBroken || row.HasNsfReversal);

        return row.Result with
        {
            PaymentScheduled = scheduled,
            NextChargeDate = nextDate,
            PaymentFlagged = flagged
        };
    }

    public async Task<int> GetDistinctClubCountAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.ClubrepRegistrationid != null)
            .Select(t => t.ClubrepRegistrationid)
            .Distinct()
            .CountAsync(ct);
    }

    public async Task<List<Teams>> GetActiveClubTeamsOrderedByOwedAsync(Guid jobId, Guid clubRepRegistrationId, CancellationToken ct = default)
    {
        return await _context.Teams
            .Include(t => t.Agegroup)
            .Include(t => t.Job)
            .Where(t => t.JobId == jobId
                && t.ClubrepRegistrationid == clubRepRegistrationId
                && t.Active == true
                && t.Agegroup != null
                && t.Agegroup!.AgegroupName != null
                && !t.Agegroup!.AgegroupName.Contains("WAITLIST")
                && !t.Agegroup!.AgegroupName.Contains("DROPPED"))
            .OrderByDescending(t => t.OwedTotal)
            .ToListAsync(ct);
    }

    public async Task<List<ClubTeamSummaryDto>> GetClubTeamSummariesAsync(Guid jobId, Guid clubRepRegistrationId, CancellationToken ct = default)
    {
        var rows = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                && t.ClubrepRegistrationid == clubRepRegistrationId)
            .Join(_context.Agegroups, t => t.AgegroupId, ag => ag.AgegroupId, (t, ag) => new { t, ag })
            .OrderBy(x => x.ag.AgegroupName)
            .ThenBy(x => x.t.TeamName)
            .Select(x => new
            {
                Dto = new ClubTeamSummaryDto
                {
                    TeamId = x.t.TeamId,
                    TeamName = x.t.TeamName ?? "",
                    AgegroupName = x.ag.AgegroupName ?? "",
                    FeeTotal = x.t.FeeTotal ?? 0,
                    PaidTotal = x.t.PaidTotal ?? 0,
                    OwedTotal = x.t.OwedTotal ?? 0,
                    FeeProcessing = x.t.FeeProcessing ?? 0,
                    Active = x.t.Active ?? false
                },
                AdnSubId = x.t.AdnSubscriptionId,
                AdnStatus = x.t.AdnSubscriptionStatus,
                AdnStart = x.t.AdnSubscriptionStartDate,
                AdnInterval = x.t.AdnSubscriptionIntervalLength,
                AdnOccurrences = x.t.AdnSubscriptionBillingOccurences,
                HasNsfReversal = _context.RegistrationAccounting.Any(a =>
                    a.TeamId == x.t.TeamId
                    && a.PaymentMethodId == FailedEcheckPaymentMethodId
                    && a.Active == true)
            })
            .ToListAsync(ct);

        var today = DateTime.Today;
        return rows.Select(r =>
        {
            var (scheduled, nextDate) = ArbScheduleHelper.ComputeDayBasedSchedule(
                r.AdnSubId, r.AdnStatus, r.AdnStart, r.AdnInterval, r.AdnOccurrences, today);
            if (r.HasNsfReversal) scheduled = false;

            var statusBroken = !string.IsNullOrEmpty(r.AdnStatus)
                && !string.Equals(r.AdnStatus, "active", StringComparison.OrdinalIgnoreCase);
            var flagged = !string.IsNullOrEmpty(r.AdnSubId)
                && r.Dto.OwedTotal > 0m
                && (statusBroken || r.HasNsfReversal);

            return r.Dto with
            {
                PaymentScheduled = scheduled,
                NextChargeDate = nextDate,
                PaymentFlagged = flagged
            };
        }).ToList();
    }

    public async Task<List<TeamResendProbe>> FindFlaggedTeamsForResendAsync(
        Guid jobId, List<Guid>? teamIds, CancellationToken ct = default)
    {
        var query = _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                && (t.Active ?? false)
                && t.AdnSubscriptionId != null
                && t.ClubrepRegistrationid != null
                && (t.OwedTotal ?? 0m) > 0m
                && (
                    (t.AdnSubscriptionStatus != null && t.AdnSubscriptionStatus != "active")
                    || _context.RegistrationAccounting.Any(a =>
                        a.TeamId == t.TeamId
                        && a.PaymentMethodId == FailedEcheckPaymentMethodId
                        && a.Active == true)
                ));

        if (teamIds is { Count: > 0 })
            query = query.Where(t => teamIds.Contains(t.TeamId));

        return await query
            .Join(_context.Agegroups, t => t.AgegroupId, ag => ag.AgegroupId, (t, ag) => new { t, ag })
            .GroupJoin(_context.Registrations, x => x.t.ClubrepRegistrationid, r => r.RegistrationId, (x, regs) => new { x.t, x.ag, regs })
            .SelectMany(x => x.regs.DefaultIfEmpty(), (x, r) => new { x.t, x.ag, r })
            .GroupJoin(_context.AspNetUsers, x => x.r != null ? x.r.UserId : null, u => u.Id, (x, users) => new { x.t, x.ag, x.r, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new TeamResendProbe
            {
                TeamId = x.t.TeamId,
                TeamName = x.t.TeamName ?? "",
                AgegroupName = x.ag.AgegroupName ?? "",
                OwedTotal = x.t.OwedTotal ?? 0m,
                RepRegistrationId = x.t.ClubrepRegistrationid!.Value,
                RepEmail = u != null ? u.Email : null,
                RepFirstName = u != null ? u.FirstName : null,
                RepLastName = u != null ? u.LastName : null,
                RepEmailOptOut = x.r != null && x.r.BemailOptOut
            })
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, int>> GetTeamCountsByDivisionAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.Active == true && t.DivId.HasValue)
            .GroupBy(t => t.DivId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
    }

    // ── Store Walk-Up methods ──

    public async Task<Guid?> GetStoreMerchTeamIdAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await (
            from t in _context.Teams.AsNoTracking()
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            where t.JobId == jobId
                  && t.TeamName == "Store Merch"
                  && ag.AgegroupName == "Dropped Teams"
            select (Guid?)t.TeamId
        ).FirstOrDefaultAsync(cancellationToken);
    }

    // ── US Lacrosse Rankings ──

    public async Task<List<RankingsTeamDto>> GetTeamsForRankingsAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                        && t.Active == true
                        && t.AgegroupId == agegroupId)
            .Select(t => new RankingsTeamDto
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName ?? "",
                AgeGroup = t.Agegroup!.AgegroupName,
                ClubName = t.ClubrepRegistration != null ? t.ClubrepRegistration.ClubName ?? "" : "",
                Color = t.Color ?? "",
                AgegroupName = t.Agegroup.AgegroupName ?? "",
                GradYearMin = t.Agegroup.GradYearMin,
                GradYearMax = t.Agegroup.GradYearMax,
                NationalRankingData = t.NationalRankingData
            })
            .ToListAsync(ct);
    }

    public async Task<int> BulkUpdateTeamCommentsAsync(
        Dictionary<Guid, string?> teamComments, CancellationToken ct = default)
    {
        if (teamComments.Count == 0) return 0;

        var teamIds = teamComments.Keys.ToList();
        var teams = await _context.Teams
            .Where(t => teamIds.Contains(t.TeamId))
            .ToListAsync(ct);

        foreach (var team in teams)
        {
            if (teamComments.TryGetValue(team.TeamId, out var comment))
                team.TeamComments = comment;
        }

        return await _context.SaveChangesAsync(ct);
    }

    public async Task<int> ClearTeamCommentsForAgegroupAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var teams = await _context.Teams
            .Where(t => t.JobId == jobId
                        && t.Active == true
                        && t.AgegroupId == agegroupId
                        && t.TeamComments != null)
            .ToListAsync(ct);

        foreach (var team in teams)
            team.TeamComments = null;

        if (teams.Count > 0)
            await _context.SaveChangesAsync(ct);

        return teams.Count;
    }

    public async Task<int> BulkUpdateNationalRankingDataAsync(
        Dictionary<Guid, string?> rankingData, CancellationToken ct = default)
    {
        if (rankingData.Count == 0) return 0;

        var teamIds = rankingData.Keys.ToList();
        var teams = await _context.Teams
            .Where(t => teamIds.Contains(t.TeamId))
            .ToListAsync(ct);

        foreach (var team in teams)
        {
            if (rankingData.TryGetValue(team.TeamId, out var data))
                team.NationalRankingData = data;
        }

        return await _context.SaveChangesAsync(ct);
    }

    public async Task<int> ClearNationalRankingDataForAgegroupAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var teams = await _context.Teams
            .Where(t => t.JobId == jobId
                        && t.Active == true
                        && t.AgegroupId == agegroupId
                        && t.NationalRankingData != null)
            .ToListAsync(ct);

        foreach (var team in teams)
            team.NationalRankingData = null;

        if (teams.Count > 0)
            await _context.SaveChangesAsync(ct);

        return teams.Count;
    }

    public async Task<TeamRosterDetailDto> GetTeamRosterMobileAsync(Guid teamId, CancellationToken ct = default)
    {
        var registrations = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.AssignedTeamId == teamId && r.BActive == true)
            .Include(r => r.User)
            .Include(r => r.FamilyUser)
            .Include(r => r.Role)
            .ToListAsync(ct);

        // Attendance counts per player
        var playerUserIds = registrations
            .Where(r => r.RoleId == RoleConstants.Player)
            .Select(r => r.UserId)
            .Where(id => id != null)
            .ToHashSet();

        var attendanceCounts = await _context.TeamAttendanceRecords
            .AsNoTracking()
            .Where(a => a.Event.TeamId == teamId && playerUserIds.Contains(a.PlayerId))
            .GroupBy(a => a.PlayerId)
            .Select(g => new
            {
                PlayerId = g.Key,
                Present = g.Count(a => a.Present),
                NotPresent = g.Count(a => !a.Present)
            })
            .ToListAsync(ct);

        var attendanceLookup = attendanceCounts.ToDictionary(a => a.PlayerId, a => (a.Present, a.NotPresent));

        var staffRoleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RoleConstants.Staff, RoleConstants.Director, RoleConstants.SuperDirector,
            RoleConstants.ClubRep, RoleConstants.Scorer
        };

        var staff = registrations
            .Where(r => r.RoleId != null && staffRoleIds.Contains(r.RoleId))
            .Select(r => new TeamRosterStaffDto
            {
                FirstName = r.User?.FirstName ?? "",
                LastName = r.User?.LastName ?? "",
                Cellphone = r.User?.Cellphone,
                Email = r.User?.Email,
                HeadshotUrl = null, // headshot URL resolution deferred
                UserName = r.User?.UserName,
                UserId = r.UserId
            })
            .ToList();

        var players = registrations
            .Where(r => r.RoleId == RoleConstants.Player)
            .Select(r =>
            {
                var att = r.UserId != null && attendanceLookup.TryGetValue(r.UserId, out var counts)
                    ? counts : (Present: 0, NotPresent: 0);
                var family = r.FamilyUser;

                return new TeamRosterPlayerDto
                {
                    FirstName = r.User?.FirstName ?? "",
                    LastName = r.User?.LastName ?? "",
                    RoleName = r.Role?.Name,
                    Cellphone = r.User?.Cellphone,
                    Email = r.User?.Email,
                    HeadshotUrl = null, // headshot URL resolution deferred
                    Mom = family != null ? $"{family.MomFirstName} {family.MomLastName}".Trim() : null,
                    MomEmail = family?.MomEmail,
                    MomCellphone = family?.MomCellphone,
                    Dad = family != null ? $"{family.DadFirstName} {family.DadLastName}".Trim() : null,
                    DadEmail = family?.DadEmail,
                    DadCellphone = family?.DadCellphone,
                    UniformNumber = r.UniformNo,
                    City = r.User?.City,
                    School = r.SchoolName,
                    UserName = r.User?.UserName,
                    UserId = r.UserId,
                    CountPresent = att.Present,
                    CountNotPresent = att.NotPresent
                };
            })
            .ToList();

        return new TeamRosterDetailDto { Staff = staff, Players = players };
    }

    // ── Public Roster methods ──

    public async Task<List<CadtClubNode>> GetPublicRosterTreeAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // Flat projection: all active teams excluding WAITLIST/DROPPED agegroups
        var rows = await (
            from t in _context.Teams
            join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
            join div in _context.Divisions on t.DivId equals div.DivId into divJoin
            from div in divJoin.DefaultIfEmpty()
            join clubReg in _context.Registrations on t.ClubrepRegistrationid equals clubReg.RegistrationId into clubJoin
            from clubReg in clubJoin.DefaultIfEmpty()
            where t.JobId == jobId
                  && t.Active == true
                  && (ag.AgegroupName == null || !ag.AgegroupName!.Contains("WAITLIST"))
                  && (ag.AgegroupName == null || !ag.AgegroupName!.Contains("DROPPED"))
            select new
            {
                ClubName = clubReg != null ? clubReg.ClubName : null,
                ag.AgegroupId,
                ag.AgegroupName,
                ag.Color,
                DivId = div != null ? div.DivId : (Guid?)null,
                DivName = div != null ? div.DivName : null,
                t.TeamId,
                t.TeamName,
                PlayerCount = _context.Registrations
                    .Count(r => r.AssignedTeamId == t.TeamId
                                && r.BActive == true
                                && r.RoleId == RoleConstants.Player)
            }
        ).AsNoTracking().ToListAsync(ct);

        // Group: Club → Agegroup → Division → Team
        return rows
            .GroupBy(r => r.ClubName ?? "(No Club)")
            .OrderBy(g => g.Key)
            .Select(clubGroup => new CadtClubNode
            {
                ClubName = clubGroup.Key,
                TeamCount = clubGroup.Count(),
                PlayerCount = clubGroup.Sum(r => r.PlayerCount),
                Agegroups = clubGroup
                    .GroupBy(r => new { r.AgegroupId, r.AgegroupName, r.Color })
                    .OrderBy(g => g.Key.AgegroupName)
                    .Select(agGroup => new CadtAgegroupNode
                    {
                        AgegroupId = agGroup.Key.AgegroupId,
                        AgegroupName = agGroup.Key.AgegroupName,
                        Color = agGroup.Key.Color,
                        TeamCount = agGroup.Count(),
                        PlayerCount = agGroup.Sum(r => r.PlayerCount),
                        Divisions = agGroup
                            .GroupBy(r => new { DivId = r.DivId ?? Guid.Empty, DivName = r.DivName ?? "(No Division)" })
                            .OrderBy(g => g.Key.DivName)
                            .Select(divGroup => new CadtDivisionNode
                            {
                                DivId = divGroup.Key.DivId,
                                DivName = divGroup.Key.DivName,
                                TeamCount = divGroup.Count(),
                                PlayerCount = divGroup.Sum(r => r.PlayerCount),
                                Teams = divGroup
                                    .OrderBy(r => r.TeamName)
                                    .Select(r => new CadtTeamNode
                                    {
                                        TeamId = r.TeamId,
                                        TeamName = r.TeamName ?? string.Empty,
                                        PlayerCount = r.PlayerCount
                                    }).ToList()
                            }).ToList()
                    }).ToList()
            }).ToList();
    }

    public async Task<List<PublicRosterPlayerDto>> GetPublicTeamRosterAsync(
        Guid jobId, Guid teamId, CancellationToken ct = default)
    {
        return await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join t in _context.Teams on r.AssignedTeamId equals t.TeamId
            join clubReg in _context.Registrations on t.ClubrepRegistrationid equals clubReg.RegistrationId into clubJoin
            from clubReg in clubJoin.DefaultIfEmpty()
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            where r.AssignedTeamId == teamId
                  && r.JobId == jobId
                  && r.BActive == true
                  && (
                      (r.RoleId == RoleConstants.Player && r.BWaiverSigned1 == true)
                      || r.RoleId == RoleConstants.Staff
                  )
            orderby role.Name descending, u.LastName, u.FirstName
            select new PublicRosterPlayerDto
            {
                DisplayName = role.Name == "Player"
                    ? $"{u.LastName}, {u.FirstName}"
                    : $"Staff: {u.LastName}, {u.FirstName}",
                RoleLabel = role.Name ?? "Player",
                Position = r.Position,
                UniformNo = r.UniformNo,
                ClubName = clubReg != null ? clubReg.ClubName : null,
                TeamName = t.TeamName
            }
        ).AsNoTracking().ToListAsync(ct);
    }
}

