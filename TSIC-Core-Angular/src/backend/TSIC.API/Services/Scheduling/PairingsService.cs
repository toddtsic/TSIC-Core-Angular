using TSIC.Contracts.Constants;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Service for the Manage Pairings scheduling tool.
/// Ports the round-robin and single-elimination algorithms from the legacy PairingsController.
/// </summary>
public sealed class PairingsService : IPairingsService
{
    private readonly IPairingsRepository _pairingsRepo;
    private readonly IBracketRepository _bracketRepo;
    private readonly IDivisionRepository _divisionRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IClubTeamRepository _clubTeamRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly TSIC.API.Services.Teams.ITeamRenameService _teamRename;
    private readonly ITeamSeatingService _teamSeating;
    private readonly ILogger<PairingsService> _logger;

    /// <summary>Ladder round type → number of teams entering that round.</summary>
    private static readonly Dictionary<string, int> RoundSize = new()
    {
        ["Z"] = 64,
        ["Y"] = 32,
        ["X"] = 16,
        ["Q"] = 8,
        ["S"] = 4,
        ["F"] = 2
    };

    public PairingsService(
        IPairingsRepository pairingsRepo,
        IBracketRepository bracketRepo,
        IDivisionRepository divisionRepo,
        ITeamRepository teamRepo,
        IClubTeamRepository clubTeamRepo,
        IScheduleRepository scheduleRepo,
        ISchedulingContextResolver contextResolver,
        TSIC.API.Services.Teams.ITeamRenameService teamRename,
        ITeamSeatingService teamSeating,
        ILogger<PairingsService> logger)
    {
        _pairingsRepo = pairingsRepo;
        _bracketRepo = bracketRepo;
        _divisionRepo = divisionRepo;
        _teamRepo = teamRepo;
        _clubTeamRepo = clubTeamRepo;
        _scheduleRepo = scheduleRepo;
        _contextResolver = contextResolver;
        _teamRename = teamRename;
        _teamSeating = teamSeating;
        _logger = logger;
    }

    // ── Navigator ──

    public async Task<List<AgegroupWithDivisionsDto>> GetAgegroupsWithDivisionsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        var agegroups = await _pairingsRepo.GetAgegroupsWithDivisionsAsync(leagueId, season, ct);

        var result = new List<AgegroupWithDivisionsDto>();
        foreach (var ag in agegroups)
        {
            var divisions = new List<DivisionSummaryDto>();
            foreach (var div in ag.Divisions.OrderBy(d => d.DivName))
            {
                var teamCount = await _pairingsRepo.GetDivisionTeamCountAsync(div.DivId, jobId, ct);
                divisions.Add(new DivisionSummaryDto
                {
                    DivId = div.DivId,
                    DivName = div.DivName ?? "",
                    TeamCount = teamCount
                });
            }

            result.Add(new AgegroupWithDivisionsDto
            {
                AgegroupId = ag.AgegroupId,
                AgegroupName = ag.AgegroupName ?? "",
                SortAge = ag.SortAge,
                Color = ag.Color,
                BChampionsByDivision = ag.BChampionsByDivision,
                Divisions = divisions
            });
        }

        return result;
    }

    // ── Division Pairings ──

    public async Task<DivisionPairingsResponse> GetDivisionPairingsAsync(
        Guid jobId, Guid divId, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        var division = await _divisionRepo.GetByIdReadOnlyAsync(divId, ct)
            ?? throw new KeyNotFoundException($"Division {divId} not found.");

        var teamCount = await _pairingsRepo.GetDivisionTeamCountAsync(divId, jobId, ct);
        var pairings = await _pairingsRepo.GetPairingsAsync(leagueId, season, teamCount, ct);

        // Determine which pairings are already scheduled for THIS division
        var scheduledKeys = await _pairingsRepo.GetScheduledPairingKeysAsync(leagueId, season, divId, ct);

        return new DivisionPairingsResponse
        {
            DivId = divId,
            DivName = division.DivName ?? "",
            TeamCount = teamCount,
            Pairings = pairings.Select(p => MapToDto(p, scheduledKeys)).ToList()
        };
    }

    // ── Who Plays Who ──

    public async Task<WhoPlaysWhoResponse> GetWhoPlaysWhoAsync(
        Guid jobId, int teamCount, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);
        var pairings = await _pairingsRepo.GetPairingsAsync(leagueId, season, teamCount, ct);

        // Build N×N matrix (0-indexed: matrix[i][j] = games between team i+1 and team j+1)
        var matrix = new int[teamCount][];
        for (var i = 0; i < teamCount; i++)
            matrix[i] = new int[teamCount];

        foreach (var p in pairings.Where(p => p.T1Type == "T" && p.T2Type == "T"))
        {
            var t1Idx = p.T1 - 1;
            var t2Idx = p.T2 - 1;
            if (t1Idx >= 0 && t1Idx < teamCount && t2Idx >= 0 && t2Idx < teamCount)
            {
                matrix[t1Idx][t2Idx]++;
                matrix[t2Idx][t1Idx]++;
            }
        }

        return new WhoPlaysWhoResponse { TeamCount = teamCount, Matrix = matrix };
    }

    // ── Add Block (Round-Robin) ──

    public async Task<List<PairingDto>> AddPairingBlockAsync(
        Guid jobId, string userId, AddPairingBlockRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);
        var (maxGame, maxRound) = await _pairingsRepo.GetMaxGameAndRoundAsync(
            leagueId, season, request.TeamCount, ct);

        var masterPairings = await _pairingsRepo.GetMasterPairingsAsync(
            request.TeamCount, request.NoRounds, ct);

        var newRecords = masterPairings.Select(mp => new PairingsLeagueSeason
        {
            GameNumber = mp.GNo + maxGame,
            GCnt = mp.GCnt,
            LeagueId = leagueId,
            LebUserId = userId,
            Modified = DateTime.Now,
            Rnd = mp.Rnd + maxRound,
            Season = season,
            T1 = mp.T1,
            T2 = mp.T2,
            T1Type = "T",
            T2Type = "T",
            TCnt = request.TeamCount
        }).ToList();

        if (newRecords.Count > 0)
        {
            await _pairingsRepo.AddRangeAsync(newRecords, ct);
            await _pairingsRepo.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "AddBlock: {Count} pairings for TCnt={TCnt}, {Rounds} rounds in league {LeagueId}",
            newRecords.Count, request.TeamCount, request.NoRounds, leagueId);

        return newRecords.Select(p => MapToDto(p, [])).ToList();
    }

    // ── Bracket Strategies (format picker) ──

    public Task<List<BracketStrategyDto>> GetBracketStrategiesAsync(CancellationToken ct = default) =>
        _bracketRepo.GetStrategiesAsync(ct);

    // ── Add Championship Bracket (strategy-template driven) ──

    /// <summary>
    /// Emit bracket pairings for a strategy (default "SE") straight from its
    /// <c>brackets.*</c> template — the template is the source of truth, not the
    /// legacy BracketDataSingleElimination table. <see cref="AddSingleEliminationRequest.StartKey"/>
    /// fixes the bracket size (Z=64…F=2); rounds run from that entry down to Finals.
    /// The optional bronze game is NOT emitted here (it is auto-placed downstream),
    /// matching the legacy cascade which also stopped at Finals.
    /// </summary>
    public async Task<List<PairingDto>> AddSingleEliminationAsync(
        Guid jobId, string userId, AddSingleEliminationRequest request, CancellationToken ct = default)
    {
        if (!RoundSize.TryGetValue(request.StartKey, out var bracketSize))
            throw new ArgumentException(
                $"Unknown bracket entry key '{request.StartKey}' (expected Z/Y/X/Q/S/F).", nameof(request));

        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        var strategyCode = string.IsNullOrWhiteSpace(request.StrategyCode) ? "SE" : request.StrategyCode;
        var template = await _bracketRepo.GetTemplateAsync(strategyCode, bracketSize, ct: ct)
            ?? throw new InvalidOperationException(
                $"No {strategyCode} bracket template of size {bracketSize} — run the template seed script.");

        var games = await _bracketRepo.GetTemplateGamesAsync(template.TemplateId, ct);
        var routes = await _bracketRepo.GetTemplateRoutesAsync(template.TemplateId, ct);
        var slotLabels = BracketTemplateTopology.ComputeSlotLabels(games, routes);

        var (maxGame, maxRound) = await _pairingsRepo.GetMaxGameAndRoundAsync(
            leagueId, season, request.TeamCount, ct);

        // Ladder rounds only (exclude the optional bronze 'B'), largest first
        // (Z→F): the earliest-played round takes the lowest new Rnd, matching the
        // legacy cascade's per-level round increment.
        var ladderRounds = games
            .Where(g => RoundSize.ContainsKey(g.RoundType))
            .GroupBy(g => g.RoundType)
            .OrderByDescending(grp => RoundSize[grp.Key]);

        var newRecords = new List<PairingsLeagueSeason>();
        var gameNo = maxGame;
        var round = maxRound;
        var now = DateTime.Now;

        foreach (var roundGroup in ladderRounds)
        {
            round++;
            // T1 = low label, T2 = high label; order the round by low label
            // ascending (the legacy "ORDER BY T1"), so GameNumbers line up.
            var orderedGames = roundGroup
                .Select(g => new
                {
                    Lo = Math.Min(slotLabels[(g.TemplateGameId, 1)], slotLabels[(g.TemplateGameId, 2)]),
                    Hi = Math.Max(slotLabels[(g.TemplateGameId, 1)], slotLabels[(g.TemplateGameId, 2)])
                })
                .OrderBy(g => g.Lo);

            foreach (var g in orderedGames)
            {
                newRecords.Add(new PairingsLeagueSeason
                {
                    T1 = g.Lo,
                    T2 = g.Hi,
                    T1Type = roundGroup.Key,
                    T2Type = roundGroup.Key,
                    GameNumber = ++gameNo,
                    GCnt = null,
                    LeagueId = leagueId,
                    LebUserId = userId,
                    Modified = now,
                    Rnd = round,
                    Season = season,
                    TCnt = request.TeamCount
                });
            }
        }

        if (newRecords.Count > 0)
        {
            await _pairingsRepo.AddRangeAsync(newRecords, ct);
            await _pairingsRepo.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "AddBracket: {Count} pairings ({Strategy} size {Size}, {StartKey}→F) for TCnt={TCnt}",
            newRecords.Count, strategyCode, bracketSize, request.StartKey, request.TeamCount);

        return newRecords.Select(p => MapToDto(p, [])).ToList();
    }

    // ── Add Consolation ──

    public async Task<PairingDto> AddConsolationPairingAsync(
        Guid jobId, string userId, AddConsolationPairingRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);
        var table = await _pairingsRepo.GetPairingsAsync(leagueId, season, request.TeamCount, ct);

        // Consolation games run in sequence: 1v2, 3v4, 5v6, ... capped at the team count.
        var t1 = HighestConsolationSlot(table) + 1;
        var t2 = t1 + 1;
        if (t2 > request.TeamCount)
            throw new InvalidOperationException(
                $"All consolation games for a {request.TeamCount}-team table already exist.");

        var pairing = new PairingsLeagueSeason
        {
            GameNumber = table.Count == 0 ? 1 : table.Max(p => p.GameNumber) + 1,
            Rnd = table.Count == 0 ? 1 : table.Max(p => p.Rnd) + 1,
            T1 = t1,
            T2 = t2,
            T1Type = GameRoundTypes.Consolation,
            T2Type = GameRoundTypes.Consolation,
            LeagueId = leagueId,
            Season = season,
            TCnt = request.TeamCount,
            LebUserId = userId,
            Modified = DateTime.Now
        };

        await _pairingsRepo.AddRangeAsync([pairing], ct);
        await _pairingsRepo.SaveChangesAsync(ct);

        return MapToDto(pairing, []);
    }

    // ── Delete Single ──

    public async Task DeletePairingAsync(int ai, CancellationToken ct = default)
    {
        var pairing = await _pairingsRepo.GetByIdAsync(ai, ct)
            ?? throw new KeyNotFoundException($"Pairing {ai} not found.");

        // Only the last consolation game may go — a hole mid-sequence is never refilled.
        if (pairing.T1Type == GameRoundTypes.Consolation)
        {
            var table = await _pairingsRepo.GetPairingsAsync(
                pairing.LeagueId, pairing.Season, pairing.TCnt ?? 0, ct);
            if (HighestConsolationSlot(table) > Math.Max(pairing.T1, pairing.T2))
                throw new InvalidOperationException(
                    "Only the last consolation game can be deleted.");
        }

        _pairingsRepo.Remove(pairing);
        await _pairingsRepo.SaveChangesAsync(ct);
    }

    // ── Remove All ──

    public async Task RemoveAllPairingsAsync(
        Guid jobId, RemoveAllPairingsRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);
        await _pairingsRepo.DeleteAllAsync(leagueId, season, request.TeamCount, ct);
        await _pairingsRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "RemoveAll: deleted pairings for TCnt={TCnt} in league {LeagueId} season {Season}",
            request.TeamCount, leagueId, season);
    }

    // ── Division Teams ──

    public async Task<List<DivisionTeamDto>> GetDivisionTeamsAsync(
        Guid jobId, Guid divId, CancellationToken ct = default)
    {
        var teams = await _teamRepo.GetByDivisionIdAsync(divId, ct);
        var activeTeams = teams.Where(t => t.Active == true).ToList();
        var clubNames = await _teamRepo.GetClubNamesByJobAsync(jobId, ct);
        var libraryNames = await _clubTeamRepo.GetLibraryNamesForClubTeamIdsAsync(
            activeTeams.Where(t => t.ClubTeamId.HasValue).Select(t => t.ClubTeamId!.Value), ct);

        return activeTeams
            .OrderBy(t => t.DivRank)
            .Select(t => new DivisionTeamDto
            {
                TeamId = t.TeamId,
                DivRank = t.DivRank,
                ClubName = clubNames.TryGetValue(t.TeamId, out var cn) ? cn : null,
                TeamName = t.TeamName,
                ClubTeamId = t.ClubTeamId,
                ClubTeamName = t.ClubTeamId is int libId && libraryNames.TryGetValue(libId, out var ln) ? ln : null
            })
            .ToList();
    }

    public async Task<List<DivisionTeamDto>> EditDivisionTeamAsync(
        Guid jobId, string userId, EditDivisionTeamRequest request, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetTeamFromTeamId(request.TeamId, ct)
            ?? throw new KeyNotFoundException($"Team {request.TeamId} not found.");

        if (team.JobId != jobId)
            throw new ArgumentException("Team does not belong to this job.");
        if (!team.DivId.HasValue)
            throw new InvalidOperationException("Team has no division assignment.");

        var divId = team.DivId.Value;

        // A rename here is THIS EVENT ONLY (director's own Teams row); a club-linked team's library and
        // other jobs keep their name. Library-wide rename lives in Search Teams (SuperUser) and the rep's library.
        //
        // Rank swap, renumber, rename and re-seat all live in ITeamSeatingService — including the
        // ordering (rename before re-seat, so the seat recompute reads the new name) that used to
        // be documented here and nowhere else. The pool-assignment screen ran the same edit
        // without the second half; the sequence is no longer any caller's to remember.
        var seating = await _teamSeating.ApplyRankChangeAsync(
            request.TeamId, jobId, request.DivRank, request.TeamName, userId, ct);

        _logger.LogInformation(
            "EditDivisionTeam: team {TeamId} in div {DivId} — {Message}",
            request.TeamId, divId, seating.Message);

        // Return refreshed team list
        return await GetDivisionTeamsAsync(jobId, divId, ct);
    }

    /// <summary>Highest slot number used by a consolation game in the table; 0 when none.</summary>
    private static int HighestConsolationSlot(IEnumerable<PairingsLeagueSeason> table) =>
        table.Where(p => p.T1Type == GameRoundTypes.Consolation)
             .Select(p => Math.Max(p.T1, p.T2))
             .DefaultIfEmpty(0)
             .Max();

    private static PairingDto MapToDto(
        PairingsLeagueSeason p, HashSet<(int Rnd, int T1, int T2)> scheduledKeys)
    {
        var isScheduled = scheduledKeys.Contains((p.Rnd, p.T1, p.T2));
        return new PairingDto
        {
            Ai = p.Ai,
            GameNumber = p.GameNumber,
            Rnd = p.Rnd,
            T1 = p.T1,
            T2 = p.T2,
            T1Type = p.T1Type,
            T2Type = p.T2Type,
            T1GnoRef = p.T1GnoRef,
            T2GnoRef = p.T2GnoRef,
            T1CalcType = p.T1CalcType,
            T2CalcType = p.T2CalcType,
            T1Annotation = p.T1Annotation,
            T2Annotation = p.T2Annotation,
            BAvailable = !isScheduled
        };
    }
}
