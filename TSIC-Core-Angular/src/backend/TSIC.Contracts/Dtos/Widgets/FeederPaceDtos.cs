namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// Year over Year — All Events: where each season stood on THIS DATE, for a customer that runs
/// many events at once. Registrations and money collected, rolled up across every event and
/// broken out per site.
/// </summary>
/// <remarks>
/// <para>
/// A SEPARATE report from the Year-over-Year widget, which reads ONE event against its own
/// prior seasons. That is the right answer for a customer running one event a season and it
/// stays as it is (Todd, 2026-09-20). It cannot answer this question: American Select runs ~24
/// regional tryout sites plus a Main Event every season, and the SuperDirector standing on the
/// Main Event wants the pace of ALL of them — the Main Event barely fills until the tryouts
/// have run.
/// </para>
/// <para><b>Every job in the season is a peer</b> (Todd, 2026-09-20), the Main Event included.
/// It is one site row like any other and it is inside the rollup. So the rollup counts
/// REGISTRATIONS, not athletes: 2,155 of the Main Event's 2,758 players in season 2026 also
/// hold a regional registration. That is what the customer sells; it must never be labelled a
/// headcount.
/// </para>
/// <para><b>The pin is the whole idea.</b> Every season is cut at the same calendar month and
/// day — <see cref="AsOfDate"/> shifted back whole years — so a season still selling is read
/// against where the prior season stood on that same date, never against its final figure. A
/// season that had not opened by its own pin reads 0, and that is correct: the opening date
/// drifting IS the pace signal. American Select opened season 2027 on 21 August, the earliest
/// in seven years, and nothing may normalise that away.
/// </para>
/// <para><b>Year to date, not calendar year.</b> The year starts at
/// <see cref="FeederPaceSeasonDto.YtdFrom"/>, derived from the customer's own registrations —
/// August for American Select, whose season runs Aug–Jul. A customer whose season runs on the
/// calendar year derives to 1 January with no special case.
/// </para>
/// <para><b>The money is the customer's own book</b>, the same ledger the Teams/Players to
/// Customer tab reports — what their registrants owe THEM. It is not TSIC's settlement view and
/// does not reconcile with the Revenue Rollup.
/// </para>
/// </remarks>
public record FeederPaceDto
{
    /// <summary>The season of the job the caller is standing in — the highlighted column.</summary>
    public required int CurrentSeason { get; init; }

    /// <summary>
    /// The date asked: the newest season's cutoff, and the origin every other season's cutoff
    /// is shifted back from.
    /// </summary>
    public required DateTime AsOfDate { get; init; }

    /// <summary>The rollup over every event the customer runs, oldest season first.</summary>
    public required List<FeederPaceSeasonDto> Seasons { get; init; }

    /// <summary>
    /// Every site running in the current or the prior season, each carrying the SAME season
    /// series as the rollup so the per-site chart is drawn from one payload rather than a
    /// round trip per click.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built from the JOB SPINE, not from registrations: a site that has not opened yet has no
    /// registrations at all, and "22 of 25 sites have not opened" is the single most useful
    /// fact on this screen in September.
    /// </para>
    /// <para><b>A SITE IS A JOB NAME with the season token stripped, and nothing more.</b>
    /// Renames, splits and merges therefore break a site's history, and no column in
    /// <c>Jobs.Jobs</c> records the lineage that would repair it — there is no clone-source or
    /// parent reference to follow. American Select ran California 2021-2025, split it into
    /// NorCal and SoCal for 2026, and merged it back to California for 2027: this reports three
    /// sites, one with a gap at 2026 and two that appear to stop after it. That is the honest
    /// reading of what the data says, and inventing the grouping would be a business judgement
    /// the schema cannot support (Todd, 2026-09-20).
    /// </para>
    /// <para>
    /// The ROLLUP is unaffected — it sums every job of the customer, so a split season's
    /// registrations and money are counted once either way. Only the per-site breakdown
    /// fragments.
    /// </para>
    /// </remarks>
    public required List<FeederPaceSiteDto> Sites { get; init; }

    /// <summary>
    /// Jobs of this customer carrying no parseable <c>Jobs.year</c>, so they could be placed in
    /// no season. Surfaced rather than swallowed: a silently dropped event is the failure a
    /// reader would never catch on their own.
    /// </summary>
    public required List<string> UngroupedJobNames { get; init; }
}

/// <summary>One season, measured at its own pin.</summary>
public record FeederPaceSeasonDto
{
    /// <summary>From <c>Jobs.year</c> — the season, NOT the year the money moved.</summary>
    public required int Season { get; init; }

    /// <summary>Where this season's year starts. Everything below is counted from here.</summary>
    public required DateTime YtdFrom { get; init; }

    /// <summary>This season's cutoff — <c>AsOfDate</c> shifted back to this season.</summary>
    public required DateTime PinDate { get; init; }

    /// <summary>Active player registrations from <see cref="YtdFrom"/> through <see cref="PinDate"/>.</summary>
    public required int Registrations { get; init; }

    /// <summary>
    /// Registrations through the WHOLE season. For a season still selling this is simply where
    /// it has got to, which is why it is reported beside <see cref="Registrations"/> and never
    /// instead of it.
    /// </summary>
    /// <remarks>
    /// <b>This is what a COMPLETED season is drawn at.</b> Reading a finished season at its pin
    /// answers "where was it on 20 September", and for this customer that is nearly always zero
    /// — season 2025 did not open until 15 October, 2024 until 25 October. Six seasons of empty
    /// columns is honest and useless. So the charts draw every season that is already over at
    /// its finished size and the live season at <see cref="Registrations"/>, labelled. The pin
    /// is not discarded: it still drives the headline pace figure, where the comparison is
    /// like-for-like against the one prior season.
    /// </remarks>
    public required int TotalRegistrations { get; init; }

    /// <summary>
    /// Received through the pin — the plotted dollar figure. All methods, corrections included,
    /// refunds inside as negatives.
    /// </summary>
    public required decimal Collected { get; init; }

    /// <summary>
    /// Charged through the pin, already net of discounts. Reported so the tooltip can show it
    /// beside <see cref="Collected"/>: the gap between them is the unpaid balance at the pin,
    /// and on a customer that collects at registration the two sit within a few percent.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>GetYoyRevenueAsync</c>'s definitions EXACTLY, including its carried-forward
    /// asymmetry that the fee terms do not test <c>bActive</c> while the counts do. Two reports
    /// disagreeing about the same customer's money would be worse than one inherited quirk.
    /// </remarks>
    public required decimal Billed { get; init; }

    /// <summary>
    /// Received across the WHOLE season — what a completed season is drawn at on the dollar
    /// axis, for the same reason <see cref="TotalRegistrations"/> is on the count axis.
    /// </summary>
    public required decimal TotalCollected { get; init; }

    /// <summary>Charged across the whole season. Tooltip only, beside <see cref="TotalCollected"/>.</summary>
    public required decimal TotalBilled { get; init; }

    /// <summary>Jobs composing this season — the whole customer, or one site's jobs on a site row.</summary>
    public required int JobCount { get; init; }
}

/// <summary>One site — a tryout region, or the final event — and its whole season series.</summary>
public record FeederPaceSiteDto
{
    /// <summary>Display name: the job name with its season designator stripped.</summary>
    public required string Site { get; init; }

    /// <summary>Oldest season first, same shape as the rollup.</summary>
    public required List<FeederPaceSeasonDto> Seasons { get; init; }

    /// <summary>
    /// False when this site did not run last season at all. Its prior figures are then 0
    /// because there was nothing, not because nobody registered — the difference between a new
    /// site and a collapsed one, which the numbers alone cannot show.
    /// </summary>
    public required bool HasPriorSeason { get; init; }

    /// <summary>True when this site is not running in the current season — it ran last season and stopped.</summary>
    public required bool IsRetired { get; init; }

    /// <summary>
    /// The jobs behind this row, newest season first. Rendered deliberately: grouping by name
    /// is a heuristic whose failure mode (a site renamed, or split in two) produces a confident
    /// row against a wrong baseline, and a reader recognises a bad pairing instantly where no
    /// parser will.
    /// </summary>
    public required List<string> JobNames { get; init; }
}
