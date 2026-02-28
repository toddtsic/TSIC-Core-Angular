using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.ChangePassword;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/change-password")]
[Authorize(Policy = "SuperUserOnly")]
public class ChangePasswordController : ControllerBase
{
    private readonly IChangePasswordService _service;

    public ChangePasswordController(IChangePasswordService service)
    {
        _service = service;
    }

    [HttpGet("role-options")]
    [ProducesResponseType(typeof(List<ChangePasswordRoleOptionDto>), 200)]
    public async Task<ActionResult<List<ChangePasswordRoleOptionDto>>> GetRoleOptions(CancellationToken ct)
    {
        var options = await _service.GetRoleOptionsAsync(ct);
        return Ok(options);
    }

    [HttpPost("search")]
    [ProducesResponseType(typeof(List<ChangePasswordSearchResultDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<List<ChangePasswordSearchResultDto>>> Search(
        [FromBody] ChangePasswordSearchRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RoleId))
            return BadRequest(new { message = "RoleId is required." });

        var results = await _service.SearchAsync(request, ct);
        return Ok(results);
    }

    [HttpPost("{regId:guid}/reset-password")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ResetPassword(
        Guid regId,
        [FromBody] AdminResetPasswordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        try
        {
            var message = await _service.ResetPasswordAsync(request.UserName, request.NewPassword, ct);
            return Ok(new { message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{regId:guid}/reset-family-password")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ResetFamilyPassword(
        Guid regId,
        [FromBody] AdminResetPasswordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        try
        {
            var message = await _service.ResetPasswordAsync(request.UserName, request.NewPassword, ct);
            return Ok(new { message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{regId:guid}/user-email")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpdateUserEmail(
        Guid regId,
        [FromBody] UpdateUserEmailRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required." });

        try
        {
            await _service.UpdateUserEmailAsync(regId, request.Email.Trim(), ct);
            return Ok(new { message = "Email updated successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{regId:guid}/family-emails")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpdateFamilyEmails(
        Guid regId,
        [FromBody] UpdateFamilyEmailsRequest request,
        CancellationToken ct)
    {
        try
        {
            await _service.UpdateFamilyEmailsAsync(
                regId,
                request.FamilyEmail?.Trim(),
                request.MomEmail?.Trim(),
                request.DadEmail?.Trim(),
                ct);
            return Ok(new { message = "Family emails updated successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{regId:guid}/merge-candidates")]
    [ProducesResponseType(typeof(List<MergeCandidateDto>), 200)]
    public async Task<ActionResult<List<MergeCandidateDto>>> GetMergeCandidates(
        Guid regId,
        CancellationToken ct)
    {
        var candidates = await _service.GetUserMergeCandidatesAsync(regId, ct);
        return Ok(candidates);
    }

    [HttpGet("{regId:guid}/family-merge-candidates")]
    [ProducesResponseType(typeof(List<MergeCandidateDto>), 200)]
    public async Task<ActionResult<List<MergeCandidateDto>>> GetFamilyMergeCandidates(
        Guid regId,
        CancellationToken ct)
    {
        var candidates = await _service.GetFamilyMergeCandidatesAsync(regId, ct);
        return Ok(candidates);
    }

    [HttpPost("{regId:guid}/merge-username")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> MergeUsername(
        Guid regId,
        [FromBody] MergeUsernameRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TargetUserName))
            return BadRequest(new { message = "Target username is required." });

        try
        {
            var count = await _service.MergeUsernameAsync(regId, request.TargetUserName, ct);
            return Ok(new { message = $"Merged {count} registration(s) to '{request.TargetUserName}'." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{regId:guid}/merge-family-username")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> MergeFamilyUsername(
        Guid regId,
        [FromBody] MergeUsernameRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TargetUserName))
            return BadRequest(new { message = "Target family username is required." });

        try
        {
            var count = await _service.MergeFamilyUsernameAsync(regId, request.TargetUserName, ct);
            return Ok(new { message = $"Merged {count} registration(s) to family '{request.TargetUserName}'." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
