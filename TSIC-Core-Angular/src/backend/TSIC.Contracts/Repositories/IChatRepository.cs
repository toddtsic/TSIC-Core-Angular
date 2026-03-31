using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IChatRepository
{
    Task<List<ChatMessages>> GetMessagesAsync(Guid teamId, int skip, int take, CancellationToken ct = default);
    Task<int> GetMessageCountAsync(Guid teamId, CancellationToken ct = default);
    Task<ChatMessages> AddMessageAsync(Guid teamId, string userId, string message, CancellationToken ct = default);
    Task<bool> DeleteMessageAsync(Guid messageId, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
