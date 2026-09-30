using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class TeamTournamentsRepository : ITeamTournamentsRepository
{
    private readonly SqlDbContext _context;

    public TeamTournamentsRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<TeamTournamentsContext?> GetTeamContextAsync(Guid teamId, CancellationToken ct = default)
    {
        return await _context.Teams.AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => new TeamTournamentsContext
            {
                TeamId = t.TeamId,
                JobId = t.JobId,
                TeamName = t.TeamName ?? string.Empty,
                Gender = t.Gender ?? t.Agegroup.Gender,
                AgegroupName = t.Agegroup.AgegroupName,
                CustomerName = t.Job.Customer.CustomerName,
                ScheduleEnabled = t.Job.JobFeatures != null && t.Job.JobFeatures.ScheduleEnabled
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<ClubTournamentRow>> GetPublicTournamentsForClubAsync(
        string clubName, Guid excludeJobId, DateTime today, DateTime finishedSince,
        Guid? onlyJobId = null, CancellationToken ct = default)
    {
        var clubTeamIds = ClubTeamIds(clubName);

        var clubGames = _context.Schedule.AsNoTracking()
            .Where(s => (s.T1Id != null && clubTeamIds.Contains(s.T1Id.Value))
                        || (s.T2Id != null && clubTeamIds.Contains(s.T2Id.Value)));

        var scheduledJobIds = clubGames.Select(s => s.JobId).Distinct();

        var rows = await _context.Jobs.AsNoTracking()
            .Where(j => scheduledJobIds.Contains(j.JobId)
                        && j.JobId != excludeJobId
                        && (onlyJobId == null || j.JobId == onlyJobId)
                        && j.BScheduleAllowPublicAccess == true)
            .Select(j => new
            {
                j.JobId,
                JobName = j.JobName ?? string.Empty,
                j.JobPath,
                // Anyone's game, not the club's: "upcoming" is the TOURNAMENT still having a
                // game ahead of it, dated rather than scored -- unscored old events never close.
                NextGameDate = _context.Schedule
                    .Where(s => s.JobId == j.JobId && s.GDate >= today)
                    .Min(s => s.GDate),
                LastGameDate = _context.Schedule
                    .Where(s => s.JobId == j.JobId)
                    .Max(s => s.GDate),
                // A finished tournament is shown only once the CLUB's games in it are all
                // scored -- the club's results are what a finished listing is for.
                ClubGameUnscored = clubGames.Any(s => s.JobId == j.JobId
                                                      && (s.T1Score == null || s.T2Score == null))
            })
            .Where(x => x.NextGameDate != null
                        || (x.LastGameDate != null && x.LastGameDate >= finishedSince && !x.ClubGameUnscored))
            .ToListAsync(ct);

        return rows
            .Select(x => new ClubTournamentRow
            {
                JobId = x.JobId,
                JobName = x.JobName,
                JobPath = x.JobPath,
                NextGameDate = x.NextGameDate,
                LastGameDate = x.LastGameDate!.Value
            })
            .ToList();
    }

    public async Task<List<ClubTournamentTeamRow>> GetClubScheduledTeamsAsync(
        Guid tournamentJobId, string clubName, CancellationToken ct = default)
    {
        var clubTeamIds = ClubTeamIds(clubName);

        return await _context.Teams.AsNoTracking()
            .Where(t => t.JobId == tournamentJobId
                        && clubTeamIds.Contains(t.TeamId)
                        && _context.Schedule.Any(s => s.JobId == tournamentJobId
                                                      && (s.T1Id == t.TeamId || s.T2Id == t.TeamId)))
            .Select(t => new ClubTournamentTeamRow
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName ?? string.Empty,
                AgegroupName = t.Agegroup.AgegroupName
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Active teams whose club rep registered under <paramref name="clubName"/>. Same shape as the
    /// view-schedule club filter, so "a club's teams" means one thing on both surfaces.
    /// </summary>
    private IQueryable<Guid> ClubTeamIds(string clubName) =>
        _context.Teams.AsNoTracking()
            .Where(t => t.Active == true && t.ClubrepRegistrationid.HasValue)
            .Join(_context.Registrations.AsNoTracking(),
                t => t.ClubrepRegistrationid!.Value,
                r => r.RegistrationId,
                (t, r) => new { t.TeamId, r.ClubName })
            .Where(x => x.ClubName == clubName)
            .Select(x => x.TeamId);
}
