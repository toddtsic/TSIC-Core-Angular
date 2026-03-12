using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for TimeslotsLeagueSeasonDates and TimeslotsLeagueSeasonFields.
/// </summary>
public class TimeslotRepository : ITimeslotRepository
{
    private readonly SqlDbContext _context;

    public TimeslotRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Dates ──

    public async Task<List<TimeslotDateDto>> GetDatesAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default)
    {
        return await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .Where(d => d.AgegroupId == agegroupId && d.Season == season && d.Year == year)
            .OrderBy(d => d.GDate)
            .ThenBy(d => d.Rnd)
            .Select(d => new TimeslotDateDto
            {
                Ai = d.Ai,
                AgegroupId = d.AgegroupId,
                GDate = d.GDate,
                Rnd = d.Rnd,
                DivId = d.DivId,
                DivName = d.Div != null ? d.Div.DivName : null
            })
            .ToListAsync(ct);
    }

    public async Task<TimeslotsLeagueSeasonDates?> GetDateByIdAsync(int ai, CancellationToken ct = default)
    {
        return await _context.TimeslotsLeagueSeasonDates.FindAsync([ai], ct);
    }

    public void AddDate(TimeslotsLeagueSeasonDates date)
    {
        _context.TimeslotsLeagueSeasonDates.Add(date);
    }

    public void RemoveDate(TimeslotsLeagueSeasonDates date)
    {
        _context.TimeslotsLeagueSeasonDates.Remove(date);
    }

    public async Task DeleteAllDatesAsync(Guid agegroupId, string season, string year, CancellationToken ct = default)
    {
        var toDelete = await _context.TimeslotsLeagueSeasonDates
            .Where(d => d.AgegroupId == agegroupId && d.Season == season && d.Year == year)
            .ToListAsync(ct);
        _context.TimeslotsLeagueSeasonDates.RemoveRange(toDelete);
    }

    public async Task DeleteDatesByDateAsync(Guid agegroupId, DateTime gDate, string season, string year, CancellationToken ct = default)
    {
        var toDelete = await _context.TimeslotsLeagueSeasonDates
            .Where(d => d.AgegroupId == agegroupId && d.GDate.Date == gDate.Date && d.Season == season && d.Year == year)
            .ToListAsync(ct);
        _context.TimeslotsLeagueSeasonDates.RemoveRange(toDelete);
    }

    // ── Field timeslots ──

    public async Task<List<TimeslotFieldDto>> GetFieldTimeslotsAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default)
    {
        return await _context.TimeslotsLeagueSeasonFields
            .AsNoTracking()
            .Where(f => f.AgegroupId == agegroupId && f.Season == season && f.Year == year)
            .OrderBy(f => f.Field.FName)
            .ThenBy(f => f.Dow)
            .ThenBy(f => f.Div!.DivName)
            .Select(f => new TimeslotFieldDto
            {
                Ai = f.Ai,
                AgegroupId = f.AgegroupId,
                FieldId = f.FieldId,
                FieldName = f.Field.FName ?? "",
                StartTime = f.StartTime ?? "",
                GamestartInterval = f.GamestartInterval,
                MaxGamesPerField = f.MaxGamesPerField,
                Dow = f.Dow,
                DivId = f.DivId,
                DivName = f.Div != null ? f.Div.DivName : null
            })
            .ToListAsync(ct);
    }

    public async Task<TimeslotsLeagueSeasonFields?> GetFieldTimeslotByIdAsync(int ai, CancellationToken ct = default)
    {
        return await _context.TimeslotsLeagueSeasonFields.FindAsync([ai], ct);
    }

    public void AddFieldTimeslot(TimeslotsLeagueSeasonFields timeslot)
    {
        _context.TimeslotsLeagueSeasonFields.Add(timeslot);
    }

    public async Task AddFieldTimeslotsRangeAsync(List<TimeslotsLeagueSeasonFields> timeslots, CancellationToken ct = default)
    {
        await _context.TimeslotsLeagueSeasonFields.AddRangeAsync(timeslots, ct);
    }

    public void RemoveFieldTimeslot(TimeslotsLeagueSeasonFields timeslot)
    {
        _context.TimeslotsLeagueSeasonFields.Remove(timeslot);
    }

    public async Task DeleteAllFieldTimeslotsAsync(Guid agegroupId, string season, string year, CancellationToken ct = default)
    {
        var toDelete = await _context.TimeslotsLeagueSeasonFields
            .Where(f => f.AgegroupId == agegroupId && f.Season == season && f.Year == year)
            .ToListAsync(ct);
        _context.TimeslotsLeagueSeasonFields.RemoveRange(toDelete);
    }

    // ── Dashboard aggregates ──

    public async Task<HashSet<Guid>> GetAgegroupIdsWithDatesAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default)
    {
        var ids = await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .Where(d => d.Season == season && d.Year == year
                && d.Agegroup.LeagueId == leagueId)
            .Select(d => d.AgegroupId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    public async Task<HashSet<Guid>> GetAgegroupIdsWithFieldTimeslotsAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default)
    {
        var ids = await _context.TimeslotsLeagueSeasonFields
            .AsNoTracking()
            .Where(f => f.Season == season && f.Year == year
                && f.Agegroup.LeagueId == leagueId)
            .Select(f => f.AgegroupId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    public async Task<Dictionary<Guid, AgegroupReadinessData>> GetReadinessDataAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default)
    {
        // Actual dates per agegroup — scoped to this league's agegroups
        // Include Rnd to count round-slots per date
        var dateRows = await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .Where(d => d.Season == season && d.Year == year
                && d.Agegroup.LeagueId == leagueId)
            .Select(d => new { d.AgegroupId, d.GDate, d.Rnd })
            .ToListAsync(ct);

        var datesByAg = dateRows.GroupBy(d => d.AgegroupId);

        // Field timeslot parameters — scoped to this league's agegroups
        var fieldRows = await _context.TimeslotsLeagueSeasonFields
            .AsNoTracking()
            .Where(f => f.Season == season && f.Year == year
                && f.Agegroup.LeagueId == leagueId)
            .Select(f => new
            {
                f.AgegroupId,
                f.FieldId,
                f.Dow,
                f.StartTime,
                f.GamestartInterval,
                f.MaxGamesPerField
            })
            .ToListAsync(ct);

        // Group by agegroup in-memory
        var fieldsByAg = fieldRows.GroupBy(f => f.AgegroupId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<Guid, AgegroupReadinessData>();

        // Seed from dates
        foreach (var g in datesByAg)
        {
            var dates = g.Select(d => d.GDate).Distinct().OrderBy(d => d).ToList();
            var roundsPerDate = g.GroupBy(d => d.GDate.Date)
                .ToDictionary(dg => dg.Key, dg => dg.Count());
            result[g.Key] = new AgegroupReadinessData
            {
                DateCount = dates.Count,
                FieldCount = 0,
                DaysOfWeek = [],
                DistinctGsi = [],
                DistinctStartTimes = [],
                DistinctMaxGames = [],
                TotalMaxGamesSum = 0,
                Dates = dates,
                RoundsPerDate = roundsPerDate,
                PerDowFields = []
            };
        }

        // Merge field data
        foreach (var (agId, fields) in fieldsByAg)
        {
            var distinctFieldCount = fields.Select(f => f.FieldId).Distinct().Count();
            var distinctDows = fields.Select(f => f.Dow).Distinct().OrderBy(d => d).ToList();
            var distinctGsi = fields.Select(f => f.GamestartInterval).Distinct().ToList();
            var distinctStartTimes = fields.Select(f => f.StartTime ?? "").Where(s => s != "").Distinct().ToList();
            var distinctMaxGames = fields.Select(f => f.MaxGamesPerField).Distinct().ToList();
            var totalMaxGamesSum = fields.Sum(f => f.MaxGamesPerField);

            // Per-DOW field data for game day line construction
            var perDowFields = fields
                .GroupBy(f => f.Dow)
                .Select(dg => new DowFieldData
                {
                    Dow = dg.Key,
                    FieldCount = dg.Select(f => f.FieldId).Distinct().Count(),
                    StartTimes = dg.Select(f => f.StartTime ?? "").Where(s => s != "").Distinct().ToList(),
                    GsiValues = dg.Select(f => f.GamestartInterval).Distinct().ToList(),
                    MaxGamesValues = dg.Select(f => f.MaxGamesPerField).Distinct().ToList(),
                    TotalMaxGamesSum = dg.Sum(f => f.MaxGamesPerField)
                })
                .ToList();

            if (result.TryGetValue(agId, out var existing))
            {
                result[agId] = existing with
                {
                    FieldCount = distinctFieldCount,
                    DaysOfWeek = distinctDows,
                    DistinctGsi = distinctGsi,
                    DistinctStartTimes = distinctStartTimes,
                    DistinctMaxGames = distinctMaxGames,
                    TotalMaxGamesSum = totalMaxGamesSum,
                    PerDowFields = perDowFields
                };
            }
            else
            {
                result[agId] = new AgegroupReadinessData
                {
                    DateCount = 0,
                    FieldCount = distinctFieldCount,
                    DaysOfWeek = distinctDows,
                    DistinctGsi = distinctGsi,
                    DistinctStartTimes = distinctStartTimes,
                    DistinctMaxGames = distinctMaxGames,
                    TotalMaxGamesSum = totalMaxGamesSum,
                    Dates = [],
                    RoundsPerDate = new Dictionary<DateTime, int>(),
                    PerDowFields = perDowFields
                };
            }
        }

        return result;
    }

    public async Task<Dictionary<string, int>> GetRoundCountsByAgegroupNameAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default)
    {
        var rows = await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .Where(d => d.Season == season && d.Year == year
                && d.Agegroup.LeagueId == leagueId)
            .Select(d => new { d.Agegroup.AgegroupName, d.Rnd })
            .ToListAsync(ct);

        return rows
            .Where(r => r.AgegroupName != null)
            .GroupBy(r => r.AgegroupName!)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Rnd));
    }

    // ── Bulk field config update ──

    public async Task<List<TimeslotsLeagueSeasonFields>> GetAllFieldTimeslotsForUpdateAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default)
    {
        // TRACKED (no AsNoTracking) — caller will modify these entities in-place
        return await _context.TimeslotsLeagueSeasonFields
            .Where(f => f.Agegroup.LeagueId == leagueId && f.Season == season && f.Year == year)
            .ToListAsync(ct);
    }

    // ── Cloning support queries ──

    public async Task<List<TimeslotsLeagueSeasonFields>> GetFieldTimeslotsByFilterAsync(
        Guid agegroupId, string season, string year,
        Guid? fieldId = null, Guid? divId = null, string? dow = null,
        CancellationToken ct = default)
    {
        var query = _context.TimeslotsLeagueSeasonFields
            .AsNoTracking()
            .Where(f => f.AgegroupId == agegroupId && f.Season == season && f.Year == year);

        if (fieldId.HasValue)
            query = query.Where(f => f.FieldId == fieldId.Value);
        if (divId.HasValue)
            query = query.Where(f => f.DivId == divId.Value);
        if (dow != null)
            query = query.Where(f => f.Dow == dow);

        return await query.ToListAsync(ct);
    }

    public async Task<List<TimeslotsLeagueSeasonDates>> GetDatesByAgegroupAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default)
    {
        return await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .Where(d => d.AgegroupId == agegroupId && d.Season == season && d.Year == year)
            .ToListAsync(ct);
    }

    // ── Division/field lists for cartesian product ──

    public async Task<List<Guid>> GetActiveDivisionIdsAsync(Guid agegroupId, Guid jobId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                && t.AgegroupId == agegroupId
                && t.Active == true
                && t.Agegroup.AgegroupName != null
                && !t.Agegroup.AgegroupName.Contains("Dropped")
                && !t.Agegroup.AgegroupName.Contains("Waitlist")
                && t.Div != null
                && t.Div.DivName != null
                && !t.Div.DivName.Contains("Unassigned"))
            .Select(t => t.DivId!.Value)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<List<Guid>> GetAssignedFieldIdsAsync(Guid leagueId, string season, CancellationToken ct = default)
    {
        return await _context.FieldsLeagueSeason
            .AsNoTracking()
            .Where(fls => fls.LeagueId == leagueId && fls.Season == season)
            .Join(_context.Fields,
                fls => fls.FieldId,
                f => f.FieldId,
                (fls, f) => new { f.FieldId, f.FName })
            .Where(x => x.FName == null || !x.FName.StartsWith('*'))
            .Select(x => x.FieldId)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, List<Guid>>> GetFieldIdsPerAgegroupAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default)
    {
        var rows = await _context.TimeslotsLeagueSeasonFields
            .AsNoTracking()
            .Where(f => f.Season == season && f.Year == year
                && f.Agegroup.LeagueId == leagueId)
            .Select(f => new { f.AgegroupId, f.FieldId })
            .Distinct()
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.AgegroupId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.FieldId).Distinct().ToList());
    }

    public async Task<List<EventFieldSummaryDto>> GetEventFieldSummariesAsync(
        Guid leagueId, string season, CancellationToken ct = default)
    {
        return await _context.FieldsLeagueSeason
            .AsNoTracking()
            .Where(fls => fls.LeagueId == leagueId && fls.Season == season)
            .Join(_context.Fields,
                fls => fls.FieldId,
                f => f.FieldId,
                (fls, f) => new { f.FieldId, f.FName })
            .Where(x => x.FName == null || !x.FName.StartsWith('*'))
            .OrderBy(x => x.FName)
            .Select(x => new EventFieldSummaryDto
            {
                FieldId = x.FieldId,
                FieldName = x.FName ?? ""
            })
            .ToListAsync(ct);
    }

    public async Task DeleteFieldTimeslotsByFieldAsync(
        Guid agegroupId, Guid fieldId, string season, string year, CancellationToken ct = default)
    {
        var toDelete = await _context.TimeslotsLeagueSeasonFields
            .Where(f => f.AgegroupId == agegroupId
                && f.FieldId == fieldId
                && f.Season == season
                && f.Year == year)
            .ToListAsync(ct);

        _context.TimeslotsLeagueSeasonFields.RemoveRange(toDelete);
    }

    // ── Prior-year defaults ──

    public async Task<FieldScheduleDefaults?> GetDominantFieldDefaultsAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default)
    {
        // Get the most common (StartTime, GSI, MaxGamesPerField) combination
        var dominant = await _context.TimeslotsLeagueSeasonFields
            .AsNoTracking()
            .Where(f => f.Agegroup.LeagueId == leagueId && f.Season == season && f.Year == year)
            .GroupBy(f => new { f.StartTime, f.GamestartInterval, f.MaxGamesPerField })
            .Select(g => new
            {
                g.Key.StartTime,
                g.Key.GamestartInterval,
                g.Key.MaxGamesPerField,
                Count = g.Count()
            })
            .OrderByDescending(g => g.Count)
            .FirstOrDefaultAsync(ct);

        if (dominant == null) return null;

        return new FieldScheduleDefaults
        {
            StartTime = dominant.StartTime,
            GamestartInterval = dominant.GamestartInterval,
            MaxGamesPerField = dominant.MaxGamesPerField
        };
    }

    // ── Capacity ──

    public async Task<int> GetPairingCountAsync(Guid leagueId, string season, int teamCount, CancellationToken ct = default)
    {
        return await _context.PairingsLeagueSeason
            .AsNoTracking()
            .CountAsync(p => p.LeagueId == leagueId && p.Season == season && p.TCnt == teamCount, ct);
    }

    // ── Cascade date support ──

    public async Task<List<TimeslotsLeagueSeasonDates>> GetDatesByDateTrackedAsync(
        Guid leagueId, DateTime gDate, string season, string year, CancellationToken ct = default)
    {
        return await _context.TimeslotsLeagueSeasonDates
            .Where(d => d.GDate.Date == gDate.Date && d.Season == season && d.Year == year
                && _context.Agegroups
                    .Where(a => a.LeagueId == leagueId)
                    .Select(a => a.AgegroupId)
                    .Contains(d.AgegroupId))
            .ToListAsync(ct);
    }

    public async Task<bool> DateExistsAsync(
        Guid leagueId, DateTime gDate, string season, string year, CancellationToken ct = default)
    {
        return await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .AnyAsync(d => d.GDate.Date == gDate.Date && d.Season == season && d.Year == year
                && _context.Agegroups
                    .Where(a => a.LeagueId == leagueId)
                    .Select(a => a.AgegroupId)
                    .Contains(d.AgegroupId), ct);
    }

    // ── Persist ──

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
