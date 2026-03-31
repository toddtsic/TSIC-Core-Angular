using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

/// <summary>
/// REST endpoint for retrieving team chat messages (history).
/// Real-time send/receive is handled by the ChatHub SignalR hub.
/// </summary>
[ApiController]
[Authorize]
[Route("api/teams/{teamId:guid}/chat")]
public class TeamChatController : ControllerBase
{
    private readonly IChatRepository _chatRepo;

    public TeamChatController(IChatRepository chatRepo)
    {
        _chatRepo = chatRepo;
    }

    /// <summary>
    /// Get paginated chat messages for a team (newest first).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(GetChatMessagesResponse), 200)]
    public async Task<IActionResult> GetMessages(
        Guid teamId, [FromBody] GetChatMessagesRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var skip = (request.PageNumber - 1) * request.RowsPerPage;
        var take = request.RowsPerPage;

        var messages = await _chatRepo.GetMessagesAsync(teamId, skip, take, ct);
        var totalCount = await _chatRepo.GetMessageCountAsync(teamId, ct);

        var dtos = messages.Select(m => new ChatMessageDto
        {
            MessageId = m.MessageId,
            Message = m.Message ?? "",
            TeamId = m.TeamId,
            CreatorUserId = m.CreatorUserId,
            Created = m.Created,
            CreatedBy = m.CreatorUser != null
                ? $"{m.CreatorUser.FirstName} {m.CreatorUser.LastName}".Trim()
                : null,
            MyMessage = m.CreatorUserId == userId
        }).ToList();

        return Ok(new GetChatMessagesResponse
        {
            Messages = dtos,
            IncludesAll = skip + take >= totalCount
        });
    }
}
