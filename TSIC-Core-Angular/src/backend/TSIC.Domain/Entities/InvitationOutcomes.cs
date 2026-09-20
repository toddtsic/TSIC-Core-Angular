using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class InvitationOutcomes
{
    public int InvitationOutcomeId { get; set; }

    public string InvitationOutcomeName { get; set; } = null!;

    public virtual ICollection<InvitationRegistrations> InvitationRegistrations { get; set; } = new List<InvitationRegistrations>();
}
