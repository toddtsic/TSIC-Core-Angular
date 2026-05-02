using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IAdnReconciliationRepository
{
    /// <summary>
    /// Returns the Authorize.Net credentials stored on the designated TSIC master Customer
    /// record (CustomerName = 'TeamSportsInfo.com'). These credentials see batches across
    /// all TSIC-merchant customers and are required to drive the monthly reconciliation pull.
    /// </summary>
    Task<AdnCredentialsViewModel?> GetTsicMasterAdnCredentialsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes existing rows in <c>Txs</c> whose <c>SettlementDateTime</c> matches the legacy
    /// month key format <c>"-MMM-yyyy "</c> (with trailing space). Mirrors legacy behavior so
    /// that a re-run for the same month repopulates idempotently.
    /// </summary>
    Task<int> DeleteTxsForMonthKeyAsync(
        string monthKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the set of TransactionId values that already exist in <c>Txs</c> from the supplied
    /// candidate list. Used to dedup the per-batch pull.
    /// </summary>
    Task<HashSet<string>> GetExistingTransactionIdsAsync(
        IEnumerable<string> transactionIds,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<Txs> txs, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
