using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Fields and FieldsLeagueSeason using Entity Framework Core.
/// </summary>
public class FieldRepository : IFieldRepository
{
    private readonly SqlDbContext _context;

    public FieldRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<Fields>> GetAvailableFieldsAsync(
        Guid leagueId,
        string season,
        List<Guid> directorJobIds,
        bool isSuperUser,
        CancellationToken ct = default)
    {
        // IDs of fields already assigned to this league-season
        var assignedFieldIds = await _context.FieldsLeagueSeason
            .Where(fls => fls.LeagueId == leagueId && fls.Season == season)
            .Select(fls => fls.FieldId)
            .ToListAsync(ct);

        var query = _context.Fields
            .AsNoTracking()
            .Where(f => !assignedFieldIds.Contains(f.FieldId))
            .Where(f => f.FName == null || !f.FName.StartsWith("*")); // Exclude system fields

        if (!isSuperUser)
        {
            // Director: only fields historically used by any of their jobs
            var directorFieldIds = await GetDirectorFieldIdsAsync(directorJobIds, ct);
            query = query.Where(f => directorFieldIds.Contains(f.FieldId));
        }

        return await query
            .OrderBy(f => f.FName)
            .ToListAsync(ct);
    }

    public async Task<List<FieldsLeagueSeason>> GetLeagueSeasonFieldsAsync(
        Guid leagueId,
        string season,
        CancellationToken ct = default)
    {
        return await _context.FieldsLeagueSeason
            .AsNoTracking()
            .Include(fls => fls.Field)
            .Where(fls => fls.LeagueId == leagueId && fls.Season == season)
            .Where(fls => fls.Field.FName == null || !fls.Field.FName.StartsWith("*")) // Exclude system fields
            .OrderBy(fls => fls.Field.FName)
            .ToListAsync(ct);
    }

    public async Task<Fields?> GetFieldByIdAsync(Guid fieldId, CancellationToken ct = default)
    {
        return await _context.Fields
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.FieldId == fieldId, ct);
    }

    public async Task<Fields?> GetFieldTrackedAsync(Guid fieldId, CancellationToken ct = default)
    {
        return await _context.Fields
            .FirstOrDefaultAsync(f => f.FieldId == fieldId, ct);
    }

    public void Add(Fields field)
    {
        _context.Fields.Add(field);
    }

    public void Remove(Fields field)
    {
        _context.Fields.Remove(field);
    }

    public async Task<bool> IsFieldReferencedAsync(Guid fieldId, CancellationToken ct = default)
    {
        var inLeagueSeason = await _context.FieldsLeagueSeason
            .AnyAsync(fls => fls.FieldId == fieldId, ct);
        if (inLeagueSeason) return true;

        var inSchedule = await _context.Schedule
            .AnyAsync(s => s.FieldId == fieldId, ct);
        if (inSchedule) return true;

        var inTimeslots = await _context.TimeslotsLeagueSeasonFields
            .AnyAsync(t => t.FieldId == fieldId, ct);

        return inTimeslots;
    }

    public async Task AssignFieldsToLeagueSeasonAsync(
        Guid leagueId,
        string season,
        List<Guid> fieldIds,
        string userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var records = fieldIds.Select(fid => new FieldsLeagueSeason
        {
            FlsId = Guid.NewGuid(),
            FieldId = fid,
            LeagueId = leagueId,
            Season = season,
            BActive = true,
            LebUserId = userId,
            Modified = now
        });

        _context.FieldsLeagueSeason.AddRange(records);
        await _context.SaveChangesAsync(ct);
    }

    public async Task RemoveFieldsFromLeagueSeasonAsync(
        Guid leagueId,
        string season,
        List<Guid> fieldIds,
        CancellationToken ct = default)
    {
        var records = await _context.FieldsLeagueSeason
            .Where(fls => fls.LeagueId == leagueId
                          && fls.Season == season
                          && fieldIds.Contains(fls.FieldId))
            .ToListAsync(ct);

        _context.FieldsLeagueSeason.RemoveRange(records);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Gets field IDs historically associated with the Director's jobs.
    /// Joins through FieldsLeagueSeason → JobLeagues and Schedule to find fields used by any of the director's jobs.
    /// </summary>
    private async Task<List<Guid>> GetDirectorFieldIdsAsync(
        List<Guid> directorJobIds,
        CancellationToken ct)
    {
        // Fields used in any league-season that belongs to one of the director's jobs
        var fromLeagueSeason = _context.FieldsLeagueSeason
            .Where(fls => _context.JobLeagues
                .Where(jl => directorJobIds.Contains(jl.JobId))
                .Select(jl => jl.LeagueId)
                .Contains(fls.LeagueId))
            .Select(fls => fls.FieldId);

        // Fields that appear in schedules for any of the director's jobs
        var fromSchedule = _context.Schedule
            .Where(s => directorJobIds.Contains(s.JobId) && s.FieldId != null)
            .Select(s => s.FieldId!.Value);

        return await fromLeagueSeason
            .Union(fromSchedule)
            .Distinct()
            .ToListAsync(ct);
    }
}
