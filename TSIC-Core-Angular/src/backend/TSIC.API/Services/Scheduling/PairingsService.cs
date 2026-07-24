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
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly TSIC.API.Services.Teams.ITeamRenameService _teamRename;
    private readonly ILogger<PairingsService> _logger;

    /// <summary>Ladder round type → number of teams entering that round.</summary>
    private static readonly Dictionary<string, int> RoundSize = new()
    {
        ["Z"] = 64, ["Y"] = 32, ["X"] = 16, ["Q"] = 8, ["S"] = 4, ["F"] = 2
    };

    public PairingsService(
        IPairingsRepository pairingsRepo,
        IBracketRepository bracketRepo,
        IDivisionRepository divisionRepo,
        ITeamRepository teamRepo,
        IScheduleRepository scheduleRepo,
        ISchedulingContextResolver contextResolver,
        TSIC.API.Services.Teams.ITeamRenameService teamRename,
        ILogger<PairingsService> logger)
    {
        _pairingsRepo = pairingsRepo;
        _bracketRepo = bracketRepo;
        _divisionRepo = divisionRepo;
        _teamRepo = teamRepo;
        _scheduleRepo = scheduleRepo;
        _contextResolver = contextResolver;
        _teamRename = teamRename;
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

    // ── Add Single Pairing ──

    public async Task<PairingDto> AddSinglePairingAsync(
        Guid jobId, string userId, AddSinglePairingRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);
        var (maxGame, maxRound) = await _pairingsRepo.GetMaxGameAndRoundAsync(
            leagueId, season, request.TeamCount, ct);

        var pairing = new PairingsLeagueSeason
        {
            GameNumber = maxGame + 1,
            Rnd = maxRound + 1,
            T1 = 0,
            T2 = 0,
            T1Type = "T",
            T2Type = "T",
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

    // ── Edit Pairing ──

    public async Task EditPairingAsync(
        string userId, EditPairingRequest request, CancellationToken ct = default)
    {
        var pairing = await _pairingsRepo.GetByIdAsync(request.Ai, ct)
            ?? throw new KeyNotFoundException($"Pairing {request.Ai} not found.");

        if (request.GameNumber.HasValue) pairing.GameNumber = request.GameNumber.Value;
        if (request.Rnd.HasValue) pairing.Rnd = request.Rnd.Value;
        if (request.T1.HasValue) pairing.T1 = request.T1.Value;
        if (request.T2.HasValue) pairing.T2 = request.T2.Value;
        if (request.T1Type != null) pairing.T1Type = request.T1Type;
        if (request.T2Type != null) pairing.T2Type = request.T2Type;
        if (request.T1GnoRef.HasValue) pairing.T1GnoRef = request.T1GnoRef;
        if (request.T2GnoRef.HasValue) pairing.T2GnoRef = request.T2GnoRef;
        if (request.T1CalcType != null) pairing.T1CalcType = request.T1CalcType;
        if (request.T2CalcType != null) pairing.T2CalcType = request.T2CalcType;
        if (request.T1Annotation != null) pairing.T1Annotation = request.T1Annotation;
        if (request.T2Annotation != null) pairing.T2Annotation = request.T2Annotation;

        pairing.LebUserId = userId;
        pairing.Modified = DateTime.Now;

        await _pairingsRepo.SaveChangesAsync(ct);
    }

    // ── Delete Single ──

    public async Task DeletePairingAsync(int ai, CancellationToken ct = default)
    {
        var pairing = await _pairingsRepo.GetByIdAsync(ai, ct)
            ?? throw new KeyNotFoundException($"Pairing {ai} not found.");

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

        return activeTeams
            .OrderBy(t => t.DivRank)
            .Select(t => new DivisionTeamDto
            {
                TeamId = t.TeamId,
                DivRank = t.DivRank,
                ClubName = clubNames.TryGetValue(t.TeamId, out var cn) ? cn : null,
                TeamName = t.TeamName,
                ClubTeamId = t.ClubTeamId
            })
            .ToList();
    }

    public async Task<List<DivisionTeamDto>> EditDivisionTeamAsync(
        Guid jobId, string userId, bool isSuperUser, EditDivisionTeamRequest request, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetTeamFromTeamId(request.TeamId, ct)
            ?? throw new KeyNotFoundException($"Team {request.TeamId} not found.");

        if (team.JobId != jobId)
            throw new ArgumentException("Team does not belong to this job.");
        if (!team.DivId.HasValue)
            throw new InvalidOperationException("Team has no division assignment.");

        var divId = team.DivId.Value;
        var rankChanged = team.DivRank != request.DivRank;
        var nameChanged = request.TeamName != null && team.TeamName != request.TeamName;

        // Ownership gate (mirrors TeamSearchService.EditTeamAsync): a club-linked team's name is the
        // club's library identity — renaming fans out to other customers' schedules, so SuperUser only.
        if (nameChanged && team.ClubTeamId != null && !isSuperUser)
            throw new InvalidOperationException(
                "This team's name comes from its club's team library and appears in other events' schedules. "
                + "Only TSIC support can rename it — ask the club rep to rename it in their team library, or contact support.");

        // Rank swap: give the team at the target rank the editing team's old rank
        if (rankChanged)
        {
            var swapTeam = await _teamRepo.GetTeamByDivRankAsync(divId, request.DivRank, ct);
            if (swapTeam != null)
            {
                swapTeam.DivRank = team.DivRank;
                swapTeam.LebUserId = userId;
                swapTeam.Modified = DateTime.Now;
            }

            team.DivRank = request.DivRank;
        }

        team.LebUserId = userId;
        team.Modified = DateTime.Now;
        await _teamRepo.SaveChangesAsync(ct);

        // Renumber to ensure contiguous 1..N
        await _teamRepo.RenumberDivRanksAsync(divId, ct);

        // Rename (name owned by TeamRenameService): library + cross-job Teams copies +
        // WAITLIST twin + schedule. Runs before the division re-resolve below so the seat
        // recompute reads the updated team name.
        if (nameChanged)
            await _teamRename.RenameTeamAsync(request.TeamId, jobId, request.TeamName!, userId, ct);

        // Re-resolve T1Id/T2Id/T1Name/T2Name in all schedule records for this division
        await _scheduleRepo.SynchronizeScheduleTeamAssignmentsForDivisionAsync(divId, jobId, ct);

        _logger.LogInformation(
            "EditDivisionTeam: team {TeamId} in div {DivId} — rankChanged={RankChanged}, nameChanged={NameChanged}",
            request.TeamId, divId, rankChanged, nameChanged);

        // Return refreshed team list
        return await GetDivisionTeamsAsync(jobId, divId, ct);
    }

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
