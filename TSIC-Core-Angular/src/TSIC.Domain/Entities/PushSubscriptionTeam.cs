using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PushSubscriptionTeam
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid TeamId { get; set; }

    public DateTime? Created { get; set; }

    public virtual PushSubscription Subscription { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
