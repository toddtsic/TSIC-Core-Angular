using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class RefGameAssigment
{
    public int Id { get; set; }

    public Guid? RefRegistrationId { get; set; }

    public int GameId { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual Schedule Game { get; set; } = null!;

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual Registration? RefRegistration { get; set; }
}
