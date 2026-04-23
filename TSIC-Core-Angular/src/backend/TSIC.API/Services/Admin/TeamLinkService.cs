using TSIC.Contracts.Dtos.TeamLink;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

public class TeamLinkService : ITeamLinkService
{
    private readonly ITeamLinkRepository _repo;

    public TeamLinkService(ITeamLinkRepository repo)
    {
        _repo = repo;
    }

    public Task<List<AdminTeamLinkDto>> GetForJobAsync(Guid jobId, CancellationToken ct = default)
        => _repo.GetByJobAsync(jobId, ct);

    public Task<List<TeamLinkTeamOptionDto>> GetAvailableTeamsAsync(Guid jobId, CancellationToken ct = default)
        => _repo.GetActiveTeamOptionsForJobAsync(jobId, ct);

    public async Task AddAsync(
        Guid jobId, string userId, CreateTeamLinkRequest request, CancellationToken ct = default)
    {
        await ReplaceGroupAsync(jobId, userId, request.Label, request.DocUrl, request.TeamId, ct);
    }

    public async Task UpdateAsync(
        Guid jobId, string userId, Guid docId, UpdateTeamLinkRequest request, CancellationToken ct = default)
    {
        if (request.TeamId == null)
        {
            // "All teams" edit: rebuild the group from scratch.
            await ReplaceGroupAsync(jobId, userId, request.Label, request.DocUrl, teamId: null, ct);
            return;
        }

        // Single-row in-place edit by DocId.
        var record = await _repo.GetByDocIdAsync(docId, ct);
        if (record == null) return;

        record.TeamId = request.TeamId;
        record.Label = request.Label;
        record.DocUrl = request.DocUrl;
        record.CreateDate = DateTime.Now;
        record.UserId = userId;

        await _repo.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid jobId, Guid docId, CancellationToken ct = default)
    {
        var record = await _repo.GetByDocIdAsync(docId, ct);
        if (record == null) return;

        var group = await _repo.GetGroupByLabelAndUrlAsync(jobId, record.Label, record.DocUrl, ct);
        if (group.Count == 0) return;

        _repo.RemoveRange(group);
        await _repo.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Legacy "replace group" write: delete every row in the job sharing the
    /// Label + DocUrl, then fan out a new group. When teamId is set, the new
    /// group is one row; when null, one row per active team.
    /// </summary>
    private async Task ReplaceGroupAsync(
        Guid jobId, string userId, string label, string docUrl, Guid? teamId, CancellationToken ct)
    {
        var existing = await _repo.GetGroupByLabelAndUrlAsync(jobId, label, docUrl, ct);
        if (existing.Count > 0)
        {
            _repo.RemoveRange(existing);
        }

        var teamIds = teamId.HasValue
            ? new List<Guid?> { teamId.Value }
            : (await _repo.GetActiveTeamIdsForJobAsync(jobId, ct)).Cast<Guid?>().ToList();

        var now = DateTime.Now;
        var newRows = teamIds.Select(tid => new TeamDocs
        {
            DocId = Guid.NewGuid(),
            JobId = jobId,
            TeamId = tid,
            Label = label,
            DocUrl = docUrl,
            UserId = userId,
            CreateDate = now
        });

        _repo.AddRange(newRows);
        await _repo.SaveChangesAsync(ct);
    }
}
