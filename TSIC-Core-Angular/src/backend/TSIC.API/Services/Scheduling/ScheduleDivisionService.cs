using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Utilities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Service for the Schedule by Division tool.
/// Focuses on manual game placement (the primary workflow) and grid assembly.
/// </summary>
public sealed class ScheduleDivisionService : IScheduleDivisionService
{
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ITimeslotRepository _timeslotRepo;
    private readonly IPairingsRepository _pairingsRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly IAgeGroupRepository _agegroupRepo;
    private readonly IDivisionRepository _divisionRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly ILogger<ScheduleDivisionService> _logger;

    public ScheduleDivisionService(
        IScheduleRepository scheduleRepo,
        ITimeslotRepository timeslotRepo,
        IPairingsRepository pairingsRepo,
        IFieldRepository fieldRepo,
        IAgeGroupRepository agegroupRepo,
        IDivisionRepository divisionRepo,
        ISchedulingContextResolver contextResolver,
        ILogger<ScheduleDivisionService> logger)
    {
        _scheduleRepo = scheduleRepo;
        _timeslotRepo = timeslotRepo;
        _pairingsRepo = pairingsRepo;
        _fieldRepo = fieldRepo;
        _agegroupRepo = agegroupRepo;
        _divisionRepo = divisionRepo;
        _contextResolver = contextResolver;
        _logger = logger;
    }

    public async Task<ScheduleGridResponse> GetScheduleGridAsync(
        Guid jobId, Guid agegroupId, Guid divId, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // 1. Get timeslot dates — try division-specific first, fall back to agegroup-level
        var dates = await _timeslotRepo.GetDatesAsync(agegroupId, season, year, ct);
        var divDates = dates.Where(d => d.DivId == divId).ToList();
        var effectiveDates = divDates.Count > 0 ? divDates : dates.Where(d => d.DivId == null).ToList();

        var gameDates = effectiveDates
            .Select(d => d.GDate.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // 2. Get field timeslots — try division-specific first, fall back to agegroup-level
        var fieldTimeslots = await _timeslotRepo.GetFieldTimeslotsAsync(agegroupId, season, year, ct);
        var divFields = fieldTimeslots.Where(f => f.DivId == divId).ToList();
        var effectiveFields = divFields.Count > 0 ? divFields : fieldTimeslots.Where(f => f.DivId == null).ToList();

        // Distinct field columns sorted by name
        var columns = effectiveFields
            .GroupBy(f => f.FieldId)
            .Select(g => new ScheduleFieldColumn
            {
                FieldId = g.Key,
                FName = g.First().Field?.FName ?? ""
            })
            .OrderBy(c => c.FName)
            .ToList();

        var fieldIds = columns.Select(c => c.FieldId).ToList();

        if (gameDates.Count == 0 || fieldIds.Count == 0)
            return new ScheduleGridResponse { Columns = columns, Rows = [] };

        // 3. Build all timeslots (date × field game intervals)
        var allTimeslots = new SortedSet<DateTime>();
        foreach (var date in gameDates)
        {
            var dow = date.DayOfWeek.ToString(); // Full name: Monday, Saturday, etc.
            var fieldsForDow = effectiveFields
                .Where(f => f.Dow.Equals(dow, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var ft in fieldsForDow)
            {
                if (TimeSpan.TryParse(ft.StartTime, out var startTime))
                {
                    for (var g = 0; g < ft.MaxGamesPerField; g++)
                    {
                        var gameTime = date + startTime + TimeSpan.FromMinutes(g * ft.GamestartInterval);
                        allTimeslots.Add(gameTime);
                    }
                }
            }
        }

        // 4. Query scheduled games for this grid
        var allGameDates = allTimeslots.ToList();
        var games = await _scheduleRepo.GetGamesForGridAsync(jobId, fieldIds, allGameDates, ct);

        // Index by (GDate, FieldId) for O(1) cell lookup; detect slot collisions
        var gameIndex = new Dictionary<(DateTime, Guid), Schedule>();
        var slotCollisionKeys = new HashSet<(DateTime, Guid)>();
        foreach (var game in games)
        {
            if (game.GDate.HasValue && game.FieldId.HasValue)
            {
                var key = (game.GDate.Value, game.FieldId.Value);
                if (gameIndex.ContainsKey(key))
                    slotCollisionKeys.Add(key);
                gameIndex[key] = game;
            }
        }

        // 5. Build agegroup color map for games (multiple agegroups may share fields)
        var agegroupIds = games
            .Where(g => g.AgegroupId.HasValue)
            .Select(g => g.AgegroupId!.Value)
            .Distinct()
            .ToList();
        var agColorMap = new Dictionary<Guid, string?>();
        foreach (var agId in agegroupIds)
        {
            var ag = await _agegroupRepo.GetByIdAsync(agId, ct);
            agColorMap[agId] = ag?.Color;
        }

        // 6. Assemble grid rows
        var rows = new List<ScheduleGridRow>();
        foreach (var timeslot in allTimeslots)
        {
            var cells = columns
                .Select(col =>
                {
                    if (!gameIndex.TryGetValue((timeslot, col.FieldId), out var game))
                        return (ScheduleGameDto?)null;

                    var agColor = game.AgegroupId.HasValue && agColorMap.TryGetValue(game.AgegroupId.Value, out var c) ? c : null;
                    var isCollision = slotCollisionKeys.Contains((timeslot, col.FieldId));
                    return ScheduleGameDtoMapper.Map(game, agColor, isCollision);
                })
                .ToList();

            rows.Add(new ScheduleGridRow { GDate = timeslot, Cells = cells });
        }

        return new ScheduleGridResponse { Columns = columns, Rows = rows };
    }

    public async Task<ScheduleGameDto> PlaceGameAsync(
        Guid jobId, string userId, PlaceGameRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var pairing = await _pairingsRepo.GetByIdAsync(request.PairingAi, ct)
            ?? throw new KeyNotFoundException($"Pairing {request.PairingAi} not found.");

        var field = await _fieldRepo.GetFieldByIdAsync(request.FieldId, ct);
        var agegroup = await _agegroupRepo.GetByIdAsync(request.AgegroupId, ct);
        var division = await _divisionRepo.GetByIdReadOnlyAsync(request.DivId, ct);

        var game = new Schedule
        {
            JobId = jobId,
            LeagueId = leagueId,
            Season = season,
            Year = year,
            AgegroupId = request.AgegroupId,
            AgegroupName = agegroup?.AgegroupName ?? "",
            DivId = request.DivId,
            DivName = division?.DivName ?? "",
            FieldId = request.FieldId,
            FName = field?.FName ?? "",
            GDate = request.GDate,
            GNo = 0,
            GStatusCode = 1, // Scheduled
            Rnd = (byte)pairing.Rnd,
            T1No = pairing.T1,
            T1Type = pairing.T1Type,
            T2No = (byte)pairing.T2,
            T2Type = pairing.T2Type,
            T1GnoRef = pairing.T1GnoRef,
            T2GnoRef = pairing.T2GnoRef,
            T1CalcType = pairing.T1CalcType,
            T2CalcType = pairing.T2CalcType,
            T1Ann = pairing.T1Annotation,
            T2Ann = pairing.T2Annotation,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _scheduleRepo.AddGame(game);
        await _scheduleRepo.SaveChangesAsync(ct);

        // Resolve team names from rank assignments (UpdateGameIds equivalent)
        await _scheduleRepo.SynchronizeScheduleTeamAssignmentsForDivisionAsync(request.DivId, jobId, ct);

        // Re-read to get resolved T1Name/T2Name
        var savedGame = await _scheduleRepo.GetGameByIdAsync(game.Gid, ct)
            ?? throw new InvalidOperationException("Failed to read back placed game.");

        _logger.LogInformation(
            "PlaceGame: Gid={Gid} pairing {PairingAi} at {GDate} on field {FieldName}",
            game.Gid, request.PairingAi, request.GDate, field?.FName);

        return ScheduleGameDtoMapper.Map(savedGame, agegroup?.Color);
    }

    public async Task MoveGameAsync(string userId, MoveGameRequest request, CancellationToken ct = default)
    {
        await SchedulingGameMutationHelper.MoveOrSwapGameAsync(
            request, userId, _scheduleRepo, _fieldRepo, _logger, ct);
    }

    public async Task DeleteGameAsync(int gid, CancellationToken ct = default)
    {
        await _scheduleRepo.DeleteGameAsync(gid, ct);
        await _scheduleRepo.SaveChangesAsync(ct);
        _logger.LogInformation("DeleteGame: Gid={Gid} with cascade cleanup", gid);
    }

    public async Task DeleteDivisionGamesAsync(Guid jobId, DeleteDivGamesRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        await _scheduleRepo.DeleteDivisionGamesAsync(request.DivId, leagueId, season, year, ct);
        await _scheduleRepo.SaveChangesAsync(ct);
        _logger.LogInformation("DeleteDivGames: DivId={DivId} with cascade cleanup", request.DivId);
    }

    public async Task<AutoScheduleResponse> AutoScheduleDivAsync(
        Guid jobId, string userId, Guid divId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // 1. Delete existing games for this division
        await _scheduleRepo.DeleteDivisionGamesAsync(divId, leagueId, season, year, ct);
        await _scheduleRepo.SaveChangesAsync(ct);

        // 2. Get division keys
        var division = await _divisionRepo.GetByIdReadOnlyAsync(divId, ct)
            ?? throw new KeyNotFoundException($"Division {divId} not found.");
        var agegroupId = division.AgegroupId;
        var agegroup = await _agegroupRepo.GetByIdAsync(agegroupId, ct);
        var agSeason = agegroup?.Season ?? season;
        var agLeagueId = agegroup?.LeagueId ?? leagueId;

        var teamCount = await _pairingsRepo.GetDivisionTeamCountAsync(divId, jobId, ct);

        // 3. Get timeslot dates — try div-specific first, fall back to agegroup
        var allDates = await _timeslotRepo.GetDatesAsync(agegroupId, season, year, ct);
        var divDates = allDates.Where(d => d.DivId == divId).ToList();
        var effectiveDates = divDates.Count > 0 ? divDates : allDates.Where(d => d.DivId == null).ToList();

        // 4. Get field timeslots — try div-specific first, fall back to agegroup
        var allFields = await _timeslotRepo.GetFieldTimeslotsAsync(agegroupId, season, year, ct);
        var divFields = allFields.Where(f => f.DivId == divId).ToList();
        var effectiveFields = divFields.Count > 0 ? divFields : allFields.Where(f => f.DivId == null).ToList();

        // 5. Get round-robin pairings ordered by Rnd, GameNumber
        var pairings = await _pairingsRepo.GetPairingsAsync(agLeagueId, agSeason, teamCount, ct);
        var rrPairings = pairings
            .Where(p => p.T1Type == "T" && p.T2Type == "T")
            .OrderBy(p => p.Rnd)
            .ThenBy(p => p.GameNumber)
            .ToList();

        if (rrPairings.Count == 0 || effectiveDates.Count == 0 || effectiveFields.Count == 0)
        {
            return new AutoScheduleResponse
            {
                TotalPairings = rrPairings.Count,
                ScheduledCount = 0,
                FailedCount = rrPairings.Count
            };
        }

        // 6. Pre-load existing occupied slots (other divisions' games on these fields)
        var fieldIds = effectiveFields.Select(f => f.FieldId).Distinct().ToList();
        var occupiedSlots = await _scheduleRepo.GetOccupiedSlotsAsync(jobId, fieldIds, ct);

        // Pre-load field names for efficiency
        var fieldNameMap = new Dictionary<Guid, string>();
        foreach (var fId in fieldIds)
        {
            var f = await _fieldRepo.GetFieldByIdAsync(fId, ct);
            if (f != null) fieldNameMap[fId] = f.FName ?? "";
        }

        // Legacy logic: if multiple dates exist, filter by Rnd; if only 1 date, use it for all
        var singleDate = effectiveDates.Count == 1;
        int scheduledCount = 0;
        int failedCount = 0;

        foreach (var pairing in rrPairings)
        {
            // Get dates applicable to this round
            var roundDates = singleDate
                ? effectiveDates
                : effectiveDates.Where(d => d.Rnd == pairing.Rnd).ToList();

            // Fall back to all dates if no round-specific dates
            if (roundDates.Count == 0)
                roundDates = effectiveDates.ToList();

            var slot = FindNextAvailableTimeslot(roundDates, effectiveFields, occupiedSlots);

            if (slot == null)
            {
                failedCount++;
                continue;
            }

            occupiedSlots.Add((slot.Value.fieldId, slot.Value.gDate));

            var fName = fieldNameMap.GetValueOrDefault(slot.Value.fieldId, "");

            var game = new Schedule
            {
                JobId = jobId,
                LeagueId = agLeagueId,
                Season = agSeason,
                Year = year,
                AgegroupId = agegroupId,
                AgegroupName = agegroup?.AgegroupName ?? "",
                DivId = divId,
                DivName = division.DivName ?? "",
                Div2Id = divId,
                FieldId = slot.Value.fieldId,
                FName = fName,
                GDate = slot.Value.gDate,
                GNo = pairing.GameNumber,
                GStatusCode = 1, // Scheduled
                Rnd = (byte)pairing.Rnd,
                T1No = pairing.T1,
                T1Type = pairing.T1Type,
                T2No = (byte)pairing.T2,
                T2Type = pairing.T2Type,
                T1Ann = pairing.T1Annotation,
                T1CalcType = pairing.T1CalcType,
                T1GnoRef = pairing.T1GnoRef,
                T2Ann = pairing.T2Annotation,
                T2CalcType = pairing.T2CalcType,
                T2GnoRef = pairing.T2GnoRef,
                LebUserId = userId,
                Modified = DateTime.UtcNow
            };

            _scheduleRepo.AddGame(game);
            await _scheduleRepo.SaveChangesAsync(ct);
            scheduledCount++;
        }

        // 7. Bulk resolve team names for the entire division
        await _scheduleRepo.SynchronizeScheduleTeamAssignmentsForDivisionAsync(divId, jobId, ct);

        _logger.LogInformation(
            "AutoScheduleDiv: DivId={DivId}, Total={Total}, Scheduled={Scheduled}, Failed={Failed}",
            divId, rrPairings.Count, scheduledCount, failedCount);

        return new AutoScheduleResponse
        {
            TotalPairings = rrPairings.Count,
            ScheduledCount = scheduledCount,
            FailedCount = failedCount
        };
    }

    /// <summary>
    /// Find the next available timeslot by walking dates, fields, and game intervals.
    /// Matches the legacy GetNextAvailableTimeslot algorithm.
    /// </summary>
    private static (Guid fieldId, DateTime gDate)? FindNextAvailableTimeslot(
        List<TimeslotsLeagueSeasonDates> dates,
        List<TimeslotsLeagueSeasonFields> fields,
        HashSet<(Guid fieldId, DateTime gDate)> occupiedSlots)
    {
        foreach (var date in dates.OrderBy(d => d.GDate))
        {
            var dow = date.GDate.DayOfWeek.ToString();
            var dowFields = fields
                .Where(f => f.Dow.Equals(dow, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.FieldId) // stable ordering
                .ToList();

            foreach (var ft in dowFields)
            {
                if (!TimeSpan.TryParse(ft.StartTime, out var startTime))
                    continue;

                var baseDate = date.GDate.Date;

                for (var g = 0; g < ft.MaxGamesPerField; g++)
                {
                    var gameTime = baseDate + startTime + TimeSpan.FromMinutes(g * ft.GamestartInterval);
                    if (!occupiedSlots.Contains((ft.FieldId, gameTime)))
                    {
                        return (ft.FieldId, gameTime);
                    }
                }
            }
        }

        return null;
    }
}
