using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class Timezones
{
    public int TzId { get; set; }

    public string? TzName { get; set; }

    public int UtcOffset { get; set; }

    public int UtcOffsetHours { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual ICollection<Customers> Customers { get; set; } = new List<Customers>();

    public virtual AspNetUsers? LebUser { get; set; }
}
