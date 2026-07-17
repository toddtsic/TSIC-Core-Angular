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

    /// <summary>
    /// Stored ARB snapshot (Registrations.AdnSubscription* columns) for every registration in
    /// the sibling set that carries one — ARB mints ONE Authorize.Net subscription per
    /// REGISTRATION, so several players (or several events of one player) can each have their
    /// own. Keyed by RegistrationId (= Players[].TeamId) so the subscription card follows the
    /// component's player selector. Stored snapshots only: the live ADN read is Production-only
    /// and fetched per card on demand, exactly like the detail panel's anchor card.
    /// </summary>
    public required List<FamilyPlayerSubscriptionDto> Subscriptions { get; init; }

    /// <summary>
    /// Jobs.PaymentMethodsAllowedCode (1=CC only, 2=CC or Check, 3=Check only). Lets the grid
    /// drop the "Check Owed" column when this job takes credit cards only — an amount that can
    /// never be tendered by check is noise. See <see cref="TSIC.Contracts.Constants.PaymentMethodConstants"/>.
    /// </summary>
    public int PaymentMethodsAllowedCode { get; init; }
}

/// <summary>
/// A registration's stored ARB snapshot, keyed so the family-payment scope selector can show
/// the right card(s) for the viewed player. See <see cref="FamilyAccountingDto.Subscriptions"/>.
/// </summary>
public record FamilyPlayerSubscriptionDto
{
    public required Guid RegistrationId { get; init; }
    public required SubscriptionDetailDto Subscription { get; init; }
}

/// <summary>
/// Raw per-child registration row consumed by RegisteredPlayerShaper — the player analog of
/// <c>RegisteredTeamInfo</c>. The shaper turns these into <see cref="RegisteredTeamDto"/> rows
/// through the canonical payment-state path (IPaymentStateService + IFeeResolutionService),
/// exactly like the team shaper.
/// </summary>
public record RegisteredPlayerInfo : TSIC.Contracts.Payments.IFeeDiscountBuckets
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required bool Active { get; init; }
    public required DateTime RegistrationTs { get; init; }
    // Assigned team + its agegroup — used to resolve the player's Deposit/BalanceDue from the
    // fee cascade (null when the player isn't on a team, so no deposit can be resolved).
    public Guid? AssignedTeamId { get; init; }
    public Guid? AgeGroupId { get; init; }
    // Assigned team's display names — surfaced per-row in the family ledger so a director
    // can tell which team (agegroup + name) each transaction belongs to when a parent has
    // several players. Null when the player isn't yet on a team.
    public string? AssignedTeamName { get; init; }
    public string? AssignedAgeGroupName { get; init; }
    // Owning club of the assigned team — ClubName off the team's club-rep registration
    // (Teams.ClubrepRegistrationid → Registrations.ClubName). Lets the family ledger prefix a
    // club-rostered team as "{ClubName}: {TeamName}". Null when no club rep is assigned.
    public string? AssignedClubName { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeDiscount { get; init; }
    // Both discount buckets travel together — the shaper re-derives owed/deposit-due/proc from these
    // components and must net the same total FeeMath did. See IFeeDiscountBuckets.
    public required decimal FeeDiscountMp { get; init; }
    public required decimal FeeLatefee { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    // Stored ARB snapshot columns — raw Registrations.AdnSubscription* values. The service
    // shapes non-null ids into FamilyAccountingDto.Subscriptions; the shaper ignores them.
    public string? AdnSubscriptionId { get; init; }
    public string? AdnSubscriptionStatus { get; init; }
    public decimal? AdnSubscriptionAmountPerOccurence { get; init; }
    public int? AdnSubscriptionBillingOccurences { get; init; }
    public int? AdnSubscriptionIntervalLength { get; init; }
    public DateTime? AdnSubscriptionStartDate { get; init; }
}
