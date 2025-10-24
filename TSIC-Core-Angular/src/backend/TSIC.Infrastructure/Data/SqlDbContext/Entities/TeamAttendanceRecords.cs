using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class TeamAttendanceRecords
{
    public int AttendanceId { get; set; }

    public int EventId { get; set; }

    public string PlayerId { get; set; } = null!;

    public bool Present { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual TeamAttendanceEvents Event { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual AspNetUsers Player { get; set; } = null!;
}
