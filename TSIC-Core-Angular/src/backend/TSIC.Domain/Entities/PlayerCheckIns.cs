using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PlayerCheckIns
{
    public Guid RegistrationId { get; set; }

    public DateTime CheckedInTs { get; set; }

    public Guid? CheckedInByRegId { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Registrations? CheckedInByReg { get; set; }

    public virtual Registrations Registration { get; set; } = null!;
}
