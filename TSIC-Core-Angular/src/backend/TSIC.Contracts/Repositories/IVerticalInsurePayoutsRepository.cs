using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IVerticalInsurePayoutsRepository
{
    Task<HashSet<string>> GetExistingPolicyNumbersAsync(
        IEnumerable<string> policyNumbers,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(
        IEnumerable<VerticalInsurePayouts> payouts,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
