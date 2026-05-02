using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface INuveiBatchesRepository
{
    Task<HashSet<string>> GetExistingFingerprintsAsync(
        IEnumerable<string> fingerprints,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(
        IEnumerable<NuveiBatches> batches,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
