using System;
using System.Collections.Generic;

namespace TSIC.Domain.LogEntities;

public partial class Platforms
{
    public int PlatformId { get; set; }

    public string PlatformName { get; set; } = null!;

    public virtual ICollection<AppUsage> AppUsage { get; set; } = new List<AppUsage>();
}
