namespace TSIC.Contracts.Dtos.EmailTroubleshooter;

/// <summary>
/// Request carrying one or more email addresses to act on. The frontend splits the
/// semicolon-separated input into this list; the backend treats each address independently.
/// </summary>
public record EmailListRequest
{
    public required IReadOnlyList<string> Emails { get; init; }
}

/// <summary>
/// Status of a single address against the Amazon SES account-level suppression list.
/// Status: "NotSuppressed" | "Suppressed" | "Unknown".
/// </summary>
public record SuppressionEntryDto
{
    public required string Email { get; init; }
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public DateTime? LastUpdate { get; init; }
}

/// <summary>
/// Outcome of removing a single address from the SES suppression list.
/// </summary>
public record SuppressionRemoveResultDto
{
    public required string Email { get; init; }
    public required bool Removed { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Result of investigating a single address: suppression status + recipient-domain DNS check +
/// forced test send + a plain-English conclusion naming the responsible side.
/// SuppressionStatus: "NotSuppressed" | "Suppressed" | "Unknown".
/// DomainStatus: "Accepts" | "NoMailExchanger" | "Unknown".
/// Side: "Sending" | "Address" | "Recipient" | "Inconclusive".
/// </summary>
public record EmailInvestigateResultDto
{
    public required string Email { get; init; }
    public required string SuppressionStatus { get; init; }
    public string? SuppressionReason { get; init; }

    /// <summary>
    /// Whether the recipient DOMAIN can receive mail at all, per DNS. "NoMailExchanger" is the case
    /// SES cannot report at send time and that no syntax check can catch - a typo like "a.com".
    /// </summary>
    public required string DomainStatus { get; init; }

    public required bool SendAccepted { get; init; }
    public required string Side { get; init; }
    public required string Conclusion { get; init; }
    public string? Error { get; init; }
}
