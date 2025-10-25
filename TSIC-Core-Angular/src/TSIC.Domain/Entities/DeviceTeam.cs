using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DeviceTeam
{
    public Guid Id { get; set; }

    public string DeviceId { get; set; } = null!;

    public Guid TeamId { get; set; }

    public DateTime Modified { get; set; }

    public Guid? RegistrationId { get; set; }

    public virtual Device Device { get; set; } = null!;

    public virtual Registration? Registration { get; set; }

    public virtual Team Team { get; set; } = null!;
}
