using TSIC.Contracts.Dtos;
using TSIC.Domain.Constants;

namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Family accounting view — all data the shared family-payment component needs to render
/// a combined ledger across every player a parent registered for the job. The parent-side
/// analog of <c>ClubRepAccountingDto</c>: where the club rep groups one registration's teams
/// by TeamId, the family groups N sibling registrations (keyed by JobId + FamilyUserId) by
/// the owning child. <see cref="Players"/> carries the same money decomposition the club-rep
/// grid shows — computed through the same canonical helpers, so the two views can never
/// disagree about a dollar — but on a PLAYER-shaped row
/// (<see cref="RegisteredPlayerLineDto"/>), not a team-shaped one. AccountingRecords
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
    /// One row per child registration. RegistrationId is also the ledger group key (it matches
    /// each record's OwnerRegistrationId).
    /// </summary>
    public required List<RegisteredPlayerLineDto> Players { get; init; }
    public required List<AccountingRecordDto> AccountingRecords { get; init; }

    /// <summary>
    /// Stored ARB snapshot (Registrations.AdnSubscription* columns) for every registration in
    /// the sibling set that carries one — ARB mints ONE Authorize.Net subscription per
    /// REGISTRATION, so several players (or several events of one player) can each have their
    /// own. Keyed by RegistrationId (= Players[].RegistrationId) so the subscription card follows the
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
/// One child's registration as a billable line on the family accounting grid.
///
/// This is the PLAYER-shaped peer of <see cref="RegisteredTeamDto"/>, not a reuse of it. The
/// money block below is deliberately field-for-field identical and is produced through the same
/// canonical helpers (IPaymentStateService / IFeeResolutionService) the team shaper uses, so the
/// director's club-rep view and the family view can never disagree about a dollar. What is NOT
/// shared is identity: a player has a name, an assigned team and an age group in their own right.
///
/// History: players used to be shipped AS <see cref="RegisteredTeamDto"/> so both grids could be
/// one component — PlayerName went in TeamName, RegistrationId in TeamId, and AgeGroupName was
/// blanked because the family grid hid that column. Blanking it silently killed the waitlist
/// badge (IsWaitlisted is derived from the age-group name), and the frontend had to rebuild the
/// team/age-group label by scraping accounting records, which failed for a registration with no
/// records yet. Both defects were the costume, not the columns.
/// </summary>
public record RegisteredPlayerLineDto
{
    /// <summary>The registration itself — identity AND the ledger group key (matches each
    /// AccountingRecordDto.OwnerRegistrationId).</summary>
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }

    /// <summary>Assigned team's display name. Null when the child isn't rostered yet.</summary>
    public required string? TeamName { get; init; }

    /// <summary>Assigned team's age group, RAW — it still carries the minted "WAITLIST - "
    /// prefix when the child sits on a waitlist mirror. Null when unrostered. Read
    /// <see cref="IsWaitlisted"/> / <see cref="AgeGroupDisplayName"/> rather than re-parsing it.</summary>
    public required string? AgeGroupName { get; init; }

    public bool IsWaitlisted => AgegroupConstants.IsWaitlist(AgeGroupName);
    public string AgeGroupDisplayName => AgegroupConstants.StripWaitlistPrefix(AgeGroupName);

    /// <summary>Owning club of the assigned team, when the team is club-rostered.</summary>
    public required string? ClubName { get; init; }

    public required bool Active { get; init; }
    public required DateTime RegistrationTs { get; init; }

    // ── Money. Same names, same semantics, same producers as RegisteredTeamDto. ──
    public required decimal FeeBase { get; init; }
    /// <summary>Statement-of-fact: raw Registrations.FeeProcessing.</summary>
    public required decimal FeeProcessing { get; init; }
    /// <summary>Display semantic: the CC processing fee still owed right now
    /// (OwedTotal − CkOwedTotal).</summary>
    public required decimal FeeProcessingDue { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeLatefee { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    /// <summary>Signed net adjustment = lateFee − discount − correction.</summary>
    public required decimal FeeAdj { get; init; }
    /// <summary>Real money received; excludes Correction-method rows (those land in FeeAdj).</summary>
    public required decimal TenderPaid { get; init; }
    /// <summary>Immutable fee structure — what this registration was committed to.</summary>
    public required decimal Deposit { get; init; }
    public required decimal BalanceDue { get; init; }
    /// <summary>Per-ROW phase (FeeBase has reached FullPrice). Siblings can differ.</summary>
    public required bool FullPaymentRequired { get; init; }
    // Net-of-paid ledger state.
    public required decimal DepositDue { get; init; }
    public required decimal AdditionalDue { get; init; }
    public required decimal CcOwedTotal { get; init; }
    public required decimal CkOwedTotal { get; init; }
    public required decimal EkOwedTotal { get; init; }
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
/// <c>RegisteredTeamInfo</c>. The shaper turns these into <see cref="RegisteredPlayerLineDto"/>
/// rows through the canonical payment-state path (IPaymentStateService + IFeeResolutionService),
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
