using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class GameClockParam
{
    public int Id { get; set; }

    public Guid JobId { get; set; }

    public decimal HalfMinutes { get; set; }

    public decimal HalfTimeMinutes { get; set; }

    public decimal TransitionMinutes { get; set; }

    public decimal PlayoffMinutes { get; set; }

    public DateTime Modified { get; set; }

    public decimal? PlayoffHalfMinutes { get; set; }

    public decimal? PlayoffHalfTimeMinutes { get; set; }

    public decimal? QuarterMinutes { get; set; }

    public decimal? QuarterTimeMinutes { get; set; }

    public int? UtcoffsetHours { get; set; }

    public virtual Job Job { get; set; } = null!;
}
