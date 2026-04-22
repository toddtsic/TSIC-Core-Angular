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

/// <summary>Inline email send response — sent/failed rollup.</summary>
public record UsLaxEmailResponse
{
    public required int Sent { get; init; }
    public required int Failed { get; init; }
    public required int MissingEmail { get; init; }
    public required IReadOnlyList<string> FailedAddresses { get; init; }
}
