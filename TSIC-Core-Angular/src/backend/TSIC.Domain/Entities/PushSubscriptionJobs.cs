using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PushSubscriptionJobs
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid JobId { get; set; }

    public DateTime? Created { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual PushSubscriptions Subscription { get; set; } = null!;
}
