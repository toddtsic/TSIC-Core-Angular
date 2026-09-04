using System;
using System.Collections.Generic;

namespace TSIC.Domain.LogEntities;

public partial class AppClients
{
    public int AppClientId { get; set; }

    public string AppClientName { get; set; } = null!;

    public virtual ICollection<AppUsage> AppUsage { get; set; } = new List<AppUsage>();
}
