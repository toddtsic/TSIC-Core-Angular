using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamCheckIns
{
    public Guid TeamId { get; set; }

    public DateTime CheckedInTs { get; set; }

    public Guid? CheckedInByRegId { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Registrations? CheckedInByReg { get; set; }

    public virtual Teams Team { get; set; } = null!;
}
