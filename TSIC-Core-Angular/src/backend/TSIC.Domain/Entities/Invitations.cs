using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Invitations
{
    public Guid InvitationId { get; set; }

    public Guid SourceJobId { get; set; }

    public Guid TargetJobId { get; set; }

    public int InviteKindId { get; set; }

    public string Subject { get; set; } = null!;

    public string BodyTemplate { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual ICollection<InvitationRegistrations> InvitationRegistrations { get; set; } = new List<InvitationRegistrations>();

    public virtual InviteKinds InviteKind { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual Jobs SourceJob { get; set; } = null!;

    public virtual Jobs TargetJob { get; set; } = null!;
}
