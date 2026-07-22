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
    private readonly IDeviceRepository _deviceRepo;

    public ViewScheduleService(
        IScheduleRepository scheduleRepo,
        ITeamRepository teamRepo,
        IBracketRepository bracketRepo,
        IBracketAdvancementService bracketAdvancement,
        IBracketSeedResolutionService bracketResolution,
        IJobRepository jobRepo,
        IGameResultPushService gameResultPush,
        IDeviceRepository deviceRepo)
    {
        _scheduleRepo = scheduleRepo;
        _teamRepo = teamRepo;
        _bracketRepo = bracketRepo;
        _bracketAdvancement = bracketAdvancement;
        _bracketResolution = bracketResolution;
        _jobRepo = jobRepo;
        _gameResultPush = gameResultPush;
        _deviceRepo = deviceRepo;
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
        return await ProjectGamesAsync(jobId, request, games, ct);
    }

    public async Task<(List<ViewGameDto> Games, int TotalCount)> GetGamesPagedAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        // Paging is applied in the repository at the SQL level (bounded fetch / first paint).
        // The record lookup below is job-wide, so a paged request stays correct — it just projects
        // the page's rows against the same whole-job records.
        var (games, total) = await _scheduleRepo.GetFilteredGamesPagedAsync(jobId, request, ct);
        return (await ProjectGamesAsync(jobId, request, games, ct), total);
    }

    private async Task<List<ViewGameDto>> ProjectGamesAsync(
        Guid jobId, ScheduleFilterRequest request, List<Domain.Entities.Schedule> games, CancellationToken ct)
    {
        var recordLookup = await BuildTeamRecordLookupAsync(jobId, ct);
        var bracketAgegroupIds = (await _scheduleRepo.GetBracketAgegroupIdsAsync(jobId, ct)).ToHashSet();
        var hideScoresAgegroupIds = (await _scheduleRepo.GetHideScoresAgegroupIdsAsync(jobId, ct)).ToHashSet();
        var subscribedTeamIds = await GetSubscribedTeamIdSetAsync(request.DeviceToken, jobId, ct);

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
                GameAgegroupHasBrackets = bracketAgegroupIds.Contains(g.AgegroupId ?? Guid.Empty),
                BHideScores = hideScoresAgegroupIds.Contains(g.AgegroupId ?? Guid.Empty),
                T1IsSubscribed = g.T1Id.HasValue && subscribedTeamIds.Contains(g.T1Id.Value),
                T2IsSubscribed = g.T2Id.HasValue && subscribedTeamIds.Contains(g.T2Id.Value)
            };
        }).ToList();
    }

    public async Task<StandingsByDivisionResponse> GetStandingsAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        return await BuildStandingsAsync(jobId, request, poolPlayOnly: true, ct);
    }

    public async Task<(StandingsByDivisionResponse Response, int TotalCount)> GetStandingsPagedAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        // Build the full, ordered standings (records computed from every game), THEN page the
        // divisions list — a division is the standings unit. Paging over the already-aggregated
        // divisions keeps every team's W-L-T correct regardless of which page is requested.
        var full = await BuildStandingsAsync(jobId, request, poolPlayOnly: true, ct);
        var total = full.Divisions.Count;

        IEnumerable<DivisionStandingsDto> divisions = full.Divisions;
        if (request.Skip is int skip and > 0) divisions = divisions.Skip(skip);
        if (request.Take is int take and > 0) divisions = divisions.Take(take);

        return (full with { Divisions = divisions.ToList() }, total);
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

        // Maintain the two teams' stored pool record from the committed score (recompute-and-
        // overwrite via the canonical method — a clear drops the game, an edit re-tallies). Pool
        // games only: the stored record is pool-only, so a bracket/consolation score changes nothing
        // here. MUST run before seed resolution, which ranks off the pool standings these columns feed.
        if (game.T1Type == GameRoundTypes.RoundRobin)
            await MaintainTeamRecordsAsync(jobId, game, ct);

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

        // Maintain the stored pool record when this edit changed a pool game's score (see
        // QuickEditScoreAsync). Annotation/reschedule-only edits leave the record untouched.
        if (game.T1Type == GameRoundTypes.RoundRobin
            && (request.T1Score.HasValue || request.T2Score.HasValue))
            await MaintainTeamRecordsAsync(jobId, game, ct);

        // R2: write the winner (and loser, for a bronze feed) forward into the next game.
        await _bracketAdvancement.AdvanceWinnerAsync(game.Gid, userId, ct);

        // R1: a pool result may lock standings — fill any bracket slots seeded from it.
        await ResolveSeedsIfPoolGameAsync(jobId, userId, game, ct);

        // Notify subscribed devices only when this edit changed a score — a pure
        // reschedule/annotation edit is not a game result.
        if (request.T1Score.HasValue || request.T2Score.HasValue)
            await _gameResultPush.PushGameResultAsync(game.Gid, ct);
    }

    public async Task<int> RebuildTeamRecordsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Canonical pool record for every team that has scored pool games.
        var computed = (await _scheduleRepo.GetTeamRecordsAsync(jobId, includeNonTGames: false, ct))
            .ToDictionary(r => r.TeamId);

        // Universe = every team in the job, so teams with no scored games are ZEROED, not left stale.
        var allTeamIds = (await _teamRepo.GetStoredTeamRecordsAsync(jobId, ct)).Select(r => r.TeamId).ToList();
        if (allTeamIds.Count == 0) return 0;

        var points = await _teamRepo.GetSportPointsByTeamAsync(jobId, allTeamIds, ct);
        var records = new Dictionary<Guid, TeamRecordAggregate>(allTeamIds.Count);
        foreach (var teamId in allTeamIds)
        {
            var rec = computed.GetValueOrDefault(teamId)
                ?? new TeamRecordAggregate
                {
                    TeamId = teamId, Games = 0, Wins = 0, Losses = 0, Ties = 0, GoalsFor = 0, GoalsVs = 0
                };
            if (points.TryGetValue(teamId, out var p))
                rec = rec.WithPoints(p.WinPts, p.DrawPts, p.LossPts);
            records[teamId] = rec;
        }
        await _teamRepo.UpdateTeamRecordsAsync(records, ct);
        return records.Count;
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
    /// Builds a lookup of teamId → "W-L-T" pool record for the games-grid record buttons and
    /// opponent-record drill-down.
    /// </summary>
    private async Task<Dictionary<Guid, string>> BuildTeamRecordLookupAsync(
        Guid jobId, CancellationToken ct)
    {
        // Hot path: read the stored, score-entry-maintained pool record columns — no per-request
        // job-wide games GROUP BY. Stale/zero until the deploy-time backfill seeds them.
        var records = await _teamRepo.GetStoredTeamRecordsAsync(jobId, ct);
        return records.ToDictionary(
            r => r.TeamId,
            r => $"{r.Wins}-{r.Losses}-{r.Ties}");
    }

    /// <summary>
    /// Recompute-and-overwrite the stored pool record for the two teams a pool-game score write
    /// touched, from the committed schedule state via the canonical method. Autocorrecting: an
    /// edit re-tallies, a clear drops the game. Points are resolved from each team's sport. The
    /// per-team recomputes are awaited sequentially — never Task.WhenAll on one scoped DbContext.
    /// </summary>
    private async Task MaintainTeamRecordsAsync(
        Guid jobId, Domain.Entities.Schedule game, CancellationToken ct)
    {
        var teamIds = new List<Guid>(2);
        if (game.T1Id.HasValue) teamIds.Add(game.T1Id.Value);
        if (game.T2Id.HasValue) teamIds.Add(game.T2Id.Value);
        if (teamIds.Count == 0) return;

        var points = await _teamRepo.GetSportPointsByTeamAsync(jobId, teamIds, ct);
        var records = new Dictionary<Guid, TeamRecordAggregate>(teamIds.Count);
        foreach (var teamId in teamIds)
        {
            var rec = await _scheduleRepo.GetTeamRecordAsync(teamId, includeNonTGames: false, ct);
            if (points.TryGetValue(teamId, out var p))
                rec = rec.WithPoints(p.WinPts, p.DrawPts, p.LossPts);
            records[teamId] = rec;
        }
        await _teamRepo.UpdateTeamRecordsAsync(records, ct);
    }

    private async Task<StandingsByDivisionResponse> BuildStandingsAsync(
        Guid jobId, ScheduleFilterRequest request, bool poolPlayOnly, CancellationToken ct)
    {
        var games = await _scheduleRepo.GetFilteredGamesAsync(jobId, request, ct);
        var sportName = await _scheduleRepo.GetSportNameAsync(jobId, ct);
        var bracketAgegroupIds = (await _scheduleRepo.GetBracketAgegroupIdsAsync(jobId, ct)).ToHashSet();
        var subscribedTeamIds = await GetSubscribedTeamIdSetAsync(request.DeviceToken, jobId, ct);

        // Standings = pool play (T/T) only; Records = every game type.
        if (poolPlayOnly)
            games = games.Where(g => g.T1Type == GameRoundTypes.RoundRobin && g.T2Type == GameRoundTypes.RoundRobin).ToList();

        // Membership + identity from the schedule rows (unchanged): every team appearing in a game
        // in scope is listed, even at 0-0-0. Names/division come off the denormalized game row.
        var identity = new Dictionary<Guid, (string TeamName, string AgegroupName, Guid AgegroupId, string DivName, Guid DivId)>();
        foreach (var g in games)
        {
            if (g.T1Id.HasValue)
                identity.TryAdd(g.T1Id.Value,
                    (g.T1Name ?? "", g.AgegroupName ?? "", g.AgegroupId ?? Guid.Empty, g.DivName ?? "", g.DivId ?? Guid.Empty));
            if (g.T2Id.HasValue)
                identity.TryAdd(g.T2Id.Value,
                    (g.T2Name ?? "", g.AgegroupName ?? "", g.AgegroupId ?? Guid.Empty, g.DivName ?? "", g.DivId ?? Guid.Empty));
        }

        // NUMBERS come from the canonical record — never a re-tally here. Pool play reads the stored,
        // score-entry-maintained columns (the hybrid cache); full-season is computed live (not stored).
        var records = poolPlayOnly
            ? (await _teamRepo.GetStoredTeamRecordsAsync(jobId, ct)).ToDictionary(r => r.TeamId)
            : (await _scheduleRepo.GetTeamRecordsAsync(jobId, includeNonTGames: true, ct)).ToDictionary(r => r.TeamId);

        // Per-division ordering config: sport point values + the league's tiebreak rule chain.
        var divIds = identity.Values.Select(v => v.DivId).Where(d => d != Guid.Empty).Distinct().ToList();
        var sortConfig = await _scheduleRepo.GetStandingsSortConfigByDivisionAsync(divIds, ct);

        // Head-to-head from decisive games in scope — only ever consulted by the points rule to
        // break a tie between exactly two teams.
        var h2h = new HeadToHead(games
            .Where(g => g.T1Score.HasValue && g.T2Score.HasValue && g.T1Id.HasValue && g.T2Id.HasValue
                && g.T1Score!.Value != g.T2Score!.Value)
            .Select(g => g.T1Score!.Value > g.T2Score!.Value
                ? (g.T1Id!.Value, g.T2Id!.Value)
                : (g.T2Id!.Value, g.T1Id!.Value)));

        var divisions = identity
            .Select(kv => new { TeamId = kv.Key, Info = kv.Value })
            .GroupBy(t => new { t.Info.DivId, t.Info.AgegroupName, t.Info.DivName })
            .OrderBy(d => d.Key.AgegroupName)
            .ThenBy(d => d.Key.DivName)
            // DivId tiebreaker → total order. Two divisions can share Agegroup+Div display names
            // (distinct DivIds); without a unique final key, GetStandingsPagedAsync's Skip/Take
            // over this list could duplicate or drop a division block between page fetches.
            .ThenBy(d => d.Key.DivId)
            .Select(divGroup =>
            {
                var config = sortConfig.GetValueOrDefault(divGroup.Key.DivId);

                var teams = divGroup.Select(t =>
                {
                    var rec = records.GetValueOrDefault(t.TeamId);
                    var wins = rec?.Wins ?? 0;
                    var losses = rec?.Losses ?? 0;
                    var ties = rec?.Ties ?? 0;
                    var gamesPlayed = rec?.Games ?? 0;
                    var goalsFor = rec?.GoalsFor ?? 0;
                    var goalsVs = rec?.GoalsVs ?? 0;
                    // Pool points are the stored value; full-season points compute from the sport.
                    var points = poolPlayOnly
                        ? (rec?.Points ?? 0)
                        : config is null
                            ? (wins * 3) + ties
                            : (wins * config.WinPts) + (ties * config.DrawPts) + (losses * config.LossPts);
                    var ppg = gamesPlayed > 0 ? Math.Round((decimal)points / gamesPlayed, 2) : 0m;

                    return new StandingsDto
                    {
                        TeamId = t.TeamId,
                        TeamName = t.Info.TeamName,
                        AgegroupName = t.Info.AgegroupName,
                        DivName = t.Info.DivName,
                        DivId = t.Info.DivId,
                        Games = gamesPlayed,
                        Wins = wins,
                        Losses = losses,
                        Ties = ties,
                        GoalsFor = goalsFor,
                        GoalsAgainst = goalsVs,
                        GoalDiffMax9 = Math.Clamp(goalsFor - goalsVs, -9, 9),
                        Points = points,
                        PointsPerGame = ppg,
                        TiePoints = ties,
                        IsFavorited = subscribedTeamIds.Contains(t.TeamId)
                    };
                }).ToList();

                // Config-driven order (default = points → goal-diff → goals-for). The league's
                // StandingsSortProfile, when present, overrides the tiebreak chain; head-to-head
                // resolves a 2-team points tie. Replaces the hardcoded soccer/lacrosse branches.
                teams = ResolveGroup(teams, BuildSortRules(config), 0, h2h);
                for (var i = 0; i < teams.Count; i++)
                    teams[i] = teams[i] with { RankOrder = i + 1 };

                var agegroupId = divGroup.First().Info.AgegroupId;
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

    /// <summary>
    /// Device favorites are an anonymous-device feature: the client passes its own push token,
    /// and the schedule marks the teams that token has favorited (star state). Empty when no
    /// token is supplied — the web app never sends one, so the flag stays false there.
    /// </summary>
    private async Task<HashSet<Guid>> GetSubscribedTeamIdSetAsync(
        string? deviceToken, Guid jobId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(deviceToken))
            return new HashSet<Guid>();
        return (await _deviceRepo.GetSubscribedTeamIdsAsync(deviceToken, jobId, ct)).ToHashSet();
    }

    // ── Config-driven standings sort engine ──
    // Canonical StandingsSortRules vocabulary (reference.StandingsSortRules.StandingsSortRuleName).
    // The engine knows how to execute each named rule; the constraint (cap) travels on the rule.
    private static class SortRuleNames
    {
        public const string PointsWithH2H = "Points310With2TeamTieRule";
        public const string GoalsAgainst = "GoalsVs";
        public const string GoalDiffUncapped = "GoalDiffNoMax";
        public const string GoalDiffCapped = "GoalDiff9Max";
        public const string GoalsFor = "GoalsFor";
    }

    /// <summary>One tiebreak step: a key selector, its direction, and whether it is the points
    /// rule that additionally applies head-to-head to a 2-team tie.</summary>
    private sealed record SortRule(Func<StandingsDto, int> Key, bool Descending, bool PointsWithH2H);

    /// <summary>
    /// Translate a league's resolved config into an ordered rule chain. No profile (or an empty
    /// chain) → the default order: points → goal-diff (uncapped) → goals-for. Unknown rule names
    /// are skipped so a future DB rule never throws.
    /// </summary>
    private static List<SortRule> BuildSortRules(StandingsSortConfig? config)
    {
        if (config is null || config.Rules.Count == 0)
            return
            [
                new(t => t.Points, true, false),
                new(t => t.GoalsFor - t.GoalsAgainst, true, false),
                new(t => t.GoalsFor, true, false),
            ];

        var rules = new List<SortRule>(config.Rules.Count);
        foreach (var r in config.Rules)
        {
            switch (r.RuleName)
            {
                case SortRuleNames.PointsWithH2H:
                    rules.Add(new(t => t.Points, true, true)); break;
                case SortRuleNames.GoalsAgainst:
                    rules.Add(new(t => t.GoalsAgainst, false, false)); break;   // fewer goals against is better
                case SortRuleNames.GoalDiffUncapped:
                    rules.Add(new(t => t.GoalsFor - t.GoalsAgainst, true, false)); break;
                case SortRuleNames.GoalDiffCapped:
                    var cap = r.Constraint ?? 9;
                    rules.Add(new(t => Math.Clamp(t.GoalsFor - t.GoalsAgainst, -cap, cap), true, false)); break;
                case SortRuleNames.GoalsFor:
                    rules.Add(new(t => t.GoalsFor, true, false)); break;
                // Unknown → skip.
            }
        }
        // Guarantee a deterministic primary key if a profile resolved to nothing usable.
        if (rules.Count == 0) rules.Add(new(t => t.Points, true, false));
        return rules;
    }

    /// <summary>
    /// Order a division's teams by the rule chain, refining tie-groups rule by rule. A rule
    /// subdivides each remaining tie-group by its key; the points rule additionally resolves an
    /// exactly-two-team tie by head-to-head (a split / never-played / 3+-way tie falls through to
    /// the next rule). Remaining ties break on team name for a stable, deterministic order.
    /// </summary>
    private static List<StandingsDto> ResolveGroup(
        List<StandingsDto> group, IReadOnlyList<SortRule> rules, int ruleIndex, HeadToHead h2h)
    {
        if (group.Count <= 1) return group;
        if (ruleIndex >= rules.Count)
            return group.OrderBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase).ToList();

        var rule = rules[ruleIndex];
        var ordered = (rule.Descending
            ? group.OrderByDescending(rule.Key)
            : group.OrderBy(rule.Key)).ToList();

        var result = new List<StandingsDto>(group.Count);
        var i = 0;
        while (i < ordered.Count)
        {
            var key = rule.Key(ordered[i]);
            var j = i + 1;
            while (j < ordered.Count && rule.Key(ordered[j]) == key) j++;
            var tie = ordered.GetRange(i, j - i);

            if (tie.Count == 1)
                result.Add(tie[0]);
            else if (rule.PointsWithH2H && tie.Count == 2 && h2h.TryOrder(tie[0], tie[1], out var byH2H))
                result.AddRange(byH2H);                               // decisive head-to-head
            else
                result.AddRange(ResolveGroup(tie, rules, ruleIndex + 1, h2h));  // fall through

            i = j;
        }
        return result;
    }

    /// <summary>
    /// Head-to-head record over the games in scope. Net = (times a beat b) − (times b beat a).
    /// Ordering is decisive only when the net is non-zero for exactly the two teams asked about.
    /// </summary>
    private sealed class HeadToHead
    {
        private readonly Dictionary<(Guid, Guid), int> _net = [];

        public HeadToHead(IEnumerable<(Guid Winner, Guid Loser)> decisiveResults)
        {
            foreach (var (w, l) in decisiveResults)
            {
                _net[(w, l)] = _net.GetValueOrDefault((w, l)) + 1;
                _net[(l, w)] = _net.GetValueOrDefault((l, w)) - 1;
            }
        }

        public bool TryOrder(StandingsDto a, StandingsDto b, out List<StandingsDto> ordered)
        {
            var net = _net.GetValueOrDefault((a.TeamId, b.TeamId));
            ordered = net >= 0 ? [a, b] : [b, a];
            return net != 0;   // 0 → never played / split / only tied → not decisive
        }
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

}
