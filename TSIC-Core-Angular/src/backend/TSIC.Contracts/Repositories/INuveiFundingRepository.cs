using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface INuveiFundingRepository
{
    Task<HashSet<string>> GetExistingFingerprintsAsync(
        IEnumerable<string> fingerprints,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(
        IEnumerable<NuveiFunding> records,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
