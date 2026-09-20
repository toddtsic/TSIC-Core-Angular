using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class InvitationRegistrations
{
    public Guid SourceRegistrationId { get; set; }

    public Guid InvitationId { get; set; }

    public int InvitationOutcomeId { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual Invitations Invitation { get; set; } = null!;

    public virtual InvitationOutcomes InvitationOutcome { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual Registrations SourceRegistration { get; set; } = null!;
}
