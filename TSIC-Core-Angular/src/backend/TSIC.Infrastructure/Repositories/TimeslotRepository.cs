using Microsoft.EntityFrameworkCore;
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

    public async Task<List<TimeslotsLeagueSeasonDates>> GetDatesAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default)
    {
        return await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .Include(d => d.Div)
            .Where(d => d.AgegroupId == agegroupId && d.Season == season && d.Year == year)
            .OrderBy(d => d.GDate)
            .ThenBy(d => d.Rnd)
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

    // ── Field timeslots ──

    public async Task<List<TimeslotsLeagueSeasonFields>> GetFieldTimeslotsAsync(
        Guid agegroupId, string season, string year, CancellationToken ct = default)
    {
        return await _context.TimeslotsLeagueSeasonFields
            .AsNoTracking()
            .Include(f => f.Field)
            .Include(f => f.Div)
            .Where(f => f.AgegroupId == agegroupId && f.Season == season && f.Year == year)
            .OrderBy(f => f.Field.FName)
            .ThenBy(f => f.Dow)
            .ThenBy(f => f.Div!.DivName)
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
        string season, string year, CancellationToken ct = default)
    {
        var ids = await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .Where(d => d.Season == season && d.Year == year)
            .Select(d => d.AgegroupId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    public async Task<HashSet<Guid>> GetAgegroupIdsWithFieldTimeslotsAsync(
        string season, string year, CancellationToken ct = default)
    {
        var ids = await _context.TimeslotsLeagueSeasonFields
            .AsNoTracking()
            .Where(f => f.Season == season && f.Year == year)
            .Select(f => f.AgegroupId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
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

    // ── Capacity ──

    public async Task<int> GetPairingCountAsync(Guid leagueId, string season, int teamCount, CancellationToken ct = default)
    {
        return await _context.PairingsLeagueSeason
            .AsNoTracking()
            .CountAsync(p => p.LeagueId == leagueId && p.Season == season && p.TCnt == teamCount, ct);
    }

    // ── Persist ──

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
