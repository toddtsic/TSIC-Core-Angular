using Microsoft.Extensions.Caching.Memory;
using TSIC.Contracts.Dtos.Reference;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Reference;

/// <summary>
/// Serves the `reference` schema lookup tables. Cached in memory — 72 rows that have not
/// changed in years, read on the anonymous registration and store walk-up forms.
/// </summary>
public sealed class ReferenceDataService : IReferenceDataService
{
    private const string StatesCacheKey = "reference:states";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

    private readonly IReferenceDataRepository _repository;
    private readonly IMemoryCache _cache;

    public ReferenceDataService(IReferenceDataRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<List<StateOptionDto>> GetStatesAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(StatesCacheKey, out List<StateOptionDto>? cached) && cached is not null)
        {
            return cached;
        }

        var states = await _repository.GetStatesAsync(ct);

        // Only cache a real result. An empty list means the query failed to find rows, and
        // caching that for 12 hours would leave every address form with an empty dropdown.
        if (states.Count > 0)
        {
            _cache.Set(StatesCacheKey, states, CacheTtl);
        }

        return states;
    }
}
