using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Service for the Manage Timeslots scheduling tool.
/// Handles date/field CRUD, cloning, cartesian product creation, and capacity preview.
/// </summary>
public sealed class TimeslotService : ITimeslotService
{
    private readonly ITimeslotRepository _tsRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly ILogger<TimeslotService> _logger;

    /// <summary>Day-of-week cycling for clone-field-dow: Mon→Tue→…→Sun→Mon.</summary>
    private static readonly string[] DowCycle =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public TimeslotService(
        ITimeslotRepository tsRepo,
        ISchedulingContextResolver contextResolver,
        ILogger<TimeslotService> logger)
    {
        _tsRepo = tsRepo;
        _contextResolver = contextResolver;
        _logger = logger;
    }

    // ── Configuration ──

    public async Task<TimeslotConfigurationResponse> GetConfigurationAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var dates = await _tsRepo.GetDatesAsync(agegroupId, season, year, ct);
        var fields = await _tsRepo.GetFieldTimeslotsAsync(agegroupId, season, year, ct);

        return new TimeslotConfigurationResponse
        {
            Dates = dates,
            Fields = fields
        };
    }

    public async Task<List<CapacityPreviewDto>> GetCapacityPreviewAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        var fields = await _tsRepo.GetFieldTimeslotsAsync(agegroupId, season, year, ct);

        // Get team count for this agegroup (approximate from division with most teams)
        var divIds = await _tsRepo.GetActiveDivisionIdsAsync(agegroupId, jobId, ct);
        var maxTeamCount = 0;
        foreach (var divId in divIds)
        {
            var tc = await _tsRepo.GetPairingCountAsync(leagueId, season, maxTeamCount, ct);
            if (tc > maxTeamCount) maxTeamCount = tc;
        }

        // Group field timeslots by DOW
        var byDow = fields.GroupBy(f => f.Dow);
        var result = new List<CapacityPreviewDto>();

        foreach (var group in byDow)
        {
            var totalSlots = group.Sum(f => f.MaxGamesPerField);
            var fieldCount = group.Select(f => f.FieldId).Distinct().Count();
            var gamesNeeded = maxTeamCount > 0 ? (int)Math.Ceiling(maxTeamCount / 2.0) : 0;

            result.Add(new CapacityPreviewDto
            {
                Dow = group.Key,
                FieldCount = fieldCount,
                TotalGameSlots = totalSlots,
                GamesNeeded = gamesNeeded,
                IsSufficient = totalSlots >= gamesNeeded
            });
        }

        return result;
    }

    // ── Dates CRUD ──

    public async Task<TimeslotDateDto> AddDateAsync(
        Guid jobId, string userId, AddTimeslotDateRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var date = new TimeslotsLeagueSeasonDates
        {
            AgegroupId = request.AgegroupId,
            GDate = request.GDate,
            Rnd = request.Rnd,
            DivId = request.DivId,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _tsRepo.AddDate(date);
        await _tsRepo.SaveChangesAsync(ct);
        return MapDateDto(date);
    }

    public async Task EditDateAsync(
        string userId, EditTimeslotDateRequest request, CancellationToken ct = default)
    {
        var date = await _tsRepo.GetDateByIdAsync(request.Ai, ct)
            ?? throw new KeyNotFoundException($"Timeslot date {request.Ai} not found.");

        date.GDate = request.GDate;
        date.Rnd = request.Rnd;
        date.LebUserId = userId;
        date.Modified = DateTime.UtcNow;

        await _tsRepo.SaveChangesAsync(ct);
    }

    public async Task DeleteDateAsync(int ai, CancellationToken ct = default)
    {
        var date = await _tsRepo.GetDateByIdAsync(ai, ct)
            ?? throw new KeyNotFoundException($"Timeslot date {ai} not found.");
        _tsRepo.RemoveDate(date);
        await _tsRepo.SaveChangesAsync(ct);
    }

    public async Task DeleteAllDatesAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        await _tsRepo.DeleteAllDatesAsync(agegroupId, season, year, ct);
        await _tsRepo.SaveChangesAsync(ct);
    }

    // ── Date cloning ──

    public async Task<TimeslotDateDto> CloneDateRecordAsync(
        string userId, CloneDateRecordRequest request, CancellationToken ct = default)
    {
        var source = await _tsRepo.GetDateByIdAsync(request.Ai, ct)
            ?? throw new KeyNotFoundException($"Timeslot date {request.Ai} not found.");

        var clone = new TimeslotsLeagueSeasonDates
        {
            AgegroupId = source.AgegroupId,
            DivId = source.DivId,
            Season = source.Season,
            Year = source.Year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        switch (request.CloneType.ToLowerInvariant())
        {
            case "day":
                clone.GDate = source.GDate.AddDays(1);
                clone.Rnd = source.Rnd + 1;
                break;
            case "week":
                clone.GDate = source.GDate.AddDays(7);
                clone.Rnd = source.Rnd + 1;
                break;
            case "round":
                clone.GDate = source.GDate;
                clone.Rnd = source.Rnd + 1;
                break;
            default:
                throw new ArgumentException($"Invalid clone type: {request.CloneType}");
        }

        _tsRepo.AddDate(clone);
        await _tsRepo.SaveChangesAsync(ct);
        return MapDateDto(clone);
    }

    // ── Field timeslots CRUD ──

    public async Task<List<TimeslotFieldDto>> AddFieldTimeslotAsync(
        Guid jobId, string userId, AddTimeslotFieldRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Determine field IDs (single or all assigned)
        var fieldIds = request.FieldId.HasValue
            ? [request.FieldId.Value]
            : await _tsRepo.GetAssignedFieldIdsAsync(leagueId, season, ct);

        // Determine division IDs (single or all active)
        var divIds = request.DivId.HasValue
            ? [request.DivId.Value]
            : await _tsRepo.GetActiveDivisionIdsAsync(request.AgegroupId, jobId, ct);

        // Cartesian product: Fields × Divisions
        var newTimeslots = new List<TimeslotsLeagueSeasonFields>();
        foreach (var fId in fieldIds)
        {
            foreach (var dId in divIds)
            {
                newTimeslots.Add(new TimeslotsLeagueSeasonFields
                {
                    AgegroupId = request.AgegroupId,
                    FieldId = fId,
                    DivId = dId,
                    StartTime = request.StartTime,
                    GamestartInterval = request.GamestartInterval,
                    MaxGamesPerField = request.MaxGamesPerField,
                    Dow = request.Dow,
                    Season = season,
                    Year = year,
                    LebUserId = userId,
                    Modified = DateTime.UtcNow
                });
            }
        }

        if (newTimeslots.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(newTimeslots, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Added {Count} field timeslots for agegroup {AgId}", newTimeslots.Count, request.AgegroupId);

        // Reload as projected DTOs
        return await _tsRepo.GetFieldTimeslotsAsync(request.AgegroupId, season, year, ct);
    }

    public async Task EditFieldTimeslotAsync(
        string userId, EditTimeslotFieldRequest request, CancellationToken ct = default)
    {
        var ts = await _tsRepo.GetFieldTimeslotByIdAsync(request.Ai, ct)
            ?? throw new KeyNotFoundException($"Field timeslot {request.Ai} not found.");

        ts.StartTime = request.StartTime;
        ts.GamestartInterval = request.GamestartInterval;
        ts.MaxGamesPerField = request.MaxGamesPerField;
        ts.Dow = request.Dow;
        if (request.FieldId.HasValue) ts.FieldId = request.FieldId.Value;
        if (request.DivId.HasValue) ts.DivId = request.DivId;
        ts.LebUserId = userId;
        ts.Modified = DateTime.UtcNow;

        await _tsRepo.SaveChangesAsync(ct);
    }

    public async Task DeleteFieldTimeslotAsync(int ai, CancellationToken ct = default)
    {
        var ts = await _tsRepo.GetFieldTimeslotByIdAsync(ai, ct)
            ?? throw new KeyNotFoundException($"Field timeslot {ai} not found.");
        _tsRepo.RemoveFieldTimeslot(ts);
        await _tsRepo.SaveChangesAsync(ct);
    }

    public async Task DeleteAllFieldTimeslotsAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        await _tsRepo.DeleteAllFieldTimeslotsAsync(agegroupId, season, year, ct);
        await _tsRepo.SaveChangesAsync(ct);
    }

    // ── Cloning: Dates agegroup→agegroup ──

    public async Task CloneDatesAsync(
        Guid jobId, string userId, CloneDatesRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Delete existing target dates first
        await _tsRepo.DeleteAllDatesAsync(request.TargetAgegroupId, season, year, ct);

        var sourceDates = await _tsRepo.GetDatesByAgegroupAsync(request.SourceAgegroupId, season, year, ct);

        foreach (var src in sourceDates)
        {
            _tsRepo.AddDate(new TimeslotsLeagueSeasonDates
            {
                AgegroupId = request.TargetAgegroupId,
                GDate = src.GDate,
                Rnd = src.Rnd,
                DivId = src.DivId,
                Season = season,
                Year = year,
                LebUserId = userId,
                Modified = DateTime.UtcNow
            });
        }

        await _tsRepo.SaveChangesAsync(ct);
        _logger.LogInformation("Cloned {Count} dates from AG {Src} to AG {Tgt}",
            sourceDates.Count, request.SourceAgegroupId, request.TargetAgegroupId);
    }

    // ── Cloning: Fields agegroup→agegroup ──

    public async Task CloneFieldsAsync(
        Guid jobId, string userId, CloneFieldsRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var sourceFields = await _tsRepo.GetFieldTimeslotsByFilterAsync(
            request.SourceAgegroupId, season, year, ct: ct);

        var clones = sourceFields.Select(src => new TimeslotsLeagueSeasonFields
        {
            AgegroupId = request.TargetAgegroupId,
            FieldId = src.FieldId,
            DivId = src.DivId,
            StartTime = src.StartTime,
            GamestartInterval = src.GamestartInterval,
            MaxGamesPerField = src.MaxGamesPerField,
            Dow = src.Dow,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        }).ToList();

        if (clones.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(clones, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Cloned {Count} field timeslots from AG {Src} to AG {Tgt}",
            clones.Count, request.SourceAgegroupId, request.TargetAgegroupId);
    }

    // ── Cloning: by field within agegroup ──

    public async Task CloneByFieldAsync(
        Guid jobId, string userId, CloneByFieldRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var sourceTimeslots = await _tsRepo.GetFieldTimeslotsByFilterAsync(
            request.AgegroupId, season, year, fieldId: request.SourceFieldId, ct: ct);

        var clones = sourceTimeslots.Select(src => new TimeslotsLeagueSeasonFields
        {
            AgegroupId = request.AgegroupId,
            FieldId = request.TargetFieldId,
            DivId = src.DivId,
            StartTime = src.StartTime,
            GamestartInterval = src.GamestartInterval,
            MaxGamesPerField = src.MaxGamesPerField,
            Dow = src.Dow,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        }).ToList();

        if (clones.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(clones, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }
    }

    // ── Cloning: by division within agegroup ──

    public async Task CloneByDivisionAsync(
        Guid jobId, string userId, CloneByDivisionRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var sourceTimeslots = await _tsRepo.GetFieldTimeslotsByFilterAsync(
            request.AgegroupId, season, year, divId: request.SourceDivId, ct: ct);

        var clones = sourceTimeslots.Select(src => new TimeslotsLeagueSeasonFields
        {
            AgegroupId = request.AgegroupId,
            FieldId = src.FieldId,
            DivId = request.TargetDivId,
            StartTime = src.StartTime,
            GamestartInterval = src.GamestartInterval,
            MaxGamesPerField = src.MaxGamesPerField,
            Dow = src.Dow,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        }).ToList();

        if (clones.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(clones, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }
    }

    // ── Cloning: by day-of-week within agegroup ──

    public async Task CloneByDowAsync(
        Guid jobId, string userId, CloneByDowRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var sourceTimeslots = await _tsRepo.GetFieldTimeslotsByFilterAsync(
            request.AgegroupId, season, year, dow: request.SourceDow, ct: ct);

        var clones = sourceTimeslots.Select(src => new TimeslotsLeagueSeasonFields
        {
            AgegroupId = request.AgegroupId,
            FieldId = src.FieldId,
            DivId = src.DivId,
            StartTime = request.NewStartTime ?? src.StartTime,
            GamestartInterval = src.GamestartInterval,
            MaxGamesPerField = src.MaxGamesPerField,
            Dow = request.TargetDow,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        }).ToList();

        if (clones.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(clones, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }
    }

    // ── Cloning: single field record to next DOW ──

    public async Task<TimeslotFieldDto> CloneFieldDowAsync(
        string userId, CloneFieldDowRequest request, CancellationToken ct = default)
    {
        var source = await _tsRepo.GetFieldTimeslotByIdAsync(request.Ai, ct)
            ?? throw new KeyNotFoundException($"Field timeslot {request.Ai} not found.");

        var nextDow = GetNextDow(source.Dow);

        var clone = new TimeslotsLeagueSeasonFields
        {
            AgegroupId = source.AgegroupId,
            FieldId = source.FieldId,
            DivId = source.DivId,
            StartTime = source.StartTime,
            GamestartInterval = source.GamestartInterval,
            MaxGamesPerField = source.MaxGamesPerField,
            Dow = nextDow,
            Season = source.Season,
            Year = source.Year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _tsRepo.AddFieldTimeslot(clone);
        await _tsRepo.SaveChangesAsync(ct);

        // Reload with navigation properties
        var reloaded = await _tsRepo.GetFieldTimeslotByIdAsync(clone.Ai, ct);
        return MapFieldDto(reloaded ?? clone);
    }

    // ── Helpers ──

    private static string GetNextDow(string dow)
    {
        var idx = Array.FindIndex(DowCycle, d => d.Equals(dow, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? DowCycle[(idx + 1) % DowCycle.Length] : dow;
    }

    private static TimeslotDateDto MapDateDto(TimeslotsLeagueSeasonDates d) => new()
    {
        Ai = d.Ai,
        AgegroupId = d.AgegroupId,
        GDate = d.GDate,
        Rnd = d.Rnd,
        DivId = d.DivId,
        DivName = d.Div?.DivName
    };

    private static TimeslotFieldDto MapFieldDto(TimeslotsLeagueSeasonFields f) => new()
    {
        Ai = f.Ai,
        AgegroupId = f.AgegroupId,
        FieldId = f.FieldId,
        FieldName = f.Field?.FName ?? "",
        StartTime = f.StartTime ?? "",
        GamestartInterval = f.GamestartInterval,
        MaxGamesPerField = f.MaxGamesPerField,
        Dow = f.Dow,
        DivId = f.DivId,
        DivName = f.Div?.DivName
    };
}
