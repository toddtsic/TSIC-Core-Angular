using TSIC.Contracts.Dtos.RegistrationSearch;

namespace TSIC.Contracts.Services;

/// <summary>
/// Director's THIS-EVENT-ONLY club rename, from the Club Rep's registration detail. Two writes, one
/// save: the registration's club name (+ its Assignment / RegistrationCategory copies) and this job's
/// round-robin schedule slots for the teams that rep owns. The Clubs row, the Club Team Library,
/// bracket slots and every other job are untouched.
/// </summary>
public interface IClubRepLocalRenameService
{
    /// <exception cref="KeyNotFoundException">The registration does not exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// Caller is not a Director; the name is blank or too long; the registration belongs to another
    /// job or is not a Club Rep; or the name is an existing club this rep does not represent.
    /// </exception>
    Task<RenameClubRepClubLocalResponse> RenameAsync(
        Guid jobId, string userId, string callerRole, Guid registrationId, string clubName,
        CancellationToken ct = default);
}
