using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class JobCalendar
{
    public int CalendarEventId { get; set; }

    public Guid JobId { get; set; }

    public string CalendarEventTitle { get; set; } = null!;

    public DateTime CalendarEventStart { get; set; }

    public DateTime CalendarEventEnd { get; set; }

    public string? CalendarEventDescription { get; set; }

    public string? CalendarEventColor { get; set; }

    public string? CalendarEventTextColor { get; set; }

    public virtual Jobs Job { get; set; } = null!;
}
