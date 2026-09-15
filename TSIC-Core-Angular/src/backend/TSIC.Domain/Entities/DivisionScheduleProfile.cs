using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DivisionScheduleProfile
{
    public Guid DivisionId { get; set; }

    public string? GamePlacement { get; set; }

    public byte? BetweenRoundRows { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public int? GameGuarantee { get; set; }

    public virtual Divisions Division { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
