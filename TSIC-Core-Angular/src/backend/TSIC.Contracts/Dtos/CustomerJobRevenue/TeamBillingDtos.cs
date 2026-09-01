namespace TSIC.Contracts.Dtos.CustomerJobRevenue;

/// <summary>
/// One (team, month) cell of the Teams/Players to Customer tab — the CLIENT's view of what
/// their own registrants owe them, as opposed to the Revenue Rollup's view of what settles
/// between TSIC and the client.
/// </summary>
/// <remarks>
/// EVENT-GRAINED. A tournament team pays a deposit in one month and its balance in another;
/// a player on an ARB plan pays across six or ten months. Each of those lands in the month
/// it actually happened, so a single team appears under every month in which something
/// occurred:
/// <list type="bullet">
///   <item><description><b>Charge</b> at <c>teams.createdate</c> (team fee) or
///   <c>Registrations.RegistrationTS</c> (player fee) — sets Billed and Owed.</description></item>
///   <item><description><b>Payment</b> at <c>Registration_Accounting.createdate</c> — sets
///   Collected.</description></item>
/// </list>
///
/// Because a charge is itself a dated event, a team that has never paid still appears — at
/// the month it registered, with Collected zero and its full balance outstanding. That is
/// the whole reason this tab exists: the payment-driven Revenue Rollup cannot see such a
/// team at all, and on Top Threat they carry the bulk of the receivable.
///
/// Consequence to expect: within any one month the three figures need not relate to each
/// other. A team billed $5,000 in March that pays in July reads as Billed $5,000 /
/// Collected $0 in March, and Collected $5,000 in July.
/// </remarks>
public record TeamBillingRecordDto
{
    public required string JobName { get; init; }

    /// <summary>Year the event occurred — charge date or payment date.</summary>
    public required int Year { get; init; }

    /// <summary>Month the event occurred — charge date or payment date.</summary>
    public required int Month { get; init; }

    /// <summary>Owning club, or <c>"(No Club)"</c> when the team has no club rep.</summary>
    public required string ClubName { get; init; }

    /// <summary><c>"{agegroupName}:{teamName} ({playerCount})"</c> — the public-rosters label convention.</summary>
    public required string TeamLabel { get; init; }

    /// <summary>Charged in this month — team fee and/or player fees.</summary>
    public required decimal Billed { get; init; }

    /// <summary>Received in this month, all methods: cards, checks, corrections, refunds as negatives.</summary>
    public required decimal Collected { get; init; }

    /// <summary>
    /// Outstanding balance AS OF THE END DATE — computed as this team's charges less its
    /// payments through that date, not read from <c>teams.owed_total</c> (which is the balance
    /// right now and would contradict an as-of report ending in the past).
    /// <para>
    /// Carried on the team's charge row rather than spread across months: it is a balance, not
    /// an event, and spreading it would make a payment-only month read as negative Owed.
    /// </para>
    /// </summary>
    public required decimal Owed { get; init; }

    /// <summary>
    /// Net fee adjustment — <c>lateFee − discount − correction</c>, the same signed figure the
    /// player and club-rep grids show as "Fee-Adj"
    /// (<c>TSIC.Contracts.Payments.PaymentState.FeeAdjustment</c>).
    /// <para>
    /// <b>Positive means the entity owes MORE</b> (late fees, charge-back corrections);
    /// <b>negative means it owes less</b> (discounts, credit corrections). Both directions
    /// occur: 4 teams system-wide carry a NEGATIVE <c>fee_discount</c>, which is a surcharge.
    /// </para>
    /// <para>
    /// A MEMO column spanning both sides of the ledger, and it adds to nothing on this row.
    /// The late-fee and discount terms are already inside <see cref="Billed"/> — a
    /// <c>fee_total</c> includes its late fee and is already net of its discount. The
    /// correction term is already inside <see cref="Collected"/>. Adding Adj to either
    /// double counts.
    /// </para>
    /// <para>
    /// Dated the way its parts are: the charge-side terms ride the entity's charge month, the
    /// correction term rides the month the correction row was written. So within one month Adj
    /// need not relate to the other columns any more than they relate to each other.
    /// </para>
    /// <para>
    /// Replaces the former separate Discounts and Corrections columns (Todd, 2026-09-01). The
    /// components are deliberately NOT broken out: <c>fee_discount</c> is a blended column —
    /// early bird is stamped from the cascade and discount codes <c>+=</c> onto it afterwards —
    /// so a typed split was never recoverable from the data.
    /// </para>
    /// </summary>
    public required decimal Adj { get; init; }

    /// <summary>
    /// Credit Card Credit rows — money returned to a card. Always negative: 4,109 of 4,109
    /// active rows system-wide carry a negative amount, so no sign correction is applied.
    /// <para>
    /// A MEMO like <see cref="Adj"/>, and already netted inside <see cref="Collected"/> —
    /// which is exactly why it is reported. Netted and invisible, an event that collected
    /// $1.2M and refunded $200K reads identically to one that collected $1M clean.
    /// </para>
    /// <para>
    /// NOT part of <see cref="Adj"/>: a refund is returned TENDER, not a fee adjustment, and
    /// it is absent from the <c>FeeAdjustment</c> formula for that reason.
    /// </para>
    /// </summary>
    public required decimal Refunds { get; init; }
}

/// <summary>
/// One row of the Adjustments tab: a single entity and its net fee adjustment, as of the
/// report's end date.
/// </summary>
/// <remarks>
/// <para>
/// <b>UNDATED, on purpose.</b> Every other detail tab buckets by Year/Month because its rows
/// are dated ledger events. Two of the three adjustment terms are not events at all —
/// <c>fee_discount</c> and <c>fee_latefee</c> are stamped columns on the entity with no
/// timestamp, no author and no reason. Inventing a date for them would be a fiction, so this
/// tab reports a rollup instead and says so.
/// </para>
/// <para>
/// Still AS OF the end date, via the inclusion rule rather than a date column: an entity is in
/// scope when it was CHARGED by the cutoff, and its correction rows — which genuinely are
/// dated — are cut at the same cutoff.
/// </para>
/// <para>
/// <b>The entity is the money-bearing one</b>, which depends on the registration's role: a
/// club rep's money lives on <c>Leagues.teams</c>, everyone else's on their own registration.
/// Verified: all 8 club-rep registrations carrying a non-zero <c>fee_discount</c> carry
/// EXACTLY their own teams' total, so reading the team rather than the rep drops nothing and
/// avoids double counting.
/// </para>
/// <para>
/// Components are not broken out — see <see cref="TeamBillingRecordDto.Adj"/> for why a typed
/// split is unrecoverable.
/// </para>
/// </remarks>
public record AdjustmentRecordDto
{
    public required string JobName { get; init; }

    /// <summary>Owning club, or <c>"(No Club)"</c>.</summary>
    public required string ClubName { get; init; }

    /// <summary><c>"Team"</c> or <c>"Registrant"</c> — which route put this row here.</summary>
    public required string EntityType { get; init; }

    /// <summary>Team label on the club-rep route, person's name on the registration route.</summary>
    public required string EntityLabel { get; init; }

    /// <summary>
    /// <c>lateFee − discount − correction</c> for this one entity. Positive = owes more.
    /// </summary>
    public required decimal Adj { get; init; }
}
