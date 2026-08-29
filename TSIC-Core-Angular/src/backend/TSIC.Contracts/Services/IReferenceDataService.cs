using TSIC.Contracts.Dtos.Reference;

namespace TSIC.Contracts.Services;

/// <summary>
/// Serves the `reference` schema lookup tables to the frontend. Cached — these tables are static.
/// </summary>
public interface IReferenceDataService
{
    /// <summary>
    /// Every state/province/territory, ordered by display name. Cached.
    /// </summary>
    Task<List<StateOptionDto>> GetStatesAsync(CancellationToken ct = default);
}
