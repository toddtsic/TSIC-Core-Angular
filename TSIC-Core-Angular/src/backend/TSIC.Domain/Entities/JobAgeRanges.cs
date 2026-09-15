using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobAgeRanges
{
    public int AgeRangeId { get; set; }

    public Guid JobId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public DateTime RangeLeft { get; set; }

    public string? RangeName { get; set; }

    public DateTime RangeRight { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
