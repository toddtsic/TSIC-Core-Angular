using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MemberTeamPrefs
{
    public Guid RegId { get; set; }

    public Guid TeamId { get; set; }

    public bool NotifyEventCreated { get; set; }

    public bool NotifyEventChanged { get; set; }

    public bool NotifyEventCancelled { get; set; }

    public bool NotifyAvailabilityReminder { get; set; }

    public bool NotifyAttendanceRecorded { get; set; }

    public int? ReminderLeadMinutes { get; set; }

    public TimeOnly? QuietStartLocal { get; set; }

    public TimeOnly? QuietEndLocal { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Registrations Reg { get; set; } = null!;

    public virtual Teams Team { get; set; } = null!;
}
