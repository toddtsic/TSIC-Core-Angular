using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Service for the View Schedule page (009-5).
/// Consumer-facing schedule viewer with Games, Standings, Records, Brackets, Contacts tabs.
/// </summary>
public sealed class ViewScheduleService : IViewScheduleService
{
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ILogger<ViewScheduleService> _logger;

    public ViewScheduleService(
        IScheduleRepository scheduleRepo,
        ILogger<ViewScheduleService> logger)
    {
        _scheduleRepo = scheduleRepo;
        _logger = logger;
    }

    public async Task<ScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _scheduleRepo.GetScheduleFilterOptionsAsync(jobId, ct);
    }

    public async Task<ScheduleCapabilitiesDto> GetCapabilitiesAsync(
        Guid jobId, bool isAuthenticated, bool isAdmin, CancellationToken ct = default)
    {
        var (allowPublicAccess, hideContacts, sportName) = await _scheduleRepo.GetScheduleFlagsAsync(jobId, ct);

        return new ScheduleCapabilitiesDto
        {
            CanScore = isAuthenticated && isAdmin,
            HideContacts = hideContacts,
            IsPublicAccess = allowPublicAccess,
            SportName = sportName
        };
    }

    public async Task<List<ViewGameDto>> GetGamesAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        var games = await _scheduleRepo.GetFilteredGamesAsync(jobId, request, ct);

        return games.Select(g => new ViewGameDto
        {
            Gid = g.Gid,
            GDate = g.GDate!.Value,
            FName = g.Field?.FName ?? g.FName ?? "",
            FieldId = g.FieldId ?? Guid.Empty,
            Latitude = g.Field?.Latitude,
            Longitude = g.Field?.Longitude,
            AgDiv = $"{g.AgegroupName}:{g.DivName}",
            T1Name = g.T1Name ?? "",
            T2Name = g.T2Name ?? "",
            T1Id = g.T1Id,
            T2Id = g.T2Id,
            T1Score = g.T1Score,
            T2Score = g.T2Score,
            T1Type = g.T1Type ?? "T",
            T2Type = g.T2Type ?? "T",
            T1Ann = g.T1Ann,
            T2Ann = g.T2Ann,
            Rnd = g.Rnd,
            GStatusCode = g.GStatusCode,
            Color = g.Agegroup?.Color
        }).ToList();
    }

    public async Task<StandingsByDivisionResponse> GetStandingsAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        return await BuildStandingsAsync(jobId, request, poolPlayOnly: true, ct);
    }

    public async Task<StandingsByDivisionResponse> GetTeamRecordsAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        return await BuildStandingsAsync(jobId, request, poolPlayOnly: false, ct);
    }

    public async Task<List<TeamResultDto>> GetTeamResultsAsync(Guid teamId, CancellationToken ct = default)
    {
        var games = await _scheduleRepo.GetTeamGamesAsync(teamId, ct);

        return games.Select(g =>
        {
            var isT1 = g.T1Id == teamId;
            var teamScore = isT1 ? g.T1Score : g.T2Score;
            var oppScore = isT1 ? g.T2Score : g.T1Score;
            var oppName = isT1 ? (g.T2Name ?? "") : (g.T1Name ?? "");
            var oppId = isT1 ? g.T2Id : g.T1Id;

            string? outcome = null;
            if (teamScore.HasValue && oppScore.HasValue)
            {
                outcome = teamScore > oppScore ? "W"
                    : teamScore < oppScore ? "L"
                    : "T";
            }

            // Determine game type from team type codes
            var teamType = isT1 ? g.T1Type : g.T2Type;
            var gameType = teamType == "T" ? "Pool Play" : GetBracketRoundName(teamType);

            return new TeamResultDto
            {
                Gid = g.Gid,
                GDate = g.GDate!.Value,
                Location = g.Field?.FName ?? g.FName ?? "",
                OpponentName = oppName,
                OpponentTeamId = oppId,
                TeamScore = teamScore,
                OpponentScore = oppScore,
                Outcome = outcome,
                GameType = gameType
            };
        }).ToList();
    }

    public async Task<List<DivisionBracketResponse>> GetBracketsAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        var bracketGames = await _scheduleRepo.GetBracketGamesAsync(jobId, request, ct);

        // Group by agegroup + division
        var grouped = bracketGames
            .GroupBy(g => new { AgName = g.AgegroupName ?? "", DivName = g.DivName ?? "" })
            .OrderBy(grp => grp.Key.AgName)
            .ThenBy(grp => grp.Key.DivName);

        var result = new List<DivisionBracketResponse>();

        foreach (var group in grouped)
        {
            var matches = group.Select(g =>
            {
                var t1Css = GetTeamCss(g.T1Score, g.T2Score);
                var t2Css = GetTeamCss(g.T2Score, g.T1Score);

                string? locationTime = null;
                if (g.GDate.HasValue)
                {
                    var fieldName = g.Field?.FName ?? g.FName ?? "";
                    locationTime = $"{fieldName} — {g.GDate.Value:ddd M/d h:mm tt}";
                }

                // Determine round type from T1Type or T2Type
                var roundType = g.T1Type ?? g.T2Type ?? "F";
                if (roundType == "T") roundType = "F"; // fallback

                return new BracketMatchDto
                {
                    Gid = g.Gid,
                    T1Name = g.T1Name ?? $"#{g.T1No}",
                    T2Name = g.T2Name ?? $"#{g.T2No}",
                    T1Score = g.T1Score,
                    T2Score = g.T2Score,
                    T1Css = t1Css,
                    T2Css = t2Css,
                    LocationTime = locationTime,
                    RoundType = roundType,
                    ParentGid = g.T1GnoRef // Parent game reference for tree structure
                };
            })
            .OrderBy(m => GetRoundOrder(m.RoundType))
            .ThenBy(m => m.Gid)
            .ToList();

            // Determine champion: winner of the Finals game
            var final = matches.FirstOrDefault(m => m.RoundType == "F");
            string? champion = null;
            if (final is { T1Score: not null, T2Score: not null })
            {
                champion = final.T1Score > final.T2Score ? final.T1Name
                    : final.T2Score > final.T1Score ? final.T2Name
                    : null;
            }

            result.Add(new DivisionBracketResponse
            {
                AgegroupName = group.Key.AgName,
                DivName = group.Key.DivName,
                Champion = champion,
                Matches = matches
            });
        }

        return result;
    }

    public async Task<List<ContactDto>> GetContactsAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        return await _scheduleRepo.GetContactsAsync(jobId, request, ct);
    }

    public async Task<FieldDisplayDto?> GetFieldInfoAsync(Guid fieldId, CancellationToken ct = default)
    {
        return await _scheduleRepo.GetFieldDisplayAsync(fieldId, ct);
    }

    public async Task QuickEditScoreAsync(
        Guid jobId, string userId, EditScoreRequest request, CancellationToken ct = default)
    {
        var game = await _scheduleRepo.GetGameByIdAsync(request.Gid, ct);
        if (game == null) return;

        game.T1Score = request.T1Score;
        game.T2Score = request.T2Score;
        game.GStatusCode = request.GStatusCode ?? 2; // 2 = Completed
        game.LebUserId = userId;
        game.Modified = DateTime.UtcNow;

        await _scheduleRepo.SaveChangesAsync(ct);
    }

    public async Task EditGameAsync(
        Guid jobId, string userId, EditGameRequest request, CancellationToken ct = default)
    {
        var game = await _scheduleRepo.GetGameByIdAsync(request.Gid, ct);
        if (game == null) return;

        if (request.T1Score.HasValue) game.T1Score = request.T1Score;
        if (request.T2Score.HasValue) game.T2Score = request.T2Score;
        if (request.T1Id.HasValue) game.T1Id = request.T1Id;
        if (request.T2Id.HasValue) game.T2Id = request.T2Id;
        if (request.T1Name != null) game.T1Name = request.T1Name;
        if (request.T2Name != null) game.T2Name = request.T2Name;
        if (request.T1Ann != null) game.T1Ann = request.T1Ann;
        if (request.T2Ann != null) game.T2Ann = request.T2Ann;
        if (request.GStatusCode.HasValue) game.GStatusCode = request.GStatusCode;

        game.LebUserId = userId;
        game.Modified = DateTime.UtcNow;

        await _scheduleRepo.SaveChangesAsync(ct);
    }

    // ── Private Helpers ──

    private async Task<StandingsByDivisionResponse> BuildStandingsAsync(
        Guid jobId, ScheduleFilterRequest request, bool poolPlayOnly, CancellationToken ct)
    {
        var games = await _scheduleRepo.GetFilteredGamesAsync(jobId, request, ct);
        var sportName = await _scheduleRepo.GetSportNameAsync(jobId, ct);

        // Filter to pool play if needed
        if (poolPlayOnly)
            games = games.Where(g => g.T1Type == "T" && g.T2Type == "T").ToList();

        // Only include scored games
        var scoredGames = games
            .Where(g => g.T1Score.HasValue && g.T2Score.HasValue
                && g.T1Id.HasValue && g.T2Id.HasValue)
            .ToList();

        // Build team standings by aggregating from both T1 and T2 perspectives
        var teamStats = new Dictionary<Guid, TeamStatsAccumulator>();

        foreach (var g in scoredGames)
        {
            // T1 perspective
            AccumulateStats(teamStats, g.T1Id!.Value, g.T1Name ?? "", g.AgegroupName ?? "",
                g.DivName ?? "", g.DivId ?? Guid.Empty, g.T1Score!.Value, g.T2Score!.Value);

            // T2 perspective
            AccumulateStats(teamStats, g.T2Id!.Value, g.T2Name ?? "", g.AgegroupName ?? "",
                g.DivName ?? "", g.DivId ?? Guid.Empty, g.T2Score!.Value, g.T1Score!.Value);
        }

        // Convert to DTOs grouped by division
        var divisions = teamStats.Values
            .GroupBy(t => new { t.DivId, t.AgegroupName, t.DivName })
            .OrderBy(d => d.Key.AgegroupName)
            .ThenBy(d => d.Key.DivName)
            .Select(divGroup =>
            {
                var teams = divGroup.Select(t =>
                {
                    var goalDiff = t.GoalsFor - t.GoalsAgainst;
                    var goalDiffMax9 = Math.Clamp(goalDiff, -9, 9);
                    var points = (t.Wins * 3) + t.Ties;
                    var ppg = t.Games > 0 ? Math.Round((decimal)points / t.Games, 2) : 0m;

                    return new StandingsDto
                    {
                        TeamId = t.TeamId,
                        TeamName = t.TeamName,
                        AgegroupName = t.AgegroupName,
                        DivName = t.DivName,
                        DivId = t.DivId,
                        Games = t.Games,
                        Wins = t.Wins,
                        Losses = t.Losses,
                        Ties = t.Ties,
                        GoalsFor = t.GoalsFor,
                        GoalsAgainst = t.GoalsAgainst,
                        GoalDiffMax9 = goalDiffMax9,
                        Points = points,
                        PointsPerGame = ppg
                    };
                }).ToList();

                // Sort based on sport
                var isLacrosse = sportName.Contains("lacrosse", StringComparison.OrdinalIgnoreCase);
                if (isLacrosse)
                {
                    teams = teams
                        .OrderByDescending(t => t.Wins)
                        .ThenBy(t => t.Losses)
                        .ThenByDescending(t => t.GoalDiffMax9)
                        .ThenByDescending(t => t.GoalsFor)
                        .ThenBy(t => t.TeamName)
                        .ToList();
                }
                else
                {
                    // Soccer sort (default)
                    teams = teams
                        .OrderByDescending(t => t.Points)
                        .ThenByDescending(t => t.Wins)
                        .ThenByDescending(t => t.GoalDiffMax9)
                        .ThenByDescending(t => t.GoalsFor)
                        .ThenBy(t => t.TeamName)
                        .ToList();
                }

                // Assign rank order
                for (var i = 0; i < teams.Count; i++)
                    teams[i] = teams[i] with { RankOrder = i + 1 };

                return new DivisionStandingsDto
                {
                    DivId = divGroup.Key.DivId,
                    AgegroupName = divGroup.Key.AgegroupName,
                    DivName = divGroup.Key.DivName,
                    Teams = teams
                };
            })
            .ToList();

        return new StandingsByDivisionResponse
        {
            Divisions = divisions,
            SportName = sportName
        };
    }

    private static void AccumulateStats(
        Dictionary<Guid, TeamStatsAccumulator> stats,
        Guid teamId, string teamName, string agegroupName, string divName, Guid divId,
        int teamScore, int opponentScore)
    {
        if (!stats.TryGetValue(teamId, out var acc))
        {
            acc = new TeamStatsAccumulator
            {
                TeamId = teamId,
                TeamName = teamName,
                AgegroupName = agegroupName,
                DivName = divName,
                DivId = divId
            };
            stats[teamId] = acc;
        }

        acc.Games++;
        if (teamScore > opponentScore) acc.Wins++;
        else if (teamScore < opponentScore) acc.Losses++;
        else acc.Ties++;
        acc.GoalsFor += teamScore;
        acc.GoalsAgainst += opponentScore;
    }

    private static string GetTeamCss(int? teamScore, int? opponentScore)
    {
        if (!teamScore.HasValue || !opponentScore.HasValue)
            return "pending";
        return teamScore > opponentScore ? "winner"
            : teamScore < opponentScore ? "loser"
            : "pending";
    }

    private static string GetBracketRoundName(string? type) => type switch
    {
        "Z" => "Round of 64",
        "Y" => "Round of 32",
        "X" => "Round of 16",
        "Q" => "Quarterfinals",
        "S" => "Semifinals",
        "F" => "Finals",
        _ => type ?? "Playoff"
    };

    private static int GetRoundOrder(string roundType) => roundType switch
    {
        "Z" => 1,
        "Y" => 2,
        "X" => 3,
        "Q" => 4,
        "S" => 5,
        "F" => 6,
        _ => 0
    };

    /// <summary>Internal accumulator for building standings.</summary>
    private sealed class TeamStatsAccumulator
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = "";
        public string AgegroupName { get; set; } = "";
        public string DivName { get; set; } = "";
        public Guid DivId { get; set; }
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Ties { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
    }
}
