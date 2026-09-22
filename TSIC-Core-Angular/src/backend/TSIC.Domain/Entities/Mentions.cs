using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Mentions
{
    public Guid MessageId { get; set; }

    public Guid RegId { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Messages Message { get; set; } = null!;

    public virtual Registrations Reg { get; set; } = null!;
}
