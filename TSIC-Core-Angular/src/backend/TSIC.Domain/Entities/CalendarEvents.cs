using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class CalendarEvents
{
    public int EventId { get; set; }

    public Guid JobId { get; set; }

    public Guid? AgegroupId { get; set; }

    public Guid? TeamId { get; set; }

    public string? EventColor { get; set; }

    public DateTimeOffset? StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public string? Location { get; set; }

    public string? Subject { get; set; }

    public string? Description { get; set; }

    public bool IsAllDay { get; set; }

    public int? RecurrenceId { get; set; }

    public string? RecurrenceRule { get; set; }

    public string? RecurrenceException { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public virtual Agegroups? Agegroup { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual Teams? Team { get; set; }
}
