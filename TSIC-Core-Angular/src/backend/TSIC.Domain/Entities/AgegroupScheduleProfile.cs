using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AgegroupScheduleProfile
{
    public Guid AgegroupId { get; set; }

    public string? GamePlacement { get; set; }

    public byte? BetweenRoundRows { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public int? GameGuarantee { get; set; }

    public string? BracketDepth { get; set; }

    public virtual Agegroups Agegroup { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
