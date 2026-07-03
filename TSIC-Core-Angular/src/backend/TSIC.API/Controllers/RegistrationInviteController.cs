using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

/// <summary>
/// Validates whether a club rep (team) or family (player) is allowed to register on
/// a target job. Player and team invites share identical logic — the only difference
/// is which pair of job flags gates the flow — so both live here behind their original
/// routes (<c>api/player-invite/validate</c>, <c>api/team-invite/validate</c>).
/// No auth required — called by the frontend guard before/after Phase 1 login.
/// </summary>
[ApiController]
[AllowAnonymous]
public class RegistrationInviteController : ControllerBase
{
    private readonly IJobRepository _jobRepo;
    private readonly IRegistrationRepository _registrationRepo;

    public RegistrationInviteController(
        IJobRepository jobRepo,
        IRegistrationRepository registrationRepo)
    {
        _jobRepo = jobRepo;
        _registrationRepo = registrationRepo;
    }

    private enum InviteKind { Player, Team }

    /// <summary>
    /// Validates a player invite. Checks BRegistrationAllowPlayer first, then BPlayerRegRequiresToken.
    /// </summary>
    [HttpGet("api/player-invite/validate")]
    public Task<ActionResult<InviteValidationResult>> ValidatePlayer(
        [FromQuery] string targetJobPath,
        [FromQuery] Guid? sourceRegId,
        [FromQuery] string? userId,
        CancellationToken ct)
        => ValidateAsync(InviteKind.Player, targetJobPath, sourceRegId, userId, ct);

    /// <summary>
    /// Validates a team invite. Checks BRegistrationAllowTeam first, then BTeamRegRequiresToken.
    /// </summary>
    [HttpGet("api/team-invite/validate")]
    public Task<ActionResult<InviteValidationResult>> ValidateTeam(
        [FromQuery] string targetJobPath,
        [FromQuery] Guid? sourceRegId,
        [FromQuery] string? userId,
        CancellationToken ct)
        => ValidateAsync(InviteKind.Team, targetJobPath, sourceRegId, userId, ct);

    private async Task<ActionResult<InviteValidationResult>> ValidateAsync(
        InviteKind kind,
        string targetJobPath,
        Guid? sourceRegId,
        string? userId,
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

        // Check 1: Is registration open at all?
        if (!registrationOpen)
            return Ok(new InviteValidationResult { Allowed = false });

        // Check 2: Does this job require a token?
        if (!requiresToken)
            return Ok(new InviteValidationResult { Allowed = true });

        // Token required — validate sourceRegId and userId
        if (sourceRegId == null || string.IsNullOrEmpty(userId))
            return Ok(new InviteValidationResult { Allowed = false });

        var sourceReg = await _registrationRepo.GetByIdAsync(sourceRegId.Value, ct);
        if (sourceReg == null)
            return Ok(new InviteValidationResult { Allowed = false });

        // UserId must match exactly
        if (sourceReg.UserId != userId)
            return Ok(new InviteValidationResult { Allowed = false });

        // Both jobs must belong to the same customer
        var sourceCustomerId = await _jobRepo.GetCustomerIdAsync(sourceReg.JobId, ct);
        var targetCustomerId = await _jobRepo.GetCustomerIdAsync(targetJobId.Value, ct);

        if (sourceCustomerId == null || targetCustomerId == null || sourceCustomerId != targetCustomerId)
            return Ok(new InviteValidationResult { Allowed = false });

        return Ok(new InviteValidationResult { Allowed = true });
    }
}

public record InviteValidationResult
{
    public required bool Allowed { get; init; }
}
