namespace TSIC.Contracts.Dtos.CustomerJobRevenue;

/// <summary>
/// One active team, with its lifetime billing position, bucketed by WHEN THE TEAM REGISTERED
/// (<c>Leagues.teams.createdate</c>) — not by when money moved.
/// </summary>
/// <remarks>
/// This is the Team Billing tab's flat pivot feed, and it answers a different question from the
/// Revenue Rollup tab. The rollup is dated cash flow; this is a cohort view of current balances,
/// so a team that registered in January and paid in March reports its Collected under January.
/// The two tabs deliberately DO NOT tie month-to-month.
///
/// Teams are enumerated independently of payment, so a team that has never paid still appears —
/// which is the whole point: 520 of Top Threat's 7,942 active teams are invisible to the
/// payment-driven rollup, and they carry the bulk of the outstanding balance.
///
/// Money reaches a team by exactly two routes (see the repository):
/// club-rep team fees off <c>Leagues.teams</c>, and player fees off registrations that carry
/// both an <c>assigned_teamID</c> and a non-zero <c>fee_total</c>. Self-rostered players are
/// free and contribute no money — they appear only inside <see cref="TeamLabel"/>'s headcount.
/// </remarks>
public record TeamBillingRecordDto
{
    public required string JobName { get; init; }

    /// <summary>Year the team registered (<c>teams.createdate</c>), NOT the year money moved.</summary>
    public required int Year { get; init; }

    /// <summary>Month the team registered (<c>teams.createdate</c>), NOT the month money moved.</summary>
    public required int Month { get; init; }

    /// <summary>Owning club, or <c>"(No Club)"</c> when the team has no club rep.</summary>
    public required string ClubName { get; init; }

    /// <summary><c>"{agegroupName}:{teamName} ({playerCount})"</c> — the public-rosters label convention.</summary>
    public required string TeamLabel { get; init; }

    /// <summary>Team fee + any per-player fees charged against this team.</summary>
    public required decimal Billed { get; init; }

    /// <summary>Lifetime collected for this team — NOT collected within the scoped period.</summary>
    public required decimal Collected { get; init; }

    /// <summary>Outstanding balance as of now.</summary>
    public required decimal Owed { get; init; }
}
