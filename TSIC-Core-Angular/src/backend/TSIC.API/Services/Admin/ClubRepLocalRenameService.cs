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

    public ClubRepLocalRenameService(
        IRegistrationRepository registrationRepo,
        IScheduleRepository scheduleRepo,
        IClubRepository clubRepo,
        IClubRepRepository clubRepRepo)
    {
        _registrationRepo = registrationRepo;
        _scheduleRepo = scheduleRepo;
        _clubRepo = clubRepo;
        _clubRepRepo = clubRepRepo;
    }

    public async Task<RenameClubRepClubLocalResponse> RenameAsync(
        Guid jobId, string userId, string callerRole, Guid registrationId, string clubName,
        CancellationToken ct = default)
    {
        // Director only (Todd 2026-09-13). The endpoint's AdminOnly policy also admits Superuser and
        // SuperDirector; neither is in scope for this operation yet.
        if (!string.Equals(callerRole, RoleConstants.Names.DirectorName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a Director can rename a club for this event.");

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

        // A registration's club name is also used to LOOK UP a club by exact name (the rep's library
        // fallback, LADT move-team-to-rep). Naming this rep after a club they do not represent would
        // point those lookups at that other club — refuse it. Renaming to one of the rep's own clubs
        // (including back to the canonical name) is allowed.
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
