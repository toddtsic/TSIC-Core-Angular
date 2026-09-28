using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobFeatures
{
    public Guid JobId { get; set; }

    public bool ScheduleEnabled { get; set; }

    public bool AvailabilityEnabled { get; set; }

    public bool AttendanceEnabled { get; set; }

    public bool RemindersEnabled { get; set; }

    public bool DutiesEnabled { get; set; }

    public bool LineupsEnabled { get; set; }

    public bool StatsEnabled { get; set; }

    public bool CalendarSyncEnabled { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
