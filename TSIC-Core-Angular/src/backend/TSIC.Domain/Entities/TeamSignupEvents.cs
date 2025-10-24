using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class TeamSignupEvents
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

    public virtual Registrations CreatorReg { get; set; } = null!;

    public virtual Teams Team { get; set; } = null!;

    public virtual ICollection<TeamSignupEventsRegistrations> TeamSignupEventsRegistrations { get; set; } = new List<TeamSignupEventsRegistrations>();
}
