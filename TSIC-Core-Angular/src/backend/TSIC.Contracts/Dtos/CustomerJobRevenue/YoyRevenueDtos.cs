namespace TSIC.Contracts.Dtos.CustomerJobRevenue;

/// <summary>
/// Year-over-Year Review: for every event the customer is running RIGHT NOW, that event's
/// prior seasons stacked side by side, each measured at the SAME POINT IN THE CALENDAR.
/// </summary>
/// <remarks>
/// <para>
/// This is the client's own book, the same ledger the Teams/Players to Customer tab reports —
/// what their registrants owe THEM. It is not TSIC's settlement view and does not reconcile
/// with the Revenue Rollup.
/// </para>
/// <para><b>The as-of pin is the whole idea.</b> Each year column is aggregated from the
/// beginning of its jobs' history up to a cutoff, and that cutoff is the report's end date
/// shifted back a whole number of years. Ask on 8/31/2026 and the prior season is measured at
/// 8/31/2025 — not at its final figure. That is what makes an event still selling comparable
/// to one that has already run: both are read at the same distance from their own season.
/// </para>
/// <para>
/// Because the pin travels with the date asked, historical columns are not frozen. Run the
/// report a month later and last season's column advances a month too, converging on its final
/// figure roughly a year after the money stopped moving.
/// </para>
/// </remarks>
public record YoyRevenueResponseDto
{
    /// <summary>The date asked — the newest column's cutoff, and the origin every other column's cutoff is shifted back from.</summary>
    public required DateTime AsOfDate { get; init; }

    /// <summary>One entry per event lineage, ordered by group label.</summary>
    public required List<YoyEventGroupDto> Groups { get; init; }

    /// <summary>
    /// Jobs that qualified as active but carry no parseable <c>Jobs.year</c>, so they could
    /// neither be placed in a column nor grouped. Surfaced rather than swallowed: a silently
    /// dropped live event is the failure a reader would never catch on their own.
    /// </summary>
    public required List<string> UngroupedJobNames { get; init; }
}

/// <summary>
/// One event lineage — "Fall Draw" — and its seasons.
/// </summary>
/// <remarks>
/// The group key is the job name with its <c>Jobs.year</c> token removed. Jobs are grouped by
/// NAME but every figure is aggregated strictly per jobId and only then attributed here, so
/// name handling can never disturb the arithmetic.
/// </remarks>
public record YoyEventGroupDto
{
    /// <summary>Display label — the job name minus its year, e.g. <c>"Top Threat Tournaments:Fall Draw"</c>.</summary>
    public required string GroupLabel { get; init; }

    /// <summary>
    /// The newest season in this group that is live as of the ask. Every other column's cutoff
    /// is this year's cutoff shifted back by the difference in years.
    /// </summary>
    public required int AnchorYear { get; init; }

    /// <summary>Oldest first, so the chart reads left to right chronologically.</summary>
    public required List<YoyYearColumnDto> Years { get; init; }
}

/// <summary>
/// One season of one event group, measured as of <see cref="AsOf"/>.
/// </summary>
public record YoyYearColumnDto
{
    /// <summary>Season, from <c>Jobs.year</c> — NOT the year the money moved.</summary>
    public required int Year { get; init; }

    /// <summary>This column's cutoff. Everything from the beginning of history through this date is included.</summary>
    public required DateTime AsOf { get; init; }

    /// <summary>
    /// True when a job in this column is still selling (<c>ExpiryUsers</c> beyond its cutoff).
    /// The chart must mark it: an in-flight column read against a concluded one looks like a
    /// collapse when it is only a season that has not finished.
    /// </summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// The jobs whose figures compose this column. Rendered on the chart deliberately — name
    /// grouping is a heuristic, and its failure mode (a split like North/South, or punctuation
    /// drift) produces a confident chart against a wrong baseline. A reader recognises a bad
    /// pairing instantly; no parser will.
    /// </summary>
    public required List<string> JobNames { get; init; }

    /// <summary>Charged through the cutoff — team fees plus player fees, already net of discounts.</summary>
    public required decimal Billed { get; init; }

    /// <summary>Received through the cutoff, all methods — corrections included, refunds inside as negatives.</summary>
    public required decimal Collected { get; init; }

    /// <summary>
    /// Net fee adjustment through the cutoff — <c>lateFee − discount − correction</c>, the same
    /// signed figure the player and club-rep grids show as "Fee-Adj". Positive = owes more.
    /// <para>
    /// A MEMO spanning both sides: the late-fee and discount terms are already inside
    /// <see cref="Billed"/>, the correction term already inside <see cref="Collected"/>.
    /// It adds to neither.
    /// </para>
    /// </summary>
    public required decimal Adj { get; init; }

    /// <summary>
    /// Credit Card Credits through the cutoff — always negative, already netted inside
    /// <see cref="Collected"/>. Reported because netted-and-invisible makes a season that
    /// collected $1.2M and refunded $200K read like one that collected $1M clean. NOT part of
    /// <see cref="Adj"/>: a refund is returned tender, not a fee adjustment.
    /// </summary>
    public required decimal Refunds { get; init; }

    /// <summary>
    /// <see cref="Billed"/> less <see cref="Collected"/> at this cutoff — computed, never read
    /// from <c>teams.owed_total</c>, which is the balance right now and would contradict a
    /// column measured in the past.
    /// </summary>
    public required decimal Owed { get; init; }

    /// <summary>
    /// Money-bearing entities that were SETTLED as of this column's cutoff — a registration on
    /// the player route, a team on the club-rep route.
    /// </summary>
    /// <remarks>
    /// Rebuilt from the ledger at the pin, never read from owed_total: that column is today's
    /// balance and would contradict a bar measured in the past. Paid + Owing is the whole
    /// population behind the bar.
    /// </remarks>
    public required int PaidCount { get; init; }

    /// <summary>Money-bearing entities still carrying a balance as of this column's cutoff.</summary>
    public required int OwingCount { get; init; }

    // NOTE: PaidCount + OwingCount is the CHARGED population, which is not TeamCount +
    // PlayerCount and on a free-roster event is nowhere near it. Present the pair as
    // "N of M charged", never as a fraction of the registrations bar.

    /// <summary>
    /// Teams present as of this column's cutoff — the club-rep route, where the TEAM is what
    /// carries the fee and its players carry none. Counted if charged OR populated, so a
    /// player-route job's free roster containers are not reported as zero teams.
    /// </summary>
    /// <remarks>
    /// Reported SEPARATELY from <see cref="PlayerCount"/> rather than summed, because the two
    /// are different things and a chart that adds them names neither. On Top Threat's 126 jobs
    /// the routes are all but disjoint — 108 team-route only, 6 player-route only, 1 mixed — so
    /// a single "registrations" count was calling 203 club teams registrations on every
    /// tournament event the customer runs.
    /// </remarks>
    public required int TeamCount { get; init; }

    /// <summary>
    /// Player registrations present as of this column's cutoff — the player route, free ones
    /// included, because on a tournament the free self-rostered players ARE the population.
    /// See <see cref="TeamCount"/> for why the two are not added together, and
    /// <see cref="PaidCount"/> for why neither is the settled/owing denominator.
    /// </summary>
    public required int PlayerCount { get; init; }
}
