using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PushSubscriptionJob
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid JobId { get; set; }

    public DateTime? Created { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual PushSubscription Subscription { get; set; } = null!;
}
