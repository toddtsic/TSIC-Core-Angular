using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// REST endpoint for retrieving team chat messages (history).
///
/// REST only. The SignalR hub that once served real-time send/receive was removed
/// along with AddSignalR/MapHub -- nothing injected IHubContext, no client dialled
/// /hubs/chat, and the teamchat rebuild does not use SignalR. Do not reintroduce a
/// hub here on the assumption that one is expected.
/// </summary>
[ApiController]
[Authorize]
[Route("api/teams/{teamId:guid}/chat")]
public class TeamChatController : ControllerBase
{
    private readonly IChatRepository _chatRepo;
    private readonly IJobLookupService _jobLookupService;

    public TeamChatController(IChatRepository chatRepo, IJobLookupService jobLookupService)
    {
        _chatRepo = chatRepo;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Rejects a teamId belonging to another job. Explicit per-action -- nothing scopes by
    /// job ambiently in this API. Superuser exempt.
    ///
    /// NOTE: this closes CROSS-JOB reads only. It does NOT stop a user reading another
    /// team's chat inside their own job -- that needs a membership rule and is not fixed here.
    /// </summary>
    private async Task<ActionResult?> DenyIfCrossJob(Guid teamId, CancellationToken ct)
    {
        if (User.IsInRole(RoleConstants.Names.SuperuserName)) return null;

        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var teamJobId = await _jobLookupService.GetJobIdByTeamAsync(teamId, ct);

        if (callerJobId == null || teamJobId == null || callerJobId.Value != teamJobId.Value)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = "TeamJobMismatch",
                Title = "Team Access Denied",
                Detail = "This team belongs to a different event than the one you are logged into."
            });

        return null;
    }

    /// <summary>
    /// Get paginated chat messages for a team (newest first).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(GetChatMessagesResponse), 200)]
    public async Task<IActionResult> GetMessages(
        Guid teamId, [FromBody] GetChatMessagesRequest request, CancellationToken ct)
    {
        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;

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
