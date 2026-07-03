using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services.Invites;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

/// <summary>
/// Pre-checks whether the current (Phase-1 authenticated) user may enter a token-gated registration
/// wizard, so the frontend guard can bounce early instead of letting the wizard fail. Player and team
/// share identical logic — only which pair of job flags gates the flow — so both live here behind their
/// original routes (<c>api/player-invite/validate</c>, <c>api/team-invite/validate</c>).
///
/// This is a UX pre-check ONLY. The authoritative gate is server-side at the wizard-entry chokepoints
/// (team <c>initialize-registration</c>, player <c>set-wizard-context</c>), which re-verify the same
/// signed token against the authenticated user before minting a wizard token.
/// </summary>
[ApiController]
[Authorize] // Phase-1 minimum: the invite is bound to a specific user, so we need to know who is asking.
public class RegistrationInviteController : ControllerBase
{
    private readonly IJobRepository _jobRepo;
    private readonly IInviteTokenService _inviteTokens;

    public RegistrationInviteController(
        IJobRepository jobRepo,
        IInviteTokenService inviteTokens)
    {
        _jobRepo = jobRepo;
        _inviteTokens = inviteTokens;
    }

    private enum InviteKind { Player, Team }

    /// <summary>Player invite pre-check. Gates on BRegistrationAllowPlayer, then BPlayerRegRequiresToken.</summary>
    [HttpGet("api/player-invite/validate")]
    public Task<ActionResult<InviteValidationResult>> ValidatePlayer(
        [FromQuery] string targetJobPath,
        [FromQuery] string? token,
        CancellationToken ct)
        => ValidateAsync(InviteKind.Player, targetJobPath, token, ct);

    /// <summary>Team invite pre-check. Gates on BRegistrationAllowTeam, then BTeamRegRequiresToken.</summary>
    [HttpGet("api/team-invite/validate")]
    public Task<ActionResult<InviteValidationResult>> ValidateTeam(
        [FromQuery] string targetJobPath,
        [FromQuery] string? token,
        CancellationToken ct)
        => ValidateAsync(InviteKind.Team, targetJobPath, token, ct);

    private async Task<ActionResult<InviteValidationResult>> ValidateAsync(
        InviteKind kind,
        string targetJobPath,
        string? token,
        CancellationToken ct)
    {
        var targetJobId = await _jobRepo.GetJobIdByPathAsync(targetJobPath, ct);
        if (!targetJobId.HasValue)
            return Ok(new InviteValidationResult { Allowed = false });

        var status = await _jobRepo.GetRegistrationStatusAsync(targetJobId.Value, ct);
        if (status == null)
            return Ok(new InviteValidationResult { Allowed = false });

        var registrationOpen = kind == InviteKind.Player
            ? status.BRegistrationAllowPlayer
            : status.BRegistrationAllowTeam;
        var requiresToken = kind == InviteKind.Player
            ? status.BPlayerRegRequiresToken
            : status.BTeamRegRequiresToken;

        // Registration must be open at all.
        if (!registrationOpen)
            return Ok(new InviteValidationResult { Allowed = false });

        // Open enrollment (no token required) — anyone authenticated may proceed.
        if (!requiresToken)
            return Ok(new InviteValidationResult { Allowed = true });

        // Token-gated: the signed invite must be valid for exactly this job AND this authenticated user.
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Ok(new InviteValidationResult { Allowed = false });

        var allowed = _inviteTokens.IsValidFor(token, targetJobId.Value, userId);
        return Ok(new InviteValidationResult { Allowed = allowed });
    }
}

public record InviteValidationResult
{
    public required bool Allowed { get; init; }
}
