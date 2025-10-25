using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Device
{
    public string Id { get; set; } = null!;

    public string Token { get; set; } = null!;

    public string Type { get; set; } = null!;

    public DateTime Modified { get; set; }

    public bool Active { get; set; }

    public virtual ICollection<DeviceGid> DeviceGids { get; set; } = new List<DeviceGid>();

    public virtual ICollection<DeviceJob> DeviceJobs { get; set; } = new List<DeviceJob>();

    public virtual ICollection<DeviceRegistrationId> DeviceRegistrationIds { get; set; } = new List<DeviceRegistrationId>();

    public virtual ICollection<DeviceTeam> DeviceTeams { get; set; } = new List<DeviceTeam>();
}
