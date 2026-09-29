using System.Text.RegularExpressions;
using TSIC.API.Services.Shared.Bulletins;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Dtos.TeamTournaments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Teams;

public sealed partial class TeamTournamentsService : ITeamTournamentsService
{
    private readonly ITeamTournamentsRepository _repo;
    private readonly IBulletinRepository _bulletins;
    private readonly IViewScheduleService _viewSchedule;

    public TeamTournamentsService(
        ITeamTournamentsRepository repo,
        IBulletinRepository bulletins,
        IViewScheduleService viewSchedule)
    {
        _repo = repo;
        _bulletins = bulletins;
        _viewSchedule = viewSchedule;
    }

    public async Task<bool> IsEnabledAsync(Guid teamId, CancellationToken ct = default)
    {
        var context = await _repo.GetTeamContextAsync(teamId, ct);
        return context?.ScheduleEnabled == true;
    }

    public async Task<List<TeamTournamentDto>> GetTournamentsAsync(Guid teamId, CancellationToken ct = default)
    {
        var context = await _repo.GetTeamContextAsync(teamId, ct);
        if (context == null) return [];

        var clubName = TournamentClubNames.Resolve(context.CustomerName, context.Gender);
        if (clubName == null) return [];

        var rows = await _repo.GetUpcomingPublicTournamentsForClubAsync(clubName, context.JobId, DateTime.Today, ct: ct);

        var announced = new List<ClubTournamentRow>();
        foreach (var row in rows)
        {
            // Sequential: one scoped DbContext.
            if (await IsAnnouncedAsync(row.JobId, ct)) announced.Add(row);
        }

        return announced
            .OrderBy(r => r.NextGameDate)
            .ThenBy(r => r.JobName)
            .Select(r => new TeamTournamentDto
            {
                TournamentJobId = r.JobId,
                TournamentJobName = r.JobName,
                NextGameDate = r.NextGameDate
            })
            .ToList();
    }

    public async Task<TeamTournamentMatchDto?> MatchAsync(Guid teamId, Guid tournamentJobId, CancellationToken ct = default)
    {
        var available = await ResolveAvailableAsync(teamId, tournamentJobId, ct);
        if (available == null) return null;
        var (context, clubName, tournament) = available.Value;

        var clubTeams = await _repo.GetClubScheduledTeamsAsync(tournamentJobId, clubName, ct);

        // The club name is the same on both sides by construction, so {club}:{team} reduces to
        // the team name. Trimmed and case-insensitive.
        var ownName = context.TeamName.Trim();
        var byName = clubTeams
            .Where(t => string.Equals(t.TeamName.Trim(), ownName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ClubTournamentTeamRow? matched = null;
        List<ClubTournamentTeamRow> candidates;

        if (byName.Count == 1)
        {
            matched = byName[0];
            candidates = [];
        }
        else if (byName.Count > 1)
        {
            // Grad year is a tie-breaker only. The grad-year columns are unpopulated, so it is
            // the one year an age group's NAME carries.
            var ownYear = GradYear(context.AgegroupName);
            var byYear = ownYear == null
                ? []
                : byName.Where(t => GradYear(t.AgegroupName) == ownYear).ToList();

            if (byYear.Count == 1)
            {
                matched = byYear[0];
                candidates = [];
            }
            else
            {
                // Never an empty pick list when names did match.
                candidates = byYear.Count > 1 ? byYear : byName;
            }
        }
        else
        {
            candidates = clubTeams;
        }

        return new TeamTournamentMatchDto
        {
            TournamentJobId = tournament.JobId,
            TournamentJobName = tournament.JobName,
            MatchedTeamId = matched?.TeamId,
            Candidates = candidates
                .OrderBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
                .Select(t => new TournamentTeamOptionDto { TournamentTeamId = t.TeamId, TeamName = t.TeamName })
                .ToList()
        };
    }

    public async Task<(List<ViewGameDto> Games, int TotalCount)?> GetGamesAsync(
        Guid teamId, Guid tournamentJobId, Guid tournamentTeamId, int? skip, int? take, CancellationToken ct = default)
    {
        var available = await ResolveAvailableAsync(teamId, tournamentJobId, ct);
        if (available == null) return null;
        var (_, clubName, _) = available.Value;

        // The team must be the club's AND in this tournament -- a mismatched id pair fails
        // rather than serving another club's or another event's games.
        var clubTeams = await _repo.GetClubScheduledTeamsAsync(tournamentJobId, clubName, ct);
        if (!clubTeams.Any(t => t.TeamId == tournamentTeamId)) return null;

        // No DeviceToken: hearts are out of TSIC-Teams. A TSIC-Teams push token filed through the
        // Events heart lands in the Events pool, where the Events sender rejects it.
        var request = new ScheduleFilterRequest
        {
            TeamIds = [tournamentTeamId],
            Skip = skip,
            Take = take
        };

        return await _viewSchedule.GetGamesPagedAsync(tournamentJobId, request, ct);
    }

    /// <summary>
    /// All gates for one tournament, re-derived from the app's team: club name, T1/T2 presence,
    /// public schedule, a game ahead, and the announce bulletin.
    /// </summary>
    private async Task<(TeamTournamentsContext Context, string ClubName, ClubTournamentRow Tournament)?> ResolveAvailableAsync(
        Guid teamId, Guid tournamentJobId, CancellationToken ct)
    {
        var context = await _repo.GetTeamContextAsync(teamId, ct);
        if (context == null) return null;

        var clubName = TournamentClubNames.Resolve(context.CustomerName, context.Gender);
        if (clubName == null) return null;

        var rows = await _repo.GetUpcomingPublicTournamentsForClubAsync(
            clubName, context.JobId, DateTime.Today, tournamentJobId, ct);
        var tournament = rows.FirstOrDefault();
        if (tournament == null) return null;

        if (!await IsAnnouncedAsync(tournament.JobId, ct)) return null;

        return (context, clubName, tournament);
    }

    /// <summary>
    /// The director's public announce: an active, in-window bulletin pointing at the schedule.
    /// The publish flag alone is rep/coach REVIEW and must not surface here.
    /// </summary>
    private async Task<bool> IsAnnouncedAsync(Guid jobId, CancellationToken ct)
    {
        var bulletins = await _bulletins.GetActiveBulletinsForJobAsync(jobId, ct);
        return bulletins.Any(b => SchedulePublicationBulletinService.PointsAtSchedule(b.Text));
    }

    /// <summary>The single 4-digit year in an age group name, else null ("2036/2037", "Alternates").</summary>
    private static int? GradYear(string? agegroupName)
    {
        if (string.IsNullOrWhiteSpace(agegroupName)) return null;

        var years = YearPattern().Matches(agegroupName)
            .Select(m => int.Parse(m.Value))
            .Distinct()
            .ToList();

        return years.Count == 1 ? years[0] : null;
    }

    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)")]
    private static partial Regex YearPattern();
}
