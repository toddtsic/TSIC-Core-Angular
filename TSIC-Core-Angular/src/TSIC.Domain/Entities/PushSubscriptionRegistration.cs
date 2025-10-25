using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PushSubscriptionRegistration
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid RegistrationId { get; set; }

    public DateTime? Created { get; set; }

    public virtual Registration Registration { get; set; } = null!;

    public virtual PushSubscription Subscription { get; set; } = null!;
}
