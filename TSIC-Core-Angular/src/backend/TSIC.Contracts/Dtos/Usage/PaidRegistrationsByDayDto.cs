namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// Paid Registrations by Day -- the count and the money of NEW registrations, per day, per
/// event, per role, dated by RegistrationTs. TSICV5 only, like Registrations over Time.
///
/// Rulings (Todd, 2026-09-28):
///  - Only a registration with a payment on it counts: PaidTotal &gt; 0. The money is that
///    PaidTotal -- what has been paid on it AS OF NOW, not the cash that arrived that day. A
///    registration made on the 5th and paid on the 10th sits on the 5th with its payment.
///  - Club Rep is counted as CLUB REPS, on the registration's own PaidTotal, which is the
///    rollup across every team the rep has entered. Deliberate: team money arriving under a
///    rep's existing registration lands on the day that registration was created.
///  - Admin roles are left out: provisioning, not intake, and job clone copies their
///    RegistrationTs off the source event, so it is not a signup date at all.
/// </summary>
public record PaidRegistrationsByDayDto
{
    /// <summary>Days in the span, today included.</summary>
    public required int WindowDays { get; init; }

    /// <summary>Midnight of the first day, server-local like RegistrationTs.</summary>
    public required DateTime Since { get; init; }

    /// <summary>Every day in the span, oldest first, including days with no rows. The chart's x axis.</summary>
    public required List<DateTime> Days { get; init; }

    /// <summary>Events the numbers cover -- the resolved scope, restated for the chart title.</summary>
    public required int JobCount { get; init; }

    /// <summary>One row per (day, event, role) with at least one paid registration. Unordered; the page sorts.</summary>
    public required List<PaidRegistrationsDayRowDto> Rows { get; init; }
}

public record PaidRegistrationsDayRowDto
{
    /// <summary>Midnight of the day, one of PaidRegistrationsByDayDto.Days.</summary>
    public required DateTime Day { get; init; }

    public required Guid JobId { get; init; }

    public required string JobName { get; init; }

    /// <summary>The exact AspNetRoles name, so a role keeps its colour and column across the page.</summary>
    public required string RoleName { get; init; }

    /// <summary>Paid registrations of this role created on this day for this event.</summary>
    public required int Registrations { get; init; }

    /// <summary>The sum of their PaidTotal as of now.</summary>
    public required decimal Paid { get; init; }
}

/// <summary>A (day, job, role) aggregate from TSICV5, before the service names the day and the job.</summary>
public record PaidRegistrationsByDayCountDto
{
    /// <summary>Whole days from the aligned <c>since</c>: 0 is the oldest.</summary>
    public required int DayIndex { get; init; }

    public required Guid JobId { get; init; }

    public required string RoleName { get; init; }

    public required int Registrations { get; init; }

    public required decimal Paid { get; init; }
}
