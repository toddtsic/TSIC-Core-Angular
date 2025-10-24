using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class JobTypes
{
    public string? JobTypeDesc { get; set; }

    public int JobTypeId { get; set; }

    public string? JobTypeName { get; set; }

    public virtual ICollection<Jobs> Jobs { get; set; } = new List<Jobs>();
}
