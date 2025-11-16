using Microsoft.EntityFrameworkCore;
using TSIC.API.DTOs;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public interface ITeamLookupService
{
    Task<IReadOnlyList<AvailableTeamDto>> GetAvailableTeamsForJobAsync(Guid jobId);
    /// <summary>
    /// Resolve the per-registrant fee and deposit for a specific team using the same rules
    /// as the available teams projection. Returns (Fee, Deposit); zeroes if team not found.
    /// </summary>
    Task<(decimal Fee, decimal Deposit)> ResolvePerRegistrantAsync(Guid teamId);
}

public class TeamLookupService : ITeamLookupService
{
    private readonly SqlDbContext _context;
    private readonly ILogger<TeamLookupService> _logger;

    public TeamLookupService(SqlDbContext context, ILogger<TeamLookupService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Returns teams eligible for player self-rostering for the given job.
    /// Basic parity with legacy logic (active, self-rostering flags, date windows, capacity).
    /// Waitlist substitution logic is deferred (Job.BUseWaitlists exposed for UI decisions).
    /// </summary>
    public async Task<IReadOnlyList<AvailableTeamDto>> GetAvailableTeamsForJobAsync(Guid jobId)
    {
        var now = DateTime.UtcNow;

        // Load job flag for waitlists once
        var jobUsesWaitlists = await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => j.BUseWaitlists)
            .SingleOrDefaultAsync();

        // Query teams with necessary joins; filter server-side where possible.
        // Self-rostering permitted if either team or its agegroup allows it.
        var baseQuery = _context.Teams
            .AsNoTracking()
            .Include(t => t.Agegroup)
            .Include(t => t.Div)
            .Where(t => t.JobId == jobId)
            .Where(t => (t.Active ?? true))
            .Where(t => (t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
            // Date windows: Effectiveasofdate must be in the past (if provided); Expireondate must be in future (if provided)
            .Where(t => (t.Effectiveasofdate == null || t.Effectiveasofdate <= now)
                        && (t.Expireondate == null || t.Expireondate >= now));

        var teamsRaw = await baseQuery.Select(t => new
        {
            t.TeamId,
            Name = t.TeamName ?? t.DisplayName ?? "(Unnamed Team)",
            t.AgegroupId,
            AgegroupName = t.Agegroup.AgegroupName,
            DivisionId = t.DivId,
            DivisionName = t.Div != null ? t.Div.DivName : null,
            t.MaxCount,
            PerRegistrantFee = (t.PerRegistrantFee > 0)
                ? t.PerRegistrantFee
                : (t.Agegroup.TeamFee > 0 && t.Agegroup.RosterFee > 0)
                    ? t.Agegroup.TeamFee
                    : (t.Agegroup.RosterFee > 0)
                        ? t.Agegroup.RosterFee
                        : 0,
            PerRegistrantDeposit = (t.PerRegistrantDeposit > 0)
                ? t.PerRegistrantDeposit
                : (t.Agegroup.TeamFee > 0 && t.Agegroup.RosterFee > 0)
                    ? t.Agegroup.RosterFee
                    : 0,
            TeamAllowsSelfRostering = t.BAllowSelfRostering,
            AgegroupAllowsSelfRostering = t.Agegroup.BAllowSelfRostering
        }).ToListAsync();

        if (teamsRaw.Count == 0)
        {
            _logger.LogInformation("No self-rostering teams found for job {JobId}", jobId);
            return Array.Empty<AvailableTeamDto>();
        }

        // Load roster counts in a single grouped query
        var teamIds = teamsRaw.Select(t => t.TeamId).ToList();
        var rosterCounts = await _context.Registrations
            .Where(r => r.AssignedTeamId != null && teamIds.Contains(r.AssignedTeamId.Value))
            .GroupBy(r => r.AssignedTeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count);

        var dtos = teamsRaw.Select(t =>
        {
            var current = rosterCounts.TryGetValue(t.TeamId, out var c) ? c : 0;
            var rosterFull = current >= t.MaxCount && t.MaxCount > 0;
            return new AvailableTeamDto
            {
                TeamId = t.TeamId,
                TeamName = t.Name,
                AgegroupId = t.AgegroupId,
                AgegroupName = t.AgegroupName,
                DivisionId = t.DivisionId,
                DivisionName = t.DivisionName,
                MaxRosterSize = t.MaxCount,
                CurrentRosterSize = current,
                RosterIsFull = rosterFull,
                TeamAllowsSelfRostering = t.TeamAllowsSelfRostering,
                AgegroupAllowsSelfRostering = t.AgegroupAllowsSelfRostering,
                PerRegistrantFee = t.PerRegistrantFee,
                PerRegistrantDeposit = t.PerRegistrantDeposit,
                JobUsesWaitlists = jobUsesWaitlists,
                WaitlistTeamId = null // deferred implementation
            };
        }).ToList();

        return dtos;
    }

    /// <summary>
    /// Centralized resolver for per-registrant fee and deposit for a single team.
    /// Uses the same computation as the team list to ensure a single source of truth.
    /// </summary>
    public async Task<(decimal Fee, decimal Deposit)> ResolvePerRegistrantAsync(Guid teamId)
    {
        var data = await _context.Teams
            .AsNoTracking()
            .Include(t => t.Agegroup)
            .Where(t => t.TeamId == teamId)
            .Select(t => new
            {
                t.PerRegistrantFee,
                t.PerRegistrantDeposit,
                TeamFee = t.Agegroup.TeamFee,
                RosterFee = t.Agegroup.RosterFee
            })
            .SingleOrDefaultAsync();

        if (data == null)
        {
            _logger.LogInformation("ResolvePerRegistrantAsync: team {TeamId} not found; returning zeros.", teamId);
            return (0m, 0m);
        }

        decimal prFee = data.PerRegistrantFee ?? 0m;
        decimal prDeposit = data.PerRegistrantDeposit ?? 0m;
        decimal agTeamFee = data.TeamFee ?? 0m;
        decimal agRosterFee = data.RosterFee ?? 0m;

        decimal fee;
        if (prFee > 0m)
        {
            fee = prFee;
        }
        else if (agTeamFee > 0m && agRosterFee > 0m)
        {
            fee = agTeamFee;
        }
        else if (agRosterFee > 0m)
        {
            fee = agRosterFee;
        }
        else
        {
            fee = 0m;
        }

        decimal deposit;
        if (prDeposit > 0m)
        {
            deposit = prDeposit;
        }
        else if (agTeamFee > 0m && agRosterFee > 0m)
        {
            deposit = agRosterFee;
        }
        else
        {
            deposit = 0m;
        }

        return (fee, deposit);
    }
}