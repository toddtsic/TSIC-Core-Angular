using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DeviceJobs
{
    public Guid Id { get; set; }

    public string DeviceId { get; set; } = null!;

    public Guid JobId { get; set; }

    public DateTime? Modified { get; set; }

    public virtual Devices Device { get; set; } = null!;

    public virtual Jobs Job { get; set; } = null!;
}
