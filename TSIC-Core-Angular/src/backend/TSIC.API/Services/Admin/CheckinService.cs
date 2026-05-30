using TSIC.Contracts.Dtos.CampGroups;
using TSIC.Contracts.Dtos.CheckIn;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Live check-in admin service. Thin orchestration over the check-in repository (roster
/// reads + idempotent upserts) and the team repository (team picker for player-mode).
/// Job-scope enforcement lives in the repository writes; team ↔ job pairing for the
/// player roster is guarded by the controller before delegation.
/// </summary>
public class CheckinService : ICheckinService
{
    private readonly ICheckinRepository _checkinRepo;
    private readonly ITeamRepository _teamRepo;

    public CheckinService(ICheckinRepository checkinRepo, ITeamRepository teamRepo)
    {
        _checkinRepo = checkinRepo;
        _teamRepo = teamRepo;
    }

    public Task<List<TeamRosterCountDto>> GetTeamsAsync(Guid jobId, CancellationToken ct = default)
        => _teamRepo.GetTeamsWithRosterCountForJobAsync(jobId, ct);

    public Task<List<TeamCheckinRowDto>> GetTeamRosterAsync(Guid jobId, CancellationToken ct = default)
        => _checkinRepo.GetTeamRosterByJobAsync(jobId, ct);

    public Task<List<PlayerCheckinRowDto>> GetPlayerRosterAsync(Guid teamId, CancellationToken ct = default)
        => _checkinRepo.GetPlayerRosterByTeamAsync(teamId, ct);

    public Task<CheckinStateDto?> CheckInPlayerAsync(
        Guid jobId, Guid registrationId, Guid byRegId, string? userId, CancellationToken ct = default)
        => _checkinRepo.UpsertPlayerCheckinAsync(jobId, registrationId, byRegId, userId, ct);

    public Task<bool> UndoPlayerCheckInAsync(Guid jobId, Guid registrationId, CancellationToken ct = default)
        => _checkinRepo.UndoPlayerCheckinAsync(jobId, registrationId, ct);

    public Task<CheckinStateDto?> CheckInTeamAsync(
        Guid jobId, Guid teamId, Guid byRegId, string? userId, CancellationToken ct = default)
        => _checkinRepo.UpsertTeamCheckinAsync(jobId, teamId, byRegId, userId, ct);

    public Task<bool> UndoTeamCheckInAsync(Guid jobId, Guid teamId, CancellationToken ct = default)
        => _checkinRepo.UndoTeamCheckinAsync(jobId, teamId, ct);
}
