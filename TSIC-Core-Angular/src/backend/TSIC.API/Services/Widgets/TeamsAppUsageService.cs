using TSIC.API.Services.Usage;
using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Widgets;

/// <summary>
/// See <see cref="ITeamsAppUsageService"/>.
///
/// The measures are chosen so an intelligent reader gets an accurate picture, not a flattering
/// or an unflattering one:
///   - PEOPLE and DAYS, never requests. The app loads roster and alerts together on every
///     open, so a request count rewards a few heavy users and says nothing about breadth.
///   - Breadth (active users, teams reached) and depth (returning users, days-of-use bands)
///     side by side, because either alone misleads: broad-but-once and deep-but-few both
///     look healthy on one axis.
///   - Days before logging began are NO DATA and are excluded from the window, never read as
///     days nobody used the app.
/// </summary>
public sealed class TeamsAppUsageService : ITeamsAppUsageService
{
    /// <summary>Days-of-use bands, open-ended at the top. A band that cannot be reached in the covered days is omitted.</summary>
    private static readonly (int Min, int? Max)[] FrequencyBands = [(1, 1), (2, 3), (4, 7), (8, null)];

    private readonly IJobRepository _jobRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IUsageStatsRepository _usageRepo;

    public TeamsAppUsageService(
        IJobRepository jobRepo,
        ITeamRepository teamRepo,
        IUsageStatsRepository usageRepo)
    {
        _jobRepo = jobRepo;
        _teamRepo = teamRepo;
        _usageRepo = usageRepo;
    }

    public async Task<TeamsAppUsageDto> GetTeamsAppUsageAsync(Guid jobId, int windowDays, CancellationToken ct = default)
    {
        if (!_usageRepo.IsAvailable)
            return Empty(windowDays, available: false, enabled: false);

        // Sequential awaits: the TSICV5 repositories share one scoped DbContext.
        var enabled = await _jobRepo.IsTsicTeamsEnabledAsync(jobId, ct);
        if (!enabled)
            return Empty(windowDays, available: true, enabled: false);

        // Server-local calendar days, matching AppUsage.OccurredAt. The window is the last
        // N days INCLUDING today, starting at midnight.
        var today = DateTime.Today;
        var windowStart = today.AddDays(-(windowDays - 1));

        var loggingStartedAt = await _usageRepo.GetFirstRecordedAtAsync(ct);
        var coverageStart = loggingStartedAt is DateTime first && first.Date > windowStart
            ? first.Date
            : windowStart;
        var daysCovered = loggingStartedAt is null ? 0 : (today - coverageStart).Days + 1;

        var roster = await _teamRepo.GetTeamsAppRosterAsync(jobId, ct);
        var activity = await _usageRepo.GetDistinctRegistrationDaysAsync(
            [jobId], windowStart, UsageClassifier.AppClientTeams, ct);

        // Distinct days per registration. The repository already returns distinct triples.
        var daysByReg = activity
            .GroupBy(a => a.RegistrationId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Day).Distinct().Count());

        var rosterRegIds = roster.Select(m => m.RegistrationId).ToHashSet();
        int DaysOf(Guid regId) => daysByReg.TryGetValue(regId, out var d) ? d : 0;

        var activeMembers = roster.Where(m => DaysOf(m.RegistrationId) > 0).ToList();

        var frequency = FrequencyBands
            .Where(b => b.Min <= Math.Max(daysCovered, 1))
            .Select(b => new TeamsAppUsageFrequencyBandDto
            {
                MinDays = b.Min,
                MaxDays = b.Max,
                Users = activeMembers.Count(m =>
                {
                    var d = DaysOf(m.RegistrationId);
                    return d >= b.Min && (b.Max is null || d <= b.Max);
                }),
            })
            .ToList();

        var teams = roster
            .GroupBy(m => new { m.TeamId, m.TeamName, m.AgegroupName })
            .Select(g => new TeamsAppUsageTeamRowDto
            {
                TeamId = g.Key.TeamId,
                TeamName = g.Key.TeamName,
                AgegroupName = g.Key.AgegroupName,
                PlayersActive = g.Count(m => !m.IsStaff && DaysOf(m.RegistrationId) > 0),
                PlayersRostered = g.Count(m => !m.IsStaff),
                StaffActive = g.Count(m => m.IsStaff && DaysOf(m.RegistrationId) > 0),
                StaffRostered = g.Count(m => m.IsStaff),
                ReturningUsers = g.Count(m => DaysOf(m.RegistrationId) >= 2),
            })
            .OrderBy(t => t.AgegroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TeamsAppUsageDto
        {
            UsageLoggingAvailable = true,
            TeamsAppEnabled = true,
            WindowDays = windowDays,
            DaysCovered = daysCovered,
            LoggingStartedAt = loggingStartedAt,
            ActiveUsers = activeMembers.Count,
            RosteredUsers = roster.Count,
            ReturningUsers = activeMembers.Count(m => DaysOf(m.RegistrationId) >= 2),
            TeamsReached = teams.Count(t => t.PlayersActive + t.StaffActive > 0),
            TeamsTotal = teams.Count,
            DaysWithActivity = activity
                .Where(a => rosterRegIds.Contains(a.RegistrationId))
                .Select(a => a.Day)
                .Distinct()
                .Count(),
            PlayersActive = activeMembers.Count(m => !m.IsStaff),
            PlayersRostered = roster.Count(m => !m.IsStaff),
            StaffActive = activeMembers.Count(m => m.IsStaff),
            StaffRostered = roster.Count(m => m.IsStaff),
            OffRosterUsers = daysByReg.Keys.Count(id => !rosterRegIds.Contains(id)),
            Frequency = frequency,
            Teams = teams,
        };
    }

    private static TeamsAppUsageDto Empty(int windowDays, bool available, bool enabled) =>
        new()
        {
            UsageLoggingAvailable = available,
            TeamsAppEnabled = enabled,
            WindowDays = windowDays,
            DaysCovered = 0,
            ActiveUsers = 0,
            RosteredUsers = 0,
            ReturningUsers = 0,
            TeamsReached = 0,
            TeamsTotal = 0,
            DaysWithActivity = 0,
            PlayersActive = 0,
            PlayersRostered = 0,
            StaffActive = 0,
            StaffRostered = 0,
            OffRosterUsers = 0,
            Frequency = [],
            Teams = [],
        };
}
