using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Family accounting view — all data the shared family-payment component needs to render
/// a combined ledger across every player a parent registered for the job. The parent-side
/// analog of <c>ClubRepAccountingDto</c>: where the club rep groups one registration's teams
/// by TeamId, the family groups N sibling registrations (keyed by JobId + FamilyUserId) by
/// the owning child. <see cref="Players"/> carries the same rich per-row financial shape
/// (<see cref="RegisteredTeamDto"/>) the club-rep grid uses, so the family grid renders the
/// identical Syncfusion table (per-method owed / proc-fee / discount columns). AccountingRecords
/// are the children's records merged and stamped with their owning player (OwnerRegistrationId /
/// OwnerName) for per-row attribution.
/// </summary>
public record FamilyAccountingDto
{
    /// <summary>The registration the panel was opened on (the in-scope "this player").</summary>
    public required Guid AnchorRegistrationId { get; init; }
    public required string FamilyName { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }

    /// <summary>
    /// One row per child, shaped as <see cref="RegisteredTeamDto"/> so the family grid reuses
    /// app-registered-teams-grid. Per child: TeamId = the child's RegistrationId (also the
    /// ledger group key, matching each record's OwnerRegistrationId), TeamName = player name.
    /// </summary>
    public required List<RegisteredTeamDto> Players { get; init; }
    public required List<AccountingRecordDto> AccountingRecords { get; init; }
}

/// <summary>
/// Raw per-child fee data used by the service to shape a <see cref="RegisteredTeamDto"/> row
/// (the per-method owed math runs through PaymentState in the service layer). Internal to the
/// family-accounting query — not exposed directly on any contract.
/// </summary>
public record FamilyPlayerAccountingDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required bool Active { get; init; }
    public required DateTime RegistrationTs { get; init; }
    // Assigned team + its agegroup — used to resolve the player's Deposit/BalanceDue from the
    // fee cascade (null when the player isn't on a team, so no deposit can be resolved).
    public Guid? AssignedTeamId { get; init; }
    public Guid? AgeGroupId { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeLatefee { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
}
