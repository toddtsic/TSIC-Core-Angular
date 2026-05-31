namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Family accounting view — all data the shared family-payment component needs to render
/// a combined ledger across every player a parent registered for the job. The parent-side
/// analog of <c>ClubRepAccountingDto</c>: where the club rep groups one registration's teams
/// by TeamId, the family groups N sibling registrations (keyed by JobId + FamilyUserId) by
/// the owning child. AccountingRecords are the children's records merged and stamped with
/// their owning player (OwnerRegistrationId / OwnerName) for per-row attribution.
/// </summary>
public record FamilyAccountingDto
{
    /// <summary>The registration the panel was opened on (the in-scope "this player").</summary>
    public required Guid AnchorRegistrationId { get; init; }
    public required string FamilyName { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required List<FamilyPlayerAccountingDto> Players { get; init; }
    public required List<AccountingRecordDto> AccountingRecords { get; init; }
}

/// <summary>
/// Per-child summary within a family — the analog of a single team row in the club-rep grid.
/// </summary>
public record FamilyPlayerAccountingDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required bool Active { get; init; }
}
