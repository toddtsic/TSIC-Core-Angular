using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-invite")]
[AllowAnonymous]
public class PlayerInviteController : ControllerBase
{
    private readonly IJobRepository _jobRepo;
    private readonly IRegistrationRepository _registrationRepo;

    public PlayerInviteController(
        IJobRepository jobRepo,
        IRegistrationRepository registrationRepo)
    {
        _jobRepo = jobRepo;
        _registrationRepo = registrationRepo;
    }

    /// <summary>
    /// Validates whether a player is allowed to register on the target job.
    /// Checks BRegistrationAllowPlayer first, then BPlayerRegRequiresToken.
    /// No auth required — called by the frontend guard before/after Phase 1 login.
    /// </summary>
    [HttpGet("validate")]
    public async Task<ActionResult<PlayerInviteValidationResult>> Validate(
        [FromQuery] string targetJobPath,
        [FromQuery] Guid? sourceRegId,
        [FromQuery] string? userId,
        CancellationToken ct)
    {
        var targetJobId = await _jobRepo.GetJobIdByPathAsync(targetJobPath, ct);
        if (!targetJobId.HasValue)
            return Ok(new PlayerInviteValidationResult { Allowed = false });

        var status = await _jobRepo.GetRegistrationStatusAsync(targetJobId.Value, ct);

        if (status == null)
            return Ok(new PlayerInviteValidationResult { Allowed = false });

        // Check 1: Is player registration open at all?
        if (!status.BRegistrationAllowPlayer)
            return Ok(new PlayerInviteValidationResult { Allowed = false });

        // Check 2: Does this job require a token?
        if (!status.BPlayerRegRequiresToken)
            return Ok(new PlayerInviteValidationResult { Allowed = true });

        // Token required — validate sourceRegId and userId
        if (sourceRegId == null || string.IsNullOrEmpty(userId))
            return Ok(new PlayerInviteValidationResult { Allowed = false });

        var sourceReg = await _registrationRepo.GetByIdAsync(sourceRegId.Value, ct);
        if (sourceReg == null)
            return Ok(new PlayerInviteValidationResult { Allowed = false });

        // UserId must match exactly
        if (sourceReg.UserId != userId)
            return Ok(new PlayerInviteValidationResult { Allowed = false });

        // Both jobs must belong to the same customer
        var sourceCustomerId = await _jobRepo.GetCustomerIdAsync(sourceReg.JobId, ct);
        var targetCustomerId = await _jobRepo.GetCustomerIdAsync(targetJobId.Value, ct);

        if (sourceCustomerId == null || targetCustomerId == null || sourceCustomerId != targetCustomerId)
            return Ok(new PlayerInviteValidationResult { Allowed = false });

        return Ok(new PlayerInviteValidationResult { Allowed = true });
    }
}

public record PlayerInviteValidationResult
{
    public required bool Allowed { get; init; }
}
