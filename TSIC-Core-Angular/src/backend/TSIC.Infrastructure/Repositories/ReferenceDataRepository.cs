using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Reference;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Reads the static lookup tables in the `reference` schema.
/// </summary>
public class ReferenceDataRepository : IReferenceDataRepository
{
    private readonly SqlDbContext _context;

    public ReferenceDataRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<StateOptionDto>> GetStatesAsync(CancellationToken ct = default)
    {
        // Ordered by display name — one flat alphabetical list. Legacy had no ORDER BY at all
        // and rendered in StateID order ("Alberta, Alaska, Alabama, Arkansas, ...").
        return await _context.States
            .AsNoTracking()
            .OrderBy(s => s.State)
            .Select(s => new StateOptionDto
            {
                Value = s.StateId,
                Label = s.State ?? s.StateId
            })
            .ToListAsync(ct);
    }
}
