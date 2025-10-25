using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamRosterRequest
{
    public Guid RequestId { get; set; }

    public Guid TeamId { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string UniformNo { get; set; } = null!;

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string? Position { get; set; }

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
