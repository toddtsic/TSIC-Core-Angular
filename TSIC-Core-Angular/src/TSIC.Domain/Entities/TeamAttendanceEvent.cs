using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamAttendanceEvent
{
    public int EventId { get; set; }

    public Guid TeamId { get; set; }

    public string? Comment { get; set; }

    public int EventTypeId { get; set; }

    public DateTime EventDate { get; set; }

    public string? EventLocation { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual TeamAttendanceType EventType { get; set; } = null!;

    public virtual AspNetUser? LebUser { get; set; }

    public virtual Team Team { get; set; } = null!;

    public virtual ICollection<TeamAttendanceRecord> TeamAttendanceRecords { get; set; } = new List<TeamAttendanceRecord>();
}
