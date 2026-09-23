using System.Text.RegularExpressions;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Director-side club rep directory. Composes three existing halves - the club-rep registration
/// rows (RegistrationRepository), the registration-to-club bridge (ClubRepRepository) and the
/// library + event history (ClubTeamRepository) - into one read-only shape. The "fits an age
/// group" rule is a port of the rep-side event-age-group.util so the director's eligible count
/// and the rep's "Available for this event" group can never disagree.
/// </summary>
public sealed class ClubRepDirectoryService : IClubRepDirectoryService
{
    private readonly IRegistrationRepository _registrations;
    private readonly IClubRepRepository _clubReps;
    private readonly IClubTeamRepository _clubTeams;
    private readonly IJobRepository _jobs;
    private readonly IJobLeagueRepository _jobLeagues;
    private readonly IAgeGroupRepository _agegroups;

    private static readonly Regex GradYearRx = new(@"(20\d{2})", RegexOptions.Compiled);

    public ClubRepDirectoryService(
        IRegistrationRepository registrations,
        IClubRepRepository clubReps,
        IClubTeamRepository clubTeams,
        IJobRepository jobs,
        IJobLeagueRepository jobLeagues,
        IAgeGroupRepository agegroups)
    {
        _registrations = registrations;
        _clubReps = clubReps;
        _clubTeams = clubTeams;
        _jobs = jobs;
        _jobLeagues = jobLeagues;
        _agegroups = agegroups;
    }

    public async Task<List<DirectorClubRepDto>> GetClubRepsAsync(Guid jobId, CancellationToken ct = default)
    {
        var reps = await _registrations.GetDirectorClubRepsForJobAsync(jobId, ct);
        if (reps.Count == 0) return reps;

        var clubByReg = await _clubReps.ResolveClubsForClubRepRegistrationsAsync(reps.Select(r => r.RegistrationId), ct);
        var clubIds = clubByReg.Values.Where(c => c > 0).Distinct().ToList();
        var library = await _clubTeams.GetByClubIdsAsync(clubIds, ct);
        var inJob = await _clubTeams.GetClubTeamIdsInJobAsync(jobId, library.Select(l => l.ClubTeamId), ct);
        var oldest = await ResolveOldestOfferedGradYearAsync(jobId, ct);

        var byClub = library.GroupBy(l => l.ClubId).ToDictionary(g => g.Key, g => g.ToList());
        var empty = new List<ClubTeams>();

        return reps.Select(r =>
        {
            var clubId = clubByReg.GetValueOrDefault(r.RegistrationId, 0);
            var rows = clubId > 0 && byClub.TryGetValue(clubId, out var l) ? l : empty;
            var active = rows.Where(x => x.Active).ToList();
            var unregistered = active.Where(x => !inJob.Contains(x.ClubTeamId)).ToList();
            return r with
            {
                ClubId = clubId,
                LibraryTeamCount = active.Count,
                LibraryUnregisteredCount = unregistered.Count,
                LibraryEligibleUnregisteredCount = unregistered.Count(x => IsTeamOfferedAtEvent(oldest, x.ClubTeamGradYear)),
            };
        }).ToList();
    }

    public async Task<DirectorClubLibraryDto?> GetClubLibraryAsync(Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default)
    {
        var reg = await _registrations.GetByIdAsync(clubRepRegistrationId, ct);
        if (reg == null || reg.JobId != jobId || reg.RoleId != RoleConstants.ClubRep) return null;

        var resolution = await _clubReps.ResolveClubForClubRepRegistrationAsync(clubRepRegistrationId, ct);
        var clubId = resolution.ClubId;
        var rows = clubId > 0 ? await _clubTeams.GetByClubIdsAsync(new[] { clubId }, ct) : new List<ClubTeams>();
        var history = await _clubTeams.GetEventHistoryForClubTeamIdsAsync(rows.Select(x => x.ClubTeamId), ct);
        var oldest = await ResolveOldestOfferedGradYearAsync(jobId, ct);

        var historyByTeam = history.GroupBy(h => h.ClubTeamId).ToDictionary(g => g.Key, g => g.ToList());
        var none = new List<ClubTeamEventHistoryDto>();

        var teams = rows.Select(x =>
        {
            var mine = historyByTeam.GetValueOrDefault(x.ClubTeamId, none);
            // This job's copy: prefer a live entry over a dropped one when a team was re-entered.
            var here = mine.Where(h => h.JobId == jobId)
                .OrderBy(h => h.IsDropped).ThenByDescending(h => h.RegisteredOn)
                .FirstOrDefault();
            var status = !x.Active ? "archived"
                : here == null ? (IsTeamOfferedAtEvent(oldest, x.ClubTeamGradYear) ? "available" : "outside")
                : here.IsDropped ? "dropped"
                : here.IsWaitlisted ? "waitlisted"
                : "registered";
            return new DirectorClubLibraryTeamDto
            {
                ClubTeamId = x.ClubTeamId,
                ClubTeamName = x.ClubTeamName,
                ClubTeamGradYear = x.ClubTeamGradYear,
                ClubTeamLevelOfPlay = x.ClubTeamLevelOfPlay,
                Archived = !x.Active,
                EventStatus = status,
                EventTeamId = here?.TeamId,
                EventTeamName = here?.EventTeamName,
                EventAgeGroupName = here?.AgeGroupName,
                OtherEvents = mine.Where(h => h.JobId != jobId).ToList(),
            };
        })
        .OrderBy(t => StatusRank(t.EventStatus))
        .ThenByDescending(t => t.ClubTeamGradYear)
        .ThenBy(t => t.ClubTeamName)
        .ToList();

        return new DirectorClubLibraryDto
        {
            RegistrationId = clubRepRegistrationId,
            ClubId = clubId,
            ClubName = reg.ClubName ?? string.Empty,
            OldestOfferedGradYear = oldest,
            Teams = teams,
        };
    }

    private static int StatusRank(string status) => status switch
    {
        "registered" => 0,
        "waitlisted" => 1,
        "available" => 2,
        "outside" => 3,
        "dropped" => 4,
        _ => 5, // archived
    };

    /// <summary>
    /// Port of resolveOldestOfferedGradYear: STRICT - every non-waitlist age group must carry a 20xx
    /// year, or the event is not grad-year-named and the threshold does not apply (null).
    /// </summary>
    private async Task<int?> ResolveOldestOfferedGradYearAsync(Guid jobId, CancellationToken ct)
    {
        var leagueId = await _jobLeagues.GetPrimaryLeagueForJobAsync(jobId, ct);
        if (leagueId == null) return null;
        var season = await _jobs.GetJobSeasonAsync(jobId, ct);
        var ageGroups = await _agegroups.GetByLeagueAndSeasonAsync(leagueId.Value, season ?? string.Empty, ct);

        int? oldest = null;
        var any = false;
        foreach (var ag in ageGroups)
        {
            if (AgegroupConstants.IsWaitlist(ag.AgegroupName)) continue;
            any = true;
            var year = ParseGradYear(ag.AgegroupName);
            if (year == null) return null;
            if (oldest == null || year < oldest) oldest = year;
        }
        return any ? oldest : null;
    }

    /// <summary>Port of isTeamOfferedAtEvent: playing UP is allowed, so a team is offered something iff
    /// its grad year is not older than the event's oldest group. Unknown on either side = offered.</summary>
    private static bool IsTeamOfferedAtEvent(int? oldestOffered, string? teamGradYear)
    {
        if (oldestOffered == null) return true;
        var gy = ParseGradYear(teamGradYear);
        return gy == null || gy >= oldestOffered;
    }

    private static int? ParseGradYear(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var m = GradYearRx.Match(name);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }
}
