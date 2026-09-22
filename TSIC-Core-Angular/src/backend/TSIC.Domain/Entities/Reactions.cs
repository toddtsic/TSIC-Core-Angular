using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Reactions
{
    public Guid MessageId { get; set; }

    public string CreatorUserId { get; set; } = null!;

    public string Emoji { get; set; } = null!;

    public Guid RegId { get; set; }

    public DateTime Created { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers CreatorUser { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Messages Message { get; set; } = null!;

    public virtual Registrations Reg { get; set; } = null!;
}
