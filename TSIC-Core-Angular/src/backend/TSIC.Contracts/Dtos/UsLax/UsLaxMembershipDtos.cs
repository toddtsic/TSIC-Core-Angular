namespace TSIC.Contracts.Dtos.UsLax;

/// <summary>Audience scope for USLax membership reconciliation.</summary>
public enum UsLaxMembershipRole
{
    Player = 0,
    Coach = 1
}

/// <summary>Pre-ping candidate row — what the job has on file before reconciliation runs.</summary>
public record UsLaxReconciliationCandidateDto
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Email { get; init; }
    public DateTime? Dob { get; init; }
    public required string MembershipId { get; init; }
    public DateTime? CurrentExpiryDate { get; init; }
    public string? TeamName { get; init; }
}

/// <summary>
/// Per-row reconciliation result. Captures what USA Lacrosse returned and whether the
/// on-file expiry date was updated. Mirrors the status grid the legacy page displayed.
/// </summary>
public record UsLaxReconciliationRowDto
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Email { get; init; }
    public required string MembershipId { get; init; }
    public string? TeamName { get; init; }

    /// <summary>HTTP-level outcome of the USALax ping. 200 = success, 500 = API error, 0 = network/parse failure.</summary>
    public required int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>"Active" / "Inactive" / null when API did not return member output.</summary>
    public string? MemStatus { get; init; }
    public string? AgeVerified { get; init; }

    /// <summary>Raw involvement list from USALax (e.g. ["Player", "Coach"]).</summary>
    public IReadOnlyList<string>? Involvement { get; init; }

    public DateTime? PreviousExpiryDate { get; init; }
    public DateTime? NewExpiryDate { get; init; }

    /// <summary>True when this reconciliation wrote a new SportAssnIdexpDate to the registration.</summary>
    public required bool ExpiryDateUpdated { get; init; }

    /// <summary>
    /// Verdict from <c>UsLaxEligibilityPolicy</c> — the SAME policy the registration wizard runs,
    /// so the reconcile reports what the front door would actually decide rather than the weaker
    /// involvement-only check it used to. Reporting only: the expiry write is unaffected.
    /// </summary>
    public required bool Eligible { get; init; }

    /// <summary>Machine-readable reason code (<c>UsLaxEligibilityReason</c>) behind <see cref="Eligible"/>.</summary>
    public required string EligibilityReason { get; init; }

    /// <summary>
    /// One plain-English line explaining the verdict, with the actual dates/values in it, for the
    /// grid's Details column. Null when the member is eligible and there is nothing to explain.
    /// </summary>
    public string? EligibilityDetail { get; init; }
}

/// <summary>Batch reconciliation request. Empty list = reconcile every eligible candidate.</summary>
public record UsLaxReconciliationRequest
{
    public List<Guid>? RegistrationIds { get; init; }

    /// <summary>Which audience to reconcile. Defaults to Player to preserve original behavior.</summary>
    public UsLaxMembershipRole Role { get; init; } = UsLaxMembershipRole.Player;
}

/// <summary>Batch reconciliation response. Rollup + per-row details.</summary>
public record UsLaxReconciliationResponse
{
    public required int TotalPinged { get; init; }
    public required int DatesUpdated { get; init; }
    public required int Failed { get; init; }
    public required IReadOnlyList<UsLaxReconciliationRowDto> Rows { get; init; }
}

/// <summary>
/// Per-recipient snapshot used by the inline USLax email send. The caller (admin UI)
/// forwards the reconciliation row data so the server can substitute row-level tokens
/// (<c>!PLAYER</c>, <c>!USLAXMEMBERID</c>, <c>!USLAXEXPIRY</c>, etc.) without a second
/// USA Lacrosse ping. Matches the legacy USLaxMembershipController email flow.
/// </summary>
public record UsLaxEmailRecipientDto
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Email { get; init; }
    public DateTime? Dob { get; init; }
    public required string MembershipId { get; init; }
    public string? MemStatus { get; init; }
    public string? AgeVerified { get; init; }
    public DateTime? ExpiryDate { get; init; }
}

/// <summary>Inline email send request — subject + body template plus the recipient snapshots.</summary>
public record UsLaxEmailRequest
{
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public required List<UsLaxEmailRecipientDto> Recipients { get; init; }
}

/// <summary>
/// Sandbox-only test send for the USLax compose: renders subject/body tokens against one
/// recipient snapshot and delivers the result to a single test inbox.
/// </summary>
public record UsLaxTestSendRequest
{
    public required string Subject { get; init; }
    public required string Body { get; init; }
    /// <summary>The recipient whose row data renders the per-recipient USLax tokens.</summary>
    public required UsLaxEmailRecipientDto Recipient { get; init; }
    public required string TestRecipient { get; init; }
}

/// <summary>
/// Inline email send START response. The send now runs as a background batch on the shared engine
/// (opt-out suppression, footer, retry, rate-limit); the caller polls
/// <c>uslax-membership/email/{batchJobId}/status</c> for sent/failed. The skip rollup below is all
/// known up front (pure checks on the recipient snapshot) and returned immediately.
/// </summary>
public record UsLaxEmailStartResponse
{
    /// <summary>Background batch id to poll for progress + final sent/failed.</summary>
    public required Guid BatchJobId { get; init; }

    /// <summary>Count actually queued for sending (action-needed, has email, not opted out applied downstream).</summary>
    public required int TotalRecipients { get; init; }

    /// <summary>Selected recipients dropped for having no email on file.</summary>
    public required int MissingEmail { get; init; }

    /// <summary>
    /// Recipients evaluated as already in good standing for the job (Active + expiry past the job's
    /// valid-through date) and therefore not emailed — prevents false-alarm messages to valid members.
    /// </summary>
    public required int SkippedHealthy { get; init; }

    public required IReadOnlyList<string> SkippedNames { get; init; }
}

/// <summary>
/// Verdict returned by the live registration-form check (GET /api/validation/uslax).
///
/// Deliberately carries NO member fields. The endpoint previously proxied USA Lacrosse's raw
/// response to the browser, which handed any anonymous caller a stranger's name, DOB, email and
/// postal code for any membership number they could guess — and left the accept/reject decision
/// in JavaScript the registrant controls.
/// </summary>
public record UsLaxValidationResultDto
{
    public required bool Valid { get; init; }

    /// <summary>Why it failed, for our logs and for the wizard's own messaging. Never a member detail.</summary>
    public required string Reason { get; init; }

    /// <summary>Text to show the registrant; null when valid. HTML for the actionable cases —
    /// the wizard already renders an HTML field error through its details popup.</summary>
    public string? Message { get; init; }
}
