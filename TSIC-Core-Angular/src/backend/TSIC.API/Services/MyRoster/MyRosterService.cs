using TSIC.Contracts.Dtos.MyRoster;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.MyRoster;

public sealed class MyRosterService : IMyRosterService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IRegistrationSearchService _searchService;

    public MyRosterService(
        IRegistrationRepository registrationRepo,
        IRegistrationSearchService searchService)
    {
        _registrationRepo = registrationRepo;
        _searchService = searchService;
    }

    public async Task<MyRosterResponseDto> GetMyRosterAsync(
        Guid callerRegistrationId, CancellationToken ct = default)
    {
        var caller = await _registrationRepo.GetByIdAsync(callerRegistrationId, ct);
        if (caller == null)
            return Denied("Registration not found.");

        var roleName = caller.Role?.Name;
        if (roleName != RoleConstants.Names.PlayerName && roleName != RoleConstants.Names.StaffName)
            return Denied("Only players and staff may view team rosters.");

        if (caller.AssignedTeamId == null)
            return Denied("You are not yet assigned to a team.");

        var flags = await _registrationRepo.GetRosterViewFlagsAsync(caller.JobId, ct);
        if (flags == null)
            return Denied("Job not found.");

        var allowed = roleName == RoleConstants.Names.PlayerName
            ? flags.Value.AllowPlayer
            : flags.Value.AllowAdult;

        if (!allowed)
            return Denied("Roster viewing is disabled for this event.");

        var teamId = caller.AssignedTeamId.Value;
        var teamName = await _registrationRepo.GetTeamNameAsync(teamId, caller.JobId, ct);
        var players = await _registrationRepo.GetMyRosterByTeamIdAsync(teamId, caller.JobId, ct);

        return new MyRosterResponseDto
        {
            Allowed = true,
            TeamId = teamId,
            TeamName = teamName,
            Players = players,
        };
    }

    public async Task<BatchEmailResponse> SendBatchEmailAsync(
        Guid callerRegistrationId,
        string callerUserId,
        MyRosterBatchEmailRequest request,
        CancellationToken ct = default)
    {
        var snapshot = await GetMyRosterAsync(callerRegistrationId, ct);
        if (!snapshot.Allowed || snapshot.Players == null)
            throw new UnauthorizedAccessException(snapshot.Reason ?? "Roster unavailable.");

        var allowedIds = snapshot.Players.Select(p => p.RegistrationId).ToHashSet();

        // "Email all" when no explicit selection provided.
        var targetIds = (request.RegistrationIds == null || request.RegistrationIds.Count == 0)
            ? allowedIds.ToList()
            : request.RegistrationIds;

        if (targetIds.Any(id => !allowedIds.Contains(id)))
            throw new UnauthorizedAccessException("One or more recipients are not on your team.");

        // Derive jobId from caller (already validated in GetMyRosterAsync, but re-read cheaply).
        var caller = await _registrationRepo.GetByIdAsync(callerRegistrationId, ct)
            ?? throw new UnauthorizedAccessException("Registration not found.");

        var batch = new BatchEmailRequest
        {
            RegistrationIds = targetIds,
            Subject = request.Subject,
            BodyTemplate = request.BodyTemplate,
        };

        return await _searchService.SendBatchEmailAsync(caller.JobId, callerUserId, batch, ct);
    }

    private static MyRosterResponseDto Denied(string reason) => new()
    {
        Allowed = false,
        Reason = reason,
    };
}
