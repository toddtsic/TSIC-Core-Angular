namespace TSIC.Contracts.Dtos.ThirdPartyAccess;

/// <summary>
/// "3rd Party Data Access" console (SU + SuperDirector): self-service management of the
/// vendor export logins (ApiAuthorized role) across the caller's customer group. One
/// screen — every job whose users window is still open, each carrying at most one vendor login.
/// </summary>
public record ThirdPartyAccessOverviewDto
{
    public required string CustomerName { get; init; }

    /// <summary>
    /// Union of every row's <see cref="ThirdPartyJobRowDto.Vendors"/>. Reuse-only by design:
    /// first-time vendor minting happens on the Administrators page, never here — an empty
    /// list means the screen shows its no-history explanation instead of a picker.
    /// </summary>
    public required List<ThirdPartyVendorDto> Vendors { get; init; }

    /// <summary>The customer group's jobs with an open users window (ExpiryUsers in the future).</summary>
    public required List<ThirdPartyJobRowDto> Jobs { get; init; }
}

public record ThirdPartyVendorDto
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>A vendor account's ApiAuthorized history with one customer (repository shape).</summary>
public record ThirdPartyCustomerVendorDto
{
    public required Guid CustomerId { get; init; }
    public required ThirdPartyVendorDto Vendor { get; init; }
}

public record ThirdPartyJobRowDto
{
    public required Guid JobId { get; init; }
    public required Guid CustomerId { get; init; }
    public required string JobName { get; init; }
    public required DateTime ExpiryUsers { get; init; }

    /// <summary>
    /// The only accounts grantable on this job: vendor history with the job's OWN customer
    /// (Todd 10-14) — the group widens which jobs are listed, never who may be granted.
    /// </summary>
    public required List<ThirdPartyVendorDto> Vendors { get; init; }

    /// <summary>The job's vendor login (0 or 1 by invariant); null = never assigned.</summary>
    public ThirdPartyAssignmentDto? Assignment { get; init; }
}

public record ThirdPartyAssignmentDto
{
    public required Guid RegistrationId { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsActive { get; init; }
}

public record GrantThirdPartyAccessRequest
{
    /// <summary>Must be a user from the customer's vendor history (reuse-only rule).</summary>
    public required string UserId { get; init; }
}
