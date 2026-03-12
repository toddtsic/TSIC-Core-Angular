using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for the 3-level scheduling cascade tables.
/// Scopes all reads through JobLeagues → Leagues → Agegroups → Divisions.
/// </summary>
public class ScheduleCascadeRepository : IScheduleCascadeRepository
{
    private readonly SqlDbContext _context;

    public ScheduleCascadeRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Event Defaults ──

    public async Task<EventScheduleDefaults?> GetEventDefaultsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.EventScheduleDefaults
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.JobId == jobId, ct);
    }

    public async Task UpsertEventDefaultsAsync(
        EventScheduleDefaults defaults, CancellationToken ct = default)
    {
        var existing = await _context.EventScheduleDefaults
            .FirstOrDefaultAsync(e => e.JobId == defaults.JobId, ct);

        if (existing != null)
        {
            existing.GamePlacement = defaults.GamePlacement;
            existing.BetweenRoundRows = defaults.BetweenRoundRows;
            // Only update GameGuarantee if explicitly provided — avoid overwriting
            // a previously saved value with null when caller only intends to update
            // GamePlacement/BetweenRoundRows (e.g., SaveStrategyProfilesAsync).
            if (defaults.GameGuarantee.HasValue)
                existing.GameGuarantee = defaults.GameGuarantee;
            existing.Modified = DateTime.UtcNow;
            if (defaults.LebUserId != null)
                existing.LebUserId = defaults.LebUserId;
        }
        else
        {
            defaults.Modified = DateTime.UtcNow;
            _context.EventScheduleDefaults.Add(defaults);
        }
    }

    // ── Agegroup Schedule Profile ──

    public async Task<List<AgegroupScheduleProfile>> GetAgegroupProfilesAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var leagueIds = await GetLeagueIdsAsync(jobId, ct);

        return await _context.AgegroupScheduleProfile
            .AsNoTracking()
            .Where(p => _context.Agegroups
                .Where(a => leagueIds.Contains(a.LeagueId))
                .Select(a => a.AgegroupId)
                .Contains(p.AgegroupId))
            .ToListAsync(ct);
    }

    public async Task UpsertAgegroupProfileAsync(
        AgegroupScheduleProfile profile, CancellationToken ct = default)
    {
        var existing = await _context.AgegroupScheduleProfile
            .FirstOrDefaultAsync(p => p.AgegroupId == profile.AgegroupId, ct);

        if (existing != null)
        {
            existing.GamePlacement = profile.GamePlacement;
            existing.BetweenRoundRows = profile.BetweenRoundRows;
            existing.GameGuarantee = profile.GameGuarantee;
            existing.Modified = DateTime.UtcNow;
            existing.LebUserId = profile.LebUserId;
        }
        else
        {
            profile.Modified = DateTime.UtcNow;
            _context.AgegroupScheduleProfile.Add(profile);
        }
    }

    public async Task DeleteAgegroupProfileAsync(
        Guid agegroupId, CancellationToken ct = default)
    {
        var existing = await _context.AgegroupScheduleProfile
            .FirstOrDefaultAsync(p => p.AgegroupId == agegroupId, ct);

        if (existing != null)
            _context.AgegroupScheduleProfile.Remove(existing);
    }

    // ── Division Schedule Profile ──

    public async Task<List<DivisionScheduleProfile>> GetDivisionProfilesAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var leagueIds = await GetLeagueIdsAsync(jobId, ct);

        return await _context.DivisionScheduleProfile
            .AsNoTracking()
            .Where(p => _context.Divisions
                .Where(d => _context.Agegroups
                    .Where(a => leagueIds.Contains(a.LeagueId))
                    .Select(a => a.AgegroupId)
                    .Contains(d.AgegroupId))
                .Select(d => d.DivId)
                .Contains(p.DivisionId))
            .ToListAsync(ct);
    }

    public async Task UpsertDivisionProfileAsync(
        DivisionScheduleProfile profile, CancellationToken ct = default)
    {
        var existing = await _context.DivisionScheduleProfile
            .FirstOrDefaultAsync(p => p.DivisionId == profile.DivisionId, ct);

        if (existing != null)
        {
            existing.GamePlacement = profile.GamePlacement;
            existing.BetweenRoundRows = profile.BetweenRoundRows;
            existing.GameGuarantee = profile.GameGuarantee;
            existing.Modified = DateTime.UtcNow;
            existing.LebUserId = profile.LebUserId;
        }
        else
        {
            profile.Modified = DateTime.UtcNow;
            _context.DivisionScheduleProfile.Add(profile);
        }
    }

    public async Task DeleteDivisionProfileAsync(
        Guid divisionId, CancellationToken ct = default)
    {
        var existing = await _context.DivisionScheduleProfile
            .FirstOrDefaultAsync(p => p.DivisionId == divisionId, ct);

        if (existing != null)
            _context.DivisionScheduleProfile.Remove(existing);
    }

    // ── Agegroup Wave Assignment ──

    public async Task<List<AgegroupWaveAssignment>> GetAgegroupWavesAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var leagueIds = await GetLeagueIdsAsync(jobId, ct);

        return await _context.AgegroupWaveAssignment
            .AsNoTracking()
            .Where(w => _context.Agegroups
                .Where(a => leagueIds.Contains(a.LeagueId))
                .Select(a => a.AgegroupId)
                .Contains(w.AgegroupId))
            .ToListAsync(ct);
    }

    public async Task UpsertAgegroupWavesAsync(
        Guid agegroupId, List<AgegroupWaveAssignment> waves, CancellationToken ct = default)
    {
        // Delete existing for this agegroup, then insert new batch
        var existing = await _context.AgegroupWaveAssignment
            .Where(w => w.AgegroupId == agegroupId)
            .ToListAsync(ct);

        _context.AgegroupWaveAssignment.RemoveRange(existing);

        if (waves.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var w in waves)
            {
                w.AgegroupId = agegroupId;
                w.Modified = now;
            }
            _context.AgegroupWaveAssignment.AddRange(waves);
        }
    }

    public async Task DeleteAgegroupWavesAsync(
        Guid agegroupId, CancellationToken ct = default)
    {
        var existing = await _context.AgegroupWaveAssignment
            .Where(w => w.AgegroupId == agegroupId)
            .ToListAsync(ct);

        _context.AgegroupWaveAssignment.RemoveRange(existing);
    }

    // ── Division Wave Assignment ──

    public async Task<List<DivisionWaveAssignment>> GetDivisionWavesAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var leagueIds = await GetLeagueIdsAsync(jobId, ct);

        return await _context.DivisionWaveAssignment
            .AsNoTracking()
            .Where(w => _context.Divisions
                .Where(d => _context.Agegroups
                    .Where(a => leagueIds.Contains(a.LeagueId))
                    .Select(a => a.AgegroupId)
                    .Contains(d.AgegroupId))
                .Select(d => d.DivId)
                .Contains(w.DivisionId))
            .ToListAsync(ct);
    }

    public async Task UpsertDivisionWavesAsync(
        Guid divisionId, List<DivisionWaveAssignment> waves, CancellationToken ct = default)
    {
        var existing = await _context.DivisionWaveAssignment
            .Where(w => w.DivisionId == divisionId)
            .ToListAsync(ct);

        _context.DivisionWaveAssignment.RemoveRange(existing);

        if (waves.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var w in waves)
            {
                w.DivisionId = divisionId;
                w.Modified = now;
            }
            _context.DivisionWaveAssignment.AddRange(waves);
        }
    }

    public async Task DeleteDivisionWavesAsync(
        Guid divisionId, CancellationToken ct = default)
    {
        var existing = await _context.DivisionWaveAssignment
            .Where(w => w.DivisionId == divisionId)
            .ToListAsync(ct);

        _context.DivisionWaveAssignment.RemoveRange(existing);
    }

    // ── Single-entity wave add ──

    public void AddAgegroupWave(AgegroupWaveAssignment wave)
    {
        _context.AgegroupWaveAssignment.Add(wave);
    }

    public void AddDivisionWave(DivisionWaveAssignment wave)
    {
        _context.DivisionWaveAssignment.Add(wave);
    }

    // ── Date-scoped wave queries (for cascade date operations) ──

    public async Task<List<AgegroupWaveAssignment>> GetAgegroupWavesByDateAsync(
        Guid jobId, DateTime gameDate, CancellationToken ct = default)
    {
        var leagueIds = await GetLeagueIdsAsync(jobId, ct);

        return await _context.AgegroupWaveAssignment
            .Where(w => w.GameDate.Date == gameDate.Date
                && _context.Agegroups
                    .Where(a => leagueIds.Contains(a.LeagueId))
                    .Select(a => a.AgegroupId)
                    .Contains(w.AgegroupId))
            .ToListAsync(ct);
    }

    public async Task DeleteAgegroupWavesByDateAsync(
        Guid jobId, DateTime gameDate, CancellationToken ct = default)
    {
        var leagueIds = await GetLeagueIdsAsync(jobId, ct);

        var existing = await _context.AgegroupWaveAssignment
            .Where(w => w.GameDate.Date == gameDate.Date
                && _context.Agegroups
                    .Where(a => leagueIds.Contains(a.LeagueId))
                    .Select(a => a.AgegroupId)
                    .Contains(w.AgegroupId))
            .ToListAsync(ct);

        _context.AgegroupWaveAssignment.RemoveRange(existing);
    }

    public async Task<List<DivisionWaveAssignment>> GetDivisionWavesByDateAsync(
        Guid jobId, DateTime gameDate, CancellationToken ct = default)
    {
        var leagueIds = await GetLeagueIdsAsync(jobId, ct);

        return await _context.DivisionWaveAssignment
            .Where(w => w.GameDate.Date == gameDate.Date
                && _context.Divisions
                    .Where(d => _context.Agegroups
                        .Where(a => leagueIds.Contains(a.LeagueId))
                        .Select(a => a.AgegroupId)
                        .Contains(d.AgegroupId))
                    .Select(d => d.DivId)
                    .Contains(w.DivisionId))
            .ToListAsync(ct);
    }

    public async Task DeleteDivisionWavesByDateAsync(
        Guid jobId, DateTime gameDate, CancellationToken ct = default)
    {
        var leagueIds = await GetLeagueIdsAsync(jobId, ct);

        var existing = await _context.DivisionWaveAssignment
            .Where(w => w.GameDate.Date == gameDate.Date
                && _context.Divisions
                    .Where(d => _context.Agegroups
                        .Where(a => leagueIds.Contains(a.LeagueId))
                        .Select(a => a.AgegroupId)
                        .Contains(d.AgegroupId))
                    .Select(d => d.DivId)
                    .Contains(w.DivisionId))
            .ToListAsync(ct);

        _context.DivisionWaveAssignment.RemoveRange(existing);
    }

    // ── Division Processing Order ──

    public async Task<List<DivisionProcessingOrder>> GetProcessingOrderAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.DivisionProcessingOrder
            .AsNoTracking()
            .Where(p => p.JobId == jobId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync(ct);
    }

    public async Task UpsertProcessingOrderAsync(
        Guid jobId, List<DivisionProcessingOrder> entries, CancellationToken ct = default)
    {
        var existing = await _context.DivisionProcessingOrder
            .Where(p => p.JobId == jobId)
            .ToListAsync(ct);

        _context.DivisionProcessingOrder.RemoveRange(existing);

        if (entries.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var e in entries)
            {
                e.JobId = jobId;
                e.Modified = now;
            }
            _context.DivisionProcessingOrder.AddRange(entries);
        }
    }

    public async Task DeleteProcessingOrderAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var existing = await _context.DivisionProcessingOrder
            .Where(p => p.JobId == jobId)
            .ToListAsync(ct);

        _context.DivisionProcessingOrder.RemoveRange(existing);
    }

    // ── Bulk Delete ──

    public async Task DeleteAllForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var leagueIds = await GetLeagueIdsAsync(jobId, ct);

        var agegroupIds = await _context.Agegroups
            .Where(a => leagueIds.Contains(a.LeagueId))
            .Select(a => a.AgegroupId)
            .ToListAsync(ct);

        var divisionIds = await _context.Divisions
            .Where(d => agegroupIds.Contains(d.AgegroupId))
            .Select(d => d.DivId)
            .ToListAsync(ct);

        // Delete in dependency order: processing order, waves, profiles, event defaults
        var procOrder = await _context.DivisionProcessingOrder
            .Where(p => p.JobId == jobId)
            .ToListAsync(ct);
        _context.DivisionProcessingOrder.RemoveRange(procOrder);

        var divWaves = await _context.DivisionWaveAssignment
            .Where(w => divisionIds.Contains(w.DivisionId))
            .ToListAsync(ct);
        _context.DivisionWaveAssignment.RemoveRange(divWaves);

        var agWaves = await _context.AgegroupWaveAssignment
            .Where(w => agegroupIds.Contains(w.AgegroupId))
            .ToListAsync(ct);
        _context.AgegroupWaveAssignment.RemoveRange(agWaves);

        var divProfiles = await _context.DivisionScheduleProfile
            .Where(p => divisionIds.Contains(p.DivisionId))
            .ToListAsync(ct);
        _context.DivisionScheduleProfile.RemoveRange(divProfiles);

        var agProfiles = await _context.AgegroupScheduleProfile
            .Where(p => agegroupIds.Contains(p.AgegroupId))
            .ToListAsync(ct);
        _context.AgegroupScheduleProfile.RemoveRange(agProfiles);

        var eventDefaults = await _context.EventScheduleDefaults
            .Where(e => e.JobId == jobId)
            .ToListAsync(ct);
        _context.EventScheduleDefaults.RemoveRange(eventDefaults);

        await _context.SaveChangesAsync(ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }

    // ── Private Helpers ──

    private async Task<List<Guid>> GetLeagueIdsAsync(Guid jobId, CancellationToken ct)
    {
        return await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId)
            .Select(jl => jl.LeagueId)
            .ToListAsync(ct);
    }
}
