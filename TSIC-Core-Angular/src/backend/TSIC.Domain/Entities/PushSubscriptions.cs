using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PushSubscriptions
{
    public Guid Id { get; set; }

    public string Endpoint { get; set; } = null!;

    public string P256dh { get; set; } = null!;

    public string Auth { get; set; } = null!;

    public DateTime? Created { get; set; }

    public virtual ICollection<PushSubscriptionJobs> PushSubscriptionJobs { get; set; } = new List<PushSubscriptionJobs>();

    public virtual ICollection<PushSubscriptionRegistrations> PushSubscriptionRegistrations { get; set; } = new List<PushSubscriptionRegistrations>();

    public virtual ICollection<PushSubscriptionTeams> PushSubscriptionTeams { get; set; } = new List<PushSubscriptionTeams>();
}
