namespace TSIC.Contracts.Dtos;

/// <summary>
/// One job admin charge, any job — the EF replacement for the data behind the
/// <c>reporting.JobAdminFeesAll</c> proc.
/// </summary>
public record JobAdminFeeAllRowDto
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required DateTime CreateDate { get; init; }
    public string? JobName { get; init; }
    public string? ChargeTypeName { get; init; }
    public required decimal ChargeAmount { get; init; }
    public string? Comment { get; init; }
}

/// <summary>
/// One bulletin with its job's name — the EF replacement for the data behind the
/// <c>utility.ExpiringBulletins</c> proc.
/// </summary>
public record BulletinExpiryRowDto
{
    public string? JobName { get; init; }
    public string? Title { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public required bool Active { get; init; }
}

/// <summary>
/// An active registration bulletin that has ended on a job still open for that registration —
/// the EF replacement for the data behind the <c>utility.PlayerRegistrationBulletinsQA</c> and
/// <c>utility.TeamRegistrationBulletinsQA</c> procs.
/// </summary>
public record RegistrationBulletinQaRowDto
{
    public string? JobName { get; init; }
    public bool? RegistrationAllowed { get; init; }
    public required DateTime ExpiryUsers { get; init; }
    public DateTime? UslaxNumberValidThroughDate { get; init; }
    public required bool Active { get; init; }
    public string? Title { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}

/// <summary>
/// A job and the date of its first active accounting record — the EF replacement for the data
/// behind the <c>reporting.NewTsicJobsWithTxs</c> proc.
/// </summary>
public record JobFirstTransactionDto
{
    public required Guid JobId { get; init; }
    public string? JobName { get; init; }
    public DateTime? FirstTransactionDate { get; init; }
}

/// <summary>
/// One club rep's contact details for one club and sport, with the number of active club rep
/// registrations behind it — the EF replacement for the data behind the
/// <c>reporting.ClubRepContacts-All</c> proc.
/// </summary>
public record ClubRepContactAllRowDto
{
    public string? ClubName { get; init; }
    public string? SportName { get; init; }
    public string? UserName { get; init; }
    public required int JobCount { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}
