using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.TeamLink;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class TeamLinkRepository : ITeamLinkRepository
{
    private static readonly string[] ExcludedAgegroupNames = { "Dropped Teams", "Registration" };

    private readonly SqlDbContext _context;

    public TeamLinkRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<AdminTeamLinkDto>> GetByJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await (
            from td in _context.TeamDocs.AsNoTracking()
            where td.JobId == jobId
            join t in _context.Teams.AsNoTracking() on td.TeamId equals t.TeamId into tj
            from t in tj.DefaultIfEmpty()
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId into agj
            from ag in agj.DefaultIfEmpty()
            orderby ag.AgegroupName, t.TeamName, td.Label
            select new AdminTeamLinkDto
            {
                DocId = td.DocId,
                TeamId = td.TeamId,
                TeamDisplay = td.TeamId == null
                    ? "— All Teams —"
                    : (ag.AgegroupName ?? "") + " - " + (t.TeamName ?? ""),
                Label = td.Label,
                DocUrl = td.DocUrl,
                CreateDate = td.CreateDate
            }
        ).ToListAsync(ct);
    }

    public async Task<List<Guid>> GetActiveTeamIdsForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await (
            from t in _context.Teams.AsNoTracking()
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            where t.JobId == jobId
                && t.Active == true
                && ag.AgegroupName != null
                && !ExcludedAgegroupNames.Contains(ag.AgegroupName)
            orderby ag.AgegroupName, t.TeamName
            select t.TeamId
        ).ToListAsync(ct);
    }

    public async Task<List<TeamLinkTeamOptionDto>> GetActiveTeamOptionsForJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await (
            from t in _context.Teams.AsNoTracking()
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            where t.JobId == jobId
                && t.Active == true
                && ag.AgegroupName != null
                && !ExcludedAgegroupNames.Contains(ag.AgegroupName)
            orderby ag.AgegroupName, t.TeamName
            select new TeamLinkTeamOptionDto
            {
                TeamId = t.TeamId,
                Display = (ag.AgegroupName ?? "") + " - " + (t.TeamName ?? "")
            }
        ).ToListAsync(ct);
    }

    public async Task<TeamDocs?> GetByDocIdAsync(Guid docId, CancellationToken ct = default)
    {
        return await _context.TeamDocs
            .FirstOrDefaultAsync(td => td.DocId == docId, ct);
    }

    public async Task<List<TeamDocs>> GetGroupByLabelAndUrlAsync(
        Guid jobId, string label, string docUrl, CancellationToken ct = default)
    {
        return await _context.TeamDocs
            .Where(td => td.JobId == jobId && td.Label == label && td.DocUrl == docUrl)
            .ToListAsync(ct);
    }

    public void AddRange(IEnumerable<TeamDocs> records)
    {
        _context.TeamDocs.AddRange(records);
    }

    public void RemoveRange(IEnumerable<TeamDocs> records)
    {
        _context.TeamDocs.RemoveRange(records);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
