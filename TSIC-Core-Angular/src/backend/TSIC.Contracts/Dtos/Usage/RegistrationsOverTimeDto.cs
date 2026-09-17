namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// Registrations over Time -- what came IN per bucket, not who used the site. The only
/// report on this page that never reads TSICLogs: every number is TSICV5, counted from
/// the row's own creation stamp.
///
/// Three series, two units, deliberately:
///   Player, Club Rep -- Jobs.Registrations rows, anchored on RegistrationTs;
///   Teams            -- Leagues.teams rows, anchored on createdate.
/// Teams is NOT derivable from the Club Rep series: a club rep's registration is minted
/// once per (user, event) and reused for every team they ever add, so a day can carry 30
/// new teams and no new club reps. Hence its own series, its own table column, and its own
/// chart axis -- players outrun teams by an order of magnitude and would flatten them on a
/// shared scale.
///
/// Scope rules (Todd, 2026-09-17):
///  - Player and Club Rep only. Admin registrations are provisioning, not signups, and
///    their RegistrationTs is not even a signup date -- job clone copies it from the source
///    event (year-shifted, or byte-for-byte when the year delta is 0), so an admin series
///    would show intake on days nobody worked. Scorer is excluded with them: an event-day
///    role nobody tracks intake for.
///  - bActive = 1. A registration row exists from PreSubmit, before payment; activation
///    happens at payment (or at completion of a free event), so the flag is what separates
///    a signup from an abandoned cart.
///  - RegistrationTs raw -- no LEAST(RegistrationTs, Modified) heuristic. It is needed only
///    for the admin rows this report does not count, and Modified is a last-edit stamp that
///    moves on every subsequent touch.
/// </summary>
public record RegistrationsOverTimeDto
{
    /// <summary>The wire word: day | week | month.</summary>
    public required string Bucket { get; init; }

    /// <summary>Start of the first bucket, server-local like the stamps themselves. Aligned to the unit (midnight, Monday, the 1st).</summary>
    public required DateTime Since { get; init; }

    /// <summary>Every bucket start in the span, oldest first, including buckets with no rows. The x axis.</summary>
    public required List<DateTime> Buckets { get; init; }

    /// <summary>Events the numbers cover -- the resolved scope (after any event lens), restated for the audit stamp.</summary>
    public required int JobCount { get; init; }

    /// <summary>One row per (bucket, series) that had at least one registration or team. Unordered; the page fills the gaps and sorts.</summary>
    public required List<RegistrationsBucketRowDto> Rows { get; init; }
}

public record RegistrationsBucketRowDto
{
    /// <summary>The bucket's start, one of RegistrationsOverTimeDto.Buckets.</summary>
    public required DateTime BucketStart { get; init; }

    /// <summary>
    /// The series: an AspNetRoles name ("Player", "Club Rep") for a registration series, or
    /// "Teams" for the team series. Role names are exact so a role keeps the colour and the
    /// column position it holds on every other report on this page.
    /// </summary>
    public required string SeriesName { get; init; }

    /// <summary>Rows created in this bucket: registrations for a role series, teams for the team series.</summary>
    public required int Count { get; init; }

    /// <summary>
    /// False for the team series. The page adds only the people series into its People total --
    /// a team is not a person, and summing the two would invent a unit.
    /// </summary>
    public required bool IsPeople { get; init; }
}

/// <summary>A (bucket, role) count from TSICV5: registrations of this role created in this bucket.</summary>
public record RegistrationCountByBucketDto
{
    /// <summary>Whole buckets from the aligned <c>since</c>: 0 is the oldest.</summary>
    public required int BucketIndex { get; init; }

    public required string RoleId { get; init; }

    public required int Count { get; init; }
}

/// <summary>A (bucket, count) pair from TSICV5: teams created in this bucket.</summary>
public record TeamCountByBucketDto
{
    /// <summary>Whole buckets from the aligned <c>since</c>: 0 is the oldest.</summary>
    public required int BucketIndex { get; init; }

    public required int Count { get; init; }
}
