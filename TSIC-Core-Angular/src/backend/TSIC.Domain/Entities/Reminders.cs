using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Reminders
{
    public Guid EventId { get; set; }

    public Guid RegId { get; set; }

    public byte Kind { get; set; }

    public int LeadMinutes { get; set; }

    public DateTime SentAt { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Events Event { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Registrations Reg { get; set; } = null!;
}
