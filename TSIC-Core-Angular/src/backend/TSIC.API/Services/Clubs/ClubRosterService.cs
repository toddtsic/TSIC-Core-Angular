using TSIC.Contracts.Dtos.ClubRoster;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Clubs;

public sealed class ClubRosterService : IClubRosterService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IRegistrationRepository _registrationRepo;

    public ClubRosterService(
        ITeamRepository teamRepo,
        IRegistrationRepository registrationRepo)
    {
        _teamRepo = teamRepo;
        _registrationRepo = registrationRepo;
    }

    public async Task<List<ClubRosterTeamDto>> GetTeamsAsync(
        Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default)
    {
        return await _teamRepo.GetClubRosterTeamsAsync(clubRepRegistrationId, jobId, ct);
    }

    public async Task<List<ClubRosterPlayerDto>> GetRosterAsync(
        Guid teamId, Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default)
    {
        await ValidateTeamOwnershipAsync(teamId, clubRepRegistrationId, jobId, ct);

        // Get team context for agegroup/team name
        var teams = await _teamRepo.GetClubRosterTeamsAsync(clubRepRegistrationId, jobId, ct);
        var team = teams.FirstOrDefault(t => t.TeamId == teamId);
        var agName = team?.AgegroupName ?? "";
        var tmName = team?.TeamName ?? "";

        var roster = await _registrationRepo.GetRosterByTeamIdAsync(teamId, jobId, ct);

        return roster
            .Where(r => r.RoleName == "Player")
            .Select(r => new ClubRosterPlayerDto
            {
                RegistrationId = r.RegistrationId,
                PlayerName = r.PlayerName,
                AgegroupName = agName,
                TeamName = tmName,
                IsActive = r.BActive
            })
            .ToList();
    }

    public async Task<ClubRosterMutationResultDto> MovePlayersAsync(
        MovePlayersRequest request, Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default)
    {
        // Validate club rep owns the target team
        await ValidateTeamOwnershipAsync(request.TargetTeamId, clubRepRegistrationId, jobId, ct);

        var targetTeam = await _teamRepo.GetByIdReadOnlyAsync(request.TargetTeamId, ct)
            ?? throw new KeyNotFoundException("Target team not found.");

        var moved = 0;
        foreach (var regId in request.RegistrationIds)
        {
            var reg = await _registrationRepo.GetByIdAsync(regId, ct);
            if (reg == null || reg.JobId != jobId) continue;

            // Validate club rep owns the source team too
            if (reg.AssignedTeamId.HasValue)
                await ValidateTeamOwnershipAsync(reg.AssignedTeamId.Value, clubRepRegistrationId, jobId, ct);

            reg.AssignedTeamId = request.TargetTeamId;
            reg.AssignedAgegroupId = targetTeam.AgegroupId;
            reg.AssignedDivId = targetTeam.DivId;
            reg.AssignedLeagueId = targetTeam.LeagueId;
            reg.Assignment = $"Player: {targetTeam.TeamName}";
            reg.Modified = DateTime.UtcNow;

            moved++;
        }

        await _registrationRepo.SaveChangesAsync(ct);

        var plural = moved == 1 ? "player" : "players";
        return new ClubRosterMutationResultDto
        {
            AffectedCount = moved,
            Message = $"Moved {moved} {plural} to {targetTeam.TeamName}."
        };
    }

    public async Task<ClubRosterMutationResultDto> DeletePlayersAsync(
        DeletePlayersRequest request, Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default)
    {
        var deleted = 0;
        foreach (var regId in request.RegistrationIds)
        {
            var reg = await _registrationRepo.GetByIdAsync(regId, ct);
            if (reg == null || reg.JobId != jobId) continue;

            // Validate club rep owns this team
            if (reg.AssignedTeamId.HasValue)
                await ValidateTeamOwnershipAsync(reg.AssignedTeamId.Value, clubRepRegistrationId, jobId, ct);

            // Block deletion if registration has accounting records
            var hasAccounting = await _registrationRepo.HasAccountingRecordsAsync(regId, ct);
            if (hasAccounting)
                throw new InvalidOperationException(
                    $"Cannot delete registration for {reg.User?.FirstName} {reg.User?.LastName} — accounting records exist. Contact the tournament director.");

            _registrationRepo.Remove(reg);
            deleted++;
        }

        await _registrationRepo.SaveChangesAsync(ct);

        var plural = deleted == 1 ? "registration" : "registrations";
        return new ClubRosterMutationResultDto
        {
            AffectedCount = deleted,
            Message = $"Deleted {deleted} {plural}."
        };
    }

    private async Task ValidateTeamOwnershipAsync(
        Guid teamId, Guid clubRepRegistrationId, Guid jobId, CancellationToken ct)
    {
        var clubRepTeams = await _teamRepo.GetTeamsByClubRepRegistrationAsync(jobId, clubRepRegistrationId, ct);
        if (!clubRepTeams.Any(t => t.TeamId == teamId))
            throw new UnauthorizedAccessException("You do not have access to this team.");
    }
}
