using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.TeamChat;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// Team chat for the TSIC-Teams mobile app. ONE THREAD PER TEAM -- teamId is the thread key,
/// everyone rostered sees every message, and there is no direct or subgroup messaging. That is
/// a standing ruling and not a scoping decision: whole-team visibility is what polices a
/// surface minors publish to, and the storage carries no participant model to build it from.
///
/// THREE GATES, IN THIS ORDER, ON EVERY ACTION:
///   1. cross-job     -- is this team even in the caller's event
///   2. reach         -- Director and Superuser reach every team; everyone else their own
///   3. enablement    -- is chat switched on for this job at all
///
/// Enablement is enforced here as well as hidden in the client. A client-side flag is a
/// convenience, never a control.
/// </summary>
[ApiController]
[Authorize]
[Route("api/teams/{teamId:guid}/chat")]
public class TeamChatController : ControllerBase
{
    /// <summary>
    /// Page size. Generous because catch-up after a weekend is the common case and a phone
    /// paging four times to read Saturday is four round trips on a car connection.
    /// </summary>
    private const int DefaultTake = 100;
    private const int MaxTake = 200;

    /// <summary>
    /// Bytes, not characters. A chat message is not a document -- the cap exists so one paste
    /// cannot push a thread's page past what a phone will render.
    /// </summary>
    private const int MaxMessageLength = 4000;

    private readonly ITeamChatService _chat;
    private readonly IJobLookupService _jobLookupService;

    public TeamChatController(ITeamChatService chat, IJobLookupService jobLookupService)
    {
        _chat = chat;
        _jobLookupService = jobLookupService;
    }

    // ── Endpoints ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Catch-up from a cursor. <c>since</c> is a LastTouchSeq -- 0 on a first load. Read
    /// <see cref="ChatPageDto.NextCursor"/> before wiring the polling loop: the next request
    /// sends THAT, never the high-water mark.
    /// </summary>
    [HttpGet("messages")]
    [ProducesResponseType(typeof(ChatPageDto), 200)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetMessages(
        Guid teamId,
        CancellationToken ct,
        [FromQuery] long? since = null,
        [FromQuery] int take = DefaultTake)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;
        if (Caller() is not { } caller) return CallerNotResolved();

        // Clamped rather than rejected: a client asking for 5,000 wants everything, and a 400
        // teaches it nothing a clamp does not.
        take = Math.Clamp(take, 1, MaxTake);

        // OMITTING `since` IS NOT `since=0`, AND THE DEFAULT MUST STAY NULL.
        //
        // Omitted means "open this thread" -- the newest page, which is what a client wants on
        // first load. Zero means "from the beginning of time", which on a team carrying a season
        // of history opens the app on its oldest hundred messages and makes the member page
        // forward to reach today. A `long` defaulting to 0 cannot tell those apart, which is
        // exactly how this shipped wrong the first time.
        //
        // A negative `since` is a client bug, not a request for the newest page: it is clamped
        // to 0 (catch up from the start) rather than folded into null, so the mistake surfaces
        // as a long scroll rather than silently meaning something else.
        if (since is { } s) since = Math.Max(0, s);

        return Ok(await _chat.GetMessagesAsync(teamId, caller.RegId, caller.UserId, since, take, ct));
    }

    /// <summary>
    /// Scrollback -- the page of messages immediately above <paramref name="before"/> in thread
    /// order. Its own route rather than a third mode on GetMessages, because it answers a
    /// different question and must return a shape with no live cursor in it: a history page's
    /// cursor fed back as `since` would rewind the reader through the whole season.
    /// </summary>
    [HttpGet("messages/history")]
    [ProducesResponseType(typeof(ChatHistoryPageDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetHistory(
        Guid teamId,
        CancellationToken ct,
        [FromQuery] long before = 0,
        [FromQuery] int take = DefaultTake)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;

        // Resolved and discarded. Scrollback needs no identity of its own -- it moves no read
        // marker and counts nothing -- but a token that cannot name a registration has no
        // business on a team thread, and every other route on this controller says so.
        if (Caller() is null) return CallerNotResolved();

        // `before` is required here in a way `since` is not: omitting it would mean "everything
        // before the beginning", which is silently always empty. A 400 says what went wrong.
        if (before <= 0)
            return BadRequest(new { error = "before must be a positive Seq from a page you already hold" });

        take = Math.Clamp(take, 1, MaxTake);

        return Ok(await _chat.GetHistoryAsync(teamId, before, take, ct));
    }

    /// <summary>
    /// Posts a message. Replaying a ClientMessageId returns the ORIGINAL message with 200 and
    /// sends no second push -- a retry on a flaky phone connection is the normal case.
    /// </summary>
    [HttpPost("messages")]
    [Authorize(Policy = "CanAuthorTeamContent")]
    [ProducesResponseType(typeof(ChatMessageDto), 201)]
    [ProducesResponseType(typeof(ChatMessageDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> PostMessage(
        Guid teamId, [FromBody] PostChatMessageRequest request, CancellationToken ct)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;
        if (Caller() is not { } caller) return CallerNotResolved();

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(Problem400("EmptyMessage", "A message cannot be empty."));

        if (request.Text.Length > MaxMessageLength)
            return BadRequest(Problem400("MessageTooLong",
                $"A message cannot exceed {MaxMessageLength} characters."));

        if (request.ClientMessageId == Guid.Empty)
            return BadRequest(Problem400("MissingClientMessageId",
                "clientMessageId is required — it is what makes a retried send one message instead of two."));

        var result = await _chat.PostMessageAsync(teamId, caller.RegId, caller.UserId, request, ct);

        // 201 for a new row, 200 for a replay. The client reconciles its optimistic bubble by
        // clientMessageId either way; the status is what tells it whether it caused this.
        return result.Created
            ? StatusCode(StatusCodes.Status201Created, result.Message)
            : Ok(result.Message);
    }

    /// <summary>
    /// Soft-deletes a message. The author may delete their own; Staff and above may delete any.
    /// The row survives as a tombstone so phones holding it are told to drop it.
    /// </summary>
    [HttpDelete("messages/{messageId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteMessage(Guid teamId, Guid messageId, CancellationToken ct)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;
        if (Caller() is not { } caller) return CallerNotResolved();

        var outcome = await _chat.DeleteMessageAsync(teamId, messageId, caller.UserId, CanModerate(), ct);

        return outcome switch
        {
            ChatDeleteOutcome.Deleted => NoContent(),
            ChatDeleteOutcome.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = "NotYourMessage",
                Title = "Delete Denied",
                Detail = "You can only delete your own messages."
            })
        };
    }

    /// <summary>
    /// Advances this reader's marker. Per TEAM, not per job -- read/unread means nothing at the
    /// job level when there are twelve threads in it.
    /// </summary>
    [HttpPost("read")]
    [ProducesResponseType(typeof(ChatReadStateDto), 200)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> MarkRead(
        Guid teamId, [FromBody] MarkChatReadRequest request, CancellationToken ct)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;
        if (Caller() is not { } caller) return CallerNotResolved();

        return Ok(await _chat.MarkReadAsync(teamId, caller.RegId, caller.UserId, request.LastReadSeq, ct));
    }

    /// <summary>This reader's notification preferences for this team.</summary>
    [HttpGet("preferences")]
    [ProducesResponseType(typeof(ChatPreferencesDto), 200)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetPreferences(Guid teamId, CancellationToken ct)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;
        if (Caller() is not { } caller) return CallerNotResolved();

        return Ok(await _chat.GetPreferencesAsync(teamId, caller.RegId, ct));
    }

    /// <summary>
    /// Replaces this reader's preferences. A full replace rather than a patch: three of the six
    /// fields are a single window and patching half of one is how a quiet window ends up with a
    /// start and no end.
    /// </summary>
    [HttpPut("preferences")]
    [ProducesResponseType(typeof(ChatPreferencesDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> SetPreferences(
        Guid teamId, [FromBody] ChatPreferencesDto prefs, CancellationToken ct)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;
        if (Caller() is not { } caller) return CallerNotResolved();

        // Both ends or neither. A half-set window silently never fires, which reads to the user
        // as the preference being ignored.
        if (string.IsNullOrWhiteSpace(prefs.QuietStartLocal) != string.IsNullOrWhiteSpace(prefs.QuietEndLocal))
            return BadRequest(Problem400("IncompleteQuietHours",
                "Quiet hours need both a start and an end, or neither."));

        if (!IsHhMmOrEmpty(prefs.QuietStartLocal) || !IsHhMmOrEmpty(prefs.QuietEndLocal))
            return BadRequest(Problem400("InvalidQuietHours",
                "Quiet hours must be 24-hour \"HH:mm\" — for example \"22:00\"."));

        return Ok(await _chat.SetPreferencesAsync(teamId, caller.RegId, caller.UserId, prefs, ct));
    }

    // ── Gates ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The three gates, in order, as one call so no action can be written that forgets the
    /// second or the third. Order matters: reach is meaningless before we know the team is
    /// even in the caller's event.
    /// </summary>
    private async Task<ActionResult?> DenyIfNotPermitted(Guid teamId, CancellationToken ct)
    {
        if (await DenyIfCrossJob(teamId, ct) is { } crossJob) return crossJob;
        if (await DenyIfOutsideReach(teamId, ct) is { } reach) return reach;
        if (await DenyIfChatDisabled(teamId, ct) is { } disabled) return disabled;
        return null;
    }

    /// <summary>
    /// Rejects a teamId belonging to another job. Explicit and per-controller by design --
    /// nothing in this API scopes by job ambiently, so every action taking a teamId off the
    /// route says so itself. Superuser exempt, matching the sibling team controllers.
    /// </summary>
    private async Task<ActionResult?> DenyIfCrossJob(Guid teamId, CancellationToken ct)
    {
        if (User.IsInRole(RoleConstants.Names.SuperuserName)) return null;

        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var teamJobId = await _jobLookupService.GetJobIdByTeamAsync(teamId, ct);

        // Fail closed: unresolvable caller job (phase-1 token, no regId) or unknown team.
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
    /// Second gate: how far inside the job the caller reaches. Director and Superuser reach
    /// every team in it -- they moderate. Everyone else is confined to the team on their OWN
    /// registration, so a player cannot read another team's thread in their own event.
    /// </summary>
    private async Task<ActionResult?> DenyIfOutsideReach(Guid teamId, CancellationToken ct)
    {
        if (HasJobWideReach()) return null;

        var ownTeamId = await User.GetTeamIdFromRegistrationAsync(_jobLookupService, ct);

        // Fail closed: unresolvable registration, inactive registration, or unrostered caller.
        if (ownTeamId == null || ownTeamId.Value != teamId)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = "TeamReachDenied",
                Title = "Team Access Denied",
                Detail = "You can only read and post to your own team's chat."
            });

        return null;
    }

    /// <summary>
    /// Third gate: the job switch. Off by default, NULL treated as off, and enforced on every
    /// endpoint however the client is configured.
    ///
    /// 403 and not 404: the client is told to hide the Chat tab from the session payload, so
    /// anything reaching here is either a stale build or someone probing, and both deserve the
    /// same answer.
    /// </summary>
    private async Task<ActionResult?> DenyIfChatDisabled(Guid teamId, CancellationToken ct)
    {
        if (await _chat.IsEnabledAsync(teamId, ct)) return null;

        return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Type = "ChatNotEnabled",
            Title = "Chat Not Available",
            Detail = "Team chat is not turned on for this event."
        });
    }

    /// <summary>Director and Superuser reach the whole job; everyone else one team.</summary>
    private bool HasJobWideReach() =>
        User.IsInRole(RoleConstants.Names.SuperuserName)
        || User.IsInRole(RoleConstants.Names.DirectorName);

    /// <summary>
    /// chat:moderate -- Staff and above. Staff is where coaches live: there is no Coach role in
    /// this system, and a coach is a Staff registration.
    /// </summary>
    private bool CanModerate() =>
        HasJobWideReach() || User.IsInRole(RoleConstants.Names.StaffName);

    // ── Caller ──────────────────────────────────────────────────────────────────────────

    private readonly record struct ChatCaller(Guid RegId, string UserId);

    /// <summary>
    /// The caller's registration and login. Both are required -- regId keys the read marker and
    /// the preferences, userId is who authored and what the unread count excludes.
    ///
    /// ClaimTypes.NameIdentifier, not "sub": ASP.NET Core remaps the standard claim names and
    /// the raw string returns null. regId is a custom claim and is NOT remapped.
    /// </summary>
    private ChatCaller? Caller()
    {
        var regId = User.GetRegistrationId();
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return regId == null || string.IsNullOrWhiteSpace(userId)
            ? null
            : new ChatCaller(regId.Value, userId);
    }

    /// <summary>
    /// A token with no regId is a phase-1 token -- role selected, job not yet chosen. It cannot
    /// address a team thread, and saying so beats a null-reference five frames down.
    /// </summary>
    private ObjectResult CallerNotResolved() =>
        StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Type = "RegistrationNotResolved",
            Title = "Registration Required",
            Detail = "Your session is not tied to a registration in this event. Sign in again and pick your role."
        });

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    private static ProblemDetails Problem400(string type, string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Type = type,
        Title = "Invalid Request",
        Detail = detail
    };

    private static bool IsHhMmOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) || TimeOnly.TryParseExact(value, "HH\\:mm", out _);
}
