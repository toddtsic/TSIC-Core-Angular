using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PushSubscription
{
    public Guid Id { get; set; }

    public string Endpoint { get; set; } = null!;

    public string P256dh { get; set; } = null!;

    public string Auth { get; set; } = null!;

    public DateTime? Created { get; set; }

    public virtual ICollection<PushSubscriptionJob> PushSubscriptionJobs { get; set; } = new List<PushSubscriptionJob>();

    public virtual ICollection<PushSubscriptionRegistration> PushSubscriptionRegistrations { get; set; } = new List<PushSubscriptionRegistration>();

    public virtual ICollection<PushSubscriptionTeam> PushSubscriptionTeams { get; set; } = new List<PushSubscriptionTeam>();
}
