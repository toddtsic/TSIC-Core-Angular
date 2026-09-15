using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DeviceGids
{
    public int Id { get; set; }

    public string DeviceId { get; set; } = null!;

    public int Gid { get; set; }

    public virtual Devices Device { get; set; } = null!;

    public virtual Schedule GidNavigation { get; set; } = null!;
}
