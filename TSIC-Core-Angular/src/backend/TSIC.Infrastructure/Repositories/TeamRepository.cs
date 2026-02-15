using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Teams entity using Entity Framework Core.
/// </summary>
public class TeamRepository : ITeamRepository
{
    private readonly SqlDbContext _context;

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

    public async Task<List<RegisteredTeamInfo>> GetRegisteredTeamsForClubAndJobAsync(
        Guid jobId,
        int clubId,
        CancellationToken cancellationToken = default)
    {
        // Join Teams → Registrations → ClubReps to filter by ClubId
        return await _context.Teams
            .Where(t => t.JobId == jobId && t.ClubrepRegistrationid != null)
            .Join(_context.Registrations,
                t => t.ClubrepRegistrationid,
                reg => reg.RegistrationId,
                (t, reg) => new { Team = t, Registration = reg })
            .Where(tr => _context.ClubReps.Any(cr => cr.ClubRepUserId == tr.Registration.UserId && cr.ClubId == clubId))
            .Select(tr => new RegisteredTeamInfo
            {
                TeamId = tr.Team.TeamId,
                TeamName = tr.Team.TeamName ?? string.Empty,
                AgeGroupId = tr.Team.AgegroupId,
                AgeGroupName = tr.Team.Agegroup!.AgegroupName ?? string.Empty,
                LevelOfPlay = tr.Team.LevelOfPlay,
                FeeBase = tr.Team.FeeBase ?? 0,
                FeeProcessing = tr.Team.FeeProcessing ?? 0,
                FeeTotal = (tr.Team.FeeBase ?? 0) + (tr.Team.FeeProcessing ?? 0),
                PaidTotal = tr.Team.PaidTotal ?? 0,
                OwedTotal = ((tr.Team.FeeBase ?? 0) + (tr.Team.FeeProcessing ?? 0)) - (tr.Team.PaidTotal ?? 0),
                // DepositDue: RosterFee - PaidTotal (0 if already paid deposit)
                DepositDue = (tr.Team.PaidTotal >= tr.Team.Agegroup.RosterFee) ? 0 : (tr.Team.Agegroup.RosterFee ?? 0) - (tr.Team.PaidTotal ?? 0),
                // AdditionalDue: TeamFee (0 if already fully paid or if full payment required upfront)
                AdditionalDue = (tr.Team.OwedTotal == 0 && (tr.Team.Job.BTeamsFullPaymentRequired ?? false)) ? 0 : (tr.Team.Agegroup.TeamFee ?? 0),
                RegistrationTs = tr.Team.Createdate,
                BWaiverSigned3 = tr.Registration.BWaiverSigned3
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetRegisteredCountForAgegroupAsync(
        Guid jobId,
        Guid agegroupId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .CountAsync(t => t.JobId == jobId && t.AgegroupId == agegroupId, cancellationToken);
    }

    public async Task<Teams?> GetTeamFromTeamId(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams.FindAsync(new object[] { teamId }, cancellationToken);
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
            .Include(t => t.Agegroup)
            .Include(t => t.Div)
            .Where(t => t.JobId == jobId)
            .Where(t => (t.Active ?? true))
            .Where(t => (t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
            .Where(t => (t.Effectiveasofdate == null || t.Effectiveasofdate <= now)
                        && (t.Expireondate == null || t.Expireondate >= now))
            .Select(t => new AvailableTeamQueryResult
            {
                TeamId = t.TeamId,
                Name = t.TeamName ?? t.DisplayName ?? "(Unnamed Team)",
                AgegroupId = t.AgegroupId,
                AgegroupName = t.Agegroup.AgegroupName,
                DivisionId = t.DivId,
                DivisionName = t.Div != null ? t.Div.DivName : null,
                MaxCount = t.MaxCount,
                RawPerRegistrantFee = t.PerRegistrantFee,
                RawPerRegistrantDeposit = t.PerRegistrantDeposit,
                RawTeamFee = t.Agegroup.TeamFee,
                RawRosterFee = t.Agegroup.RosterFee,
                TeamAllowsSelfRostering = t.BAllowSelfRostering,
                AgegroupAllowsSelfRostering = t.Agegroup.BAllowSelfRostering,
                LeaguePlayerFeeOverride = t.League.PlayerFeeOverride,
                AgegroupPlayerFeeOverride = t.Agegroup.PlayerFeeOverride
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<TeamFeeData?> GetTeamFeeDataAsync(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Include(t => t.Agegroup)
            .Where(t => t.TeamId == teamId)
            .Select(t => new TeamFeeData
            {
                PerRegistrantFee = t.PerRegistrantFee,
                PerRegistrantDeposit = t.PerRegistrantDeposit,
                TeamFee = t.Agegroup.TeamFee,
                RosterFee = t.Agegroup.RosterFee,
                LeaguePlayerFeeOverride = t.League.PlayerFeeOverride,
                AgegroupPlayerFeeOverride = t.Agegroup.PlayerFeeOverride
            })
            .SingleOrDefaultAsync(cancellationToken);
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
            .Include(t => t.Agegroup)
            .Include(t => t.Job)
            .Include(t => t.ClubrepRegistration)
            .Select(t => new RegisteredTeamInfo
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName ?? string.Empty,
                AgeGroupId = t.AgegroupId,
                AgeGroupName = t.Agegroup.AgegroupName ?? string.Empty,
                LevelOfPlay = t.LevelOfPlay,
                FeeBase = t.FeeBase ?? 0,
                FeeProcessing = t.FeeProcessing ?? 0,
                FeeTotal = t.FeeTotal ?? 0,
                PaidTotal = t.PaidTotal ?? 0,
                OwedTotal = t.OwedTotal ?? 0,
                // DepositDue: RosterFee - PaidTotal (0 if already paid deposit)
                DepositDue = (t.PaidTotal >= t.Agegroup.RosterFee) ? 0 : (t.Agegroup.RosterFee ?? 0) - (t.PaidTotal ?? 0),
                // AdditionalDue: TeamFee (0 if already fully paid or if full payment required upfront)
                AdditionalDue = (t.OwedTotal == 0 && (t.Job.BTeamsFullPaymentRequired ?? false)) ? 0 : (t.Agegroup.TeamFee ?? 0),
                RegistrationTs = t.Createdate,
                BWaiverSigned3 = t.ClubrepRegistration.BWaiverSigned3
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
                      join j in _context.Jobs on t.JobId equals j.JobId
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
                          FeeTotal = (t.FeeBase ?? 0) + (t.FeeProcessing ?? 0),
                          PaidTotal = t.PaidTotal ?? 0,
                          OwedTotal = ((t.FeeBase ?? 0) + (t.FeeProcessing ?? 0)) - (t.PaidTotal ?? 0),
                          DepositDue = (t.PaidTotal >= ag.RosterFee) ? 0 : (ag.RosterFee ?? 0) - (t.PaidTotal ?? 0),
                          AdditionalDue = (t.OwedTotal == 0 && (j.BTeamsFullPaymentRequired ?? false)) ? 0 : (ag.TeamFee ?? 0),
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
            .Where(r => r.JobId == jobId && r.BActive == true && r.AssignedTeamId != null)
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

    // ── Pool Assignment methods ──

    public async Task<List<Contracts.Dtos.PoolAssignment.PoolTeamDto>> GetPoolAssignmentTeamsAsync(
        Guid divId, Guid jobId, CancellationToken ct = default)
    {
        var clubNames = await GetClubNamesByJobAsync(jobId, ct);
        var clubRepNames = await GetClubRepNamesByJobAsync(jobId, ct);
        var scheduledTeamIds = await GetScheduledTeamIdsAsync(jobId, ct);

        var rawTeams = await _context.Teams
            .AsNoTracking()
            .Include(t => t.Agegroup)
            .Include(t => t.Div)
            .Where(t => t.JobId == jobId && t.DivId == divId)
            .Select(t => new
            {
                t.TeamId,
                TeamName = t.TeamName ?? "Unnamed Team",
                t.LevelOfPlay,
                t.TeamComments,
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
                AgegroupName = t.Agegroup.AgegroupName,
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
                || (request.PayStatuses.Contains("OVER PAID") && x.t.OwedTotal < 0));
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

        return await query
            .OrderBy(x => x.r != null ? x.r.ClubName : "")
            .ThenBy(x => x.ag.AgegroupName)
            .ThenBy(x => x.d != null ? x.d.DivName : "")
            .ThenBy(x => x.t.TeamName)
            .Select(x => new TeamSearchResultDto
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
                TeamComments = x.t.TeamComments
            })
            .ToListAsync(ct);
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

        return new TeamFilterOptionsDto
        {
            Clubs = clubs,
            LevelOfPlays = lops,
            AgeGroups = ageGroups,
            ActiveStatuses = activeStatuses,
            PayStatuses = payStatuses
        };
    }

    public async Task<TeamDetailQueryResult?> GetTeamDetailAsync(Guid teamId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Join(_context.Agegroups, t => t.AgegroupId, ag => ag.AgegroupId, (t, ag) => new { t, ag })
            .GroupJoin(_context.Divisions, x => x.t.DivId, d => d.DivId, (x, divs) => new { x.t, x.ag, divs })
            .SelectMany(x => x.divs.DefaultIfEmpty(), (x, d) => new { x.t, x.ag, d })
            .GroupJoin(_context.Registrations, x => x.t.ClubrepRegistrationid, r => r.RegistrationId, (x, regs) => new { x.t, x.ag, x.d, regs })
            .SelectMany(x => x.regs.DefaultIfEmpty(), (x, r) => new { x.t, x.ag, x.d, r })
            .GroupJoin(_context.AspNetUsers, x => x.r != null ? x.r.UserId : null, u => u.Id, (x, users) => new { x.t, x.ag, x.d, x.r, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new { x.t, x.ag, x.d, x.r, u })
            .Select(x => new TeamDetailQueryResult
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
                JobId = x.t.JobId
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<Teams>> GetActiveClubTeamsOrderedByOwedAsync(Guid jobId, Guid clubRepRegistrationId, CancellationToken ct = default)
    {
        return await _context.Teams
            .Include(t => t.Agegroup)
            .Include(t => t.Job)
            .Where(t => t.JobId == jobId
                && t.ClubrepRegistrationid == clubRepRegistrationId
                && t.Active == true)
            .OrderByDescending(t => t.OwedTotal)
            .ToListAsync(ct);
    }

    public async Task<List<ClubTeamSummaryDto>> GetClubTeamSummariesAsync(Guid jobId, Guid clubRepRegistrationId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.ClubrepRegistrationid == clubRepRegistrationId)
            .Join(_context.Agegroups, t => t.AgegroupId, ag => ag.AgegroupId, (t, ag) => new { t, ag })
            .OrderBy(x => x.ag.AgegroupName)
            .ThenBy(x => x.t.TeamName)
            .Select(x => new ClubTeamSummaryDto
            {
                TeamId = x.t.TeamId,
                TeamName = x.t.TeamName ?? "",
                AgegroupName = x.ag.AgegroupName ?? "",
                FeeTotal = x.t.FeeTotal ?? 0,
                PaidTotal = x.t.PaidTotal ?? 0,
                OwedTotal = x.t.OwedTotal ?? 0,
                Active = x.t.Active ?? false
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
}

