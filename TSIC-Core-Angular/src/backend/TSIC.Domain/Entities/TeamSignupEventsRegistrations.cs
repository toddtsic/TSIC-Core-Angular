using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamSignupEventsRegistrations
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public bool BAccept { get; set; }

    public bool BAttend { get; set; }

    public Guid RegistrationId { get; set; }

    public virtual TeamSignupEvents Event { get; set; } = null!;

    public virtual Registrations Registration { get; set; } = null!;
}
