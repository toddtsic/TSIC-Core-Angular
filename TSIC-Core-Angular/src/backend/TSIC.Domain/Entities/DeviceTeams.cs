using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DeviceTeams
{
    public Guid Id { get; set; }

    public string DeviceId { get; set; } = null!;

    public Guid TeamId { get; set; }

    public DateTime Modified { get; set; }

    public Guid? RegistrationId { get; set; }

    public virtual Devices Device { get; set; } = null!;

    public virtual Registrations? Registration { get; set; }

    public virtual Teams Team { get; set; } = null!;
}
