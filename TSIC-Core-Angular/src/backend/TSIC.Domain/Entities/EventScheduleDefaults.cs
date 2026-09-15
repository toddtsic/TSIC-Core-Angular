using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class EventScheduleDefaults
{
    public Guid JobId { get; set; }

    public string GamePlacement { get; set; } = null!;

    public byte BetweenRoundRows { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public int? GameGuarantee { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
