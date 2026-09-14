using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Admin;

/// <summary>
/// See <see cref="IClubRepLocalRenameService"/>.
/// </summary>
public sealed class ClubRepLocalRenameService : IClubRepLocalRenameService
{
    /// <summary>Same cap as Clubs.ClubName (varchar(80)), so a local name is always a valid club name.</summary>
    public const int MaxClubNameLength = 80;

    private readonly IRegistrationRepository _registrationRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IClubRepository _clubRepo;
    private readonly IClubRepRepository _clubRepRepo;
    private readonly ITeamRepository _teamRepo;

    public ClubRepLocalRenameService(
        IRegistrationRepository registrationRepo,
        IScheduleRepository scheduleRepo,
        IClubRepository clubRepo,
        IClubRepRepository clubRepRepo,
        ITeamRepository teamRepo)
    {
        _registrationRepo = registrationRepo;
        _scheduleRepo = scheduleRepo;
        _clubRepo = clubRepo;
        _clubRepRepo = clubRepRepo;
        _teamRepo = teamRepo;
    }

    public async Task<RenameClubRepClubLocalResponse> RenameAsync(
        Guid jobId, string userId, string callerRole, Guid registrationId, string clubName,
        CancellationToken ct = default)
    {
        // Director or Superuser (Todd 2026-09-14: Superuser renames a club per event too — there is no
        // rename that reaches past one event). The endpoint's AdminOnly policy also admits SuperDirector,
        // which is not in scope.
        if (!string.Equals(callerRole, RoleConstants.Names.DirectorName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(callerRole, RoleConstants.Names.SuperuserName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a Director or Superuser can rename a club for this event.");

        var next = (clubName ?? string.Empty).Trim();
        if (next.Length == 0)
            throw new InvalidOperationException("Club name is required.");
        if (next.Length > MaxClubNameLength)
            throw new InvalidOperationException($"Club name cannot exceed {MaxClubNameLength} characters.");

        var reg = await _registrationRepo.GetByIdAsync(registrationId, ct)
            ?? throw new KeyNotFoundException($"Registration {registrationId} not found.");
        if (reg.JobId != jobId)
            throw new InvalidOperationException("Registration does not belong to this job.");
        if (!string.Equals(reg.RoleId, RoleConstants.ClubRep, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a Club Rep registration carries a club name to rename.");

        // A club is renamed once it has at least one team (any status).
        if (await _teamRepo.CountTeamsByClubRepRegistrationAsync(reg.RegistrationId, ct) == 0)
            throw new InvalidOperationException(
                "This club has no teams registered for this event yet. A club can be renamed once it has at least one team.");

        // Once renamed, club_name no longer names the rep's club, so the club must be reachable without it:
        // through a library-linked team, or because the rep represents exactly one club.
        var resolution = await _clubRepRepo.ResolveClubForClubRepRegistrationAsync(reg.RegistrationId, ct);
        if (!resolution.SurvivesRename)
            throw new InvalidOperationException(
                "This club can't be renamed for this event: its rep represents more than one club and none of its teams here are linked to a club team library, so the rename would disconnect the rep from their club.");

        // Inside the event this name reads as the club's identity. Naming this rep after a club they do not
        // represent would present their teams as that other club — refuse it. Renaming to one of the rep's
        // own clubs (including back to the library name) is allowed.
        var sameNamedClub = await _clubRepo.GetByNameAsync(next, ct);
        if (sameNamedClub != null)
        {
            var repsOwnClub = reg.UserId != null
                && (await _clubRepRepo.GetClubsForUserAsync(reg.UserId, ct))
                    .Exists(c => c.ClubId == sameNamedClub.ClubId);
            if (!repsOwnClub)
                throw new InvalidOperationException(
                    $"\"{next}\" is the name of a different club. Choose a name that is not another club's.");
        }

        // Same three copies the club-rep registration is created with (InitializeRegistrationAsync).
        reg.ClubName = next;
        reg.Assignment = next;
        reg.RegistrationCategory = $"Club Rep: {next}";
        reg.Modified = DateTime.Now;
        reg.LebUserId = userId;

        var slots = await _scheduleRepo.RestampClubRepTeamSlotsAsync(jobId, reg.RegistrationId, next, ct);

        // One save: the registration and the schedule slots land together or not at all.
        await _registrationRepo.SaveChangesAsync(ct);

        return new RenameClubRepClubLocalResponse { ClubName = next, ScheduleSlotsUpdated = slots };
    }
}
