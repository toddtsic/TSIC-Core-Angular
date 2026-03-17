using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/team-invite")]
[AllowAnonymous]
public class TeamInviteController : ControllerBase
{
    private readonly IJobRepository _jobRepo;
    private readonly IRegistrationRepository _registrationRepo;

    public TeamInviteController(
        IJobRepository jobRepo,
        IRegistrationRepository registrationRepo)
    {
        _jobRepo = jobRepo;
        _registrationRepo = registrationRepo;
    }

    /// <summary>
    /// Validates whether a club rep is allowed to register teams on the target job.
    /// Checks BRegistrationAllowTeam first, then BTeamRegRequiresToken.
    /// No auth required — called by the frontend guard before/after Phase 1 login.
    /// </summary>
    [HttpGet("validate")]
    public async Task<ActionResult<TeamInviteValidationResult>> Validate(
        [FromQuery] string targetJobPath,
        [FromQuery] Guid? sourceRegId,
        [FromQuery] string? userId,
        CancellationToken ct)
    {
        var targetJobId = await _jobRepo.GetJobIdByPathAsync(targetJobPath, ct);
        if (!targetJobId.HasValue)
            return Ok(new TeamInviteValidationResult { Allowed = false });

        var status = await _jobRepo.GetRegistrationStatusAsync(targetJobId.Value, ct);

        if (status == null)
            return Ok(new TeamInviteValidationResult { Allowed = false });

        // Check 1: Is team registration open at all?
        if (!status.BRegistrationAllowTeam)
            return Ok(new TeamInviteValidationResult { Allowed = false });

        // Check 2: Does this job require a token?
        if (!status.BTeamRegRequiresToken)
            return Ok(new TeamInviteValidationResult { Allowed = true });

        // Token required — validate sourceRegId and userId
        if (sourceRegId == null || string.IsNullOrEmpty(userId))
            return Ok(new TeamInviteValidationResult { Allowed = false });

        var sourceReg = await _registrationRepo.GetByIdAsync(sourceRegId.Value, ct);
        if (sourceReg == null)
            return Ok(new TeamInviteValidationResult { Allowed = false });

        // UserId must match exactly
        if (sourceReg.UserId != userId)
            return Ok(new TeamInviteValidationResult { Allowed = false });

        // Both jobs must belong to the same customer
        var sourceCustomerId = await _jobRepo.GetCustomerIdAsync(sourceReg.JobId, ct);
        var targetCustomerId = await _jobRepo.GetCustomerIdAsync(targetJobId.Value, ct);

        if (sourceCustomerId == null || targetCustomerId == null || sourceCustomerId != targetCustomerId)
            return Ok(new TeamInviteValidationResult { Allowed = false });

        return Ok(new TeamInviteValidationResult { Allowed = true });
    }
}

public record TeamInviteValidationResult
{
    public required bool Allowed { get; init; }
}
