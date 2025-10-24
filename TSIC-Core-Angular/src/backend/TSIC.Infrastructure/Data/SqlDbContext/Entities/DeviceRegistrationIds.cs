using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class DeviceRegistrationIds
{
    public Guid Id { get; set; }

    public string DeviceId { get; set; } = null!;

    public Guid RegistrationId { get; set; }

    public DateTime Modified { get; set; }

    public bool Active { get; set; }

    public virtual Devices Device { get; set; } = null!;

    public virtual Registrations Registration { get; set; } = null!;
}
