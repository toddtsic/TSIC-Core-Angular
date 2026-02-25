namespace TSIC.Contracts.Dtos.Scheduling;

// ══════════════════════════════════════════════════════════════════════
// Tournament Parking Per Site — Request / Response
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Parameters for computing tournament parking pressure per field complex.
/// </summary>
public record TournamentParkingRequest
{
    /// <summary>Minutes before first game a team arrives at a complex (0-60, step 5).</summary>
    public required int ArrivalBufferMinutes { get; init; }

    /// <summary>Minutes after last game a team departs a complex (0-60, step 5).</summary>
    public required int DepartureBufferMinutes { get; init; }

    /// <summary>Average cars per team (0-30).</summary>
    public required int CarMultiplier { get; init; }
}

/// <summary>
/// Full parking report: overall rollup + per-complex/day slices + summary KPIs.
/// </summary>
public record TournamentParkingResponse
{
    /// <summary>All timeslot rows across all complexes/days.</summary>
    public required List<ParkingTimeslotDto> Rollup { get; init; }

    /// <summary>Per-complex/day breakdowns, each with its own time-series data.</summary>
    public required List<ParkingComplexDayDto> ComplexDays { get; init; }

    /// <summary>Summary KPIs for header cards.</summary>
    public required ParkingSummaryDto Summary { get; init; }
}

/// <summary>
/// A single (complex, day) slice with its timeslot series.
/// </summary>
public record ParkingComplexDayDto
{
    public required string FieldComplex { get; init; }
    public required DateTime Day { get; init; }

    /// <summary>Display label, e.g. "Central — Sat 3/14".</summary>
    public required string Label { get; init; }

    public required List<ParkingTimeslotDto> Timeslots { get; init; }
}

/// <summary>
/// One time-bucket in the parking report.
/// </summary>
public record ParkingTimeslotDto
{
    public required string FieldComplex { get; init; }
    public required DateTime Day { get; init; }
    public required DateTime Time { get; init; }

    public int? TeamsArriving { get; init; }
    public int? TeamsDeparting { get; init; }

    /// <summary>Net team movement this timeslot (arriving - departing).</summary>
    public required int TeamsNet { get; init; }

    /// <summary>Running total of teams on-site (cumulative per complex/day).</summary>
    public required int TeamsOnSite { get; init; }

    public int? CarsArriving { get; init; }
    public int? CarsDeparting { get; init; }

    /// <summary>Net car movement this timeslot.</summary>
    public required int CarsNet { get; init; }

    /// <summary>Running total of cars on-site (cumulative per complex/day).</summary>
    public required int CarsOnSite { get; init; }
}

/// <summary>
/// Top-level KPIs for the header cards.
/// </summary>
public record ParkingSummaryDto
{
    public required int TotalComplexes { get; init; }
    public required int TotalDays { get; init; }
    public required int PeakTeamsOnSite { get; init; }
    public required string PeakTeamsComplex { get; init; }
    public required int PeakCarsOnSite { get; init; }
    public required string PeakCarsComplex { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Internal — Repository-level DTO
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Raw per-game record for a team at a field complex.
/// Used by the service to compute first/last game per team per complex per day.
/// </summary>
public record TeamGamePresenceDto
{
    public required string FieldComplex { get; init; }
    public required Guid TeamId { get; init; }
    public required Guid AgegroupId { get; init; }
    public required DateTime GameDate { get; init; }
}
