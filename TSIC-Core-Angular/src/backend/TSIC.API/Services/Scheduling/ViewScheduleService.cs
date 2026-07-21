using TSIC.API.Services.Shared.Firebase;
using TSIC.Contracts.Constants;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Helpers;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Service for the View Schedule page (009-5).
/// Consumer-facing schedule viewer with Games, Standings, Records, Brackets, Contacts tabs.
/// </summary>
public sealed class ViewScheduleService : IViewScheduleService
{
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IBracketRepository _bracketRepo;
    private readonly IBracketAdvancementService _bracketAdvancement;
    private readonly IBracketSeedResolutionService _bracketResolution;
    private readonly IJobRepository _jobRepo;
    private readonly IGameResultPushService _gameResultPush;

    public ViewScheduleService(
        IScheduleRepository scheduleRepo,
        ITeamRepository teamRepo,
        IBracketRepository bracketRepo,
        IBracketAdvancementService bracketAdvancement,
        IBracketSeedResolutionService bracketResolution,
        IJobRepository jobRepo,
        IGameResultPushService gameResultPush)
    {
        _scheduleRepo = scheduleRepo;
        _teamRepo = teamRepo;
        _bracketRepo = bracketRepo;
        _bracketAdvancement = bracketAdvancement;
        _bracketResolution = bracketResolution;
        _jobRepo = jobRepo;
        _gameResultPush = gameResultPush;
    }

    // A round-robin result can complete a pool and lock its standings — resolve any
    // bracket leaf slots that draw from a now-final pool. No-op for bracket games and
    // for jobs without seed slots. (Bracket-game results advance via _bracketAdvancement.)
    //
    // Gated on THIS game's pool being final. A pool game can only move seeds drawn from
    // its own division, and only once that division is fully scored — so every score
    // before the pool's last one has nothing to resolve. Resolution costs a full-job
    // standings sweep, and auto-scoring an event walks every pool game in sequence, so
    // without this gate the sweep runs once per game instead of once per pool.
    private async Task ResolveSeedsIfPoolGameAsync(
        Guid jobId, string userId, Domain.Entities.Schedule game, CancellationToken ct)
    {
        if (game.T1Type != "T" || game.DivId is null) return;

        var incomplete = await _bracketRepo.GetIncompletePoolDivIdsAsync(jobId, ct);
        if (incomplete.Contains(game.DivId.Value)) return;

        await _bracketResolution.ResolveJobAsync(
            jobId, userId,
            (divIds, c) => GetStandingsAsync(
                jobId, new ScheduleFilterRequest { DivisionIds = [.. divIds] }, c), ct);
    }

    public async Task<ScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _scheduleRepo.GetScheduleFilterOptionsAsync(jobId, ct);
    }

    public async Task<ScheduleCapabilitiesDto> GetCapabilitiesAsync(
        Guid jobId, bool isAuthenticated, bool isAdmin, CancellationToken ct = default)
    {
        var (allowPublicAccess, hideContacts, sportName) = await _scheduleRepo.GetScheduleFlagsAsync(jobId, ct);
        var statusOptions = await _scheduleRepo.GetGameStatusOptionsAsync(ct);
        // Only meaningful for admins in a sandbox — the seed strip is gated on both anyway —
        // but cheap to always resolve so the flag is available wherever capabilities is read.
        var isReseedTournament = await _jobRepo.GetReseedTournamentFlagAsync(jobId, ct);
        var restrictPublicRosters = await _jobRepo.IsPublicRostersRestrictedAsync(jobId, ct);

        return new ScheduleCapabilitiesDto
        {
            CanScore = isAuthenticated && isAdmin,
            HideContacts = hideContacts,
            IsPublicAccess = allowPublicAccess,
            SportName = sportName,
            GameStatusOptions = statusOptions,
            IsReseedTournament = isReseedTournament,
            RestrictPublicRosters = restrictPublicRosters
        };
    }

    public async Task<List<ViewGameDto>> GetGamesAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        var games = await _scheduleRepo.GetFilteredGamesAsync(jobId, request, ct);
        var recordLookup = await BuildTeamRecordLookupAsync(jobId, ct);
        var bracketAgegroupIds = (await _scheduleRepo.GetBracketAgegroupIdsAsync(jobId, ct)).ToHashSet();

        return games.Select(g =>
        {
            var t1Type = g.T1Type ?? "T";
            var t2Type = g.T2Type ?? "T";

            return new ViewGameDto
            {
                Gid = g.Gid,
                GDate = g.GDate!.Value,
                FName = g.Field?.FName ?? g.FName ?? "",
                FieldId = g.FieldId ?? Guid.Empty,
                Latitude = g.Field?.Latitude,
                Longitude = g.Field?.Longitude,
                FAddress = FieldAddressFormatter.Build(g.Field),
                AgDiv = $"{g.AgegroupName}:{g.DivName}",
                T1Name = g.T1Name ?? "",
                T2Name = g.T2Name ?? "",
                T1Id = g.T1Id,
                T2Id = g.T2Id,
                T1Score = g.T1Score,
                T2Score = g.T2Score,
                T1Type = t1Type,
                T2Type = t2Type,
                T1TypeDesc = g.T1TypeNavigation?.TeamTypeDesc,
                T2TypeDesc = g.T2TypeNavigation?.TeamTypeDesc,
                // Bracket slot label "{type}{seed}" (e.g. "X1", "Q8") so the Games grid reads a
                // seeded bracket slot as "USC (X1)" and an unresolved one as "(Q1)" instead of a
                // blank — restoring the legacy schedule annotation (IScheduleService.FormatTeamName)
                // that the migration dropped. Null for round-robin/consolation (no label).
                T1SlotLabel = GameRoundTypes.IsBracket(t1Type) ? $"{t1Type}{g.T1No}" : null,
                T2SlotLabel = GameRoundTypes.IsBracket(t2Type) ? $"{t2Type}{g.T2No}" : null,
                T1Ann = g.T1Ann,
                T2Ann = g.T2Ann,
                Rnd = g.Rnd,
                GStatusCode = g.GStatusCode,
                GStatusText = g.GStatusCodeNavigation?.GStatusText,
                Color = g.Agegroup?.Color,
                T1Record = t1Type == "T" && g.T1Id.HasValue
                    ? recordLookup.GetValueOrDefault(g.T1Id.Value) : null,
                T2Record = t2Type == "T" && g.T2Id.HasValue
                    ? recordLookup.GetValueOrDefault(g.T2Id.Value) : null,
                DivName = g.DivName,
                GameAgegroupHasBrackets = bracketAgegroupIds.Contains(g.AgegroupId ?? Guid.Empty)
            };
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

    public async Task<TeamResultsResponse> GetTeamResultsAsync(Guid teamId, CancellationToken ct = default)
    {
        // Look up team identity (name, agegroup, club) for the modal title
        var detail = await _teamRepo.GetTeamDetailAsync(teamId, ct);

        var games = await _scheduleRepo.GetTeamGamesAsync(teamId, ct);

        // Build opponent record lookup
        var jobId = detail?.JobId ?? Guid.Empty;
        var recordLookup = jobId != Guid.Empty
            ? await BuildTeamRecordLookupAsync(jobId, ct)
            : new Dictionary<Guid, string>();

        var gameResults = games.Select(g =>
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

            // Translate team-role code → game-type label. reference.scheduleTeamTypes.teamTypeDesc
            // describes the TEAM's role (e.g. "Finalist"), not the GAME type (e.g. "Finals"),
            // so we intentionally map codes to game-oriented copy here.
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
                GameType = gameType,
                OpponentRecord = oppId.HasValue ? recordLookup.GetValueOrDefault(oppId.Value) : null,
                Latitude = g.Field?.Latitude,
                Longitude = g.Field?.Longitude,
                FAddress = FieldAddressFormatter.Build(g.Field),
                GStatusCode = g.GStatusCode,
                GStatusText = g.GStatusCodeNavigation?.GStatusText
            };
        }).ToList();

        return new TeamResultsResponse
        {
            TeamName = detail?.TeamName ?? "Unknown Team",
            AgegroupName = detail?.AgegroupName ?? "",
            ClubName = detail?.ClubName,
            // Subject team's own record, already sitting in the lookup built for OpponentRecord.
            TeamRecord = recordLookup.GetValueOrDefault(teamId),
            Games = gameResults
        };
    }

    public async Task<List<DivisionBracketResponse>> GetBracketsAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        var bracketGames = await _scheduleRepo.GetBracketGamesAsync(jobId, request, ct);
        var consolationGames = await _scheduleRepo.GetConsolationGamesAsync(jobId, request, ct);

        // Agegroups with BChampionsByDivision = true get per-division brackets;
        // all others (false / null) get per-agegroup brackets (the common case).
        var distinctAgIds = bracketGames.Concat(consolationGames)
            .Where(g => g.AgegroupId.HasValue)
            .Select(g => g.AgegroupId!.Value)
            .Distinct();
        var byDivAgIds = await _scheduleRepo.GetChampionsByDivisionAgegroupIdsAsync(distinctAgIds, ct);

        // Group key: agegroup name, plus division name ONLY for champions-by-division agegroups.
        (string AgName, string DivName) KeyOf(Domain.Entities.Schedule g) => (
            g.AgegroupName ?? "",
            g.AgegroupId.HasValue && byDivAgIds.Contains(g.AgegroupId.Value) ? (g.DivName ?? "") : "");

        var bracketByKey = bracketGames.GroupBy(KeyOf).ToDictionary(grp => grp.Key, grp => grp.ToList());
        var consolationByKey = consolationGames.GroupBy(KeyOf).ToDictionary(grp => grp.Key, grp => grp.ToList());

        var result = new List<DivisionBracketResponse>();

        // Union of keys so a consolation-only division still surfaces (with empty Matches).
        foreach (var key in bracketByKey.Keys.Union(consolationByKey.Keys)
            .OrderBy(k => k.AgName).ThenBy(k => k.DivName))
        {
            var gameList = bracketByKey.GetValueOrDefault(key, []);

            // Compute ParentGid using legacy's proven algorithm:
            // For each game, find the game in the SAME division, NEXT round (Rnd+1),
            // where T1_No or T2_No matches this game's T1_No (seed position).
            // This builds the bracket tree: child → parent (upward).

            var matches = gameList.Select(g =>
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

                // Legacy Pgid algorithm: find parent game in next round
                int? parentGid = null;
                if (g.Rnd.HasValue)
                {
                    var t1Rank = g.T1No ?? 0;
                    var nextRnd = (byte)(g.Rnd.Value + 1);

                    // FirstOrDefault (over a deterministic order), NOT SingleOrDefault: a data
                    // anomaly with two candidate parents (e.g. bronze sharing Rnd with the final
                    // and a null seed no. coalescing to 0) must not throw and 500 the whole tab.
                    parentGid = gameList
                        .Where(p => p.DivId == g.DivId
                                    && p.Rnd == nextRnd
                                    && ((p.T1No ?? 0) == t1Rank || (p.T2No ?? 0) == t1Rank))
                        .OrderBy(p => p.Gid)
                        .Select(p => (int?)p.Gid)
                        .FirstOrDefault();

                    // Legacy: Pgid == 0 means no parent → null
                    if (parentGid == 0) parentGid = null;
                }

                return new BracketMatchDto
                {
                    Gid = g.Gid,
                    T1Name = g.T1Name ?? $"({g.T1Type}{g.T1No})",
                    T2Name = g.T2Name ?? $"({g.T2Type}{g.T2No})",
                    T1Id = g.T1Id,
                    T2Id = g.T2Id,
                    T1Score = g.T1Score,
                    T2Score = g.T2Score,
                    T1Css = t1Css,
                    T2Css = t2Css,
                    LocationTime = locationTime,
                    GDate = g.GDate,
                    FName = g.Field?.FName ?? g.FName,
                    FieldId = g.FieldId,
                    RoundType = roundType,
                    ParentGid = parentGid
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

            // Consolation games ride alongside the ladder, never inside Matches.
            var consolation = consolationByKey.GetValueOrDefault(key, [])
                .Select(g => new ConsolationGameDto
                {
                    Gid = g.Gid,
                    AgegroupName = g.AgegroupName ?? "",
                    AgegroupId = g.AgegroupId ?? Guid.Empty,
                    FName = g.Field?.FName ?? g.FName,
                    GDate = g.GDate,
                    T1Name = g.T1Name ?? "",
                    T2Name = g.T2Name ?? "",
                    T1Id = g.T1Id,
                    T2Id = g.T2Id,
                    T1Score = g.T1Score,
                    T2Score = g.T2Score,
                    FAddress = FieldAddressFormatter.Build(g.Field)
                })
                .ToList();

            result.Add(new DivisionBracketResponse
            {
                AgegroupName = key.AgName,
                DivName = key.DivName,
                Champion = champion,
                Matches = matches,
                ConsolationGames = consolation
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

        // Job scoping: the caller's JWT resolves to one job — it must own this game.
        // Without this, a Scorer/Director token for job A could score job B's games.
        if (game.JobId != jobId)
            throw new InvalidOperationException("Game does not belong to this event.");

        game.T1Score = request.T1Score;   // null clears the score back to unscored
        game.T2Score = request.T2Score;
        // Leagues.GameStatusCodes: 1=scheduled, 3=rescheduled, 4=forfeit, 5=cancelled, 6=final.
        // Entering a score implies the game has concluded → default to 6 (final).
        var status = request.GStatusCode ?? 6;
        // A cleared game (both scores null) cannot be "final" (6) — that pairing is
        // contradictory. Reset to 1 (scheduled). Only the contradictory final case is
        // overridden: an explicit forfeit/cancel/reschedule legitimately has no score.
        if (request.T1Score is null && request.T2Score is null && status == 6)
            status = 1;
        game.GStatusCode = status;
        game.LebUserId = userId;
        game.Modified = DateTime.Now;

        // R2: a single-elimination bracket game cannot end in a tie — reject before persisting.
        _bracketAdvancement.EnsureBracketScoreValid(game);

        await _scheduleRepo.SaveChangesAsync(ct);

        // R2: write the winner (and loser, for a bronze feed) forward into the next game.
        await _bracketAdvancement.AdvanceWinnerAsync(game.Gid, userId, ct);

        // R1: a pool result may lock standings — fill any bracket slots seeded from it.
        await ResolveSeedsIfPoolGameAsync(jobId, userId, game, ct);

        // Notify devices subscribed to either team (best-effort; never throws).
        await _gameResultPush.PushGameResultAsync(game.Gid, ct);
    }

    public async Task EditGameAsync(
        Guid jobId, string userId, EditGameRequest request, CancellationToken ct = default)
    {
        var game = await _scheduleRepo.GetGameByIdAsync(request.Gid, ct);
        if (game == null) return;

        // Job scoping — same guard as QuickEditScoreAsync.
        if (game.JobId != jobId)
            throw new InvalidOperationException("Game does not belong to this event.");

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
        game.Modified = DateTime.Now;

        // R2: a single-elimination bracket game cannot end in a tie — reject before persisting.
        _bracketAdvancement.EnsureBracketScoreValid(game);

        await _scheduleRepo.SaveChangesAsync(ct);

        // R2: write the winner (and loser, for a bronze feed) forward into the next game.
        await _bracketAdvancement.AdvanceWinnerAsync(game.Gid, userId, ct);

        // R1: a pool result may lock standings — fill any bracket slots seeded from it.
        await ResolveSeedsIfPoolGameAsync(jobId, userId, game, ct);

        // Notify subscribed devices only when this edit changed a score — a pure
        // reschedule/annotation edit is not a game result.
        if (request.T1Score.HasValue || request.T2Score.HasValue)
            await _gameResultPush.PushGameResultAsync(game.Gid, ct);
    }

    // ── Mobile deep-link lookups ──
    // Resolve the owning job + division from a gid/teamId the mobile app already holds,
    // then delegate to the matching tab method. Division-less rows fall back to the
    // agegroup filter (brackets are agegroup-scoped when divisions aren't assigned).

    public async Task<List<DivisionBracketResponse>?> GetBracketsByGameAsync(int gid, CancellationToken ct = default)
    {
        var game = await _scheduleRepo.GetGameByIdAsync(gid, ct);
        if (game == null) return null;

        return await GetBracketsAsync(game.JobId, DivisionOrAgegroupFilter(game.DivId, game.AgegroupId), ct);
    }

    public async Task<List<DivisionBracketResponse>?> GetBracketsByTeamAsync(Guid teamId, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetByIdReadOnlyAsync(teamId, ct);
        if (team == null) return null;

        return await GetBracketsAsync(team.JobId, DivisionOrAgegroupFilter(team.DivId, team.AgegroupId), ct);
    }

    public async Task<StandingsByDivisionResponse?> GetStandingsByGameAsync(int gid, CancellationToken ct = default)
    {
        var game = await _scheduleRepo.GetGameByIdAsync(gid, ct);
        if (game == null) return null;

        return await GetStandingsAsync(game.JobId, DivisionOrAgegroupFilter(game.DivId, game.AgegroupId), ct);
    }

    public async Task<StandingsByDivisionResponse?> GetStandingsByTeamAsync(Guid teamId, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetByIdReadOnlyAsync(teamId, ct);
        if (team == null) return null;

        return await GetStandingsAsync(team.JobId, DivisionOrAgegroupFilter(team.DivId, team.AgegroupId), ct);
    }

    private static ScheduleFilterRequest DivisionOrAgegroupFilter(Guid? divId, Guid? agegroupId) =>
        divId != null
            ? new ScheduleFilterRequest { DivisionIds = [divId.Value] }
            : agegroupId != null
                ? new ScheduleFilterRequest { AgegroupIds = [agegroupId.Value] }
                : new ScheduleFilterRequest();

    // ── Private Helpers ──

    /// <summary>
    /// Builds a lookup of teamId → "W-L-T" from all scored pool-play games in the job.
    /// </summary>
    private async Task<Dictionary<Guid, string>> BuildTeamRecordLookupAsync(
        Guid jobId, CancellationToken ct)
    {
        var allGames = await _scheduleRepo.GetFilteredGamesAsync(jobId, new ScheduleFilterRequest(), ct);

        var scoredPoolPlay = allGames
            .Where(g => g.T1Type == "T" && g.T2Type == "T"
                && g.T1Score.HasValue && g.T2Score.HasValue
                && g.T1Id.HasValue && g.T2Id.HasValue)
            .ToList();

        var teamStats = new Dictionary<Guid, TeamStatsAccumulator>();

        foreach (var g in scoredPoolPlay)
        {
            AccumulateStats(teamStats, g.T1Id!.Value, g.T1Name ?? "", g.AgegroupName ?? "",
                g.DivName ?? "", g.DivId ?? Guid.Empty, g.T1Score!.Value, g.T2Score!.Value);
            AccumulateStats(teamStats, g.T2Id!.Value, g.T2Name ?? "", g.AgegroupName ?? "",
                g.DivName ?? "", g.DivId ?? Guid.Empty, g.T2Score!.Value, g.T1Score!.Value);
        }

        return teamStats.ToDictionary(
            kvp => kvp.Key,
            kvp => $"{kvp.Value.Wins}-{kvp.Value.Losses}-{kvp.Value.Ties}");
    }

    private async Task<StandingsByDivisionResponse> BuildStandingsAsync(
        Guid jobId, ScheduleFilterRequest request, bool poolPlayOnly, CancellationToken ct)
    {
        var games = await _scheduleRepo.GetFilteredGamesAsync(jobId, request, ct);
        var sportName = await _scheduleRepo.GetSportNameAsync(jobId, ct);
        var bracketAgegroupIds = (await _scheduleRepo.GetBracketAgegroupIdsAsync(jobId, ct)).ToHashSet();

        // Filter to pool play if needed
        if (poolPlayOnly)
            games = games.Where(g => g.T1Type == "T" && g.T2Type == "T").ToList();

        // Seed every team that appears in the schedule so unscored teams show as 0-0-0
        var teamStats = new Dictionary<Guid, TeamStatsAccumulator>();

        foreach (var g in games)
        {
            if (g.T1Id.HasValue && !teamStats.ContainsKey(g.T1Id.Value))
            {
                teamStats[g.T1Id.Value] = new TeamStatsAccumulator
                {
                    TeamId = g.T1Id.Value,
                    TeamName = g.T1Name ?? "",
                    AgegroupName = g.AgegroupName ?? "",
                    AgegroupId = g.AgegroupId ?? Guid.Empty,
                    DivName = g.DivName ?? "",
                    DivId = g.DivId ?? Guid.Empty
                };
            }
            if (g.T2Id.HasValue && !teamStats.ContainsKey(g.T2Id.Value))
            {
                teamStats[g.T2Id.Value] = new TeamStatsAccumulator
                {
                    TeamId = g.T2Id.Value,
                    TeamName = g.T2Name ?? "",
                    AgegroupName = g.AgegroupName ?? "",
                    AgegroupId = g.AgegroupId ?? Guid.Empty,
                    DivName = g.DivName ?? "",
                    DivId = g.DivId ?? Guid.Empty
                };
            }
        }

        // Accumulate stats from scored games only
        var scoredGames = games
            .Where(g => g.T1Score.HasValue && g.T2Score.HasValue
                && g.T1Id.HasValue && g.T2Id.HasValue)
            .ToList();

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
                        PointsPerGame = ppg,
                        TiePoints = t.Ties
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

                var agegroupId = divGroup.First().AgegroupId;
                return new DivisionStandingsDto
                {
                    DivId = divGroup.Key.DivId,
                    AgegroupId = agegroupId,
                    AgegroupName = divGroup.Key.AgegroupName,
                    DivName = divGroup.Key.DivName,
                    Teams = teams,
                    AgegroupHasBrackets = bracketAgegroupIds.Contains(agegroupId)
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

    // Translates a team-role code (from reference.scheduleTeamTypes) into game-type copy.
    // The DB's teamTypeDesc describes the TEAM's role ("Finalist"); this method maps to
    // game-oriented labels ("Finals") for display in the Team Results modal's game-type badge.
    private static string GetBracketRoundName(string? type) => type switch
    {
        "Z" => "Round of 64",
        "Y" => "Round of 32",
        "X" => "Round of 16",
        "Q" => "Quarterfinals",
        "S" => "Semifinals",
        "F" => "Finals",
        "B" => "Bronze",
        "C" => "Consolation",
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
        "B" => 6, // Bronze (3rd place) sits at the same level as Finals; Gid breaks the tie.
        _ => 0
    };

    /// <summary>Internal accumulator for building standings.</summary>
    private sealed class TeamStatsAccumulator
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = "";
        public string AgegroupName { get; set; } = "";
        public Guid AgegroupId { get; set; }
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
