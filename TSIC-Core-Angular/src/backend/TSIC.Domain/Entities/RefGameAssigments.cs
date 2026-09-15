using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class RefGameAssigments
{
    public int Id { get; set; }

    public Guid? RefRegistrationId { get; set; }

    public int GameId { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual Schedule Game { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual Registrations? RefRegistration { get; set; }
}
