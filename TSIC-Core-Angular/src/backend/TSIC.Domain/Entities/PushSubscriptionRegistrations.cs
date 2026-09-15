using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PushSubscriptionRegistrations
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid RegistrationId { get; set; }

    public DateTime? Created { get; set; }

    public virtual Registrations Registration { get; set; } = null!;

    public virtual PushSubscriptions Subscription { get; set; } = null!;
}
