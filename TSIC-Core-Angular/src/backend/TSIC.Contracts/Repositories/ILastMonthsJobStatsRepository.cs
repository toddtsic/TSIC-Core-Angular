using TSIC.Contracts.Dtos.LastMonthsJobStats;

namespace TSIC.Contracts.Repositories;

public interface ILastMonthsJobStatsRepository
{
    /// <summary>
    /// Returns last calendar month's MonthlyJobStats rows joined with Jobs + Customers,
    /// sorted by CustomerName then JobName. SuperUser cross-customer view.
    /// </summary>
    Task<List<LastMonthsJobStatRowDto>> GetLastMonthsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the 6 count fields on a single MonthlyJobStats row by Aid. Stamps LebUserId
    /// and Modified. Returns false if the row does not exist.
    /// </summary>
    Task<bool> UpdateCountsAsync(
        int aid,
        UpdateLastMonthsJobStatRequest request,
        string lebUserId,
        CancellationToken cancellationToken = default);
}
