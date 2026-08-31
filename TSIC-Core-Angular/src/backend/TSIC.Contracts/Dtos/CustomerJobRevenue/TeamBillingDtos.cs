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
}
