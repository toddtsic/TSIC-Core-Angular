using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DivisionScheduleProfile
{
    public Guid ProfileId { get; set; }

    public Guid JobId { get; set; }

    public string DivisionName { get; set; } = null!;

    public byte Placement { get; set; }

    public byte GapPattern { get; set; }

    public Guid? InferredFromJob { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime ModifiedUtc { get; set; }

    public byte Wave { get; set; }
}
