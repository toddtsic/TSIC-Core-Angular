using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for per-user widget customization (UserWidget table).
/// </summary>
public class UserWidgetRepository : IUserWidgetRepository
{
    private readonly SqlDbContext _context;

    public UserWidgetRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<UserWidget>> GetByRegistrationIdAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        return await _context.UserWidget
            .AsNoTracking()
            .Where(uw => uw.RegistrationId == registrationId)
            .OrderBy(uw => uw.DisplayOrder)
            .ToListAsync(ct);
    }

    public void RemoveRange(IEnumerable<UserWidget> entities)
    {
        _context.UserWidget.RemoveRange(entities);
    }

    public async Task AddRangeAsync(
        IEnumerable<UserWidget> entities,
        CancellationToken ct = default)
    {
        await _context.UserWidget.AddRangeAsync(entities, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
