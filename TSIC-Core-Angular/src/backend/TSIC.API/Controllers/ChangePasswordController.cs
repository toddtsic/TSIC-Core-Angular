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
    /// What the reset dialog shows BEFORE anyone types a password: the account this registration resolves
    /// to, whose login it is, and what it signs in for.
    ///
    /// For a player row that account is the FAMILY's — the child has no usable login — and "signs in for"
    /// lists the children. That line is the whole reason this is a server call and not a grid cell.
    /// </summary>
    [HttpGet("{regId:guid}/reset-context")]
    [ProducesResponseType(typeof(ResetContextDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ResetContextDto>> GetResetContext(
        Guid regId,
        [FromQuery] ResetPasswordTarget target,
        CancellationToken ct)
    {
        var context = await _service.GetResetContextAsync(regId, target, ct);

        if (context is null)
        {
            return NotFound(new
            {
                message = target == ResetPasswordTarget.Family
                    ? "This registration has no family login."
                    : "This registration has no user account."
            });
        }

        return Ok(context);
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
            _logger.LogWarning(ex,
                "CHANGE-PASSWORD AUDIT: {Actor} FAILED to reset the {Target} password for registration {RegistrationId} (account {TargetUserName}) — {Outcome}: {Reason}",
                Actor, request.Target, regId, request.ExpectedUserName, "FAILED", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an ADULT registrant's own email and/or phone — an adult IS their own account.
    /// PATCH semantics: an OMITTED field is left alone; an EMPTY field CLEARS it, which is a legitimate
    /// edit that legacy's grid allowed and rejecting it made a stale address unremovable (`5a121a2c`).
    /// </summary>
    [HttpPut("{regId:guid}/user-contact")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpdateUserContact(
        Guid regId,
        [FromBody] UpdateUserContactRequest request,
        CancellationToken ct)
    {
        using var scope = AuditScope("UpdateUserContact");

        try
        {
            await _service.UpdateUserContactAsync(regId, request.Email, request.Cellphone, ct);

            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} set the user contact on registration {RegistrationId} — email={Email} phone={Cellphone} — {Outcome}",
                Actor, regId, Audit(request.Email), Audit(request.Cellphone), "OK");

            return Ok(new { message = "Contact details updated successfully." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "CHANGE-PASSWORD AUDIT: {Actor} FAILED to set the user contact on registration {RegistrationId} — {Outcome}: {Reason}",
                Actor, regId, "FAILED", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update a PLAYER's household contacts — the mother and father, email and phone, on the
    /// <c>Families</c> row. PATCH semantics: an OMITTED field is left alone; an EMPTY field clears it.
    ///
    /// There is no family-login field, by design. The family login IS the mother, so the server brings
    /// its <c>AspNetUsers</c> row to parity with her rather than letting an admin type into it separately
    /// and drift the two apart. See <c>UpdateFamilyContactsAsync</c>.
    /// </summary>
    [HttpPut("{regId:guid}/family-contacts")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpdateFamilyContacts(
        Guid regId,
        [FromBody] UpdateFamilyContactsRequest request,
        CancellationToken ct)
    {
        using var scope = AuditScope("UpdateFamilyContacts");

        try
        {
            await _service.UpdateFamilyContactsAsync(
                regId,
                request.MomEmail,
                request.MomCellphone,
                request.DadEmail,
                request.DadCellphone,
                ct);

            // Mom's line is the one that also moved the login, so the audit says so: a reader six months
            // from now must be able to see WHY the family's password-reset address changed.
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} set household contacts on registration {RegistrationId} — "
                + "mom.email={MomEmail} mom.phone={MomCellphone} dad.email={DadEmail} dad.phone={DadCellphone}; "
                + "family login mirrored on mom = {Mirrored} — {Outcome}",
                Actor, regId,
                Audit(request.MomEmail), Audit(request.MomCellphone),
                Audit(request.DadEmail), Audit(request.DadCellphone),
                request.MomEmail != null || request.MomCellphone != null,
                "OK");

            return Ok(new { message = "Contact details updated successfully." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "CHANGE-PASSWORD AUDIT: {Actor} FAILED to set household contacts on registration {RegistrationId} — {Outcome}: {Reason}",
                Actor, regId, "FAILED", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>PATCH semantics, rendered for the audit log: null is not the same event as "".</summary>
    private static string Audit(string? value) => value switch
    {
        null => "(unchanged)",
        "" => "(cleared)",
        _ => value
    };

    /// <summary>
    /// Adult logins that are the same person IN THE SAME ROLE. Empty for a player — a child has no
    /// login, and is collapsed only inside their household's merge.
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

    /// <summary>
    /// Retire ONE duplicate adult login onto the one the person asked for. Only their registrations in
    /// this registration's role move — a Club Rep never merges with their own Staff login.
    /// </summary>
    [HttpPost("{regId:guid}/merge-username")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> MergeUsername(
        Guid regId,
        [FromBody] MergeUsernameRequest request,
        CancellationToken ct)
        => await Merge(regId, request, isFamily: false, ct);

    /// <summary>
    /// Retire ONE duplicate family login onto the one the parent asked for — the whole reason this tool
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
        => await Merge(regId, request, isFamily: true, ct);

    /// <summary>
    /// Both merges, one body. They differ only in which FK moves and whether children come with it —
    /// the validation, the audit line and the reversal payload are the same obligation, and writing them
    /// twice is how the two halves of this tool drifted apart in the first place.
    /// </summary>
    private async Task<IActionResult> Merge(
        Guid regId,
        MergeUsernameRequest request,
        bool isFamily,
        CancellationToken ct)
    {
        var what = isFamily ? "family login" : "login";

        if (string.IsNullOrWhiteSpace(request.KeepUserName))
            return BadRequest(new { message = $"The {what} to keep is required." });

        if (string.IsNullOrWhiteSpace(request.RetireUserName))
            return BadRequest(new { message = $"The {what} to retire is required." });

        using var scope = AuditScope(isFamily ? "MergeFamily" : "MergeUser");

        try
        {
            var result = isFamily
                ? await _service.MergeFamilyUsernameAsync(regId, request.KeepUserName, request.RetireUserName, ct)
                : await _service.MergeUsernameAsync(regId, request.KeepUserName, request.RetireUserName, ct);

            // Irreversible. This event is the ONLY record that it happened AND the only way back:
            // ReversalPayload carries each moved RegistrationId with the account it was moved off.
            //
            // The retiree is logged as {TargetUserName} — the SAME property the reset events use — so
            // that `TargetUserName = 'jsmith'` in Seq still means "everything ever done TO this
            // account". A merge-only property here would make that query silently miss the single most
            // destructive thing this tool does. {KeepUserName} answers the other direction: what landed
            // ON an account.
            _logger.LogWarning(
                "CHANGE-PASSWORD AUDIT: {Actor} RETIRED {What} '{TargetUserName}' onto '{KeepUserName}' "
                + "({KeepUserId}) from registration {RegistrationId} — {MovedCount} registration(s) re-pointed, "
                + "{ChildrenCollapsed} child(ren) collapsed — {Outcome}. Reversal: {ReversalPayload}",
                Actor, what, result.RetireUserName, result.KeepUserName, result.KeepUserId, regId,
                result.Moved.Count, result.ChildrenCollapsed, "OK", ReversalPayload(result));

            var children = result.ChildrenCollapsed > 0
                ? $", and merged {result.ChildrenCollapsed} child record(s)"
                : "";

            return Ok(new
            {
                message = $"Retired '{result.RetireUserName}' — moved {result.Moved.Count} registration(s) "
                          + $"to '{result.KeepUserName}'{children}."
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "CHANGE-PASSWORD AUDIT: {Actor} FAILED to retire {What} '{TargetUserName}' onto "
                + "'{KeepUserName}' from registration {RegistrationId} — {Outcome}: {Reason}",
                Actor, what, request.RetireUserName, request.KeepUserName, regId, "FAILED", ex.Message);

            return BadRequest(new { message = ex.Message });
        }
    }
}
