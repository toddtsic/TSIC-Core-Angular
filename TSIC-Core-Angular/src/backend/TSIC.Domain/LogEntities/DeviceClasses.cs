using System;
using System.Collections.Generic;

namespace TSIC.Domain.LogEntities;

public partial class DeviceClasses
{
    public int DeviceClassId { get; set; }

    public string DeviceClassName { get; set; } = null!;

    public virtual ICollection<AppUsage> AppUsage { get; set; } = new List<AppUsage>();
}
