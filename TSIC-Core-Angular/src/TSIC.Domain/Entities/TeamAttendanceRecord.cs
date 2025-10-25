using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamAttendanceRecord
{
    public int AttendanceId { get; set; }

    public int EventId { get; set; }

    public string PlayerId { get; set; } = null!;

    public bool Present { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual TeamAttendanceEvent Event { get; set; } = null!;

    public virtual AspNetUser? LebUser { get; set; }

    public virtual AspNetUser Player { get; set; } = null!;
}
