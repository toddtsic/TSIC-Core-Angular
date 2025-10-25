using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DeviceJob
{
    public Guid Id { get; set; }

    public string DeviceId { get; set; } = null!;

    public Guid JobId { get; set; }

    public DateTime? Modified { get; set; }

    public virtual Device Device { get; set; } = null!;

    public virtual Job Job { get; set; } = null!;
}
