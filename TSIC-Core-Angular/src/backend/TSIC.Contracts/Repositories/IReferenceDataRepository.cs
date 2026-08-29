using TSIC.Contracts.Dtos.Reference;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for the small, static lookup tables in the `reference` schema.
/// </summary>
public interface IReferenceDataRepository
{
    /// <summary>
    /// Every row of reference.States (US states + DC + territories + Canadian provinces),
    /// ordered by display name.
    /// </summary>
    Task<List<StateOptionDto>> GetStatesAsync(CancellationToken ct = default);
}
