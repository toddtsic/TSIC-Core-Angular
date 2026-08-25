using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class TeamDocsRepository : ITeamDocsRepository
{
    private readonly SqlDbContext _context;

    public TeamDocsRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<TeamLinkDto>> GetTeamLinksAsync(Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        return await _context.TeamDocs
            .AsNoTracking()
            .Where(td => td.TeamId == teamId || (td.JobId == jobId && td.TeamId == null))
            .OrderByDescending(td => td.CreateDate)
            .Select(td => new TeamLinkDto
            {
                DocId = td.DocId,
                TeamId = td.TeamId,
                JobId = td.JobId,
                Label = td.Label ?? "",
                DocUrl = td.DocUrl ?? "",
                User = td.User.FirstName + " " + td.User.LastName,
                UserId = td.UserId,
                CreateDate = td.CreateDate
            })
            .ToListAsync(ct);
    }

    public async Task<TeamDocs> AddTeamLinkAsync(
        Guid? teamId, Guid? jobId, string userId, string label, string docUrl, CancellationToken ct = default)
    {
        var doc = new TeamDocs
        {
            DocId = Guid.NewGuid(),
            TeamId = teamId,
            JobId = jobId,
            UserId = userId,
            Label = label,
            DocUrl = docUrl,
            CreateDate = DateTime.Now
        };
        _context.TeamDocs.Add(doc);
        return doc;
    }

    public async Task<bool> DeleteTeamLinkAsync(
        Guid docId, Guid teamId, Guid jobId, bool allowJobLevel, CancellationToken ct = default)
    {
        var doc = await _context.TeamDocs.FindAsync([docId], ct);
        if (doc == null) return false;

        // Mirrors GetTeamLinksAsync ownership exactly: the team own doc, or a job-level
        // doc (TeamId null) in the team job. Without this, docId alone deletes any
        // team link -- the route teamId was bound and never used.
        //
        // allowJobLevel splits that second clause off for callers confined to one team
        // (Player, Staff). A job-level row is visible on their team but is not THEIR row --
        // letting them delete it would hand a single player a job-wide destructive reach,
        // which the routed teamId cannot catch because a job-level row has TeamId null.
        var owned = doc.TeamId == teamId
            || (allowJobLevel && doc.TeamId == null && doc.JobId == jobId);
        if (!owned) return false;
        _context.TeamDocs.Remove(doc);
        return true;
    }

    public async Task<List<TeamPushDto>> GetTeamPushesAsync(Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobPushNotificationsToAll
            .AsNoTracking()
            .Where(p => p.TeamId == teamId || (p.JobId == jobId && p.TeamId == null))
            .OrderByDescending(p => p.Modified)
            .Select(p => new TeamPushDto
            {
                Id = p.Id,
                JobId = p.JobId,
                TeamId = p.TeamId,
                UserId = p.LebUserId,
                User = p.LebUser.FirstName + " " + p.LebUser.LastName,
                PushText = p.PushText,
                DeviceCount = p.DeviceCount,
                AddAllTeams = p.TeamId == null,
                CreateDate = p.Modified
            })
            .ToListAsync(ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
