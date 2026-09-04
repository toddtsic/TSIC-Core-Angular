using System;
using System.Collections.Generic;

namespace TSIC.Domain.LogEntities;

public partial class Browsers
{
    public int BrowserId { get; set; }

    public string BrowserName { get; set; } = null!;

    public virtual ICollection<AppUsage> AppUsage { get; set; } = new List<AppUsage>();
}
