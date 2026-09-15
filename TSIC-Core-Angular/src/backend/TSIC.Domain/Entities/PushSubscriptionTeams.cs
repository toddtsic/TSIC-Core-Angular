using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PushSubscriptionTeams
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid TeamId { get; set; }

    public DateTime? Created { get; set; }

    public virtual PushSubscriptions Subscription { get; set; } = null!;

    public virtual Teams Team { get; set; } = null!;
}
