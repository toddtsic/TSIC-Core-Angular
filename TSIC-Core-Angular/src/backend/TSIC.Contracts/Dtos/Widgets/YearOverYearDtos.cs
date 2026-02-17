namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// Year-over-year registration pace comparison.
/// Each series represents one year's cumulative registration curve
/// plotted against calendar dates.
/// </summary>
public record YearOverYearComparisonDto
{
    /// <summary>
    /// One series per sibling job (year). Ordered most recent first.
    /// </summary>
    public required List<YearSeriesDto> Series { get; init; }

    /// <summary>
    /// The current job's year (highlighted series).
    /// </summary>
    public required string CurrentYear { get; init; }
}

/// <summary>
/// A single year's registration curve.
/// </summary>
public record YearSeriesDto
{
    public required string Year { get; init; }
    public required string JobName { get; init; }
    public required int FinalTotal { get; init; }

    /// <summary>
    /// Daily cumulative registration counts with real calendar dates.
    /// </summary>
    public required List<YearDayPointDto> DailyData { get; init; }
}

/// <summary>
/// A single data point on the year curve.
/// </summary>
public record YearDayPointDto
{
    /// <summary>
    /// The actual calendar date of this data point.
    /// </summary>
    public required DateTime Date { get; init; }

    /// <summary>
    /// Cumulative registration count as of this date.
    /// </summary>
    public required int CumulativeCount { get; init; }
}
