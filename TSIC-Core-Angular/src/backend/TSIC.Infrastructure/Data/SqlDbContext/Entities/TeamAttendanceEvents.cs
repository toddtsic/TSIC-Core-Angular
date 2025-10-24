using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class TeamAttendanceEvents
{
    public int EventId { get; set; }

    public Guid TeamId { get; set; }

    public string? Comment { get; set; }

    public int EventTypeId { get; set; }

    public DateTime EventDate { get; set; }

    public string? EventLocation { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual TeamAttendanceTypes EventType { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Teams Team { get; set; } = null!;

    public virtual ICollection<TeamAttendanceRecords> TeamAttendanceRecords { get; set; } = new List<TeamAttendanceRecords>();
}
