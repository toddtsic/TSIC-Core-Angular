using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamSignupEvent
{
    public Guid EventId { get; set; }

    public Guid TeamId { get; set; }

    public string EventCategory { get; set; } = null!;

    public string? EventComments { get; set; }

    public DateTime EventDate { get; set; }

    public string? EventWhere { get; set; }

    public Guid CreatorRegId { get; set; }

    public DateTime CreateDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public int EventAi { get; set; }

    public virtual Registration CreatorReg { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;

    public virtual ICollection<TeamSignupEventsRegistration> TeamSignupEventsRegistrations { get; set; } = new List<TeamSignupEventsRegistration>();
}
