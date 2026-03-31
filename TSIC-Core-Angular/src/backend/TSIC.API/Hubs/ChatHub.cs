using Microsoft.AspNetCore.SignalR;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Hubs;

/// <summary>
/// SignalR hub for real-time team chat messaging.
/// Clients join/leave team-specific groups and broadcast messages.
/// </summary>
public class ChatHub : Hub
{
    private readonly IChatRepository _chatRepo;

    public ChatHub(IChatRepository chatRepo)
    {
        _chatRepo = chatRepo;
    }

    /// <summary>Join a team chat group to receive messages.</summary>
    public async Task JoinGroup(Guid teamId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, teamId.ToString());
    }

    /// <summary>Leave a team chat group.</summary>
    public async Task LeaveGroup(Guid teamId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, teamId.ToString());
    }

    /// <summary>Send a chat message to a team group.</summary>
    public async Task AddTeamChatMessage(Guid teamId, string userId, string message)
    {
        var entity = await _chatRepo.AddMessageAsync(teamId, userId, message);

        var dto = new ChatMessageDto
        {
            MessageId = entity.MessageId,
            Message = entity.Message ?? "",
            TeamId = entity.TeamId,
            CreatorUserId = entity.CreatorUserId,
            Created = entity.Created,
            CreatedBy = null, // caller knows their own name
            MyMessage = false // each client determines this locally
        };

        await Clients.Group(teamId.ToString()).SendAsync($"newmessage_{teamId}", dto);
    }

    /// <summary>Delete a chat message and notify the group.</summary>
    public async Task DeleteTeamChatMessage(Guid teamId, Guid messageId)
    {
        var deleted = await _chatRepo.DeleteMessageAsync(messageId);
        if (deleted) await _chatRepo.SaveChangesAsync();

        await Clients.Group(teamId.ToString()).SendAsync($"deletemessage_{teamId}", messageId);
    }
}
