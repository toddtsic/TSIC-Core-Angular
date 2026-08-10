using TSIC.Contracts.Dtos.Scoring;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IMobileScorerRepository
{
    Task<List<MobileScorerDto>> GetScorersForJobAsync(Guid jobId, CancellationToken ct = default);
    Task<Registrations?> GetScorerRegistrationAsync(Guid registrationId, CancellationToken ct = default);
    Task<int> GetUserRegistrationCountAsync(string userId, CancellationToken ct = default);
    void AddRegistration(Registrations registration);
    void RemoveRegistration(Registrations registration);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
