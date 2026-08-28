namespace TSIC.Contracts.Dtos.Store;

/// <summary>
/// One purchased SKU line, with everything the three fulfilment reports need to pack a bag, hand
/// it over, and reconcile it — bag labels, the pickup signoff sheet, and the per-family pivot all
/// read this single shape.
///
/// <para>
/// It replaces the Crystal pair <c>reporting.StoreLabels</c> / <c>reporting.StorePickupConfirmation</c>,
/// which are the same query written twice with slightly different columns. Two behaviours are
/// deliberately NOT carried forward:
/// </para>
///
/// <list type="bullet">
/// <item><b>The club-rep inner join.</b> Both procs join
/// <c>Teams.clubrep_registrationid</c> to a registration, <c>StoreLabels</c> with an INNER join.
/// Walk-up sales sit on the "Store Merch" team, which has no club rep, so every walk-up line was
/// dropped from the label run without a trace. On the live data that is 43 of 484 lines overall
/// and <b>42 of 43 for StateOne Lacrosse: Onsite Merch 2026</b> — a job whose label sheet would
/// have printed exactly one label. Here the club rep is optional and absence is shown, not
/// filtered.</item>
/// <item><b>The registration inner join.</b> A line with a null <c>DirectToRegId</c>, or one
/// pointing at a registration with no assigned team, likewise vanished. Those rows now survive
/// and group under an "Unassigned" heading so a director sees them at the end of the run.</item>
/// </list>
///
/// <para>
/// Quantity is the NET of restocks (<c>Quantity - Restocked</c>), which is what is physically in
/// the bag. The gross and the restocked count are both carried so the signoff sheet can show a
/// returned item as returned rather than silently shrinking the order.
/// </para>
/// </summary>
public record StoreFulfillmentRowDto
{
    // ── Batch / order identity ──

    public required int BatchId { get; init; }
    public required int CartSkuId { get; init; }
    public required DateTime BatchDate { get; init; }
    public string? InvoiceNo { get; init; }

    /// <summary>Set once the family has signed for the batch (<c>StoreCartBatches.SignedForBy</c>).</summary>
    public string? SignedForBy { get; init; }
    public DateTime? SignedForDate { get; init; }

    // ── Family (the buyer / the login) ──

    public required string FamilyUserId { get; init; }
    public string? FamilyUsername { get; init; }
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomCellphone { get; init; }
    public string? MomEmail { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadCellphone { get; init; }
    public string? DadEmail { get; init; }

    // ── Player the item is for (null on an unattached line) ──

    public Guid? PlayerRegId { get; init; }
    public string? PlayerUserId { get; init; }
    public string? PlayerFirstName { get; init; }
    public string? PlayerLastName { get; init; }
    public string? PlayerCellphone { get; init; }
    public string? PlayerEmail { get; init; }

    // ── Placement (all optional — walk-ups and unrostered players have none) ──

    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? ClubName { get; init; }
    public string? TeamName { get; init; }

    /// <summary>
    /// True when the line is a counter sale. Mirrors <c>StoreAnalyticsRepository.WalkUpLines()</c>:
    /// a registration on the "Store Merch" team under the "Dropped Teams" agegroup and division.
    /// A walk-up has a real player row (the counter registration), so it is not "unassigned" —
    /// it just has no club or team worth printing.
    /// </summary>
    public required bool IsWalkUp { get; init; }

    // ── The item ──

    public required string ItemName { get; init; }
    public string? SizeName { get; init; }
    public string? ColorName { get; init; }

    /// <summary>Gross ordered quantity, before returns.</summary>
    public required int Quantity { get; init; }

    /// <summary>Units returned to stock. Never exceeds <see cref="Quantity"/>.</summary>
    public required int Restocked { get; init; }

    /// <summary>What is actually in the bag: <c>Quantity - Restocked</c>, floored at zero.</summary>
    public int NetQuantity => Math.Max(0, Quantity - Restocked);
}
