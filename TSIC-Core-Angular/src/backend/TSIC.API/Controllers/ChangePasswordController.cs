using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.ChangePassword;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// SuperUser account-repair utility. Cross-tenant by design — it is not scoped to a job.
/// See <c>docs/Domain/change-password-contract.md</c>.
///
/// ── THE AUDIT TRAIL LIVES IN SEQ ──
/// This tool changes credentials across every customer in the system and its merges are irreversible.
/// It previously kept no record at all of who did what to whom. Every mutating action below now emits
/// a Serilog event tagged <c>cp_audit=true</c>, which is the Seq query that isolates them:
///
///     cp_audit = true                       -- everything this tool has ever done
///     cp_audit = true and Outcome = 'FAILED'   -- everything it refused to do, and why
///     cp_audit = true and AuditAction like 'Merge%'
///     TargetUserName = 'jsmith'             -- everything done TO one account
///
/// There is deliberately no audit TABLE. Seq is the logging system of record here; a second one would
/// be a second thing to keep in sync.
/// </summary>
[ApiController]
[Route("api/change-password")]
[Authorize(Policy = "SuperUserOnly")]
public class ChangePasswordController : ControllerBase
{
    private readonly IChangePasswordService _service;
    private readonly ILogger<ChangePasswordController> _logger;

    public ChangePasswordController(
        IChangePasswordService service,
        ILogger<ChangePasswordController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Who is doing this. `username` is a custom claim and is NOT remapped by ASP.NET.</summary>
    private string Actor =>
        User.FindFirst("username")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "unknown";

    /// <summary>
    /// Attaches the machine-readable half of the audit event — the part that shouldn't clutter the
    /// rendered message. Mirrors the <c>boot_audit=true</c> convention already used for the
    /// startup-config audit in <c>Program.cs</c>.
    /// </summary>
    private IDisposable? AuditScope(string action) =>
        _logger.BeginScope(new Dictionary<string, object?>
        {
            ["cp_audit"] = true,
            ["AuditAction"] = action,
            ["ActorUserId"] = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            ["ClientIp"] = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

    /// <summary>
    /// The undo, as JSON: every registration the merge moved, paired with the account it moved OFF.
    ///
    /// Serialized to a STRING rather than handed to Serilog as a destructured <c>{@Moved}</c> because
    /// destructuring is subject to collection-depth limits, and a silently truncated list here is
    /// indistinguishable from a complete one — while being useless. A merge of a large household moves
    /// hundreds of rows (the Shoulbergs own 244), and that is exactly the merge someone will need to
    /// reverse. Copy this out of Seq, feed it to OPENJSON, restore PreviousUserId.
    /// </summary>
    private static string ReversalPayload(MergeResultDto result) =>
        JsonSerializer.Serialize(result.Moved);

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

    /// <summary>
    /// Reset the password on one of this REGISTRATION's two accounts — its own login, or the family
    /// login that owns it. Which one is <c>request.Target</c>; the account itself is resolved from the
    /// registration's FK, server-side.
    ///
    /// This replaces the old <c>reset-password</c> / <c>reset-family-password</c> pair, which were
    /// byte-for-byte identical, ignored <c>regId</c> entirely, and reset whatever username the body
    /// happened to name — so any SuperUser could reset any account in the system, including another
    /// SuperUser's, with an arbitrary GUID in the route.
    /// </summary>
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

        if (string.IsNullOrWhiteSpace(request.ExpectedUserName))
            return BadRequest(new { message = "ExpectedUserName is required." });

        using var scope = AuditScope("ResetPassword");

        try
        {
            var message = await _service.ResetPasswordAsync(regId, request, ct);

            // The service has already verified ExpectedUserName against the account the registration
            // resolves to, so logging it here is logging the account that actually changed.
            //
            // The password itself is never logged — not the plaintext, not the hash, not a fragment.
            // That the reset happened, by whom, to whom, is the whole fact.
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} reset the {Target} password for registration {RegistrationId} (account {TargetUserName}) — {Outcome}",
                Actor, request.Target, regId, request.ExpectedUserName, "OK");

            return Ok(new { message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} FAILED to reset the {Target} password for registration {RegistrationId} (account {TargetUserName}) — {Outcome}: {Reason}",
                Actor, request.Target, regId, request.ExpectedUserName, "FAILED", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update the registrant's own email. A BLANK email is a legitimate edit — it CLEARS the address,
    /// as legacy's grid did. Rejecting it made a stale address unremovable (`5a121a2c`).
    /// </summary>
    [HttpPut("{regId:guid}/user-email")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpdateUserEmail(
        Guid regId,
        [FromBody] UpdateUserEmailRequest request,
        CancellationToken ct)
    {
        using var scope = AuditScope("UpdateUserEmail");

        try
        {
            await _service.UpdateUserEmailAsync(regId, request.Email, ct);

            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} set the user email on registration {RegistrationId} to {Email} — {Outcome}",
                Actor, regId, string.IsNullOrWhiteSpace(request.Email) ? "(cleared)" : request.Email, "OK");

            return Ok(new { message = "Email updated successfully." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} FAILED to set the user email on registration {RegistrationId} — {Outcome}: {Reason}",
                Actor, regId, "FAILED", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update the family login's email and/or the mom/dad addresses.
    /// PATCH semantics: an OMITTED field is left alone; an EMPTY field clears the address.
    /// </summary>
    [HttpPut("{regId:guid}/family-emails")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpdateFamilyEmails(
        Guid regId,
        [FromBody] UpdateFamilyEmailsRequest request,
        CancellationToken ct)
    {
        using var scope = AuditScope("UpdateFamilyEmails");

        try
        {
            await _service.UpdateFamilyEmailsAsync(
                regId,
                request.FamilyEmail,
                request.MomEmail,
                request.DadEmail,
                ct);

            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} set family emails on registration {RegistrationId} — family={FamilyEmail} mom={MomEmail} dad={DadEmail} — {Outcome}",
                Actor, regId,
                request.FamilyEmail ?? "(unchanged)",
                request.MomEmail ?? "(unchanged)",
                request.DadEmail ?? "(unchanged)",
                "OK");

            return Ok(new { message = "Family emails updated successfully." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} FAILED to set family emails on registration {RegistrationId} — {Outcome}: {Reason}",
                Actor, regId, "FAILED", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Other logins that are the same ADULT. Empty for a player — a child has no login, and is
    /// collapsed only inside their household's merge.
    /// </summary>
    [HttpGet("{regId:guid}/merge-candidates")]
    [ProducesResponseType(typeof(MergeCandidatesResponse), 200)]
    public async Task<ActionResult<MergeCandidatesResponse>> GetMergeCandidates(
        Guid regId,
        CancellationToken ct)
    {
        var result = await _service.GetUserMergeCandidatesAsync(regId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Family logins that are the same HOUSEHOLD — the mother's email, phone and name all agree.
    /// Not keyed on the child: sharing a child means two households OVERLAP, not that they are one.
    /// </summary>
    [HttpGet("{regId:guid}/family-merge-candidates")]
    [ProducesResponseType(typeof(MergeCandidatesResponse), 200)]
    public async Task<ActionResult<MergeCandidatesResponse>> GetFamilyMergeCandidates(
        Guid regId,
        CancellationToken ct)
    {
        var result = await _service.GetFamilyMergeCandidatesAsync(regId, ct);
        return Ok(result);
    }

    /// <summary>Fold duplicate adult logins into the one the person asked for.</summary>
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

        if (request.SourceUserNames.Count == 0)
            return BadRequest(new { message = "Select at least one account to merge." });

        using var scope = AuditScope("MergeUser");

        try
        {
            var result = await _service.MergeUsernameAsync(
                regId, request.TargetUserName, request.SourceUserNames, ct);

            // Irreversible. This event is the ONLY record that it happened AND the only way back:
            // ReversalPayload carries each moved RegistrationId with the UserId it was moved off.
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} MERGED {SourceCount} account(s) [{SourceUserNames}] onto user "
                + "'{TargetUserName}' ({TargetUserId}) from registration {RegistrationId} — {MovedCount} registration(s) "
                + "re-pointed — {Outcome}. Reversal: {ReversalPayload}",
                Actor, request.SourceUserNames.Count, string.Join(", ", request.SourceUserNames),
                result.TargetUserName, result.TargetUserId, regId,
                result.Moved.Count, "OK", ReversalPayload(result));

            return Ok(new { message = $"Merged {result.Moved.Count} registration(s) to '{result.TargetUserName}'." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} FAILED to merge [{SourceUserNames}] onto '{TargetUserName}' "
                + "from registration {RegistrationId} — {Outcome}: {Reason}",
                Actor, string.Join(", ", request.SourceUserNames), request.TargetUserName, regId,
                "FAILED", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Fold duplicate family logins into the one the parent asked for — the whole reason this tool
    /// exists. Moves the registrations AND collapses each child, so the parent does not sign in to find
    /// every child listed twice.
    /// </summary>
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

        if (request.SourceUserNames.Count == 0)
            return BadRequest(new { message = "Select at least one account to merge." });

        using var scope = AuditScope("MergeFamily");

        try
        {
            var result = await _service.MergeFamilyUsernameAsync(
                regId, request.TargetUserName, request.SourceUserNames, ct);

            // The merge this tool exists for, and the one whose Reversal payload will be needed if it
            // is ever wrong. It moves children between households.
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} MERGED {SourceCount} family login(s) [{SourceUserNames}] onto "
                + "'{TargetUserName}' ({TargetUserId}) from registration {RegistrationId} — {MovedCount} registration(s) "
                + "re-pointed — {Outcome}. Reversal: {ReversalPayload}",
                Actor, request.SourceUserNames.Count, string.Join(", ", request.SourceUserNames),
                result.TargetUserName, result.TargetUserId, regId,
                result.Moved.Count, "OK", ReversalPayload(result));

            return Ok(new { message = $"Merged {result.Moved.Count} registration(s) to family '{result.TargetUserName}'." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} FAILED to merge family login(s) [{SourceUserNames}] onto "
                + "'{TargetUserName}' from registration {RegistrationId} — {Outcome}: {Reason}",
                Actor, string.Join(", ", request.SourceUserNames), request.TargetUserName, regId,
                "FAILED", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }
}
