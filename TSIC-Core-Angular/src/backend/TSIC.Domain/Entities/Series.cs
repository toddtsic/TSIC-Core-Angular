using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Series
{
    public Guid SeriesId { get; set; }

    public Guid TeamId { get; set; }

    public Guid JobId { get; set; }

    public string? RecurrenceRule { get; set; }

    public DateOnly? StartsOn { get; set; }

    public DateOnly? EndsOn { get; set; }

    public byte Source { get; set; }

    public string? ExternalSeriesId { get; set; }

    public DateTime Created { get; set; }

    public string? CreatorUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? CreatorUser { get; set; }

    public virtual ICollection<Events> Events { get; set; } = new List<Events>();

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Teams Team { get; set; } = null!;
}
