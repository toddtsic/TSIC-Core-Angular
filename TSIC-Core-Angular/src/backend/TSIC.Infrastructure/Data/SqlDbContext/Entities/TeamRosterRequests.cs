using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class TeamRosterRequests
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

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual Teams Team { get; set; } = null!;
}
