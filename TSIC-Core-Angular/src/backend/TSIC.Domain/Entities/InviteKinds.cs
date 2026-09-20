using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class InviteKinds
{
    public int InviteKindId { get; set; }

    public string InviteKindName { get; set; } = null!;

    public virtual ICollection<Invitations> Invitations { get; set; } = new List<Invitations>();
}
