using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Devices
{
    public string Id { get; set; } = null!;

    public string Token { get; set; } = null!;

    public string Type { get; set; } = null!;

    public DateTime Modified { get; set; }

    public bool Active { get; set; }

    public virtual ICollection<DeviceGids> DeviceGids { get; set; } = new List<DeviceGids>();

    public virtual ICollection<DeviceJobs> DeviceJobs { get; set; } = new List<DeviceJobs>();

    public virtual ICollection<DeviceRegistrationIds> DeviceRegistrationIds { get; set; } = new List<DeviceRegistrationIds>();

    public virtual ICollection<DeviceTeams> DeviceTeams { get; set; } = new List<DeviceTeams>();
}
