using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly SqlDbContext _context;

    public ChatRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<ChatMessages>> GetMessagesAsync(
        Guid teamId, int skip, int take, CancellationToken ct = default)
    {
        return await _context.ChatMessages
            .AsNoTracking()
            .Where(m => m.TeamId == teamId)
            .OrderByDescending(m => m.Created)
            .Skip(skip)
            .Take(take)
            .Include(m => m.CreatorUser)
            .ToListAsync(ct);
    }

    public async Task<int> GetMessageCountAsync(Guid teamId, CancellationToken ct = default)
    {
        return await _context.ChatMessages
            .AsNoTracking()
            .Where(m => m.TeamId == teamId)
            .CountAsync(ct);
    }

    public async Task<ChatMessages> AddMessageAsync(
        Guid teamId, string userId, string message, CancellationToken ct = default)
    {
        var entity = new ChatMessages
        {
            MessageId = Guid.NewGuid(),
            TeamId = teamId,
            CreatorUserId = userId,
            Message = message,
            Created = DateTime.UtcNow
        };
        _context.ChatMessages.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<bool> DeleteMessageAsync(Guid messageId, CancellationToken ct = default)
    {
        var entity = await _context.ChatMessages.FindAsync([messageId], ct);
        if (entity == null) return false;
        _context.ChatMessages.Remove(entity);
        return true;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
