using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DeviceRegistrationId
{
    public Guid Id { get; set; }

    public string DeviceId { get; set; } = null!;

    public Guid RegistrationId { get; set; }

    public DateTime Modified { get; set; }

    public bool Active { get; set; }

    public virtual Device Device { get; set; } = null!;

    public virtual Registration Registration { get; set; } = null!;
}
