using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Scoring;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IMobileScorerRepository
{
    Task<List<MobileScorerDto>> GetScorersForJobAsync(Guid jobId, CancellationToken ct = default);
    Task<Registrations?> GetScorerRegistrationAsync(Guid registrationId, CancellationToken ct = default);

    /// <summary>
    /// The active Scorer registration this user holds for this job, or null.
    /// Login-path read: requires bActive plus an unexpired user window
    /// (Jobs.ExpiryUsers — the same predicate Referee / Ref Assignor / Store Admin
    /// ride, since Scorer is an event-day role and not in RoleConstants.AdminRoleIds).
    /// Returns the job's path and logo so the caller can mint an enriched token
    /// without a second query.
    /// </summary>
    Task<RegistrationDto?> GetScorerRegistrationForUserAndJobAsync(
        string userId, Guid jobId, CancellationToken ct = default);
    Task<int> GetUserRegistrationCountAsync(string userId, CancellationToken ct = default);
    void AddRegistration(Registrations registration);
    void RemoveRegistration(Registrations registration);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
