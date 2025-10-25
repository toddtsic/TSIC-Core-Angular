using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobAgeRange
{
    public int AgeRangeId { get; set; }

    public Guid JobId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public DateTime RangeLeft { get; set; }

    public string? RangeName { get; set; }

    public DateTime RangeRight { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual AspNetUser? LebUser { get; set; }
}
